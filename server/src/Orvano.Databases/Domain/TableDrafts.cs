namespace Orvano.Databases.Domain;

/// <summary>The fixed limits of spec 0015 (*Limits*): constants, not settings.</summary>
internal static class DataLimits
{
    /// <summary>Extra databases per project, besides <c>main</c>.</summary>
    public const int ExtraDatabases = 10;

    /// <summary>Tables per database made through the API.</summary>
    public const int Tables = 500;

    /// <summary>User columns per table made through the API (the three system columns don't count).</summary>
    public const int Columns = 200;
}

/// <summary>A new column's default as the request gives it.</summary>
internal sealed record DefaultDraft(DefaultKind? Kind, string? Value);

/// <summary>A new column as the request gives it; <paramref name="Kind"/> is null for a type this server does not know.</summary>
internal sealed record ColumnDraft(string? Name, ColumnKind? Kind, bool Required, bool Unique, DefaultDraft? Default);

/// <summary>A new column that passed the input rules; its default still needs the database's cast check.</summary>
internal sealed record ColumnSpec(string Name, ColumnKind Kind, bool Required, bool Unique, ColumnDefault? Default);

/// <summary>The input rules of <c>tables.create</c> (spec 0015, AC-5), checked before the database is touched.</summary>
internal static class TableDrafts
{
    /// <summary>
    /// Checks the table name and every column, and answers the columns to create or the first problem, its
    /// <c>detail</c> starting with the input path (<c>columns[2].name: ...</c>).
    /// </summary>
    public static (IReadOnlyList<ColumnSpec>? Columns, string? Problem) Check(string? name, IReadOnlyList<ColumnDraft>? columns)
    {
        if (Identifiers.TableNameProblem(name) is { } tableProblem) return (null, $"name: {tableProblem}");
        columns ??= [];
        if (columns.Count > DataLimits.Columns) return (null, $"columns: at most {DataLimits.Columns} columns");

        var specs = new List<ColumnSpec>(columns.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            var (spec, problem) = CheckColumn(columns[i]);
            if (problem is not null) return (null, $"columns[{i}].{problem}");
            if (!seen.Add(spec!.Name)) return (null, $"columns[{i}].name: '{spec.Name}' is used twice");
            specs.Add(spec);
        }

        return (specs, null);
    }

    /// <summary>One column's rules: the problem starts with the field (<c>name: ...</c>, <c>default: ...</c>).</summary>
    public static (ColumnSpec? Spec, string? Problem) CheckColumn(ColumnDraft column)
    {
        if (Identifiers.ColumnNameProblem(column.Name) is { } nameProblem) return (null, $"name: {nameProblem}");
        if (column.Kind is not { } kind || kind == ColumnKind.Other) return (null, "type: not a type a column can be created with");

        ColumnDefault? @default = null;
        if (column.Default is { } draft)
        {
            var problem = draft.Kind switch
            {
                null => "default.kind: not a known kind",
                DefaultKind.Expression => "default.kind: expression defaults can only be made in SQL",
                DefaultKind.Value when draft.Value is null => "default.value: required for kind value",
                DefaultKind.Now when kind is not (ColumnKind.Timestamp or ColumnKind.Date) => "default.kind: now is for timestamp and date columns",
                DefaultKind.Uuidv7 or DefaultKind.RandomUuid when kind != ColumnKind.Uuid => "default.kind: uuid defaults are for uuid columns",
                not DefaultKind.Value when draft.Value is not null => "default.value: only kind value takes a value",
                _ => null,
            };
            if (problem is not null) return (null, problem);
            @default = new ColumnDefault(draft.Kind!.Value, draft.Value);
        }

        return (new ColumnSpec(column.Name!, kind, column.Required, column.Unique, @default), null);
    }

    /// <summary>The SQL of a default kind that is not a value; values are quoted by Postgres itself after a cast check.</summary>
    public static string FunctionSql(DefaultKind kind) => kind switch
    {
        DefaultKind.Now => "now()",
        DefaultKind.Uuidv7 => "uuidv7()",
        DefaultKind.RandomUuid => "gen_random_uuid()",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a function default."),
    };
}
