using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Orvano.Databases.Domain;

/// <summary>One key of a listing's order: a column and its direction. Nulls sort last ascending, first descending.</summary>
internal sealed record SortKey(ColumnInfo Column, bool Descending);

/// <summary>SQL text with <c>$n</c> placeholders and the values to bind to them, in order.</summary>
internal sealed record SqlQuery(string Text, IReadOnlyList<BoundValue> Parameters);

/// <summary>
/// Paging through rows (spec 0015, AC-18): the order with its tie breaker, the keyset predicate that continues after
/// the last row, and the cursor that carries it. Every identifier comes from the cached structure and is quoted;
/// every cursor value is bound and cast back to the key's own type.
/// </summary>
internal static class RowListing
{
    /// <summary>
    /// The order of a listing with no <c>sort</c>: <c>created_at</c> ascending when the table has a sortable one,
    /// then the tie breaker (<c>id</c>, or the primary key columns of a table without the standard <c>id</c>).
    /// </summary>
    public static IReadOnlyList<SortKey> DefaultOrder(TableInfo table)
    {
        var keys = new List<SortKey>();
        if (table.Column(Identifiers.CreatedAt) is { } createdAt && ColumnTypes.IsSortable(createdAt.Kind)
            && !table.PrimaryKey.Contains(Identifiers.CreatedAt))
        {
            keys.Add(new SortKey(createdAt, Descending: false));
        }

        foreach (var name in table.PrimaryKey) keys.Add(new SortKey(table.Column(name)!, Descending: false));
        return keys;
    }

    /// <summary>
    /// One page: every column (each read as the row reader expects, see <see cref="SelectColumn"/>), then each key as
    /// text for the next cursor, after <paramref name="after"/> when given, one row more than <paramref name="limit"/>
    /// to learn whether another page exists.
    /// </summary>
    public static SqlQuery Page(TableInfo table, IReadOnlyList<SortKey> keys, IReadOnlyList<string?>? after, int limit)
    {
        // Keys are qualified by the table: the key text columns share their names, and a bare ORDER BY name would be
        // ambiguous between them.
        var target = Identifiers.Quote(table.Name);
        var parameters = new List<BoundValue>();
        var select = table.Columns.Select(SelectColumn)
            .Concat(keys.Select(k => $"{target}.{Identifiers.Quote(k.Column.Name)}::text"));
        var sql = new StringBuilder("SELECT ").AppendJoin(", ", select).Append(" FROM ").Append(target);
        if (after is not null) sql.Append(" WHERE ").Append(After(target, keys, after, parameters));
        sql.Append(" ORDER BY ").AppendJoin(", ", keys.Select(k => $"{target}.{Identifiers.Quote(k.Column.Name)}" + (k.Descending ? " DESC" : " ASC")));
        parameters.Add(new BoundValue(limit + 1, "integer"));
        sql.Append(" LIMIT $").Append(parameters.Count.ToString(CultureInfo.InvariantCulture));
        return new SqlQuery(sql.ToString(), parameters);
    }

    /// <summary>
    /// How a column is selected: numeric and types the API does not map as their exact text, everything else as
    /// itself for the typed readers.
    /// </summary>
    public static string SelectColumn(ColumnInfo column) =>
        column.Kind is ColumnKind.Decimal or ColumnKind.Other
            ? $"{Identifiers.Quote(column.Name)}::text"
            : Identifiers.Quote(column.Name);

    /// <summary>
    /// The keyset predicate: a row comes after the last one when it equals it on the first keys and comes after it on
    /// the next, with explicit null branches (nulls last ascending, first descending), expanded so mixed directions work.
    /// </summary>
    private static string After(string target, IReadOnlyList<SortKey> keys, IReadOnlyList<string?> values, List<BoundValue> parameters)
    {
        var alternatives = new List<string>();
        var equalSoFar = new List<string>();
        for (var i = 0; i < keys.Count; i++)
        {
            var column = $"{target}.{Identifiers.Quote(keys[i].Column.Name)}";
            var value = values[i];
            string? later;
            string equal;
            if (value is null)
            {
                later = keys[i].Descending ? $"{column} IS NOT NULL" : null;
                equal = $"{column} IS NULL";
            }
            else
            {
                parameters.Add(new BoundValue(value, keys[i].Column.SqlType));
                var p = $"${parameters.Count.ToString(CultureInfo.InvariantCulture)}::{keys[i].Column.SqlType}";
                later = keys[i].Descending
                    ? $"{column} < {p}"
                    : keys[i].Column.Required ? $"{column} > {p}" : $"({column} > {p} OR {column} IS NULL)";
                equal = $"{column} = {p}";
            }

            if (later is not null) alternatives.Add("(" + string.Join(" AND ", equalSoFar.Append(later)) + ")");
            equalSoFar.Add(equal);
        }

        return alternatives.Count == 0 ? "false" : "(" + string.Join(" OR ", alternatives) + ")";
    }

    /// <summary>What binds a cursor to its listing: SHA-256 of the database ID, table, filter, and sort.</summary>
    public static string Binding(string databaseId, string table, string filter, string sort) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{databaseId}\n{table}\n{filter}\n{sort}")));

    /// <summary>The cursor after a row: base64url JSON of its key values as text (null kept) and the binding.</summary>
    public static string EncodeCursor(IReadOnlyList<string?> values, string binding)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartArray("v");
            foreach (var v in values)
            {
                if (v is null) w.WriteNullValue();
                else w.WriteStringValue(v);
            }

            w.WriteEndArray();
            w.WriteString("h", binding);
            w.WriteEndObject();
        }

        return Base64Url.EncodeToString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a cursor of this listing: <paramref name="keyCount"/> values and the same binding, else false (400
    /// <c>invalid_cursor</c>).
    /// </summary>
    public static bool TryDecodeCursor(string cursor, int keyCount, string binding, out IReadOnlyList<string?> values)
    {
        values = [];
        try
        {
            using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(cursor));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("h", out var h) || h.ValueKind != JsonValueKind.String || h.GetString() != binding
                || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != keyCount)
            {
                return false;
            }

            var list = new List<string?>(keyCount);
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null) list.Add(null);
                else if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString());
                else return false;
            }

            values = list;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }
}
