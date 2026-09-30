using System.Text.Json;
using Npgsql;
using Orvano.Core.Events;

namespace Orvano.Messaging.Application;

/// <summary>
/// The Messaging module's outbox events (spec 0009, AC-26). Each carries the project ID, the acting console user,
/// and for updates the names of the changed fields. Never an address, host, username, password, subject, or content.
/// </summary>
internal static class MessagingEvents
{
    public const string SmtpUpdated = "messaging.smtp.updated";
    public const string SmtpDeleted = "messaging.smtp.deleted";

    /// <summary>Writes one event about <paramref name="projectId"/>'s SMTP settings in <paramref name="tx"/>.</summary>
    public static Task<long> WriteSmtpAsync(
        NpgsqlTransaction tx, string type, Guid actorUserId, string projectId, IReadOnlyList<string>? changed, CancellationToken ct) =>
        Outbox.WriteAsync(tx, new EventDraft(type, Payload(actorUserId, projectId, changed), projectId, projectId), ct);

    internal static string Payload(Guid actorUserId, string projectId, IReadOnlyList<string>? changed)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("projectId", projectId);
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
