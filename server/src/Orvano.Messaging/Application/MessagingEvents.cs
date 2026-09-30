using System.Text.Json;
using Npgsql;
using Orvano.Core.Events;

namespace Orvano.Messaging.Application;

/// <summary>
/// The Messaging module's outbox events (spec 0009, AC-26). Each carries the project ID, the actor, and the email
/// ID and error code, or the template kind, or the names of the changed fields. Never an address, host, username, password, subject, or
/// content.
/// </summary>
internal static class MessagingEvents
{
    public const string SmtpUpdated = "messaging.smtp.updated";
    public const string SmtpDeleted = "messaging.smtp.deleted";

    /// <summary>Writes one event about <paramref name="projectId"/>'s SMTP settings in <paramref name="tx"/>.</summary>
    public static Task<long> WriteSmtpAsync(
        NpgsqlTransaction tx, string type, Guid actorUserId, string projectId, IReadOnlyList<string>? changed, CancellationToken ct) =>
        Outbox.WriteAsync(tx, new EventDraft(type, Payload(actorUserId, projectId, changed), projectId, projectId), ct);

    public const string TemplateUpdated = "messaging.template.updated";
    public const string TemplateReset = "messaging.template.reset";

    /// <summary>Writes one event about a template of <paramref name="projectId"/> in <paramref name="tx"/>. It names the kind, never the content.</summary>
    public static Task<long> WriteTemplateAsync(
        NpgsqlTransaction tx, string type, Guid actorUserId, string projectId, string kind, IReadOnlyList<string>? changed, CancellationToken ct) =>
        Outbox.WriteAsync(tx, new EventDraft(type, Payload(actorUserId, projectId, changed, kind), projectId, kind), ct);

    public const string EmailSent = "messaging.email.sent";
    public const string EmailFailed = "messaging.email.failed";

    /// <summary>
    /// Writes one event about an email that became final, in the transaction that made it final. The worker acts,
    /// so the actor is <c>system</c>.
    /// </summary>
    public static Task<long> WriteEmailAsync(
        NpgsqlTransaction tx, string type, string projectId, Guid emailId, string? errorCode, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("projectId", projectId);
            w.WriteString("emailId", emailId);
            if (errorCode is not null) w.WriteString("errorCode", errorCode);
            w.WriteStartObject("actor");
            w.WriteString("type", "system");
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return Outbox.WriteAsync(tx, new EventDraft(type, System.Text.Encoding.UTF8.GetString(buffer.ToArray()), projectId, emailId.ToString()), ct);
    }

    internal static string Payload(Guid actorUserId, string projectId, IReadOnlyList<string>? changed, string? templateKind = null)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("projectId", projectId);
            if (templateKind is not null) w.WriteString("kind", templateKind);
            w.WriteStartObject("actor");
            w.WriteString("type", "user");
            w.WriteString("id", actorUserId);
            w.WriteEndObject();
            if (changed is not null)
            {
                w.WriteStartArray("changed");
                foreach (var field in changed) w.WriteStringValue(field);
                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
