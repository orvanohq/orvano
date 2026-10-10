using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Databases.Domain;

namespace Orvano.Databases.Application;

/// <summary>Every table of one schema, by name (case sensitive), as of one catalog read.</summary>
internal sealed record SchemaStructure(IReadOnlyDictionary<string, TableInfo> Tables, DateTimeOffset LoadedAt);

/// <summary>
/// The structure of each schema, read live from <c>pg_catalog</c> and never copied into <c>orvano</c> (spec 0015,
/// AC-6, AC-10). An entry lives 60 seconds; the instance that changes a schema drops its entry after the commit, and
/// a name the entry lacks reloads it at most once a second, so a column added in <c>psql</c> works on the next call.
/// The loader runs as <c>orvano_app</c> on a plain pooled connection: every role can read <c>pg_catalog</c>.
/// </summary>
internal sealed class StructureCache([FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db)
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReloadSpacing = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<(string Project, string Schema), SchemaStructure> _entries = new();

    /// <summary>The time source; tests replace it.</summary>
    internal TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The schema's structure, from the cache while it is fresh.</summary>
    public async Task<SchemaStructure> GetAsync(string projectId, string schema, CancellationToken ct)
    {
        if (_entries.TryGetValue((projectId, schema), out var entry) && Time.GetUtcNow() - entry.LoadedAt < TimeToLive) return entry;
        return await LoadAsync(projectId, schema, ct);
    }

    /// <summary>
    /// One table, or null. A name the cached entry lacks reloads the schema first (at most once a second), so a
    /// table made outside Orvano is found at once.
    /// </summary>
    public async Task<TableInfo?> GetTableAsync(string projectId, string schema, string table, CancellationToken ct)
    {
        var entry = await GetAsync(projectId, schema, ct);
        if (entry.Tables.TryGetValue(table, out var info)) return info;
        if (Time.GetUtcNow() - entry.LoadedAt < ReloadSpacing) return null;
        entry = await LoadAsync(projectId, schema, ct);
        return entry.Tables.GetValueOrDefault(table);
    }

    /// <summary>Drops the cached structure of every schema of the project, as after its own schema change.</summary>
    public void Evict(string projectId)
    {
        foreach (var key in _entries.Keys.Where(k => k.Project == projectId)) _entries.TryRemove(key, out _);
    }

    /// <summary>Drops every cached schema, as after the listener reconnects and may have missed a change.</summary>
    public void Clear() => _entries.Clear();

    private async Task<SchemaStructure> LoadAsync(string projectId, string schema, CancellationToken ct)
    {
        var entry = new SchemaStructure(await CatalogReader.ReadAsync(db, schema, ct), Time.GetUtcNow());
        _entries[(projectId, schema)] = entry;
        return entry;
    }
}

/// <summary>Reads one schema's ordinary and partitioned tables from <c>pg_catalog</c> (AC-6). No view is listed.</summary>
internal static class CatalogReader
{
    // Partitions of a partitioned table are listed only through their parent.
    private const string TablesSql = """
        SELECT c.oid, c.relname, c.reltuples
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = $1 AND c.relkind IN ('r', 'p') AND NOT c.relispartition
        """;

    private const string ColumnsSql = """
        SELECT a.attrelid, a.attname, t.typname, t.typnamespace = 'pg_catalog'::regnamespace,
               format_type(a.atttypid, a.atttypmod), a.attnotnull,
               CASE WHEN a.attgenerated = '' THEN pg_get_expr(d.adbin, d.adrelid) END,
               a.attidentity <> '', a.attgenerated <> ''
        FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_type t ON t.oid = a.atttypid
        LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
        WHERE n.nspname = $1 AND c.relkind IN ('r', 'p') AND NOT c.relispartition AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY a.attrelid, a.attnum
        """;

    // Primary keys and unique constraints, then unique indexes that back no constraint (whole, not partial, and on
    // columns, not expressions). Column lists are in key order and read from the keys, never from names.
    private const string KeysSql = """
        SELECT con.conrelid, con.conname, con.contype = 'p',
               ARRAY(SELECT a.attname FROM unnest(con.conkey) WITH ORDINALITY k(attnum, ord)
                     JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum ORDER BY k.ord)
        FROM pg_constraint con
        JOIN pg_namespace n ON n.oid = con.connamespace
        WHERE n.nspname = $1 AND con.contype IN ('p', 'u')
        UNION ALL
        SELECT i.indrelid, ic.relname, false,
               ARRAY(SELECT a.attname FROM unnest(i.indkey::int2[]) WITH ORDINALITY k(attnum, ord)
                     JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum ORDER BY k.ord)
        FROM pg_index i
        JOIN pg_class ic ON ic.oid = i.indexrelid
        JOIN pg_namespace n ON n.oid = ic.relnamespace
        WHERE n.nspname = $1 AND i.indisunique AND i.indpred IS NULL AND i.indexprs IS NULL
          AND NOT EXISTS (SELECT 1 FROM pg_constraint con WHERE con.conindid = i.indexrelid)
        """;

    public static async Task<IReadOnlyDictionary<string, TableInfo>> ReadAsync(NpgsqlDataSource db, string schema, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        var tables = new Dictionary<uint, (string Name, double RelTuples)>();
        await using (var cmd = Command(conn, TablesSql, schema))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct)) tables[reader.GetFieldValue<uint>(0)] = (reader.GetString(1), reader.GetFloat(2));
        }

        var columns = tables.Keys.ToDictionary(k => k, _ => new List<CatalogColumn>());
        await using (var cmd = Command(conn, ColumnsSql, schema))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (!columns.TryGetValue(reader.GetFieldValue<uint>(0), out var list)) continue;
                list.Add(new CatalogColumn(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetString(4),
                    reader.GetBoolean(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetBoolean(7),
                    reader.GetBoolean(8)));
            }
        }

        var primaryKeys = new Dictionary<uint, IReadOnlyList<string>>();
        var singles = tables.Keys.ToDictionary(k => k, _ => new List<IReadOnlyList<string>>());
        var constraints = tables.Keys.ToDictionary(k => k, _ => new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
        await using (var cmd = Command(conn, KeysSql, schema))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var relation = reader.GetFieldValue<uint>(0);
                if (!tables.ContainsKey(relation)) continue;
                var keyColumns = reader.GetFieldValue<string[]>(3);
                constraints[relation][reader.GetString(1)] = keyColumns;
                if (reader.GetBoolean(2)) primaryKeys[relation] = keyColumns;
                else singles[relation].Add(keyColumns);
            }
        }

        return tables.ToDictionary(
            t => t.Value.Name,
            t => Structure.Build(new CatalogTable(
                t.Value.Name,
                columns[t.Key],
                primaryKeys.GetValueOrDefault(t.Key) ?? [],
                singles[t.Key],
                constraints[t.Key],
                t.Value.RelTuples)),
            StringComparer.Ordinal);
    }

    private static NpgsqlCommand Command(NpgsqlConnection conn, string sql, string schema)
    {
        var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = schema });
        return cmd;
    }
}
