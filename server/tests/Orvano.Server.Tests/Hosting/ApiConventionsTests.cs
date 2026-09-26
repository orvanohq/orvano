using System.Net;
using System.Text.Json;
using Orvano.Contract;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Hosting;

// Spec 0001, Milestone 2, against the real binary: problem details (AC-6), the console route rule
// (AC-17), and test operations that exist only in the Test environment (AC-18). The Test
// environment also validates every response against the contract, so each 2xx and problem body
// below has passed that check too.
public class ApiConventionsTests(PostgresFixture postgres)
{
    private const string ConsoleSession = "console-session-for-tests";

    [Fact]
    public async Task A_console_route_answers_a_valid_console_session()
    {
        await using var api = await StartApiAsync("Test");
        using var http = api.Http();

        using var response = await http.SendAsync(Console(ConsoleSession), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonAsync(response);
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("X-Orvano-Key", "test-server-key")]
    [InlineData("X-Orvano-Session", "an-app-session")]
    public async Task A_console_route_rejects_an_API_key_or_an_app_session_even_with_a_console_session(string header, string value)
    {
        await using var api = await StartApiAsync("Test");
        using var http = api.Http();
        using var request = Console(ConsoleSession);
        request.Headers.Add(header, value);

        using var response = await http.SendAsync(request, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCode.ConsoleSessionRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-known-session")]
    public async Task A_console_route_rejects_a_missing_or_unknown_console_session(string? cookie)
    {
        await using var api = await StartApiAsync("Test");
        using var http = api.Http();

        using var response = await http.SendAsync(Console(cookie), Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCode.ConsoleSessionRequired);
    }

    [Fact]
    public async Task An_error_is_a_problem_with_a_stable_code_and_the_request_id()
    {
        await using var api = await StartApiAsync("Test");
        using var http = api.Http();

        using var response = await http.PostAsync("/v1/test/conflict", null, Ct);

        using var body = await AssertProblemAsync(response, HttpStatusCode.Conflict, TestErrorCode.TestConflict);
        Assert.Equal("https://orvano.dev/errors/test_conflict", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("Conflict", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            Assert.Single(response.Headers.GetValues("X-Request-Id")),
            body.RootElement.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task A_success_carries_a_request_id_too()
    {
        await using var api = await StartApiAsync("Test");
        using var http = api.Http();

        using var response = await http.GetAsync("/v1/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(response.Headers.GetValues("X-Request-Id")));
    }

    [Fact]
    public async Task A_route_that_does_not_exist_is_a_not_found_problem()
    {
        await using var api = await StartApiAsync("Test");
        using var http = api.Http();

        using var response = await http.GetAsync("/v1/projects", Ct);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, ErrorCode.NotFound);
    }

    [Fact]
    public async Task The_test_list_pages_through_five_items_with_an_opaque_cursor()
    {
        await using var api = await StartApiAsync("Test");
        using var http = api.Http();
        var ids = new List<string>();
        string? cursor = null;

        do
        {
            var url = "/v1/test/items?limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            using var response = await http.GetAsync(url, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var page = await JsonAsync(response);
            ids.AddRange(page.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(["item-1", "item-2", "item-3", "item-4", "item-5"], ids);
    }

    [Theory]
    [InlineData("/v1/test/items?limit=0", "invalid_request")]
    [InlineData("/v1/test/items?limit=101", "invalid_request")]
    [InlineData("/v1/test/items?limit=abc", "invalid_request")]
    [InlineData("/v1/test/items?cursor=not-a-cursor", "invalid_cursor")]
    [InlineData("/v1/test/items?cursor=Ng", "invalid_cursor")] // base64url of "6", past the end
    public async Task The_test_list_rejects_a_bad_limit_or_cursor(string url, string code)
    {
        await using var api = await StartApiAsync("Test");
        using var http = api.Http();

        using var response = await http.GetAsync(url, Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task Test_routes_do_not_exist_outside_the_Test_environment()
    {
        await using var api = await StartApiAsync("Production");
        using var http = api.Http();

        using var conflict = await http.PostAsync("/v1/test/conflict", null, Ct);
        using var list = await http.GetAsync("/v1/test/items", Ct);

        Assert.Equal(HttpStatusCode.NotFound, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    }

    [Fact]
    public async Task No_console_session_is_valid_outside_the_Test_environment()
    {
        await using var api = await StartApiAsync("Production");
        using var http = api.Http();

        using var response = await http.SendAsync(Console(ConsoleSession), Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCode.ConsoleSessionRequired);
    }

    [Fact]
    public async Task Refuses_to_start_with_test_fixtures_outside_the_Test_environment()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var fixtures = await WriteFixturesAsync();

        await using var api = OrvanoProcess.Start(["api"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ORVANO_TEST_FIXTURES"] = fixtures,
        }, listen: true);
        await api.WaitForExitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, api.ExitCode);
        Assert.Contains("it is only allowed in Test", api.Output);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpRequestMessage Console(string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/console/test/ping");
        if (cookie is not null) request.Headers.Add("Cookie", $"orvano_console={cookie}");
        return request;
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

    private static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("requestId").GetString()));
        return body;
    }

    private static async Task<string> WriteFixturesAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orvano-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(path, $"consoleSessions:\n  - {ConsoleSession}\n", Ct);
        return path;
    }

    private async Task<OrvanoProcess> StartApiAsync(string environment)
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var env = new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = environment,
        };
        if (environment == "Test") env["ORVANO_TEST_FIXTURES"] = await WriteFixturesAsync();

        var api = OrvanoProcess.Start(["api"], env, listen: true);
        try
        {
            await api.WaitUntilListeningAsync();
            return api;
        }
        catch
        {
            await api.DisposeAsync();
            throw;
        }
    }
}
