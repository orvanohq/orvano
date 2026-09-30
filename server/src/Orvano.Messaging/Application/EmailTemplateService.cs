using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Core.Http;
using Orvano.Core.Secrets;
using Orvano.Messaging.Domain;
using Orvano.Platform.Contracts;

namespace Orvano.Messaging.Application;

/// <summary>One template in the catalog: whether the project edited it, and when.</summary>
internal sealed record TemplateSummary(EmailTemplateInfo Info, bool IsCustom, DateTimeOffset? UpdatedAt);

/// <summary>One template as the editor shows it: the project's own content or the default, and the sample values.</summary>
internal sealed record TemplateView(EmailTemplateInfo Info, TemplateSource Source, bool IsCustom, DateTimeOffset? UpdatedAt, TemplateValues Sample);

/// <summary>
/// The auth email templates of a project (spec 0009, AC-8 to AC-12): the catalog, one template, save, reset,
/// preview, and a test email. The caller already passed the project, kind, role, and rate limit checks; the rest
/// answer in AC-24's order: the body (400, then 422), the project's state (409), the SMTP (409), then the send.
/// Saves are last write wins.
/// </summary>
internal sealed class EmailTemplateService(
    MessagingStore store,
    SecretBox secrets,
    IProjectDirectory projects,
    IConsoleUserDirectory users,
    ISmtpSender sender,
    MessagingSettings settings,
    PublicUrl publicUrl,
    ILogger<EmailTemplateService> logger)
{
    public Task<IReadOnlyList<TemplateSummary>> GetCatalogAsync(string projectId, CancellationToken ct) =>
        store.ReadAsync<IReadOnlyList<TemplateSummary>>(async (db, ct) =>
        {
            var edited = await db.EmailTemplates.AsNoTracking()
                .Where(t => t.ProjectId == projectId && t.Locale == EmailTemplateCatalog.Locale)
                .ToDictionaryAsync(t => t.Kind, t => t.UpdatedAt, ct);
            return [.. EmailTemplateCatalog.All.Select(info =>
                edited.TryGetValue(info.Wire, out var updatedAt) ? new TemplateSummary(info, true, updatedAt) : new TemplateSummary(info, false, null))];
        }, ct);

    public async Task<Outcome<TemplateView>> GetAsync(ProjectCaller caller, EmailTemplateInfo info, CancellationToken ct)
    {
        var sample = await SampleAsync(caller, info, ct);
        if (!sample.Succeeded) return sample.Failure!;

        var row = await store.ReadAsync((db, ct) => db.EmailTemplates.AsNoTracking().SingleOrDefaultAsync(
            t => t.ProjectId == caller.ProjectId && t.Kind == info.Wire && t.Locale == EmailTemplateCatalog.Locale, ct), ct);
        return row is null
            ? new TemplateView(info, DefaultTemplates.Get(info.Kind), false, null, sample.Value!.Values)
            : new TemplateView(info, new TemplateSource(row.Subject, row.Html, row.Text), true, row.UpdatedAt, sample.Value!.Values);
    }

    public async Task<Outcome<TemplateView>> UpdateAsync(
        ProjectCaller caller, EmailTemplateInfo info, string? subject, string? html, string? text, CancellationToken ct)
    {
        var check = await CheckAsync(caller, info, subject, html, text, ct);
        if (!check.Succeeded) return check.Failure!;
        var (source, _, sample) = check.Value!;
        if (sample.Project.Status != ProjectStatus.Active) return Failure.ProjectNotReady;

        return await store.WriteAsync<TemplateView>(async (uow, ct) =>
        {
            var before = await uow.Db.EmailTemplates.AsNoTracking().SingleOrDefaultAsync(
                t => t.ProjectId == caller.ProjectId && t.Kind == info.Wire && t.Locale == EmailTemplateCatalog.Locale, ct);
            var now = await uow.NowAsync(ct);
            List<string> changed = before is null ? ["subject", "html", "text"] : [];
            if (before is not null)
            {
                if (before.Subject != source.Subject) changed.Add("subject");
                if (before.Html != source.Html) changed.Add("html");
                if (before.Text != source.Text) changed.Add("text");
            }

            // One statement, so two people saving at the same moment both succeed and the later one stays.
            await uow.Db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO orvano.messaging_email_templates
                    (project_id, kind, locale, subject, html, text, updated_by_user_id, created_at, updated_at)
                VALUES ({caller.ProjectId}, {info.Wire}, {EmailTemplateCatalog.Locale}, {source.Subject}, {source.Html}, {source.Text},
                        {caller.UserId}, {now}, {now})
                ON CONFLICT (project_id, kind, locale) DO UPDATE
                SET subject = excluded.subject, html = excluded.html, text = excluded.text,
                    updated_by_user_id = excluded.updated_by_user_id, updated_at = excluded.updated_at
                """, ct);

            await MessagingEvents.WriteTemplateAsync(uow.Tx, MessagingEvents.TemplateUpdated, caller.UserId, caller.ProjectId, info.Wire, changed, ct);
            logger.LogInformation(
                "Console user {UserId} saved the {TemplateKind} email template of project {ProjectId}", caller.UserId, info.Wire, caller.ProjectId);
            return new TemplateView(info, source, true, now, sample.Values);
        }, ct);
    }

    /// <summary>Succeeds also when the template is already the default.</summary>
    public async Task<Outcome<Done>> ResetAsync(ProjectCaller caller, EmailTemplateInfo info, CancellationToken ct)
    {
        var project = await projects.GetAsync(caller.ProjectId, ct);
        if (project is null) return Failure.ProjectNotFound;
        if (project.Status != ProjectStatus.Active) return Failure.ProjectNotReady;

        return await store.WriteAsync<Done>(async (uow, ct) =>
        {
            var deleted = await uow.Db.EmailTemplates
                .Where(t => t.ProjectId == caller.ProjectId && t.Kind == info.Wire && t.Locale == EmailTemplateCatalog.Locale)
                .ExecuteDeleteAsync(ct);
            if (deleted > 0)
            {
                await MessagingEvents.WriteTemplateAsync(uow.Tx, MessagingEvents.TemplateReset, caller.UserId, caller.ProjectId, info.Wire, null, ct);
                logger.LogInformation(
                    "Console user {UserId} reset the {TemplateKind} email template of project {ProjectId}", caller.UserId, info.Wire, caller.ProjectId);
            }

            return new Done();
        }, ct);
    }

    /// <summary>Renders the given content, saved or not, with the sample values. It works on a project in any state.</summary>
    public async Task<Outcome<EmailContent>> PreviewAsync(
        ProjectCaller caller, EmailTemplateInfo info, string? subject, string? html, string? text, CancellationToken ct)
    {
        var check = await CheckAsync(caller, info, subject, html, text, ct);
        return check.Succeeded ? check.Value!.Rendered : check.Failure!;
    }

    /// <summary>
    /// Renders the given content with the sample values and sends it to the caller's own console email through the
    /// SMTP settings the project uses right now (AC-12). One attempt, nothing stored. Returns the address.
    /// </summary>
    public async Task<Outcome<string>> TestAsync(
        ProjectCaller caller, EmailTemplateInfo info, string? subject, string? html, string? text, CancellationToken ct)
    {
        var check = await CheckAsync(caller, info, subject, html, text, ct);
        if (!check.Succeeded) return check.Failure!;
        var (_, rendered, sample) = check.Value!;
        if (sample.Project.Status != ProjectStatus.Active) return Failure.ProjectNotReady;

        var smtp = await store.ReadAsync((db, ct) => EffectiveSmtp.FindAsync(db, caller.ProjectId, ct), ct);
        if (smtp is null) return Failure.EmailNotConfigured;

        var password = smtp.Row.PasswordCiphertext is { } blob
            ? Encoding.UTF8.GetString(secrets.Decrypt(blob, SmtpSettingsService.PasswordBinding(smtp.Row.ProjectId)))
            : null;
        var server = new SmtpServer(
            smtp.Row.Host, smtp.Row.Port, SmtpSettingsRule.ParseSecurity(smtp.Row.Security), smtp.Row.Username, password,
            PublicOnly: smtp.Source == SmtpSources.Project && !settings.AllowPrivateHosts);
        var email = new OutgoingEmail(
            smtp.Row.FromEmail, smtp.Row.FromName, smtp.Row.ReplyTo, sample.User.Email,
            $"{Guid.CreateVersion7()}@{new Uri(publicUrl.Origin).Host}", rendered);

        if (await sender.SendAsync(server, email, SmtpSettingsService.TestBudget, ct) is { } failure)
        {
            logger.LogWarning(
                "A test of the {TemplateKind} email template of project {ProjectId} failed with {ErrorCode} (SMTP reply {ReplyCode})",
                info.Wire, caller.ProjectId, SmtpOutcomes.Code(failure.Kind), failure.ReplyCode);
            return Failure.Smtp(failure);
        }

        logger.LogInformation(
            "Console user {UserId} sent a test of the {TemplateKind} email template of project {ProjectId}", caller.UserId, info.Wire, caller.ProjectId);
        return sample.User.Email;
    }

    /// <summary>The body checks every write shares: the sizes (400), then the template's own rules with a sample render (422).</summary>
    private async Task<Outcome<Checked>> CheckAsync(
        ProjectCaller caller, EmailTemplateInfo info, string? subject, string? html, string? text, CancellationToken ct)
    {
        if (!EmailTemplates.TryNormalize(subject, html, text, out var source, out var error)) return Failure.Invalid(error);
        var sample = await SampleAsync(caller, info, ct);
        if (!sample.Succeeded) return sample.Failure!;

        var (rendered, problem) = await EmailTemplates.CheckAsync(info, source, sample.Value!.Values);
        return problem is null ? new Checked(source, rendered!, sample.Value!) : Failure.TemplateInvalid(problem.Detail);
    }

    /// <summary>The sample values: the catalog's, with the real project name and the caller's own email and name.</summary>
    private async Task<Outcome<Sample>> SampleAsync(ProjectCaller caller, EmailTemplateInfo info, CancellationToken ct)
    {
        var project = await projects.GetAsync(caller.ProjectId, ct);
        if (project is null) return Failure.ProjectNotFound;
        if ((await users.GetManyAsync([caller.UserId], ct)).SingleOrDefault() is not { } user)
            return Failure.Invalid("Your console account has no email address to use as the sample.");
        return new Sample(EmailTemplateCatalog.Sample(info.Kind, project.Name, user.Email, user.Name), project, user);
    }

    private sealed record Sample(TemplateValues Values, ProjectInfo Project, ConsoleUserSummary User);

    private sealed record Checked(TemplateSource Source, EmailContent Rendered, Sample Sample);
}
