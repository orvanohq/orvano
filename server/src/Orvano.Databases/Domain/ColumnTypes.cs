using System.Collections.Frozen;

namespace Orvano.Databases.Domain;

/// <summary>A column's type in the API (spec 0015, AC-5). <see cref="Other"/> is any type Orvano does not map.</summary>
internal enum ColumnKind
{
    Text,
    Integer,
    Bigint,
    Float,
    Decimal,
    Boolean,
    Timestamp,
    Date,
    Uuid,
    Json,
    TextArray,
    IntegerArray,
    UuidArray,
    Other,
}

/// <summary>How each <see cref="ColumnKind"/> maps to Postgres: its catalog name and the type written in DDL.</summary>
internal static class ColumnTypes
{
    private sealed record Mapping(ColumnKind Kind, string PgName, string Sql);

    private static readonly Mapping[] Mappings =
    [
        new(ColumnKind.Text, "text", "text"),
        new(ColumnKind.Integer, "int4", "integer"),
        new(ColumnKind.Bigint, "int8", "bigint"),
        new(ColumnKind.Float, "float8", "double precision"),
        new(ColumnKind.Decimal, "numeric", "numeric"),
        new(ColumnKind.Boolean, "bool", "boolean"),
        new(ColumnKind.Timestamp, "timestamptz", "timestamptz"),
        new(ColumnKind.Date, "date", "date"),
        new(ColumnKind.Uuid, "uuid", "uuid"),
        new(ColumnKind.Json, "jsonb", "jsonb"),
        new(ColumnKind.TextArray, "_text", "text[]"),
        new(ColumnKind.IntegerArray, "_int4", "integer[]"),
        new(ColumnKind.UuidArray, "_uuid", "uuid[]"),
    ];

    private static readonly FrozenDictionary<string, ColumnKind> ByPgName =
        Mappings.ToFrozenDictionary(m => m.PgName, m => m.Kind, StringComparer.Ordinal);

    private static readonly FrozenDictionary<ColumnKind, string> SqlByKind = Mappings.ToFrozenDictionary(m => m.Kind, m => m.Sql);

    /// <summary>
    /// The API type of a catalog column: its <c>pg_type.typname</c> mapped by AC-5, else <see cref="ColumnKind.Other"/>.
    /// A <c>numeric</c> with a precision is still <c>decimal</c>; a typmod never changes the kind.
    /// </summary>
    public static ColumnKind FromCatalog(string pgName) => ByPgName.TryGetValue(pgName, out var kind) ? kind : ColumnKind.Other;

    /// <summary>The type to write in DDL for a column the API creates.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is <see cref="ColumnKind.Other"/>.</exception>
    public static string Sql(ColumnKind kind) =>
        SqlByKind.TryGetValue(kind, out var sql) ? sql : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No DDL type.");

    /// <summary>True for the array kinds, whose JSON form is an array of the element's form.</summary>
    public static bool IsArray(ColumnKind kind) => kind is ColumnKind.TextArray or ColumnKind.IntegerArray or ColumnKind.UuidArray;

    /// <summary>The element kind of an array kind.</summary>
    public static ColumnKind Element(ColumnKind kind) => kind switch
    {
        ColumnKind.TextArray => ColumnKind.Text,
        ColumnKind.IntegerArray => ColumnKind.Integer,
        ColumnKind.UuidArray => ColumnKind.Uuid,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an array kind."),
    };

    /// <summary>True when the kind can be sorted and used as a keyset key (AC-18): no json, arrays, or other types.</summary>
    public static bool IsSortable(ColumnKind kind) => kind is not (ColumnKind.Json or ColumnKind.Other) && !IsArray(kind);
}
