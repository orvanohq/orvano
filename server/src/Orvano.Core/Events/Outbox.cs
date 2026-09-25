using Npgsql;
using NpgsqlTypes;

namespace Orvano.Core.Events;

public sealed record EventDraft(string Type, string PayloadJson, string? ProjectId = null, string? Subject = null);

public sealed record OutboxEvent(long Id, string? ProjectId, string Type, string? Subject, string Payload, DateTimeOffset CreatedAt);

/// <summary>
/// Writes an event in the caller's transaction, next to the change it describes. The NOTIFY carries
/// only the ID and is delivered after commit, so there are no lost or phantom events.
/// </summary>
public static class Outbox
{
    public const string Channel = "orvano_events";

    public static async Task<long> WriteAsync(NpgsqlTransaction tx, EventDraft e, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            WITH inserted AS (
                INSERT INTO orvano.events (project_id, type, subject, payload)
                VALUES (@project_id, @type, @subject, @payload)
                RETURNING id)
            SELECT id, pg_notify('orvano_events', id::text) FROM inserted
            """, tx.Connection, tx);
        cmd.Parameters.AddWithValue("project_id", NpgsqlDbType.Text, (object?)e.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("type", e.Type);
        cmd.Parameters.AddWithValue("subject", NpgsqlDbType.Text, (object?)e.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, e.PayloadJson);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public static async Task<OutboxEvent?> ReadAsync(NpgsqlDataSource db, long id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            "SELECT id, project_id, type, subject, payload::text, created_at FROM orvano.events WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    internal static OutboxEvent Map(NpgsqlDataReader r) => new(
        r.GetInt64(0),
        r.IsDBNull(1) ? null : r.GetString(1),
        r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.GetString(4),
        r.GetFieldValue<DateTimeOffset>(5));
}
