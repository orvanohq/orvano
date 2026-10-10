using System.Buffers.Text;
using System.Text;
using Npgsql;
using Orvano.Core.Paging;
using Orvano.Databases.Domain;

namespace Orvano.Databases.Application;

/// <summary>One page of tables, by name.</summary>
internal sealed record TableList(IReadOnlyList<TableInfo> Items, string? NextCursor);

/// <summary>
/// The table operations (spec 0015, AC-5, AC-6): listings and lookups read the structure cache, and a create runs as
/// the project role holding the schema's advisory lock, with its event and NOTIFY in the same transaction.
/// </summary>
internal sealed class TableService(DataScope scope, StructureCache cache)
{
    /// <summary>A page of the database's tables by name (ordinal order); the cursor is the last name.</summary>
    public async Task<Outcome<TableList>> ListAsync(string projectId, DatabaseRef database, string? cursor, int? limit, CancellationToken ct)
    {
        if (PageCursor.Limit(limit) is not { } size) return Failure.InvalidLimit;
        string? after = null;
        if (cursor is not null && !TryDecode(cursor, out after)) return Failure.InvalidCursor;

        var structure = await cache.GetAsync(projectId, database.Schema, ct);
        var tables = structure.Tables.Values
            .Where(t => after is null || string.CompareOrdinal(t.Name, after) > 0)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Take(size + 1)
            .ToList();
        var more = tables.Count > size;
        if (more) tables.RemoveAt(size);
        return new TableList(tables, more ? Encode(tables[^1].Name) : null);
    }

    /// <summary>The table named <paramref name="table"/>, or 404 <c>table_not_found</c>.</summary>
    public async Task<Outcome<TableInfo>> GetAsync(string projectId, DatabaseRef database, string table, CancellationToken ct) =>
        await cache.GetTableAsync(projectId, database.Schema, table, ct) is { } info ? info : Failure.TableNotFound;

    /// <summary>
    /// Creates a table (AC-5): the system columns, then <paramref name="columns"/> in order, and the
    /// <c>orvano_updated_at</c> trigger. Names and limits are checked under the schema's advisory lock, so two creates
    /// can't both pass; each value default is cast checked and then quoted by Postgres itself.
    /// </summary>
    public async Task<Outcome<TableInfo>> CreateAsync(
        string projectId, DatabaseRef database, string? name, IReadOnlyList<ColumnDraft>? columns, DataActor actor, CancellationToken ct)
    {
        var (specs, problem) = TableDrafts.Check(name, columns);
        if (problem is not null) return Failure.Invalid(problem);

        var created = await scope.RunAsync(
            projectId,
            database.Schema,
            DataTimeouts.Schema,
            async (conn, tx, token) =>
            {
                await LockSchemaAsync(conn, tx, database.Schema, token);
                if (await ScalarAsync<bool>(conn, tx, "SELECT EXISTS (SELECT 1 FROM pg_class WHERE relnamespace = current_schema()::regnamespace AND relname = $1)", token, name!))
                    throw new FailureException(Failure.NameTaken(name!));
                var count = await ScalarAsync<long>(
                    conn, tx, "SELECT count(*) FROM pg_class WHERE relnamespace = current_schema()::regnamespace AND relkind IN ('r', 'p') AND NOT relispartition", token);
                if (count >= DataLimits.Tables) throw new FailureException(Failure.TableLimit);

                var definitions = new List<string>
                {
                    $"{Identifiers.Quote(Identifiers.Id)} uuid PRIMARY KEY DEFAULT uuidv7()",
                    $"{Identifiers.Quote(Identifiers.CreatedAt)} timestamptz NOT NULL DEFAULT now()",
                    $"{Identifiers.Quote(Identifiers.UpdatedAt)} timestamptz NOT NULL DEFAULT now()",
                };
                for (var i = 0; i < specs!.Count; i++)
                    definitions.Add(await ColumnSqlAsync(conn, tx, specs[i], $"columns[{i}]", token));

                var table = Identifiers.Quote(name!);
                var ddl = $"""
                    CREATE TABLE {table} ({string.Join(", ", definitions)});
                    CREATE TRIGGER {Identifiers.Quote(Identifiers.UpdatedAtTrigger)} BEFORE UPDATE ON {table}
                        FOR EACH ROW EXECUTE FUNCTION orvano_fn.set_updated_at();
                    """;
                await using (var cmd = new NpgsqlCommand(ddl, conn, tx))
                {
                    await cmd.ExecuteNonQueryAsync(token);
                }

                return name!;
            },
            (conn, tx, table, token) => DataEvents.WriteTableAsync(
                tx, DataEvents.TableCreated, projectId, database, table, [.. specs!.Select(s => s.Name)], actor, token),
            ex => ex.SqlState == PostgresErrorCodes.DuplicateTable ? Failure.NameTaken(name!) : null,
            ct);
        if (!created.Succeeded) return created.Failure!;

        // The new table is read back from the catalog, after this instance drops its stale entry.
        cache.Evict(projectId);
        return await cache.GetTableAsync(projectId, database.Schema, created.Value!, ct) is { } info ? info : Failure.TableNotFound;
    }

    /// <summary>Takes the schema's advisory lock for the rest of the transaction (AC-5, AC-9).</summary>
    internal static async Task LockSchemaAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string schema, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext($1))", conn, tx);
        cmd.Parameters.Add(new NpgsqlParameter { Value = schema });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// One column's DDL. A value default is first cast to the column's type (in a savepoint, so a bad value leaves the
    /// transaction usable) and then quoted by <c>quote_literal</c>, never by string building here.
    /// </summary>
    private static async Task<string> ColumnSqlAsync(NpgsqlConnection conn, NpgsqlTransaction tx, ColumnSpec spec, string path, CancellationToken ct)
    {
        var type = ColumnTypes.Sql(spec.Kind);
        var sql = new StringBuilder($"{Identifiers.Quote(spec.Name)} {type}");
        if (spec.Required) sql.Append(" NOT NULL");
        if (spec.Unique) sql.Append(" UNIQUE");
        if (spec.Default is { } @default)
        {
            if (@default.Kind != DefaultKind.Value)
            {
                sql.Append(" DEFAULT ").Append(TableDrafts.FunctionSql(@default.Kind));
            }
            else
            {
                await tx.SaveAsync("default_check", ct);
                try
                {
                    await ScalarAsync<object>(conn, tx, $"SELECT $1::{type}", ct, @default.Value!);
                    await tx.ReleaseAsync("default_check", ct);
                }
                catch (PostgresException ex) when (ex.SqlState.StartsWith("22", StringComparison.Ordinal))
                {
                    await tx.RollbackAsync("default_check", ct);
                    throw new FailureException(Failure.Invalid($"{path}.default.value: not a valid {spec.Kind.ToString().ToLowerInvariant()} value"));
                }

                var quoted = await ScalarAsync<string>(conn, tx, "SELECT quote_literal($1)", ct, @default.Value!);
                sql.Append(" DEFAULT ").Append(quoted).Append("::").Append(type);
            }
        }

        return sql.ToString();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object[] values)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var value in values) cmd.Parameters.Add(new NpgsqlParameter { Value = value });
        return (T)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static string Encode(string name) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(name));

    private static bool TryDecode(string cursor, out string? name)
    {
        name = null;
        try
        {
            name = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            return name.Length is > 0 and <= 63;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
