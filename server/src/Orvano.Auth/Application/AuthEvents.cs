using System.Text.Json;
using Npgsql;
using Orvano.Core.Events;

namespace Orvano.Auth.Application;

/// <summary>Who did something (AC-33): the signed in user or console user, a server's API key, or the system.</summary>
internal sealed record Actor(string Type, string? Id)
{
    public static Actor User(Guid id) => new("user", id.ToString());

    public static Actor ApiKey(Guid keyId) => new("apiKey", keyId.ToString());

    public static Actor System { get; } = new("system", null);
}

/// <summary>
/// The Auth module's outbox events (AC-33), each written in the transaction of the change it describes. Payloads carry
/// IDs, changed field names, the end reason, and the actor; never an email, name, password, token, hash, IP address,
/// or user agent.
/// </summary>
internal static class AuthEvents
{
    public const string UserCreated = "auth.user.created";
    public const string UserUpdated = "auth.user.updated";
    public const string UserBlocked = "auth.user.blocked";
    public const string UserUnblocked = "auth.user.unblocked";
    public const string UserDeleted = "auth.user.deleted";
    public const string PasswordChanged = "auth.password.changed";
    public const string SessionCreated = "auth.session.created";
    public const string SessionEnded = "auth.session.ended";
    public const string KeyRotated = "auth.key.rotated";

    /// <summary>Writes one event about a user (the event's subject) or one of their sessions.</summary>
    public static Task<long> WriteAsync(
        NpgsqlTransaction tx,
        string type,
        string projectId,
        Actor actor,
        string subject,
        IReadOnlyDictionary<string, string> ids,
        IReadOnlyList<string>? changed = null,
        string? reason = null,
        CancellationToken ct = default) =>
        Outbox.WriteAsync(tx, new EventDraft(type, Payload(actor, ids, changed, reason), projectId, subject), ct);

    internal static string Payload(Actor actor, IReadOnlyDictionary<string, string> ids, IReadOnlyList<string>? changed, string? reason)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            foreach (var (name, id) in ids.OrderBy(i => i.Key, StringComparer.Ordinal)) w.WriteString(name, id);
            w.WriteStartObject("actor");
            w.WriteString("type", actor.Type);
            if (actor.Id is { } actorId) w.WriteString("id", actorId);
            else w.WriteNull("id");
            w.WriteEndObject();
            if (changed is not null)
            {
                w.WriteStartArray("changed");
                foreach (var field in changed) w.WriteStringValue(field);
                w.WriteEndArray();
            }

            if (reason is not null) w.WriteString("reason", reason);
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
