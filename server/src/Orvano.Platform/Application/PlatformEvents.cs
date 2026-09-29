using System.Text.Json;
using Npgsql;
using Orvano.Core.Events;

namespace Orvano.Platform.Application;

/// <summary>Who did something: a console user, or the system (a job).</summary>
internal sealed record Actor(string Type, Guid? Id)
{
    public static Actor User(Guid id) => new("user", id);

    public static Actor System { get; } = new("system", null);
}

/// <summary>
/// The Platform module's outbox events (AC-19). Each carries the affected IDs, the actor, and for updates the names
/// of the changed fields; member and invitation events also carry role values and reasons (spec 0008, AC-11).
/// Never a key secret, hash, invite token, invite url, or email.
/// </summary>
internal static class PlatformEvents
{
    public const string OrgCreated = "platform.org.created";
    public const string OrgUpdated = "platform.org.updated";
    public const string OrgDeleting = "platform.org.deleting";
    public const string OrgRestored = "platform.org.restored";
    public const string OrgPurged = "platform.org.purged";
    public const string MemberAdded = "platform.member.added";
    public const string MemberRemoved = "platform.member.removed";
    public const string MemberRoleChanged = "platform.member.role_changed";
    public const string InvitationCreated = "platform.invitation.created";
    public const string InvitationRevoked = "platform.invitation.revoked";
    public const string InvitationAccepted = "platform.invitation.accepted";
    public const string ProjectCreated = "platform.project.created";
    public const string ProjectUpdated = "platform.project.updated";
    public const string ProjectProvisioned = "platform.project.provisioned";
    public const string ProjectFailed = "platform.project.failed";
    public const string ProjectDeleting = "platform.project.deleting";
    public const string ProjectRestored = "platform.project.restored";
    public const string ProjectPurgeFailed = "platform.project.purge_failed";
    public const string ProjectPurged = "platform.project.purged";
    public const string KeyCreated = "platform.key.created";
    public const string KeyDeleted = "platform.key.deleted";
    public const string PlatformCreated = "platform.platform.created";
    public const string PlatformUpdated = "platform.platform.updated";
    public const string PlatformDeleted = "platform.platform.deleted";
    public const string InstallSettingsUpdated = "platform.install.settings_updated";
    public const string InstallAdminAdded = "platform.install.admin_added";

    /// <summary>
    /// Writes one event in <paramref name="tx"/>. <paramref name="ids"/> are the affected IDs by name
    /// (<c>orgId</c>, <c>projectId</c>, ...), plus any role values and reasons (<c>role</c>, <c>from</c>, <c>to</c>,
    /// <c>reason</c>); <paramref name="projectId"/> also goes on the event row for
    /// project scoped events. <paramref name="subject"/> is the ID the event is about.
    /// </summary>
    public static Task<long> WriteAsync(
        NpgsqlTransaction tx,
        string type,
        Actor actor,
        string subject,
        IReadOnlyDictionary<string, string> ids,
        string? projectId = null,
        IReadOnlyList<string>? changed = null,
        CancellationToken ct = default) =>
        Outbox.WriteAsync(tx, new EventDraft(type, Payload(actor, ids, changed), projectId, subject), ct);

    internal static string Payload(Actor actor, IReadOnlyDictionary<string, string> ids, IReadOnlyList<string>? changed)
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

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
