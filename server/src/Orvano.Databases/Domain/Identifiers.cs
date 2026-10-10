using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Orvano.Databases.Domain;

/// <summary>
/// The rules for table, column, and database names (spec 0015, AC-2, AC-5), and how a name from the catalog goes
/// into SQL. Names the API creates are lowercase and quoted; names made in SQL can be anything, so SQL only ever uses
/// <see cref="Quote"/> on a name read from the structure cache.
/// </summary>
internal static partial class Identifiers
{
    /// <summary>The standard table's system columns, which every table made through the API starts with.</summary>
    public const string Id = "id";

    /// <summary>When the row was created (<c>now()</c>, the transaction start).</summary>
    public const string CreatedAt = "created_at";

    /// <summary>When the row last changed, moved by the <c>orvano_updated_at</c> trigger.</summary>
    public const string UpdatedAt = "updated_at";

    /// <summary>The name of the trigger that moves <see cref="UpdatedAt"/>.</summary>
    public const string UpdatedAtTrigger = "orvano_updated_at";

    /// <summary>
    /// Postgres 18's keywords with category <c>R</c> (reserved) or <c>T</c> (reserved, can be a function or type), from
    /// <c>pg_get_keywords()</c>. A test checks this list against the real catalog. Unreserved words such as <c>name</c>
    /// stay allowed, since every identifier is quoted.
    /// </summary>
    public static readonly FrozenSet<string> ReservedWords = FrozenSet.ToFrozenSet(
    [
        "all", "analyse", "analyze", "and", "any", "array", "as", "asc", "asymmetric", "authorization", "binary", "both",
        "case", "cast", "check", "collate", "collation", "column", "concurrently", "constraint", "create", "cross",
        "current_catalog", "current_date", "current_role", "current_schema", "current_time", "current_timestamp",
        "current_user", "default", "deferrable", "desc", "distinct", "do", "else", "end", "except", "false", "fetch",
        "for", "foreign", "freeze", "from", "full", "grant", "group", "having", "ilike", "in", "initially", "inner",
        "intersect", "into", "is", "isnull", "join", "lateral", "leading", "left", "like", "limit", "localtime",
        "localtimestamp", "natural", "not", "notnull", "null", "offset", "on", "only", "or", "order", "outer",
        "overlaps", "placing", "primary", "references", "returning", "right", "select", "session_user", "similar",
        "some", "symmetric", "system_user", "table", "tablesample", "then", "to", "trailing", "true", "union", "unique",
        "user", "using", "variadic", "verbose", "when", "where", "window", "with",
    ], StringComparer.Ordinal);

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,62}\z")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*\z")]
    private static partial Regex AddressablePattern();

    /// <summary>The problem with a new table name, or null when it is allowed.</summary>
    public static string? TableNameProblem(string? name) => NameProblem(name);

    /// <summary>The problem with a new column name, or null when it is allowed.</summary>
    public static string? ColumnNameProblem(string? name) =>
        name is Id or CreatedAt or UpdatedAt ? $"'{name}' is a system column" : NameProblem(name);

    /// <summary>
    /// True when the API can address a name in a filter, sort, or row body: <c>^[A-Za-z_][A-Za-z0-9_]*$</c>. Columns
    /// made in SQL with other names are listed but read only (AC-6).
    /// </summary>
    public static bool IsAddressable(string name) => AddressablePattern().IsMatch(name);

    /// <summary>A name as a quoted SQL identifier, with any double quote doubled.</summary>
    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string? NameProblem(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "required";
        if (!NamePattern().IsMatch(name)) return "use 1 to 63 characters of a to z, 0 to 9, and _, starting with a letter";
        if (ReservedWords.Contains(name)) return $"'{name}' is a reserved Postgres word";
        if (name.StartsWith("pg_", StringComparison.Ordinal) || name.StartsWith("orvano", StringComparison.Ordinal))
            return "names can't start with pg_ or orvano";
        return null;
    }
}
