using System.Text.Json;
using Orvano.Databases.Domain;

namespace Orvano.Server.Tests.Databases;

// Spec 0015's plain rules, with no database: names (AC-5), defaults read from the catalog (AC-6), the JSON forms of
// values (AC-16), and the keyset order and cursor (AC-18).
public class DataDomainTests
{
    [Theory]
    [InlineData("tasks", null)]
    [InlineData("name", null)] // unreserved words are fine: every identifier is quoted
    [InlineData("a", null)]
    [InlineData("", "required")]
    [InlineData("Tasks", "use 1 to 63")]
    [InlineData("1tasks", "use 1 to 63")]
    [InlineData("my-table", "use 1 to 63")]
    [InlineData("table", "'table' is a reserved")]
    [InlineData("user", "'user' is a reserved")]
    [InlineData("pg_stats", "names can't start")]
    [InlineData("orvano_x", "names can't start")]
    public void Table_names_follow_the_rules(string name, string? problem)
    {
        var actual = Identifiers.TableNameProblem(name);
        if (problem is null) Assert.Null(actual);
        else Assert.StartsWith(problem, actual);
    }

    [Fact]
    public void Names_are_63_characters_at_most_and_columns_never_take_a_system_name()
    {
        Assert.Null(Identifiers.TableNameProblem("a" + new string('b', 62)));
        Assert.NotNull(Identifiers.TableNameProblem("a" + new string('b', 63)));
        Assert.All(["id", "created_at", "updated_at"], n => Assert.Contains("system column", Identifiers.ColumnNameProblem(n)));
        Assert.Null(Identifiers.TableNameProblem("id"));
    }

    [Fact]
    public void Quoting_doubles_quotes_so_any_catalog_name_is_one_identifier()
    {
        Assert.Equal("\"tasks\"", Identifiers.Quote("tasks"));
        Assert.Equal("\"a\"\"; DROP TABLE x; --\"", Identifiers.Quote("a\"; DROP TABLE x; --"));
        Assert.False(Identifiers.IsAddressable("Odd Name"));
        Assert.True(Identifiers.IsAddressable("CamelCase_1"));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("now()", "now", null)]
    [InlineData("uuidv7()", "uuidv7", null)]
    [InlineData("gen_random_uuid()", "random_uuid", null)]
    [InlineData("'open'::text", "value", "open")]
    [InlineData("'it''s'::text", "value", "it's")]
    [InlineData("'{a,b}'::text[]", "value", "{a,b}")]
    [InlineData("'2026-01-01 00:00:00+00'::timestamp with time zone", "value", "2026-01-01 00:00:00+00")]
    [InlineData("42", "value", "42")]
    [InlineData("-1.5", "value", "-1.5")]
    [InlineData("true", "value", "true")]
    [InlineData("'a'::text || 'b'::text", "expression", "'a'::text || 'b'::text")]
    [InlineData("nextval('t_id_seq'::regclass)", "expression", "nextval('t_id_seq'::regclass)")]
    public void Catalog_defaults_read_as_their_kind(string? sql, string? kind, string? value)
    {
        var parsed = Structure.ParseDefault(sql);

        Assert.Equal(kind, parsed?.Kind switch
        {
            null => null,
            DefaultKind.RandomUuid => "random_uuid",
            { } k => k.ToString().ToLowerInvariant(),
        });
        Assert.Equal(value, parsed?.Value);
    }

    [Theory]
    [InlineData("Integer", "2147483647", 2147483647)]
    [InlineData("Bigint", "9223372036854775807", 9223372036854775807L)]
    [InlineData("Bigint", "\"-9223372036854775808\"", -9223372036854775808L)]
    [InlineData("Decimal", "0.1000000000000000000000000001", "0.1000000000000000000000000001")]
    [InlineData("Decimal", "\"1e400\"", "1e400")]
    [InlineData("Boolean", "false", false)]
    [InlineData("Text", "\"\"", "")]
    [InlineData("Timestamp", "\"2026-10-10T08:30:00.123456+05:30\"", "2026-10-10T08:30:00.123456+05:30")]
    [InlineData("Date", "\"2024-02-29\"", "2024-02-29")]
    public void Values_are_read_from_the_raw_token(string type, string json, object expected)
    {
        var kind = Enum.Parse<ColumnKind>(type);
        using var doc = JsonDocument.Parse(json);

        var (value, problem) = RowValues.Read(doc.RootElement, Column(kind));

        Assert.Null(problem);
        Assert.Equal(expected, value!.Value);
        Assert.Equal(ColumnTypes.Sql(kind), value.Cast);
    }

    [Theory]
    [InlineData("Integer", "1.0")]
    [InlineData("Integer", "1e3")]
    [InlineData("Integer", "2147483648")]
    [InlineData("Integer", "\"1\"")]
    [InlineData("Bigint", "9223372036854775808")]
    [InlineData("Bigint", "\"01\"")]
    [InlineData("Float", "\"NaN\"")]
    [InlineData("Decimal", "\"NaN\"")]
    [InlineData("Decimal", "\"Infinity\"")]
    [InlineData("Boolean", "1")]
    [InlineData("Text", "1")]
    [InlineData("Uuid", "\"not-a-uuid\"")]
    [InlineData("Timestamp", "\"2026-10-10T08:30:00\"")]
    [InlineData("Timestamp", "\"2026-10-10\"")]
    [InlineData("Date", "\"2026-02-30\"")]
    [InlineData("Date", "\"2026-2-3\"")]
    [InlineData("TextArray", "\"a\"")]
    [InlineData("IntegerArray", "[1, 1.5]")]
    public void Values_that_do_not_fit_are_refused(string type, string json)
    {
        var kind = Enum.Parse<ColumnKind>(type);
        using var doc = JsonDocument.Parse(json);

        Assert.NotNull(RowValues.Read(doc.RootElement, Column(kind)).Problem);
    }

    [Fact]
    public void Null_is_null_for_every_type_and_json_null_is_sql_null()
    {
        using var doc = JsonDocument.Parse("null");

        Assert.All(Enum.GetValues<ColumnKind>().Where(k => k != ColumnKind.Other), kind =>
            Assert.Equal(DBNull.Value, RowValues.Read(doc.RootElement, Column(kind)).Value!.Value));
    }

    [Fact]
    public void Output_forms_keep_six_fraction_digits_and_name_special_floats()
    {
        Assert.Equal("2026-10-10T08:30:00.000000Z", RowValues.FormatTimestamp(new DateTime(2026, 10, 10, 8, 30, 0, DateTimeKind.Utc)));
        Assert.Equal("Infinity", RowValues.FormatTimestamp(DateTime.MaxValue));
        Assert.Equal("NaN", RowValues.FormatFloat(double.NaN));
        Assert.Equal("-Infinity", RowValues.FormatFloat(double.NegativeInfinity));
        Assert.Equal(1.5, RowValues.FormatFloat(1.5));
        Assert.Equal("0f6b4b4e-0000-7000-8000-00000000000a", RowValues.FormatUuid(Guid.Parse("0F6B4B4E-0000-7000-8000-00000000000A")));
    }

    [Fact]
    public void A_cursor_carries_its_values_and_only_works_with_its_binding()
    {
        var binding = RowListing.Binding("main", "tasks", "", "");
        var cursor = RowListing.EncodeCursor(["2026-10-10 08:30:00+00", null], binding);

        Assert.True(RowListing.TryDecodeCursor(cursor, 2, binding, out var values));
        Assert.Equal(["2026-10-10 08:30:00+00", null], values);
        Assert.False(RowListing.TryDecodeCursor(cursor, 2, RowListing.Binding("main", "other", "", ""), out _));
        Assert.False(RowListing.TryDecodeCursor(cursor, 2, RowListing.Binding("d0000000000000000001", "tasks", "", ""), out _));
        Assert.False(RowListing.TryDecodeCursor(cursor, 3, binding, out _));
        Assert.False(RowListing.TryDecodeCursor("not base64!", 2, binding, out _));
        Assert.False(RowListing.TryDecodeCursor("bm9wZQ", 2, binding, out _));
    }

    [Fact]
    public void The_default_order_is_created_at_then_the_primary_key_and_the_keyset_has_null_branches()
    {
        var table = Structure.Build(new CatalogTable(
            "tasks",
            [
                new("id", "uuid", true, "uuid", true, "uuidv7()", false, false),
                new("created_at", "timestamptz", true, "timestamp with time zone", false, "now()", false, false),
            ],
            ["id"],
            [],
            new Dictionary<string, IReadOnlyList<string>>(),
            -1));

        var keys = RowListing.DefaultOrder(table);
        var page = RowListing.Page(table, keys, ["2026-10-10 08:30:00+00", "0f6b4b4e-0000-7000-8000-00000000000a"], 25);

        Assert.Equal(["created_at", "id"], keys.Select(k => k.Column.Name));
        Assert.Null(table.EstimatedRows);
        Assert.Contains("""("tasks"."created_at" > $1::timestamp with time zone OR "tasks"."created_at" IS NULL)""", page.Text);
        Assert.Contains("""("tasks"."created_at" = $1::timestamp with time zone AND "tasks"."id" > $2::uuid)""", page.Text);
        Assert.EndsWith("""ORDER BY "tasks"."created_at" ASC, "tasks"."id" ASC LIMIT $3""", page.Text);
        Assert.Equal(26, page.Parameters[^1].Value);
    }

    [Fact]
    public void Only_the_standard_id_uuid_key_makes_a_table_writable()
    {
        static TableInfo Build(string[] key, string idType) => Structure.Build(new CatalogTable(
            "t", [new("id", idType, true, idType, true, null, false, false)], key, [], new Dictionary<string, IReadOnlyList<string>>(), 0));

        Assert.True(Build(["id"], "uuid") is { Readable: true, Writable: true });
        Assert.True(Build(["id"], "int8") is { Readable: true, Writable: false });
        Assert.True(Build([], "uuid") is { Readable: false, Writable: false });
        Assert.True(Build(["id"], "uuid").Columns[0] is { System: true, Writable: false });
    }

    [Theory]
    [InlineData("expression", "uuid", "default.kind: expression")]
    [InlineData("uuidv7", "text", "default.kind: uuid defaults")]
    [InlineData("now", "integer", "default.kind: now")]
    public void Defaults_must_fit_their_column(string kind, string type, string problem)
    {
        var draft = new ColumnDraft("a", Kind(type), false, false, new DefaultDraft(DefaultKindOf(kind), null));

        Assert.StartsWith(problem, TableDrafts.CheckColumn(draft).Problem);
    }

    private static ColumnInfo Column(ColumnKind kind) =>
        new("c", kind, "", "", false, false, null, false, false, false, true);

    private static ColumnKind Kind(string type) => type switch
    {
        "uuid" => ColumnKind.Uuid,
        "text" => ColumnKind.Text,
        _ => ColumnKind.Integer,
    };

    private static DefaultKind DefaultKindOf(string kind) => kind switch
    {
        "expression" => DefaultKind.Expression,
        "uuidv7" => DefaultKind.Uuidv7,
        _ => DefaultKind.Now,
    };
}
