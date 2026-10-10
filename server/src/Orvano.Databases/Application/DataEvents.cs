using System.Text;
using System.Text.Json;
using Npgsql;
using Orvano.Core.Events;

namespace Orvano.Databases.Application;

/// <summary>Who made a change: an API key, an app user, or a console user (<c>user</c>), by ID (spec 0015, AC-26).</summary>
internal sealed record DataActor(string Type, string Id)
{
    public static DataActor Key(Guid keyId) => new("api_key", keyId.ToString());

    public static DataActor AppUser(Guid userId) => new("app_user", userId.ToString());

    public static DataActor ConsoleUser(Guid userId) => new("user", userId.ToString());
}

/// <summary>
/// The Databases module's outbox events (spec 0015, AC-26, AC-27), written in the change's own transaction after
/// <c>SET LOCAL ROLE NONE</c>. They carry IDs, names, and the actor; never a value. A schema change also sends
/// <c>pg_notify('orvano_schema', projectId)</c>, so every api instance drops its cached structure.
/// </summary>
internal static class DataEvents
{
    public const string TableCreated = "databases.table.created";
    public const string RowCreated = "databases.row.created";

    /// <summary>The NOTIFY channel of schema changes; the payload is the project ID.</summary>
    public const string SchemaChannel = "orvano_schema";

    /// <summary>Writes a table event and the schema NOTIFY.</summary>
    public static async Task WriteTableAsync(
        NpgsqlTransaction tx, string type, string projectId, DatabaseRef database, string table, IReadOnlyList<string> columns, DataActor actor, CancellationToken ct)
    {
        var payload = Payload(w =>
        {
            w.WriteString("database", database.Id);
            w.WriteString("table", table);
            Names(w, "columns", columns);
            Actor(w, actor);
        });
        await Outbox.WriteAsync(tx, new EventDraft(type, payload, projectId, table), ct);
        await NotifySchemaAsync(tx, projectId, ct);
    }

    /// <summary>Writes one row event: the row ID and the changed column names, never the values.</summary>
    public static Task<long> WriteRowAsync(
        NpgsqlTransaction tx, string type, string projectId, DatabaseRef database, string table, string rowId, IReadOnlyList<string> columns, DataActor actor, CancellationToken ct)
    {
        var payload = Payload(w =>
        {
            w.WriteString("database", database.Id);
            w.WriteString("table", table);
            w.WriteString("rowId", rowId);
            Names(w, "columns", columns);
            Actor(w, actor);
        });
        return Outbox.WriteAsync(tx, new EventDraft(type, payload, projectId, rowId), ct);
    }

    private static async Task NotifySchemaAsync(NpgsqlTransaction tx, string projectId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_notify($1, $2)", tx.Connection, tx);
        cmd.Parameters.Add(new NpgsqlParameter { Value = SchemaChannel });
        cmd.Parameters.Add(new NpgsqlParameter { Value = projectId });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Payload(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            write(w);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Names(Utf8JsonWriter w, string name, IReadOnlyList<string> values)
    {
        w.WriteStartArray(name);
        foreach (var value in values) w.WriteStringValue(value);
        w.WriteEndArray();
    }

    private static void Actor(Utf8JsonWriter w, DataActor actor)
    {
        w.WriteStartObject("actor");
        w.WriteString("type", actor.Type);
        w.WriteString("id", actor.Id);
        w.WriteEndObject();
    }
}
