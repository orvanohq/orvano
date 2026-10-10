using System.Text.Json;

namespace Orvano.Databases.Domain;

/// <summary>A row's values ready for one <c>INSERT</c>: each column with its bound value, in the order sent.</summary>
internal sealed record RowWrite(IReadOnlyList<(ColumnInfo Column, BoundValue Value)> Values);

/// <summary>The body rules of <c>rows.create</c> (spec 0015, AC-11), checked against the cached structure.</summary>
internal static class RowInput
{
    /// <summary>
    /// Reads a create body: every bad field is listed (the first one leads <c>detail</c>), with paths starting with
    /// <paramref name="prefix"/> (<c>rows[3].</c> in a batch). <c>id</c> may be sent; <c>created_at</c> and
    /// <c>updated_at</c> never are.
    /// </summary>
    public static (RowWrite? Row, IReadOnlyList<FieldProblem> Problems) ForCreate(JsonElement body, TableInfo table, string prefix = "")
    {
        if (body.ValueKind != JsonValueKind.Object)
            return (null, [new FieldProblem(prefix.Length == 0 ? "body" : prefix.TrimEnd('.'), "expected an object of column values")]);

        var problems = new List<FieldProblem>();
        var values = new List<(ColumnInfo, BoundValue)>();
        var sent = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in body.EnumerateObject())
        {
            var field = prefix + property.Name;
            if (!sent.Add(property.Name))
            {
                problems.Add(new FieldProblem(field, "sent twice"));
                continue;
            }

            if (table.Column(property.Name) is not { } column)
            {
                problems.Add(new FieldProblem(field, "unknown column"));
                continue;
            }

            // The standard id may be chosen on create; every other system or read only column refuses a value.
            var settable = column.Writable || (column.System && column.Name == Identifiers.Id);
            if (!settable)
            {
                problems.Add(new FieldProblem(field, "read only"));
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null && column.Required)
            {
                problems.Add(new FieldProblem(field, "required"));
                continue;
            }

            var (value, problem) = RowValues.Read(property.Value, column);
            if (problem is not null) problems.Add(new FieldProblem(field, problem));
            else values.Add((column, value!));
        }

        // A required column with nothing to fill it must be sent.
        foreach (var column in table.Columns)
        {
            if (column.Required && !column.HasDefault && !sent.Contains(column.Name))
                problems.Add(new FieldProblem(prefix + column.Name, "required"));
        }

        return problems.Count > 0 ? (null, problems) : (new RowWrite(values), []);
    }
}
