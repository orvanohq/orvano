using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.IntegrationTests.Api;

// Spec 0014 build plan step 11: paging through the real pipeline, on the test only GET /v1/test/rows.
public sealed class PagingTests(WireServerFixture server) : IClassFixture<WireServerFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-1"), Trait("Spec", "0014:PAG-2"), Trait("Spec", "0014:PAG-4")]
    public async Task List_walks_in_pages_of_the_default_size_when_no_limit_is_sent()
    {
        var sizes = new List<int>();
        var ids = new List<string>();
        string? cursor = null;
        JsonElement page;
        do
        {
            page = await PageAsync(cursor is null ? "/v1/test/rows" : $"/v1/test/rows?cursor={cursor}");
            JsonElement[] items = [.. page.GetProperty("items").EnumerateArray()];
            sizes.Add(items.Length);
            ids.AddRange(items.Select(item => item.GetProperty("id").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null && sizes.Count < 10);

        int rows = server.Factory.Services.GetRequiredService<FixtureRows>().Snapshot().Length;
        sizes.Sum().ShouldBe(rows);
        sizes.Take(2).ShouldBe([PageQuery.DefaultLimit, PageQuery.DefaultLimit]);
        ids.Distinct().Count().ShouldBe(rows);
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-3")]
    public async Task List_returns_the_total_when_the_endpoint_counts()
    {
        JsonElement page = await PageAsync("/v1/test/rows?limit=1");

        page.GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(120);
        page.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-4")]
    public async Task List_order_is_stable_when_rows_are_inserted_between_pages()
    {
        FixtureRows rows = server.Factory.Services.GetRequiredService<FixtureRows>();
        JsonElement first = await PageAsync("/v1/test/rows?sort=name&limit=30");
        string lastName = first.GetProperty("items")[29].GetProperty("name").GetString()!;
        string[] expected = [.. rows.Snapshot()
            .OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.Id)
            .Skip(30).Take(30).Select(row => row.Id.ToString())];

        // One row sorts before the cursor (never seen), one sorts after the next page; neither moves the next page.
        rows.Add(new FixtureRow(Guid.CreateVersion7(), "row 00", DateTimeOffset.UnixEpoch));
        rows.Add(new FixtureRow(Guid.CreateVersion7(), "zz last", DateTimeOffset.UnixEpoch));
        JsonElement second = await PageAsync($"/v1/test/rows?sort=name&limit=30&cursor={first.GetProperty("nextCursor").GetString()}");

        string.CompareOrdinal(second.GetProperty("items")[0].GetProperty("name").GetString(), lastName).ShouldBeGreaterThanOrEqualTo(0);
        second.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ShouldBe(expected);
    }

    [Theory, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-5")]
    [InlineData("sort=name")]
    [InlineData("sort=created")]
    [InlineData("sort=name&extra=1")]
    public async Task Cursor_fails_invalid_cursor_when_replayed_with_another_filter_or_sort(string other)
    {
        JsonElement first = await PageAsync("/v1/test/rows?limit=5");

        using HttpResponseMessage response = await GetAsync($"/v1/test/rows?{other}&cursor={first.GetProperty("nextCursor").GetString()}");

        await ShouldBeProblemAsync(response, ErrorCodes.InvalidCursor);
    }

    [Theory, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-5")]
    [InlineData("not-a-cursor!")]
    [InlineData("eyJ2IjoxfQ")] // {"v":1}: base64url JSON without keys or hash
    [InlineData("bm90IGpzb24")] // "not json"
    public async Task Cursor_fails_invalid_cursor_when_it_does_not_parse(string cursor)
    {
        using HttpResponseMessage response = await GetAsync($"/v1/test/rows?cursor={cursor}");

        await ShouldBeProblemAsync(response, ErrorCodes.InvalidCursor);
    }

    // A cursor is not signed and its hash is computable, so a forged one with the right hash but keys of the wrong
    // count or kind must still answer invalid_cursor, not 500 (PAG-5, PRB-8).
    [Theory, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-5"), Trait("Spec", "0014:PRB-8")]
    [InlineData("", "[5]")]
    [InlineData("", "[\"x\"]")]
    [InlineData("", "[\"2026-10-22T09:00:00Z\",\"x\"]")]
    [InlineData("", "[\"not a time\",\"0199f0a2-0000-7000-8000-00000000abcd\"]")]
    [InlineData("", "[\"2026-10-22T09:00:00Z\",5]")]
    [InlineData("", "[\"2026-10-22T09:00:00Z\",\"0199f0a2-0000-7000-8000-00000000abcd\",\"extra\"]")]
    [InlineData("", "[\"2026-10-22T09:00:00Z\",null]")]
    [InlineData("&sort=name", "[5,\"0199f0a2-0000-7000-8000-00000000abcd\"]")]
    [InlineData("&sort=name", "[[\"row 01\"],\"0199f0a2-0000-7000-8000-00000000abcd\"]")]
    public async Task Cursor_fails_invalid_cursor_when_its_hash_matches_but_its_keys_have_the_wrong_shape(string sort, string keys)
    {
        JsonElement first = await PageAsync($"/v1/test/rows?limit=5{sort}");
        JsonObject body = JsonNode.Parse(Base64Url.DecodeFromChars(first.GetProperty("nextCursor").GetString()))!.AsObject();
        body["k"] = JsonNode.Parse(keys);
        string forged = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(body.ToJsonString()));

        using HttpResponseMessage response = await GetAsync($"/v1/test/rows?limit=5{sort}&cursor={forged}");

        await ShouldBeProblemAsync(response, ErrorCodes.InvalidCursor);
    }

    [Theory, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-1")]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Limit_fails_out_of_range_when_outside_1_to_100(int limit)
    {
        using HttpResponseMessage response = await GetAsync($"/v1/test/rows?limit={limit}");

        JsonElement problem = await ShouldBeProblemAsync(response, ErrorCodes.ValidationFailed);
        JsonElement error = problem.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("field").GetString().ShouldBe("limit");
        error.GetProperty("location").GetString().ShouldBe("query");
        error.GetProperty("code").GetString().ShouldBe(FieldCodes.OutOfRange);
    }

    [Fact, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-1")]
    public async Task Limit_answers_that_many_items_when_at_the_maximum()
    {
        JsonElement page = await PageAsync($"/v1/test/rows?limit={PageQuery.MaxLimit}");

        page.GetProperty("items").GetArrayLength().ShouldBe(PageQuery.MaxLimit);
    }

    [Fact, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-2")]
    public async Task List_ends_without_an_empty_page_when_the_rows_fill_the_last_page_exactly()
    {
        int rows = server.Factory.Services.GetRequiredService<FixtureRows>().Snapshot().Length;
        int limit = Enumerable.Range(1, PageQuery.MaxLimit).Last(size => rows % size == 0 && rows / size >= 2);

        var sizes = new List<int>();
        string? cursor = null;
        do
        {
            JsonElement page = await PageAsync($"/v1/test/rows?limit={limit}{(cursor is null ? "" : $"&cursor={cursor}")}");
            sizes.Add(page.GetProperty("items").GetArrayLength());
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null && sizes.Count <= rows);

        sizes.ShouldAllBe(size => size == limit);
        sizes.Count.ShouldBe(rows / limit);
    }

    [Fact, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-5")]
    public async Task Cursor_keeps_working_when_the_limit_changes_between_pages()
    {
        JsonElement first = await PageAsync("/v1/test/rows?sort=created&limit=10");
        JsonElement wide = await PageAsync("/v1/test/rows?sort=created&limit=30");

        JsonElement second = await PageAsync($"/v1/test/rows?sort=created&limit=20&cursor={first.GetProperty("nextCursor").GetString()}");

        second.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString())
            .ShouldBe(wide.GetProperty("items").EnumerateArray().Skip(10).Select(item => item.GetProperty("id").GetString()));
    }

    [Theory, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public async Task Limit_fails_invalid_request_when_it_is_not_an_integer(string limit)
    {
        using HttpResponseMessage response = await GetAsync($"/v1/test/rows?limit={limit}");

        await ShouldBeProblemAsync(response, ErrorCodes.InvalidRequest);
    }

    [Fact, Trait("Spec", "0014:AC-10"), Trait("Spec", "0014:PAG-1")]
    public async Task Cursor_fails_too_long_when_it_is_longer_than_2048_characters()
    {
        using HttpResponseMessage response = await GetAsync($"/v1/test/rows?cursor={new string('A', PageQuery.MaxCursorLength + 1)}");

        JsonElement problem = await ShouldBeProblemAsync(response, ErrorCodes.ValidationFailed);
        JsonElement error = problem.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("field").GetString().ShouldBe("cursor");
        error.GetProperty("location").GetString().ShouldBe("query");
        error.GetProperty("code").GetString().ShouldBe(FieldCodes.TooLong);
    }

    private async Task<JsonElement> PageAsync(string path)
    {
        using HttpResponseMessage response = await GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone();
    }

    private static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        JsonElement problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.Clone();
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem;
    }

    private async Task<HttpResponseMessage> GetAsync(string path)
    {
        using HttpClient client = server.Factory.CreateClient(new() { AllowAutoRedirect = false });
        return await client.GetAsync(new Uri(path, UriKind.Relative), Token);
    }
}
