using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orvano.Core.Jobs;
using Orvano.Core.Secrets;
using Orvano.Messaging.Application;
using Orvano.Messaging.Data;
using Orvano.Messaging.Domain;
using Orvano.Platform.Contracts;

namespace Orvano.Messaging.Jobs;

/// <summary>
/// The worker's <c>messaging.email.send</c> job (spec 0009, AC-15 to AC-17): it unseals one queued email and sends
/// it through the SMTP settings in effect at that moment, so a fixed password rescues emails still retrying. It
/// never renders. Every outcome is one conditional update on <c>status = 'queued'</c>, so a duplicate run (a lease
/// that expired mid send) can never flip a final row. Once the row is final the job succeeds; it throws only to ask
/// for a retry.
/// </summary>
internal static class EmailSendJob
{
    public const string Kind = "messaging.email.send";

    /// <summary>The job for email <paramref name="emailId"/>. Its payload carries the ID and nothing else (AC-26).</summary>
    public static NewJob For(Guid emailId, string projectId) => new(
        Kind,
        JsonSerializer.Serialize(new Dictionary<string, string> { ["emailId"] = emailId.ToString() }),
        MessagingJobs.Queue,
        projectId,
        MaxAttempts: EmailDelivery.MaxAttempts);

    public static async Task RunAsync(JobContext job, CancellationToken ct)
    {
        Guid emailId;
        using (var payload = JsonDocument.Parse(job.Job.Payload))
        {
            emailId = payload.RootElement.TryGetProperty("emailId", out var value) && Guid.TryParse(value.GetString(), out var id)
                ? id
                : throw new PermanentJobFailureException("The job payload has no 'emailId'.");
        }

        var services = job.Services;
        var store = services.GetRequiredService<MessagingStore>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(EmailSendJob));
        var attempt = job.Job.Attempts;

        var loaded = await store.ReadAsync(async (db, ct) =>
        {
            // Missing (retention or a purge ran) or already final: nothing to do.
            if (await db.Emails.Where(e => e.Id == emailId && e.Status == EmailStatuses.Queued)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Attempts, attempt), ct) == 0)
            {
                return null;
            }

            var row = await db.Emails.AsNoTracking().SingleOrDefaultAsync(e => e.Id == emailId && e.Status == EmailStatuses.Queued, ct);
            if (row is null) return null;
            var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT now() AS \"Value\"").SingleAsync(ct);
            return new Loaded(row, now, await EffectiveSmtp.FindAsync(db, row.ProjectId, ct));
        }, ct);
        if (loaded is null) return;
        var (email, smtp) = (loaded.Row, loaded.Smtp);

        // A worker that was down must not deliver stale codes and links.
        if (EmailDelivery.IsStale(email.CreatedAt, loaded.Now))
        {
            await FailAsync(store, logger, email, null, EmailFailures.Expired, null, ct);
            return;
        }

        if (await services.GetRequiredService<IProjectDirectory>().GetAsync(email.ProjectId, ct) is not { Status: ProjectStatus.Active })
        {
            await FailAsync(store, logger, email, null, EmailFailures.ProjectNotActive, null, ct);
            return;
        }

        if (smtp is null)
        {
            await FailAsync(store, logger, email, null, EmailFailures.NotConfigured, null, ct);
            return;
        }

        SealedEmail content;
        string? password;
        try
        {
            var secrets = services.GetRequiredService<SecretBox>();
            content = EmailSealer.Open(secrets, email.Id, email.ContentCiphertext);
            password = smtp.Row.PasswordCiphertext is { } blob
                ? Encoding.UTF8.GetString(secrets.Decrypt(blob, SmtpSettingsService.PasswordBinding(smtp.Row.ProjectId)))
                : null;
        }
        catch (SecretBoxException)
        {
            // A master key was removed, or the data was tampered with; no retry can fix that.
            await FailAsync(store, logger, email, null, EmailFailures.Unreadable, null, ct);
            return;
        }

        var settings = services.GetRequiredService<MessagingSettings>();
        var server = new SmtpServer(
            smtp.Row.Host, smtp.Row.Port, SmtpSettingsRule.ParseSecurity(smtp.Row.Security), smtp.Row.Username, password,
            PublicOnly: smtp.Source == SmtpSources.Project && !settings.AllowPrivateHosts);
        var outgoing = new OutgoingEmail(
            smtp.Row.FromEmail, smtp.Row.FromName, smtp.Row.ReplyTo, content.To, content.MessageId,
            new EmailContent(content.Subject, content.Html, content.Text));

        var started = Stopwatch.GetTimestamp();
        var failure = await services.GetRequiredService<ISmtpSender>().SendAsync(server, outgoing, EmailDelivery.AttemptBudget, ct);
        var code = failure is null ? null : SmtpOutcomes.Code(failure.Kind);
        MessagingTelemetry.RecordAttempt(Stopwatch.GetElapsedTime(started), email.Template, smtp.Source, code);

        if (failure is null)
        {
            if (await FinishAsync(store, email, EmailStatuses.Sent, smtp.Source, null, ct))
            {
                MessagingTelemetry.RecordSent(email.Template, smtp.Source);
                logger.LogInformation("Sent email {EmailId} of project {ProjectId} on attempt {Attempt}", email.Id, email.ProjectId, attempt);
            }

            return;
        }

        if (SmtpOutcomes.Retries(failure) && attempt < job.Job.MaxAttempts)
        {
            logger.LogWarning(
                "Attempt {Attempt} of email {EmailId} of project {ProjectId} failed with {ErrorCode} (SMTP reply {ReplyCode}); it will be retried",
                attempt, email.Id, email.ProjectId, code, failure.ReplyCode);
            throw new JobRetryException(EmailDelivery.RetryDelay(attempt), $"Email {email.Id} failed with {code}; retrying.");
        }

        await FailAsync(store, logger, email, smtp.Source, code!, failure.ReplyCode, ct);
    }

    private static async Task FailAsync(
        MessagingStore store, ILogger logger, EmailRow email, string? smtpSource, string errorCode, int? replyCode, CancellationToken ct)
    {
        if (!await FinishAsync(store, email, EmailStatuses.Failed, null, errorCode, ct)) return;
        MessagingTelemetry.RecordFailed(email.Template, smtpSource, errorCode);
        logger.LogWarning(
            "Email {EmailId} of project {ProjectId} failed with {ErrorCode} (SMTP reply {ReplyCode})", email.Id, email.ProjectId, errorCode, replyCode);
    }

    /// <summary>
    /// Makes the row final and writes its event in one transaction. Returns false when the row was already final or
    /// is gone, and then writes nothing.
    /// </summary>
    private static async Task<bool> FinishAsync(
        MessagingStore store, EmailRow email, string status, string? smtpSource, string? errorCode, CancellationToken ct)
    {
        var outcome = await store.WriteAsync<bool>(async (uow, ct) =>
        {
            var now = await uow.NowAsync(ct);
            var changed = await uow.Db.Emails.Where(e => e.Id == email.Id && e.Status == EmailStatuses.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, status)
                    .SetProperty(e => e.SmtpSource, smtpSource)
                    .SetProperty(e => e.ErrorCode, errorCode)
                    .SetProperty(e => e.CompletedAt, now)
                    .SetProperty(e => e.ContentCiphertext, (byte[]?)null), ct);
            if (changed == 0) return false;

            await MessagingEvents.WriteEmailAsync(
                uow.Tx, status == EmailStatuses.Sent ? MessagingEvents.EmailSent : MessagingEvents.EmailFailed, email.ProjectId, email.Id, errorCode, ct);
            return true;
        }, ct);
        return outcome.Value;
    }

    private sealed record Loaded(EmailRow Row, DateTimeOffset Now, EffectiveSmtp? Smtp);
}
