using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orvano.Databases.Domain;

/// <summary>
/// A value ready to bind: the .NET value (or <see cref="DBNull"/>) and the SQL type to cast the parameter to, so
/// Postgres never guesses. Exact numbers and times travel as text and are parsed by Postgres itself.
/// </summary>
internal sealed record BoundValue(object Value, string Cast);

/// <summary>One bad field of a row: its path and a few words, never the value (AC-11).</summary>
internal sealed record FieldProblem(string Field, string Message);

/// <summary>
/// The JSON forms of row values (spec 0015, AC-16): reading a request's values into parameters, and the output
/// forms the row reader writes. Values are read from the raw JSON token, never through a double, so a bigint or
/// decimal keeps every digit.
/// </summary>
internal static partial class RowValues
{
    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)\z")]
    private static partial Regex IntegerToken();

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?\z")]
    private static partial Regex DecimalToken();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}\z")]
    private static partial Regex DateText();

    // ISO 8601 with a date, a time, and an offset (Z or ±hh:mm); fractions up to microseconds.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}(:\d{2}(\.\d{1,6})?)?([Zz]|[+-]\d{2}(:?\d{2})?)\z")]
    private static partial Regex TimestampText();

    /// <summary>
    /// Reads one value for <paramref name="column"/>: the bound value, or a problem message. JSON <c>null</c> is SQL
    /// NULL for every type; whether the column takes null is the caller's check.
    /// </summary>
    public static (BoundValue? Value, string? Problem) Read(JsonElement json, ColumnInfo column)
    {
        var cast = ColumnTypes.Sql(column.Kind);
        if (json.ValueKind == JsonValueKind.Null) return (new BoundValue(DBNull.Value, cast), null);
        if (ColumnTypes.IsArray(column.Kind)) return ReadArray(json, column.Kind, cast);
        if (column.Kind == ColumnKind.Json) return (new BoundValue(json.GetRawText(), cast), null);

        var (value, problem) = ReadScalar(json, column.Kind);
        return problem is null ? (new BoundValue(value!, cast), null) : (null, problem);
    }

    private static (BoundValue?, string?) ReadArray(JsonElement json, ColumnKind kind, string cast)
    {
        if (json.ValueKind != JsonValueKind.Array) return (null, "expected an array");
        var element = ColumnTypes.Element(kind);
        var items = new List<object?>(json.GetArrayLength());
        foreach (var item in json.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null)
            {
                items.Add(null);
                continue;
            }

            var (value, problem) = ReadScalar(item, element);
            if (problem is not null) return (null, $"array items: {problem}");
            items.Add(value);
        }

        object array = element switch
        {
            ColumnKind.Text => items.Cast<string?>().ToArray(),
            ColumnKind.Integer => items.Select(i => (int?)i).ToArray(),
            ColumnKind.Uuid => items.Select(i => (Guid?)i).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return (new BoundValue(array, cast), null);
    }

    private static (object? Value, string? Problem) ReadScalar(JsonElement json, ColumnKind kind)
    {
        switch (kind)
        {
            case ColumnKind.Text:
                return json.ValueKind == JsonValueKind.String ? (json.GetString(), null) : (null, "expected a string");

            case ColumnKind.Uuid:
                return json.ValueKind == JsonValueKind.String && Guid.TryParseExact(json.GetString(), "D", out var uuid)
                    ? (uuid, null)
                    : (null, "expected a uuid");

            case ColumnKind.Boolean:
                return json.ValueKind is JsonValueKind.True or JsonValueKind.False ? (json.GetBoolean(), null) : (null, "expected true or false");

            case ColumnKind.Integer:
                return json.ValueKind == JsonValueKind.Number && IntegerToken().IsMatch(json.GetRawText())
                    && int.TryParse(json.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var int32)
                    ? (int32, null)
                    : (null, "expected a whole number from -2147483648 to 2147483647");

            case ColumnKind.Bigint:
                {
                    var text = NumberText(json);
                    return text is not null && IntegerToken().IsMatch(text)
                        && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var int64)
                        ? (int64, null)
                        : (null, "expected a whole number in the bigint range, as a string or a number");
                }

            case ColumnKind.Float:
                return json.ValueKind == JsonValueKind.Number && json.TryGetDouble(out var real) && double.IsFinite(real)
                    ? (real, null)
                    : (null, "expected a finite number");

            case ColumnKind.Decimal:
                {
                    var text = NumberText(json);
                    return text is not null && DecimalToken().IsMatch(text) ? (text, null) : (null, "expected a decimal number, as a string or a number");
                }

            case ColumnKind.Timestamp:
                return json.ValueKind == JsonValueKind.String && json.GetString() is { } stamp && TimestampText().IsMatch(stamp)
                    && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? (stamp, null)
                    : (null, "expected an ISO 8601 time with an offset, such as 2026-10-10T08:30:00Z");

            case ColumnKind.Date:
                return json.ValueKind == JsonValueKind.String && json.GetString() is { } day && DateText().IsMatch(day)
                    && DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? (day, null)
                    : (null, "expected a date as YYYY-MM-DD");

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a scalar kind.");
        }
    }

    /// <summary>The raw token of a number, or the text of a string, for types that accept both.</summary>
    private static string? NumberText(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Number => json.GetRawText(),
        JsonValueKind.String => json.GetString(),
        _ => null,
    };

    /// <summary>A timestamp's output form: ISO 8601 in UTC with exactly six fraction digits.</summary>
    public static string FormatTimestamp(DateTime utc) =>
        utc == DateTime.MaxValue ? "Infinity"
        : utc == DateTime.MinValue ? "-Infinity"
        : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A date's output form, <c>YYYY-MM-DD</c>.</summary>
    public static string FormatDate(DateOnly date) =>
        date == DateOnly.MaxValue ? "Infinity"
        : date == DateOnly.MinValue ? "-Infinity"
        : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A uuid's output form, lowercase.</summary>
    public static string FormatUuid(Guid uuid) => uuid.ToString("D");

    /// <summary>A float's output: the number itself, or the strings <c>NaN</c>, <c>Infinity</c>, <c>-Infinity</c>.</summary>
    public static object FormatFloat(double value) =>
        double.IsNaN(value) ? "NaN" : double.IsPositiveInfinity(value) ? "Infinity" : double.IsNegativeInfinity(value) ? "-Infinity" : value;
}
