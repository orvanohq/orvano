using System.Buffers.Text;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Http;

using Orvano.SharedKernel.Api;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Api;

// Spec 0014 PAG-2, PAG-4, PAG-5: the cursor format and building a page from a keyset query.
public sealed class PagingTypesTests
{
    private const string Query = "0123456789abcdef";

    [Fact, Trait("Spec", "0014:PAG-5")]
    public void Cursor_round_trips_its_keys_when_read_for_the_same_query()
    {
        string cursor = CursorCodec.Encode(["2026-10-22T09:00:00Z", "0199f0a2-0000-7000-8000-00000000abcd"], Query);

        Result<JsonArray> keys = CursorCodec.Decode(cursor, Query);

        keys.Error.ShouldBeNull();
        keys.Value!.ToJsonString().ShouldBe("""["2026-10-22T09:00:00Z","0199f0a2-0000-7000-8000-00000000abcd"]""");
    }

    [Fact, Trait("Spec", "0014:PAG-5")]
    public void Cursor_is_base64url_json_with_version_keys_and_hash()
    {
        string cursor = CursorCodec.Encode([7], Query);

        cursor.ShouldNotContain("=");
        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).ShouldBe("""{"v":1,"k":[7],"q":"0123456789abcdef"}""");
    }

    [Theory, Trait("Spec", "0014:PAG-5")]
    [InlineData("""{"v":1,"k":[7],"q":"fedcba9876543210"}""")] // another filter or sort
    [InlineData("""{"v":2,"k":[7],"q":"0123456789abcdef"}""")] // another version
    [InlineData("""{"v":1,"k":[],"q":"0123456789abcdef"}""")] // no keys
    [InlineData("""{"v":1,"q":"0123456789abcdef"}""")]
    [InlineData("""[1]""")]
    [InlineData("""not json""")]
    public void Cursor_fails_invalid_cursor_when_it_does_not_match(string json)
    {
        string cursor = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

        CursorCodec.Decode(cursor, Query).Error!.Code.ShouldBe(ErrorCodes.InvalidCursor);
    }

    [Theory, Trait("Spec", "0014:PAG-5")]
    [InlineData("")]
    [InlineData("not base64!")]
    public void Cursor_fails_invalid_cursor_when_it_is_not_base64url(string cursor) =>
        CursorCodec.Decode(cursor, Query).Error!.Code.ShouldBe(ErrorCodes.InvalidCursor);

    [Fact, Trait("Spec", "0014:PAG-5")]
    public void Cursor_fails_invalid_cursor_when_longer_than_the_limit() =>
        CursorCodec.Decode(new string('a', PageQuery.MaxCursorLength + 1), Query).Error!.Code.ShouldBe(ErrorCodes.InvalidCursor);

    [Fact, Trait("Spec", "0014:PAG-5")]
    public void Query_hash_ignores_limit_cursor_and_parameter_order()
    {
        string first = CursorCodec.Query(Request("?sort=name&filter=a&limit=5&cursor=x", ("projectId", "p1")));
        string second = CursorCodec.Query(Request("?filter=a&sort=name", ("projectId", "p1")));

        first.ShouldBe(second);
        first.Length.ShouldBe(16);
        first.ShouldMatch("^[0-9a-f]{16}$");
    }

    [Theory, Trait("Spec", "0014:PAG-5")]
    [InlineData("?sort=created&filter=a", "p1")]
    [InlineData("?sort=name", "p1")]
    [InlineData("?sort=name&filter=a", "p2")]
    public void Query_hash_changes_when_a_filter_sort_or_route_value_changes(string query, string project)
    {
        string baseline = CursorCodec.Query(Request("?sort=name&filter=a", ("projectId", "p1")));

        CursorCodec.Query(Request(query, ("projectId", project))).ShouldNotBe(baseline);
    }

    [Fact, Trait("Spec", "0014:PAG-2"), Trait("Spec", "0014:PAG-4")]
    public void Page_has_a_next_cursor_from_the_last_row_when_the_query_fetched_an_extra_row()
    {
        var page = Page.From([1, 2, 3], limit: 2, Query, row => [row], row => $"row {row}");

        page.Items.ShouldBe(["row 1", "row 2"]);
        CursorCodec.Decode(page.NextCursor!, Query).Value!.ToJsonString().ShouldBe("[2]");
        page.Total.ShouldBeNull();
    }

    [Fact, Trait("Spec", "0014:PAG-2")]
    public void Page_has_no_next_cursor_on_the_last_page()
    {
        var page = Page.From([1, 2], limit: 2, Query, row => [row], row => row, total: 2);

        page.Items.ShouldBe([1, 2]);
        page.NextCursor.ShouldBeNull();
        page.Total.ShouldBe(2);
    }

    private static HttpRequest Request(string query, params (string Name, string Value)[] route)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        foreach ((string name, string value) in route)
        {
            context.Request.RouteValues[name] = value;
        }

        return context.Request;
    }
}
