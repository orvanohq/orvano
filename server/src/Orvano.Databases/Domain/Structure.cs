using System.Text.RegularExpressions;

namespace Orvano.Databases.Domain;

/// <summary>How a column's default is made (spec 0015, AC-5, AC-6).</summary>
internal enum DefaultKind
{
    Value,
    Now,
    Uuidv7,
    RandomUuid,
    Expression,
}

/// <summary>A column default: its kind, and the value text for <see cref="DefaultKind.Value"/> or the SQL of an expression.</summary>
internal sealed record ColumnDefault(DefaultKind Kind, string? Value);

/// <summary>
/// One column as the catalog has it (AC-6). <paramref name="SqlType"/> is <c>format_type</c>'s text, used to cast
/// cursor values back; <paramref name="HasDefault"/> also covers identity columns, which fill themselves.
/// </summary>
internal sealed record ColumnInfo(
    string Name,
    ColumnKind Kind,
    string PgType,
    string SqlType,
    bool Required,
    bool Unique,
    ColumnDefault? Default,
    bool HasDefault,
    bool Generated,
    bool System,
    bool Writable);

/// <summary>
/// One ordinary or partitioned table as the catalog has it, with the flags AC-6 and AC-21 derive from its primary
/// key. <paramref name="UniqueConstraints"/> maps each unique constraint or index name to its columns, so a unique
/// violation names the column from names only (AC-20).
/// </summary>
internal sealed record TableInfo(
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<string> PrimaryKey,
    bool Readable,
    bool Writable,
    long? EstimatedRows,
    IReadOnlyDictionary<string, IReadOnlyList<string>> UniqueConstraints)
{
    /// <summary>The column named <paramref name="name"/> (case sensitive), or null.</summary>
    public ColumnInfo? Column(string name) => Columns.FirstOrDefault(c => c.Name == name);
}

/// <summary>
/// A column as read from the catalog, before the table level flags are known. <paramref name="BuiltIn"/> is true for a
/// type in <c>pg_catalog</c>: only those map to an API type, so a user type named like one stays <c>other</c>.
/// </summary>
internal sealed record CatalogColumn(
    string Name, string PgType, bool BuiltIn, string SqlType, bool NotNull, string? DefaultSql, bool Identity, bool Generated);

/// <summary>A table as read from the catalog, before <see cref="Structure.Build"/> derives its flags.</summary>
internal sealed record CatalogTable(
    string Name,
    IReadOnlyList<CatalogColumn> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<IReadOnlyList<string>> SingleColumnUniques,
    IReadOnlyDictionary<string, IReadOnlyList<string>> UniqueConstraints,
    double RelTuples);

/// <summary>The derivations of AC-6 and AC-21 from catalog facts, kept free of any database code.</summary>
internal static partial class Structure
{
    [GeneratedRegex(@"^'((?:[^']|'')*)'::[^']+\z")]
    private static partial Regex CastLiteral();

    [GeneratedRegex(@"^-?\d+(\.\d+)?\z")]
    private static partial Regex BareNumber();

    /// <summary>Derives a table's columns and flags from what the catalog holds.</summary>
    public static TableInfo Build(CatalogTable table)
    {
        // AC-21: only the standard `id uuid` primary key makes rows writable; any primary key makes them readable.
        var idColumn = table.Columns.FirstOrDefault(c => c.Name == Identifiers.Id);
        var writable = table.PrimaryKey is [Identifiers.Id] && idColumn is { BuiltIn: true, PgType: "uuid" };
        var readable = table.PrimaryKey.Count > 0;

        var uniques = table.SingleColumnUniques.Where(u => u.Count == 1).Select(u => u[0]).ToHashSet(StringComparer.Ordinal);
        var columns = table.Columns.Select(c =>
        {
            var kind = c.BuiltIn ? ColumnTypes.FromCatalog(c.PgType) : ColumnKind.Other;
            var system = writable && c.Name is Identifiers.Id or Identifiers.CreatedAt or Identifiers.UpdatedAt;
            return new ColumnInfo(
                c.Name,
                kind,
                c.PgType,
                c.SqlType,
                c.NotNull,
                uniques.Contains(c.Name),
                ParseDefault(c.DefaultSql),
                c.DefaultSql is not null || c.Identity || c.Generated,
                c.Generated,
                system,
                !system && kind != ColumnKind.Other && !c.Generated && Identifiers.IsAddressable(c.Name));
        }).ToList();

        return new TableInfo(
            table.Name,
            columns,
            table.PrimaryKey,
            readable,
            writable,
            table.RelTuples < 0 ? null : (long)table.RelTuples,
            table.UniqueConstraints);
    }

    /// <summary>
    /// A default as the API shows it (AC-6): <c>now()</c>, <c>uuidv7()</c>, and <c>gen_random_uuid()</c> as their
    /// kinds; a quoted literal with a cast, a bare number, or <c>true</c>/<c>false</c> as a value; anything else as an
    /// expression with its SQL text.
    /// </summary>
    public static ColumnDefault? ParseDefault(string? sql)
    {
        if (sql is null) return null;
        switch (sql)
        {
            case "now()":
                return new ColumnDefault(DefaultKind.Now, null);
            case "uuidv7()":
                return new ColumnDefault(DefaultKind.Uuidv7, null);
            case "gen_random_uuid()":
                return new ColumnDefault(DefaultKind.RandomUuid, null);
            case "true" or "false":
                return new ColumnDefault(DefaultKind.Value, sql);
        }

        if (CastLiteral().Match(sql) is { Success: true } literal)
            return new ColumnDefault(DefaultKind.Value, literal.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal));
        if (BareNumber().IsMatch(sql)) return new ColumnDefault(DefaultKind.Value, sql);
        return new ColumnDefault(DefaultKind.Expression, sql);
    }
}
