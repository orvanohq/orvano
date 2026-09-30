using Npgsql;
using Orvano.Core.Http;
using Orvano.Core.Jobs;
using Orvano.Core.Secrets;
using Orvano.Messaging.Domain;
using Orvano.Messaging.Jobs;

namespace Orvano.Messaging.Application;

/// <summary>Why an email was not queued. Nothing was written.</summary>
internal abstract record QueueRefusal
{
    private QueueRefusal() { }

    /// <summary>Neither the project nor the install has an SMTP server.</summary>
    public sealed record NotConfigured : QueueRefusal;

    /// <summary>The project reached the install's hourly cap (AC-19); a slot frees after <paramref name="RetryAfter"/>.</summary>
    public sealed record RateLimited(TimeSpan RetryAfter) : QueueRefusal;
}

/// <summary>
/// The shared core of queueing an email (spec 0009, AC-14 and AC-19), in raw Npgsql on the caller's transaction, so
/// the row and its job exist only if the change that asked for them commits, and no second EF context shares the
/// caller's connection. A caller first asks <see cref="AdmitAsync"/>, then renders, then calls
/// <see cref="InsertAsync"/>.
/// </summary>
internal sealed class EmailQueue(SecretBox secrets, PublicUrl publicUrl, MessagingSettings settings)
{
    /// <summary>
    /// Resolves the project's effective SMTP and applies the install cap. Returns <see langword="null"/> when the
    /// email may be queued. The cap is soft: two requests at the same moment can both pass (no lock).
    /// </summary>
    public async Task<QueueRefusal?> AdmitAsync(NpgsqlTransaction tx, string projectId, CancellationToken ct)
    {
        bool own, install;
        await using (var smtp = new NpgsqlCommand(
            """
            SELECT EXISTS (SELECT 1 FROM orvano.messaging_smtp_settings WHERE project_id = @project),
                   EXISTS (SELECT 1 FROM orvano.messaging_smtp_settings WHERE project_id = @install)
            """, tx.Connection, tx))
        {
            smtp.Parameters.AddWithValue("project", projectId);
            smtp.Parameters.AddWithValue("install", SmtpSettingsService.InstallProjectId);
            await using var reader = await smtp.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            (own, install) = (reader.GetBoolean(0), reader.GetBoolean(1));
        }

        if (!own && !install) return new QueueRefusal.NotConfigured();
        // A project with its own SMTP spends its own reputation and has no cap. The console project's emails
        // (invites) always go through the install's.
        if (own && projectId != SmtpSettingsService.InstallProjectId) return null;

        // The row at offset count - limit, oldest first, is the one whose hour ending frees a slot.
        await using var cap = new NpgsqlCommand(
            """
            WITH recent AS (
                SELECT created_at FROM orvano.messaging_emails
                WHERE project_id = @project AND created_at > now() - @window)
            SELECT (SELECT count(*) FROM recent),
                   (SELECT extract(epoch FROM created_at + @window - now())::float8 FROM recent
                    ORDER BY created_at
                    OFFSET greatest((SELECT count(*) FROM recent) - @limit, 0) LIMIT 1)
            """, tx.Connection, tx);
        cap.Parameters.AddWithValue("project", projectId);
        cap.Parameters.AddWithValue("window", EmailDelivery.CapWindow);
        cap.Parameters.AddWithValue("limit", (long)settings.InstallHourlyLimit);
        await using var counted = await cap.ExecuteReaderAsync(ct);
        await counted.ReadAsync(ct);
        if (counted.GetInt64(0) < settings.InstallHourlyLimit) return null;
        var seconds = counted.IsDBNull(1) ? 0 : Math.Max(0, counted.GetDouble(1));
        return new QueueRefusal.RateLimited(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// Inserts the <c>queued</c> row with its content sealed to a new ID, and enqueues the send job whose payload is
    /// that ID alone. Returns the email's ID.
    /// </summary>
    public async Task<Guid> InsertAsync(NpgsqlTransaction tx, string projectId, string template, string to, EmailContent content, CancellationToken ct)
    {
        // Set in code: the content is sealed to the ID before the row exists.
        var id = Guid.CreateVersion7();
        var messageId = $"{id}@{new Uri(publicUrl.Origin).Host}";
        var sealedContent = EmailSealer.Seal(secrets, id, new SealedEmail(to, content.Subject, content.Html, content.Text, messageId));

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO orvano.messaging_emails (id, project_id, template, recipient_masked, content_ciphertext)
            VALUES (@id, @project, @template, @masked, @content)
            """, tx.Connection, tx))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("project", projectId);
            insert.Parameters.AddWithValue("template", template);
            insert.Parameters.AddWithValue("masked", RecipientMask.Mask(to));
            insert.Parameters.AddWithValue("content", sealedContent);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await JobQueue.EnqueueAsync(tx, EmailSendJob.For(id, projectId), ct);
        return id;
    }
}
