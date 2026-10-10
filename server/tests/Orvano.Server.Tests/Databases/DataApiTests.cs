using System.Net;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Databases;

// Spec 0015 build task 1 over HTTP against the real binary: main, tables.create and get, rows.create and list, the
// three callers, and the console mirrors.
public class DataApiTests(PostgresFixture postgres)
{
    private static readonly object[] EveryType =
    [
        new { name = "title", type = "text", required = true },
        new { name = "rank", type = "integer", unique = true },
        new { name = "views", type = "bigint", @default = new { kind = "value", value = "9007199254740993" } },
        new { name = "score", type = "float" },
        new { name = "price", type = "decimal" },
        new { name = "done", type = "boolean", @default = new { kind = "value", value = "false" } },
        new { name = "due_at", type = "timestamp", @default = new { kind = "now" } },
        new { name = "day", type = "date" },
        new { name = "ref", type = "uuid", @default = new { kind = "random_uuid" } },
        new { name = "meta", type = "json", @default = new { kind = "value", value = "{\"a\": [1, \"it's\"]}" } },
        new { name = "tags", type = "text[]", @default = new { kind = "value", value = "{a,b}" } },
        new { name = "counts", type = "integer[]" },
        new { name = "refs", type = "uuid[]" },
        new { name = "name", type = "text", @default = new { kind = "value", value = "it's" } },
    ];

    [Fact]
    public async Task Main_is_synthesized_and_listed_first()
    {
        await using var api = await DataApi.StartAsync(postgres);

        using var list = await api.AsServerAsync(HttpMethod.Get, "/v1/databases");
        using var main = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main");
        using var missing = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/analytics");
        using var tables = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/analytics/tables");
        using var badLimit = await api.AsServerAsync(HttpMethod.Get, "/v1/databases?limit=0");

        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.Equal(["main"], list.Body.GetProperty("items").EnumerateArray().Select(d => d.GetProperty("slug").GetString()));
        Assert.Equal(JsonValueKind.Null, list.Body.GetProperty("nextCursor").ValueKind);
        Assert.Equal("main", main.Body.GetProperty("id").GetString());
        Assert.Equal("Main", main.Body.GetProperty("name").GetString());
        Assert.Equal("active", main.Body.GetProperty("status").GetString());
        Assert.True(main.Body.GetProperty("main").GetBoolean());
        Assert.Equal(JsonValueKind.Null, main.Body.GetProperty("createdAt").ValueKind);
        Assert.Equal("database_not_found", missing.Code);
        Assert.Equal("database_not_found", tables.Code);
        Assert.Equal("invalid_request", badLimit.Code);
    }

    [Fact]
    public async Task Creates_a_table_with_every_type_and_reads_its_structure_live()
    {
        await using var api = await DataApi.StartAsync(postgres);

        using var created = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables", new { name = "tasks", columns = EveryType });
        using var got = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks");
        using var list = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables");

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(created.Body.ToString(), got.Body.ToString());
        Assert.Equal("main", got.Body.GetProperty("database").GetString());
        Assert.True(got.Body.GetProperty("readable").GetBoolean());
        Assert.True(got.Body.GetProperty("writable").GetBoolean());
        var columns = got.Body.GetProperty("columns").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!);
        Assert.Equal(
            ["id", "created_at", "updated_at", "title", "rank", "views", "score", "price", "done", "due_at", "day", "ref", "meta", "tags", "counts", "refs", "name"],
            got.Body.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(("uuid", "uuid", true, true, false), Shape(columns["id"]));
        Assert.Equal("uuidv7", columns["id"].GetProperty("default").GetProperty("kind").GetString());
        Assert.Equal(("timestamp", "timestamptz", true, true, false), Shape(columns["created_at"]));
        Assert.Equal("now", columns["updated_at"].GetProperty("default").GetProperty("kind").GetString());
        Assert.Equal(("text", "text", true, false, true), Shape(columns["title"]));
        Assert.True(columns["rank"].GetProperty("unique").GetBoolean());
        Assert.Equal(("bigint", "int8", false, false, true), Shape(columns["views"]));
        Assert.Equal("9007199254740993", columns["views"].GetProperty("default").GetProperty("value").GetString());
        Assert.Equal(("float", "float8", false, false, true), Shape(columns["score"]));
        Assert.Equal(("decimal", "numeric", false, false, true), Shape(columns["price"]));
        Assert.Equal("false", columns["done"].GetProperty("default").GetProperty("value").GetString());
        Assert.Equal("now", columns["due_at"].GetProperty("default").GetProperty("kind").GetString());
        Assert.Equal("random_uuid", columns["ref"].GetProperty("default").GetProperty("kind").GetString());
        Assert.Equal("{\"a\": [1, \"it's\"]}", columns["meta"].GetProperty("default").GetProperty("value").GetString());
        Assert.Equal(("text[]", "_text", false, false, true), Shape(columns["tags"]));
        Assert.Equal("{a,b}", columns["tags"].GetProperty("default").GetProperty("value").GetString());
        Assert.Equal(("integer[]", "_int4", false, false, true), Shape(columns["counts"]));
        Assert.Equal(("uuid[]", "_uuid", false, false, true), Shape(columns["refs"]));
        Assert.Equal("it's", columns["name"].GetProperty("default").GetProperty("value").GetString());
        Assert.Equal(["tasks"], list.Body.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(1L, await api.ScalarAsync<long>(
            "SELECT count(*) FROM pg_trigger WHERE tgname = 'orvano_updated_at' AND tgrelid = @t::regclass", ("t", $"{api.Schema}.tasks")));
        Assert.Equal(api.Schema, await api.ScalarAsync<string>(
            "SELECT tableowner::text FROM pg_tables WHERE schemaname = @s AND tablename = 'tasks'", ("s", api.Schema)));
    }

    [Theory]
    [InlineData("""{ "name": "select", "columns": [] }""", "name: 'select' is a reserved Postgres word")]
    [InlineData("""{ "name": "pg_things", "columns": [] }""", "name: names can't start with pg_ or orvano")]
    [InlineData("""{ "name": "Tasks", "columns": [] }""", "name: use 1 to 63")]
    [InlineData("""{ "name": "tasks", "columns": [{ "name": "a", "type": "text" }, { "name": "a", "type": "text" }] }""", "columns[1].name: 'a' is used twice")]
    [InlineData("""{ "name": "tasks", "columns": [{ "name": "id", "type": "uuid" }] }""", "columns[0].name: 'id' is a system column")]
    [InlineData("""{ "name": "tasks", "columns": [{ "name": "a", "type": "money" }] }""", "columns[0].type:")]
    [InlineData("""{ "name": "tasks", "columns": [{ "name": "a", "type": "other" }] }""", "columns[0].type:")]
    [InlineData("""{ "name": "tasks", "columns": [{ "name": "a", "type": "text", "default": { "kind": "expression", "value": "now()" } }] }""", "columns[0].default.kind:")]
    [InlineData("""{ "name": "tasks", "columns": [{ "name": "a", "type": "text", "default": { "kind": "now", "value": null } }] }""", "columns[0].default.kind: now is for")]
    [InlineData("""{ "name": "tasks", "columns": [{ "name": "a", "type": "integer", "default": { "kind": "value", "value": "1.5" } }] }""", "columns[0].default.value: not a valid integer")]
    [InlineData("""{ "name": "tasks", "columns": [{ "name": "a", "type": "json", "default": { "kind": "value", "value": "{oops" } }] }""", "columns[0].default.value: not a valid json")]
    public async Task Refuses_bad_names_types_and_defaults_with_the_input_path(string body, string detail)
    {
        await using var api = await DataApi.StartAsync(postgres);

        using var refused = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables", body);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("invalid_request", refused.Code);
        Assert.StartsWith(detail, refused.Body.GetProperty("detail").GetString());
        Assert.Equal(0L, await api.ScalarAsync<long>("SELECT count(*) FROM pg_tables WHERE schemaname = @s", ("s", api.Schema)));
    }

    [Fact]
    public async Task A_name_used_by_any_table_in_the_schema_is_taken()
    {
        await using var api = await DataApi.StartAsync(postgres);
        await api.CreateTableAsync("tasks");
        await api.ExecuteAsProjectAsync("CREATE TABLE made_in_sql (n int)");

        using var again = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables", new { name = "tasks", columns = Array.Empty<object>() });
        using var sql = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables", new { name = "made_in_sql", columns = Array.Empty<object>() });

        Assert.Equal("name_taken", again.Code);
        Assert.Equal("name_taken", sql.Code);
        Assert.Equal(1L, await api.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE type = 'databases.table.created'"));
    }

    [Fact]
    public async Task Rows_travel_in_their_json_forms()
    {
        await using var api = await DataApi.StartAsync(postgres);
        await api.CreateTableAsync("tasks", EveryType);
        var id = Guid.CreateVersion7();
        var refId = Guid.CreateVersion7();

        using var created = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", $$"""
            {
              "id": "{{id.ToString().ToUpperInvariant()}}",
              "title": "Write the spec",
              "rank": -2147483648,
              "views": 9223372036854775807,
              "score": 1.5e300,
              "price": "123456789012345678901234567890.000000000000000001",
              "done": true,
              "due_at": "2026-10-10T10:30:00.123456+02:00",
              "day": "2026-02-28",
              "ref": "{{refId}}",
              "meta": { "nested": [1, null, "x"] },
              "tags": ["a", null, "c"],
              "counts": [1, null, 3],
              "refs": [null, "{{refId.ToString().ToUpperInvariant()}}"],
              "name": null
            }
            """);
        using var defaults = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { title = "Defaults" });
        using var list = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows");

        Assert.Equal(HttpStatusCode.Created, created.Status);
        var row = created.Body;
        Assert.Equal(id.ToString(), row.GetProperty("id").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{6}Z$", row.GetProperty("created_at").GetString());
        Assert.Equal(row.GetProperty("created_at").GetString(), row.GetProperty("updated_at").GetString());
        Assert.Equal(-2147483648, row.GetProperty("rank").GetInt32());
        Assert.Equal("9223372036854775807", row.GetProperty("views").GetString());
        Assert.Equal(1.5e300, row.GetProperty("score").GetDouble());
        Assert.Equal("123456789012345678901234567890.000000000000000001", row.GetProperty("price").GetString());
        Assert.True(row.GetProperty("done").GetBoolean());
        Assert.Equal("2026-10-10T08:30:00.123456Z", row.GetProperty("due_at").GetString());
        Assert.Equal("2026-02-28", row.GetProperty("day").GetString());
        Assert.Equal(refId.ToString(), row.GetProperty("ref").GetString());
        Assert.Equal("""{"nested":[1,null,"x"]}""", row.GetProperty("meta").GetRawText());
        Assert.Equal("""["a",null,"c"]""", row.GetProperty("tags").GetRawText());
        Assert.Equal("[1,null,3]", row.GetProperty("counts").GetRawText());
        Assert.Equal($"""[null,"{refId}"]""", row.GetProperty("refs").GetRawText());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("name").ValueKind);

        var filled = defaults.Body;
        Assert.Equal("9007199254740993", filled.GetProperty("views").GetString());
        Assert.False(filled.GetProperty("done").GetBoolean());
        Assert.Equal("""["a","b"]""", filled.GetProperty("tags").GetRawText());
        Assert.Equal("it's", filled.GetProperty("name").GetString());
        Assert.Equal(7, filled.GetProperty("id").GetString()![14] - '0'); // uuidv7
        Assert.Equal(JsonValueKind.Null, filled.GetProperty("score").ValueKind);

        Assert.Equal([row.ToString(), filled.ToString()], list.Body.GetProperty("items").EnumerateArray().Select(r => r.ToString()));
    }

    [Fact]
    public async Task Refuses_a_bad_row_naming_every_field_and_writes_nothing()
    {
        await using var api = await DataApi.StartAsync(postgres);
        await api.CreateTableAsync("tasks", EveryType);

        using var bad = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", """
            { "rank": 1.0, "views": 1e3, "score": "1", "price": "NaN", "done": "yes", "due_at": "2026-10-10T10:30:00",
              "day": "2026-02-30", "ref": "nope", "tags": "a", "counts": [1.5], "created_at": "2026-10-10T10:30:00Z", "nope": 1 }
            """);
        using var nullRequired = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { title = (string?)null });
        using var notAnObject = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", "[1]");
        using var notJson = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", "{ oops");
        using var big = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", """{ "title": "x", "rank": 2147483648 }""");

        Assert.Equal("invalid_row", bad.Code);
        Assert.StartsWith("rank: ", bad.Body.GetProperty("detail").GetString());
        Assert.Equal(
            ["rank", "views", "score", "price", "done", "due_at", "day", "ref", "tags", "counts", "created_at", "nope", "title"],
            bad.Body.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()));
        Assert.Equal("read only", Message(bad, "created_at"));
        Assert.Equal("unknown column", Message(bad, "nope"));
        Assert.Equal("required", Message(bad, "title"));
        Assert.Equal("title: required", nullRequired.Body.GetProperty("detail").GetString());
        Assert.Equal("invalid_row", notAnObject.Code);
        Assert.Equal("invalid_row", notJson.Code);
        Assert.Equal("rank", big.Body.GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal(0L, await api.ScalarAsync<long>($"SELECT count(*) FROM {api.Schema}.tasks"));
        Assert.Equal(0L, await api.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE type = 'databases.row.created'"));
    }

    [Fact]
    public async Task A_duplicate_value_is_a_row_conflict_naming_the_column()
    {
        await using var api = await DataApi.StartAsync(postgres);
        await api.CreateTableAsync("tasks", EveryType);
        using var first = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { title = "a", rank = 1 });
        var id = first.Body.GetProperty("id").GetString();

        using var sameRank = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { title = "b", rank = 1 });
        using var sameId = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { id, title = "c" });

        Assert.Equal(HttpStatusCode.Conflict, sameRank.Status);
        Assert.Equal("row_conflict", sameRank.Code);
        Assert.StartsWith("rank:", sameRank.Body.GetProperty("detail").GetString());
        Assert.StartsWith("id:", sameId.Body.GetProperty("detail").GetString());
        Assert.DoesNotContain("1", sameRank.Body.GetProperty("detail").GetString()!.Replace("rank", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pages_walk_every_row_once_in_created_order_and_cursors_stay_with_their_table()
    {
        await using var api = await DataApi.StartAsync(postgres);
        await api.CreateTableAsync("tasks", new { name = "n", type = "integer" });
        await api.CreateTableAsync("other");
        var ids = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            using var row = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { n = i });
            ids.Add(row.Body.GetProperty("id").GetString()!);
        }

        var walked = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            using var page = await api.AsServerAsync(HttpMethod.Get,
                "/v1/databases/main/tables/tasks/rows?limit=3" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));
            walked.AddRange(page.Body.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetString()!));
            cursor = page.Body.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null);

        using var firstPage = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows?limit=3");
        var tasksCursor = Uri.EscapeDataString(firstPage.Body.GetProperty("nextCursor").GetString()!);
        using var elsewhere = await api.AsServerAsync(HttpMethod.Get, $"/v1/databases/main/tables/other/rows?cursor={tasksCursor}");
        using var forged = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows?cursor=bm9wZQ");
        using var tooMany = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows?limit=101");

        Assert.Equal(ids, walked);
        Assert.Equal(3, pages);
        Assert.Equal("invalid_cursor", elsewhere.Code);
        Assert.Equal("invalid_cursor", forged.Code);
        Assert.Equal("invalid_request", tooMany.Code);
    }

    [Fact]
    public async Task Tables_made_in_sql_are_found_at_once_and_flagged_by_their_primary_key()
    {
        await using var api = await DataApi.StartAsync(postgres);
        using var before = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables");

        await api.ExecuteAsProjectAsync("""
            CREATE TABLE no_key (n int);
            CREATE TABLE int_key (id bigint PRIMARY KEY, label varchar(20) UNIQUE, "Odd Name" text, total int GENERATED ALWAYS AS (id * 2) STORED);
            INSERT INTO int_key (id, label) VALUES (2, 'b'), (1, 'a'), (3, NULL)
            """);
        await Task.Delay(TimeSpan.FromSeconds(1.1), TestContext.Current.CancellationToken); // AC-10: unknown names reload at most once a second
        using var noKey = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/no_key");
        using var intKey = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/int_key");
        using var noKeyRows = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/no_key/rows");
        using var intKeyRows = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/int_key/rows?limit=2");
        using var intKeyCreate = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/int_key/rows", new { label = "c" });

        Assert.Empty(before.Body.GetProperty("items").EnumerateArray());
        Assert.False(noKey.Body.GetProperty("readable").GetBoolean());
        Assert.False(noKey.Body.GetProperty("writable").GetBoolean());
        Assert.True(intKey.Body.GetProperty("readable").GetBoolean());
        Assert.False(intKey.Body.GetProperty("writable").GetBoolean());
        var columns = intKey.Body.GetProperty("columns").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!);
        Assert.False(columns["id"].GetProperty("system").GetBoolean());
        Assert.Equal("other", columns["label"].GetProperty("type").GetString());
        Assert.Equal("varchar", columns["label"].GetProperty("pgType").GetString());
        Assert.True(columns["label"].GetProperty("unique").GetBoolean());
        Assert.False(columns["Odd Name"].GetProperty("writable").GetBoolean());
        Assert.False(columns["total"].GetProperty("writable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, columns["total"].GetProperty("default").ValueKind);
        Assert.Equal("table_not_writable", noKeyRows.Code);
        Assert.Equal(["1", "2"], intKeyRows.Body.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetString()));
        Assert.Equal("a", intKeyRows.Body.GetProperty("items")[0].GetProperty("label").GetString());
        Assert.NotEqual(JsonValueKind.Null, intKeyRows.Body.GetProperty("nextCursor").ValueKind);
        Assert.Equal("table_not_writable", intKeyCreate.Code);
    }

    [Fact]
    public async Task Callers_without_a_key_are_denied_before_anything_is_looked_up()
    {
        await using var api = await DataApi.StartAsync(postgres);
        await api.CreateTableAsync("tasks");
        var token = await api.AppUserTokenAsync();

        using var anonymous = await api.SendAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows");
        using var anonymousMissing = await api.SendAsync(HttpMethod.Get, "/v1/databases/nope/tables/nothing/rows");
        using var appUser = await api.SendAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { }, bearer: token);
        using var appUserMissing = await api.SendAsync(HttpMethod.Get, "/v1/databases/main/tables/nothing/rows", bearer: token);
        using var badToken = await api.SendAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows", bearer: "nope");
        using var both = await api.SendAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows", key: DataApi.FullKey, bearer: token);
        using var badKey = await api.SendAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows", key: "orv_sk_" + new string('x', 43));
        using var noScope = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows", key: DataApi.UsersKey);
        using var readOnly = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { }, key: DataApi.ReadKey);
        using var readOnlyTable = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables", new { name = "x", columns = Array.Empty<object>() }, key: DataApi.ReadKey);
        using var tablesWithoutKey = await api.SendAsync(HttpMethod.Get, "/v1/databases/main/tables", bearer: token);
        using var readOk = await api.AsServerAsync(HttpMethod.Get, "/v1/databases/main/tables/tasks/rows", key: DataApi.ReadKey);

        Assert.Equal(HttpStatusCode.Forbidden, anonymous.Status);
        Assert.Equal("table_access_denied", anonymous.Code);
        Assert.Equal(anonymous.Body.GetProperty("detail").GetString(), anonymousMissing.Body.GetProperty("detail").GetString());
        Assert.Equal("table_access_denied", anonymousMissing.Code);
        Assert.Equal("table_access_denied", appUser.Code);
        Assert.Equal("table_access_denied", appUserMissing.Code);
        Assert.Equal("invalid_token", badToken.Code);
        Assert.Equal("invalid_request", both.Code);
        Assert.Equal("invalid_api_key", badKey.Code);
        Assert.Equal("insufficient_scope", noScope.Code);
        Assert.Equal("insufficient_scope", readOnly.Code);
        Assert.Equal("insufficient_scope", readOnlyTable.Code);
        Assert.Equal("invalid_api_key", tablesWithoutKey.Code);
        Assert.Equal(HttpStatusCode.OK, readOk.Status);
        Assert.Equal(0L, await api.ScalarAsync<long>($"SELECT count(*) FROM {api.Schema}.tasks"));
    }

    [Fact]
    public async Task Changes_write_events_with_names_and_the_actor_but_no_values()
    {
        await using var api = await DataApi.StartAsync(postgres);
        await api.CreateTableAsync("tasks", new { name = "title", type = "text" }, new { name = "secret", type = "text" });
        using var row = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { title = "visible-title", secret = (string?)null });

        var table = await api.ScalarAsync<string>("SELECT payload::text FROM orvano.events WHERE type = 'databases.table.created'");
        var rowEvent = await api.ScalarAsync<string>("SELECT payload::text FROM orvano.events WHERE type = 'databases.row.created'");

        using var tablePayload = JsonDocument.Parse(table);
        Assert.Equal("main", tablePayload.RootElement.GetProperty("database").GetString());
        Assert.Equal("tasks", tablePayload.RootElement.GetProperty("table").GetString());
        Assert.Equal(["title", "secret"], tablePayload.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal("api_key", tablePayload.RootElement.GetProperty("actor").GetProperty("type").GetString());
        using var rowPayload = JsonDocument.Parse(rowEvent);
        Assert.Equal(row.Body.GetProperty("id").GetString(), rowPayload.RootElement.GetProperty("rowId").GetString());
        Assert.Equal(["id", "created_at", "updated_at", "title"], rowPayload.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        Assert.DoesNotContain("visible-title", rowEvent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_console_reads_as_any_member_and_creates_as_a_developer()
    {
        await using var api = await DataApi.StartAsync(postgres);
        await api.CreateTableAsync("tasks", new { name = "title", type = "text" });
        using var row = await api.AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables/tasks/rows", new { title = "a" });

        using var databases = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/databases", account: DataApi.Viewer);
        using var tables = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/databases/main/tables", account: DataApi.Viewer);
        using var rows = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/databases/main/tables/tasks/rows", account: DataApi.Viewer);
        using var viewerCreate = await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/databases/nope/tables",
            new { name = "notes", columns = Array.Empty<object>() }, account: DataApi.Viewer);
        using var developerCreate = await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/databases/main/tables",
            new { name = "notes", columns = Array.Empty<object>() }, account: DataApi.Developer);
        using var stranger = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/databases/main/tables", account: DataApi.Stranger);

        Assert.Equal("main", databases.Body.GetProperty("items")[0].GetProperty("slug").GetString());
        Assert.Equal("tasks", tables.Body.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(row.Body.ToString(), rows.Body.GetProperty("items")[0].ToString());
        Assert.Equal(HttpStatusCode.Forbidden, viewerCreate.Status);
        Assert.Equal("forbidden", viewerCreate.Code);
        Assert.Equal(HttpStatusCode.Created, developerCreate.Status);
        Assert.Equal("project_not_found", stranger.Code);
        Assert.Equal("user", await api.ScalarAsync<string>(
            "SELECT payload->'actor'->>'type' FROM orvano.events WHERE type = 'databases.table.created' AND payload->>'table' = 'notes'"));
    }

    [Fact]
    public async Task The_reserved_word_list_matches_postgres()
    {
        var reserved = new List<string>();
        await using (var cmd = postgres.Superuser.CreateCommand("SELECT word FROM pg_get_keywords() WHERE catcode IN ('R', 'T')"))
        await using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken)) reserved.Add(reader.GetString(0));
        }

        Assert.Equal(reserved.Order(StringComparer.Ordinal), Orvano.Databases.Domain.Identifiers.ReservedWords.Order(StringComparer.Ordinal));
    }

    private static (string Type, string PgType, bool Required, bool System, bool Writable) Shape(JsonElement column) => (
        column.GetProperty("type").GetString()!,
        column.GetProperty("pgType").GetString()!,
        column.GetProperty("required").GetBoolean(),
        column.GetProperty("system").GetBoolean(),
        column.GetProperty("writable").GetBoolean());

    private static string? Message(Auth.Reply reply, string field) =>
        reply.Body.GetProperty("errors").EnumerateArray().Single(e => e.GetProperty("field").GetString() == field).GetProperty("message").GetString();
}
