using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Messaging.Contracts;
using Orvano.Messaging.Domain;

namespace Orvano.Messaging.Application;

/// <summary>
/// Queues an auth email for another module (spec 0009, AC-14): it checks the request, resolves the SMTP and the
/// install cap, renders the project's template (or the default) with the caller's values, and inserts the sealed
/// row and its job, all on the caller's transaction in raw Npgsql.
/// </summary>
internal sealed class AuthEmailQueue(EmailQueue queue, [FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db, ILogger<AuthEmailQueue> logger) : IEmailQueue
{
    public async Task<EmailAvailability> CheckAvailabilityAsync(string projectId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        await using var conn = await db.OpenConnectionAsync(ct);
        return await queue.AdmitAsync(conn, null, projectId, ct) switch
        {
            null => new EmailAvailability.Available(),
            QueueRefusal.NotConfigured => new EmailAvailability.NotConfigured(),
            QueueRefusal.RateLimited limited => new EmailAvailability.RateLimited(limited.RetryAfter),
            var other => throw new InvalidOperationException($"Unknown queue refusal {other.GetType().Name}."),
        };
    }

    public async Task<EmailQueueResult> QueueAuthEmailAsync(NpgsqlTransaction tx, AuthEmail email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var values = AuthEmailRule.Check(email);

        switch (await queue.AdmitAsync(tx, email.ProjectId, ct))
        {
            case QueueRefusal.NotConfigured:
                return new EmailQueueResult.NotConfigured();
            case QueueRefusal.RateLimited limited:
                return new EmailQueueResult.RateLimited(limited.RetryAfter);
        }

        var info = EmailTemplateCatalog.Get(email.Kind);
        var custom = await FindCustomAsync(tx, email.ProjectId, info.Wire, ct);
        var content = custom is null ? null : await TryRenderAsync(info, custom, values);
        var fellBack = custom is not null && content is null;
        content ??= await RenderDefaultAsync(info, values);

        var id = await queue.InsertAsync(tx, email.ProjectId, info.Wire, values.UserEmail, content, ct);
        // AC-18: the project's own template can't be sent as it is, so the user still gets the default.
        if (fellBack)
        {
            logger.LogWarning(
                "Email {EmailId} of project {ProjectId} used the default {TemplateKind} template: the project's own failed to render",
                id, email.ProjectId, info.Wire);
        }

        return new EmailQueueResult.Queued(id);
    }

    /// <summary>The rendered custom template, or null when it can't be used: it breaks a rule, reaches a limit, or renders an empty subject.</summary>
    private static async Task<EmailContent?> TryRenderAsync(EmailTemplateInfo info, TemplateSource source, TemplateValues values)
    {
        if (!EmailTemplates.TryCompile(info, source, out var compiled, out _)) return null;
        try
        {
            var content = await EmailTemplates.RenderAsync(compiled, values);
            return content.Subject.Length == 0 ? null : content;
        }
        catch (EmailRenderException)
        {
            return null;
        }
    }

    private static async Task<EmailContent> RenderDefaultAsync(EmailTemplateInfo info, TemplateValues values)
    {
        if (!EmailTemplates.TryCompile(info, DefaultTemplates.Get(info.Kind), out var compiled, out var problem))
            throw new InvalidOperationException($"The default {info.Wire} template is invalid: {problem.Detail}");
        return await EmailTemplates.RenderAsync(compiled, values);
    }

    private static async Task<TemplateSource?> FindCustomAsync(NpgsqlTransaction tx, string projectId, string kind, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT subject, html, text FROM orvano.messaging_email_templates WHERE project_id = @project AND kind = @kind AND locale = @locale",
            tx.Connection, tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("locale", EmailTemplateCatalog.Locale);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new TemplateSource(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }
}
