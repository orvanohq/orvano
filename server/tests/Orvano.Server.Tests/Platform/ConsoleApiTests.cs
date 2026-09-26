using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orvano.Contract;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Platform;

// Spec 0003, row 7, over HTTP against the real binary in the Test environment, where every response is also
// checked against the contract: the console path from an org to a scoped key and a web and a Flutter platform.
public class ConsoleApiTests(PostgresFixture postgres)
{
    private const string Session = "console-api-tests";
    private const string OtherSession = "console-api-tests-other";
    private const string FixtureProject = "fixtureproject0001";
    private const string FixtureSecret = "orv_sk_fixtureFixtureFixtureFixtureFixtureFixture0";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_console_user_creates_an_org_a_project_a_scoped_key_and_a_web_and_a_Flutter_platform()
    {
        await using var api = await StartApiAsync();
        using var http = api.Http();

        using var org = await SendAsync(http, HttpMethod.Post, "/v1/console/orgs", new { name = "Acme" });
        Assert.Equal(HttpStatusCode.Created, org.Status);
        Assert.Equal("owner", org.Body.GetProperty("role").GetString());
        var orgId = org.Body.GetProperty("id").GetString()!;

        using var project = await SendAsync(http, HttpMethod.Post, $"/v1/console/orgs/{orgId}/projects", new { name = "Shop" });
        Assert.Equal(HttpStatusCode.Created, project.Status);
        Assert.Equal("provisioning", project.Body.GetProperty("status").GetString());
        var projectId = project.Body.GetProperty("id").GetString()!;

        using var key = await SendAsync(http, HttpMethod.Post, "/v1/console/project/keys", new { name = "Backend", scopes = new[] { "users.read" } }, projectId);
        Assert.Equal(HttpStatusCode.Created, key.Status);
        var secret = key.Body.GetProperty("secret").GetString()!;
        Assert.StartsWith("orv_sk_", secret);

        using var web = await SendAsync(http, HttpMethod.Post, "/v1/console/project/platforms", new { type = "web", name = "Site", identifier = "app.example.com" }, projectId);
        using var android = await SendAsync(http, HttpMethod.Post, "/v1/console/project/platforms", new { type = "android", name = "Flutter", identifier = "com.example.app" }, projectId);
        using var ios = await SendAsync(http, HttpMethod.Post, "/v1/console/project/platforms", new { type = "ios", name = "Flutter", identifier = "com.example.app" }, projectId);
        Assert.Equal(HttpStatusCode.Created, web.Status);
        Assert.Equal(HttpStatusCode.Created, android.Status);
        Assert.Equal(HttpStatusCode.Created, ios.Status);

        using var keys = await SendAsync(http, HttpMethod.Get, "/v1/console/project/keys", null, projectId);
        var listed = Assert.Single(keys.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(secret[..12], listed.GetProperty("prefix").GetString());
        Assert.DoesNotContain(secret, keys.Body.GetRawText());

        using var platforms = await SendAsync(http, HttpMethod.Get, "/v1/console/project/platforms", null, projectId);
        Assert.Equal(3, platforms.Body.GetProperty("items").GetArrayLength());

        using var deleted = await SendAsync(http, HttpMethod.Delete, $"/v1/console/project/keys/{listed.GetProperty("id").GetString()}", null, projectId);
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
    }

    [Fact]
    public async Task Console_errors_are_problems_with_stable_codes()
    {
        await using var api = await StartApiAsync();
        using var http = api.Http();
        using var org = await SendAsync(http, HttpMethod.Post, "/v1/console/orgs", new { name = "Acme" });
        using var project = await SendAsync(http, HttpMethod.Post, $"/v1/console/orgs/{org.Body.GetProperty("id").GetString()}/projects", new { name = "Shop" });
        var projectId = project.Body.GetProperty("id").GetString()!;

        await AssertProblemAsync(http, HttpMethod.Get, "/v1/console/project/keys", null, null, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);
        await AssertProblemAsync(http, HttpMethod.Post, "/v1/console/project/keys", new { name = "K", scopes = new[] { "nope.read" } }, projectId, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);
        await AssertProblemAsync(http, HttpMethod.Post, "/v1/console/project/platforms", new { type = "web", name = "Any", identifier = "*" }, projectId, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);
        await AssertProblemAsync(http, HttpMethod.Get, "/v1/console/project", null, projectId, HttpStatusCode.NotFound, ErrorCode.ProjectNotFound, OtherSession);
        await AssertProblemAsync(http, HttpMethod.Get, "/v1/console/project", null, "console", HttpStatusCode.NotFound, ErrorCode.ProjectNotFound);
        await AssertProblemAsync(http, HttpMethod.Post, "/v1/console/project/restore", null, projectId, HttpStatusCode.Conflict, ErrorCode.ProjectNotReady);
        await AssertProblemAsync(http, HttpMethod.Get, "/v1/console/orgs?cursor=bogus", null, null, HttpStatusCode.BadRequest, ErrorCode.InvalidCursor);
        await AssertProblemAsync(http, HttpMethod.Get, "/v1/console/install/settings", null, null, HttpStatusCode.Forbidden, ErrorCode.Forbidden, OtherSession);
    }

    [Fact]
    public async Task Fixture_projects_and_keys_are_seeded_once_and_the_first_session_owns_them()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var fixtures = await WriteFixturesAsync();

        for (var run = 0; run < 2; run++)
        {
            await using var api = await StartApiAsync(database, fixtures);
            using var http = api.Http();
            using var project = await SendAsync(http, HttpMethod.Get, "/v1/console/project", null, FixtureProject);
            Assert.Equal(HttpStatusCode.OK, project.Status);
            Assert.Equal("Fixture project", project.Body.GetProperty("name").GetString());
        }

        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(database.Superuser, "SELECT count(*) FROM orvano.platform_projects WHERE id = @p", ("p", FixtureProject)));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(database.Superuser, "SELECT count(*) FROM orvano.platform_api_keys WHERE prefix = @p", ("p", FixtureSecret[..12])));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(database.Superuser, "SELECT count(*) FROM orvano.platform_install_admins"));
    }

    [Fact]
    public async Task A_bad_grace_setting_stops_the_role_from_starting()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await using var api = OrvanoProcess.Start(["api"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ORVANO_DELETE_GRACE_DAYS"] = "91",
        }, listen: true);
        await api.WaitForExitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, api.ExitCode);
        Assert.Contains("ORVANO_DELETE_GRACE_DAYS must be a whole number from 0 to 90", api.Output);
    }

    private sealed record Reply(HttpStatusCode Status, JsonDocument? Document) : IDisposable
    {
        public JsonElement Body => Document!.RootElement;

        public void Dispose() => Document?.Dispose();
    }

    private static async Task<Reply> SendAsync(HttpClient http, HttpMethod method, string url, object? body, string? project = null, string session = Session)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", $"orvano_console={session}");
        if (project is not null) request.Headers.Add("X-Orvano-Project", project);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return new Reply(response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text) : null);
    }

    private static async Task AssertProblemAsync(
        HttpClient http, HttpMethod method, string url, object? body, string? project, HttpStatusCode status, string code, string session = Session)
    {
        using var reply = await SendAsync(http, method, url, body, project, session);
        Assert.Equal(status, reply.Status);
        Assert.Equal(code, reply.Body.GetProperty("code").GetString());
    }

    private static async Task<string> WriteFixturesAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orvano-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(path, $"""
            consoleSessions:
              - {Session}
              - {OtherSession}
            projects:
              - id: {FixtureProject}
                name: Fixture project
            apiKeys:
              - project: {FixtureProject}
                secret: {FixtureSecret}
                scopes: [users.read, users.write]

            """, Ct);
        return path;
    }

    private async Task<OrvanoProcess> StartApiAsync()
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        return await StartApiAsync(database, await WriteFixturesAsync());
    }

    private static async Task<OrvanoProcess> StartApiAsync(TestDatabase database, string fixtures)
    {
        var api = OrvanoProcess.Start(["api"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["ORVANO_TEST_FIXTURES"] = fixtures,
        }, listen: true);
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
