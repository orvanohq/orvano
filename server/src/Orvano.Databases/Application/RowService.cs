using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Orvano.Core.Paging;
using Orvano.Databases.Domain;

namespace Orvano.Databases.Application;

/// <summary>One page of rows and the cursor after its last row.</summary>
internal sealed record RowList(IReadOnlyList<JsonObject> Items, string? NextCursor);

/// <summary>
/// The row operations (spec 0015, AC-11, AC-16, AC-18, AC-20, AC-26). Each runs in one <see cref="DataScope"/>
/// transaction as the project role. Identifiers come only from the structure cache and are quoted; every value is a
/// parameter. A change writes its event in the same transaction, naming columns, never values.
/// </summary>
internal sealed class RowService(DataScope scope, StructureCache cache)
{
    /// <summary>Creates one row and answers it as stored (<c>RETURNING</c>), or 400 <c>invalid_row</c> with every bad field.</summary>
    public Task<Outcome<JsonObject>> CreateAsync(
        string projectId, DatabaseRef database, string tableName, JsonElement body, DataActor actor, DataTimeouts timeouts, CancellationToken ct) =>
        SelfHealingAsync(projectId, database, tableName, async table =>
        {
            if (WriteRefusal(table) is { } refusal) return refusal;
            var (row, problems) = RowInput.ForCreate(body, table);
            if (row is null) return Failure.InvalidRow([.. problems]);

            var returning = string.Join(", ", table.Columns.Select(RowListing.SelectColumn));
            var target = Identifiers.Quote(table.Name);
            var sql = row.Values.Count == 0
                ? $"INSERT INTO {target} DEFAULT VALUES RETURNING {returning}"
                : $"INSERT INTO {target} ({string.Join(", ", row.Values.Select(v => Identifiers.Quote(v.Column.Name)))}) "
                    + $"VALUES ({string.Join(", ", row.Values.Select((v, i) => $"${i + 1}::{v.Value.Cast}"))}) RETURNING {returning}";

            return await scope.RunAsync(
                projectId,
                database.Schema,
                timeouts,
                async (conn, tx, token) =>
                {
                    await using var cmd = new NpgsqlCommand(sql, conn, tx).With(row.Values.Select(v => v.Value));
                    await using var reader = await cmd.ExecuteReaderAsync(token);
                    await reader.ReadAsync(token);
                    return RowReader.Read(reader, table);
                },
                (conn, tx, created, token) => DataEvents.WriteRowAsync(
                    tx, DataEvents.RowCreated, projectId, database, table.Name, RowId(created),
                    [.. created.Where(p => p.Value is not null).Select(p => p.Key)], actor, token),
                ex => WriteFailure(ex, table),
                ct);
        }, ct);

    /// <summary>
    /// A page of rows by <c>created_at</c> then the primary key (AC-18), continuing after <paramref name="cursor"/>
    /// when given. The cursor is bound to this database and table, so it fails anywhere else.
    /// </summary>
    public Task<Outcome<RowList>> ListAsync(
        string projectId, DatabaseRef database, string tableName, string? cursor, int? limit, DataTimeouts timeouts, CancellationToken ct) =>
        SelfHealingAsync(projectId, database, tableName, async table =>
        {
            if (!table.Readable) return Failure.NoPrimaryKey;
            if (PageCursor.Limit(limit) is not { } size) return Failure.InvalidLimit;

            var keys = RowListing.DefaultOrder(table);
            var binding = RowListing.Binding(database.Id, table.Name, filter: "", sort: "");
            IReadOnlyList<string?>? after = null;
            if (cursor is not null)
            {
                if (!RowListing.TryDecodeCursor(cursor, keys.Count, binding, out var values)) return Failure.InvalidCursor;
                after = values;
            }

            var query = RowListing.Page(table, keys, after, size);
            return await scope.RunAsync(
                projectId,
                database.Schema,
                timeouts,
                async (conn, tx, token) =>
                {
                    await using var cmd = new NpgsqlCommand(query.Text, conn, tx).With(query.Parameters);
                    await using var reader = await cmd.ExecuteReaderAsync(token);
                    var items = new List<JsonObject>(size);
                    IReadOnlyList<string?>? last = null;
                    var more = false;
                    while (await reader.ReadAsync(token))
                    {
                        if (items.Count == size)
                        {
                            more = true;
                            break;
                        }

                        items.Add(RowReader.Read(reader, table));
                        last = [.. Enumerable.Range(table.Columns.Count, keys.Count).Select(i => reader.IsDBNull(i) ? null : reader.GetString(i))];
                    }

                    return new RowList(items, more ? RowListing.EncodeCursor(last!, binding) : null);
                },
                afterAsApp: null,
                // A cursor value that no longer casts (a key's type changed) is a cursor this listing can't continue.
                ex => after is not null && ex.SqlState.StartsWith("22", StringComparison.Ordinal) ? Failure.InvalidCursor : null,
                ct);
        }, ct);

    /// <summary>
    /// Runs <paramref name="work"/> on the table from the cache, and once more after a reload when Postgres says a
    /// table or column the cache had is gone (<c>42P01</c>, <c>42703</c>), so a change made outside Orvano heals at
    /// once (AC-10).
    /// </summary>
    private async Task<Outcome<T>> SelfHealingAsync<T>(
        string projectId, DatabaseRef database, string tableName, Func<TableInfo, Task<Outcome<T>>> work, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (await cache.GetTableAsync(projectId, database.Schema, tableName, ct) is not { } table) return Failure.TableNotFound;
            try
            {
                return await work(table);
            }
            catch (PostgresException ex) when (attempt == 0 && ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
            {
                cache.Evict(projectId);
            }
        }
    }

    /// <summary>Why a table's rows can't be changed through the API (AC-21), or null when they can.</summary>
    private static Failure? WriteRefusal(TableInfo table) =>
        !table.Readable ? Failure.NoPrimaryKey : !table.Writable ? Failure.ReadOnlyTable : null;

    /// <summary>
    /// A write's Postgres errors (AC-20), named from the cached structure only: a unique violation names its column, a
    /// foreign key its constraint, and bad data the column Postgres names. No Postgres message or detail is used.
    /// </summary>
    private static Failure? WriteFailure(PostgresException ex, TableInfo table)
    {
        switch (ex.SqlState)
        {
            case PostgresErrorCodes.UniqueViolation:
                var columns = ex.ConstraintName is { } name && table.UniqueConstraints.TryGetValue(name, out var cols) ? cols : [];
                return Failure.RowConflict(columns.Count > 0
                    ? $"{string.Join(", ", columns)}: another row already has this value."
                    : "Another row already has this value.");
            case PostgresErrorCodes.ForeignKeyViolation:
                return Failure.RowConflict(ex.ConstraintName is { } fk && Identifiers.IsAddressable(fk)
                    ? $"The row breaks the foreign key {fk}."
                    : "The row breaks a foreign key.");
            case PostgresErrorCodes.NotNullViolation:
                return Failure.InvalidRow([new FieldProblem(Field(ex.ColumnName, table), "required")]);
            case PostgresErrorCodes.CheckViolation:
                return Failure.InvalidRow([new FieldProblem("body", ex.ConstraintName is { } check && Identifiers.IsAddressable(check)
                    ? $"breaks the check {check}"
                    : "breaks a check constraint")]);
            default:
                return ex.SqlState.StartsWith("22", StringComparison.Ordinal)
                    ? Failure.InvalidRow([new FieldProblem(Field(ex.ColumnName, table), "does not fit its column")])
                    : null;
        }
    }

    /// <summary>A column Postgres named, only when the cache knows it; otherwise the whole body.</summary>
    private static string Field(string? column, TableInfo table) => column is not null && table.Column(column) is not null ? column : "body";

    private static string RowId(JsonObject row) => row[Identifiers.Id]?.GetValue<string>() ?? "";
}

/// <summary>
/// Reads one row with typed readers into its JSON form (AC-16), the columns at ordinals 0 to n-1 in table order. A
/// timestamp always carries six fraction digits; bigint and decimal are strings; floats keep NaN and infinities as
/// strings; <c>other</c> columns and decimals arrive as text (see <see cref="RowListing.SelectColumn"/>).
/// </summary>
internal static class RowReader
{
    public static JsonObject Read(NpgsqlDataReader reader, TableInfo table)
    {
        var row = new JsonObject();
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            row[column.Name] = reader.IsDBNull(i) ? null : Value(reader, i, column.Kind);
        }

        return row;
    }

    private static JsonNode? Value(NpgsqlDataReader reader, int i, ColumnKind kind) => kind switch
    {
        ColumnKind.Text or ColumnKind.Decimal or ColumnKind.Other => JsonValue.Create(reader.GetString(i)),
        ColumnKind.Integer => JsonValue.Create(reader.GetInt32(i)),
        ColumnKind.Bigint => JsonValue.Create(reader.GetInt64(i).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ColumnKind.Float => RowValues.FormatFloat(reader.GetDouble(i)) is string text ? JsonValue.Create(text) : JsonValue.Create(reader.GetDouble(i)),
        ColumnKind.Boolean => JsonValue.Create(reader.GetBoolean(i)),
        ColumnKind.Timestamp => JsonValue.Create(RowValues.FormatTimestamp(reader.GetFieldValue<DateTime>(i))),
        ColumnKind.Date => JsonValue.Create(RowValues.FormatDate(reader.GetFieldValue<DateOnly>(i))),
        ColumnKind.Uuid => JsonValue.Create(RowValues.FormatUuid(reader.GetGuid(i))),
        ColumnKind.Json => JsonNode.Parse(reader.GetString(i)),
        ColumnKind.TextArray => new JsonArray([.. reader.GetFieldValue<string?[]>(i).Select(v => (JsonNode?)JsonValue.Create(v))]),
        ColumnKind.IntegerArray => new JsonArray([.. reader.GetFieldValue<int?[]>(i).Select(v => (JsonNode?)(v is { } n ? JsonValue.Create(n) : null))]),
        ColumnKind.UuidArray => new JsonArray([.. reader.GetFieldValue<Guid?[]>(i).Select(v => (JsonNode?)(v is { } g ? JsonValue.Create(RowValues.FormatUuid(g)) : null))]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
