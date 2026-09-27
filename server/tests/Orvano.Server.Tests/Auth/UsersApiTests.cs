using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0004 build task 6 over HTTP against the real binary: a server manages users with an API key (AC-17, AC-18),
// the key and scope rules (spec 0003 AC-5, AC-12), and the browser origin check and CORS (spec 0003 AC-13).
public class UsersApiTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_server_creates_lists_and_gets_users_newest_first_with_filters()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var ada = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "ada@x.com", password = "correct horse battery", name = "Ada" });
        using var adam = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "Adam@x.com", password = "correct horse battery" });
        using var ab = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "ab@x.com", password = "correct horse battery" });
        using var bob = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "bob@x.com", password = "correct horse battery" });
        using var elsewhere = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "ada2@x.com", password = "correct horse battery" },
            key: AuthApi.OtherProjectKey, project: AuthApi.OtherProject);

        using var all = await api.AsServerAsync(HttpMethod.Get, "/v1/users");
        using var firstPage = await api.AsServerAsync(HttpMethod.Get, "/v1/users?limit=3");
        using var secondPage = await api.AsServerAsync(HttpMethod.Get,
            $"/v1/users?limit=3&cursor={Uri.EscapeDataString(firstPage.Body.GetProperty("nextCursor").GetString()!)}");
        using var prefix = await api.AsServerAsync(HttpMethod.Get, "/v1/users?email=ADA");
        using var underscore = await api.AsServerAsync(HttpMethod.Get, "/v1/users?email=a_");
        using var blocked = await api.AsServerAsync(HttpMethod.Get, "/v1/users?status=blocked");
        using var badStatus = await api.AsServerAsync(HttpMethod.Get, "/v1/users?status=gone");
        var cutoff = Uri.EscapeDataString(adam.Body.GetProperty("createdAt").GetDateTimeOffset().ToString("O"));
        using var after = await api.AsServerAsync(HttpMethod.Get, $"/v1/users?createdAfter={cutoff}");
        using var before = await api.AsServerAsync(HttpMethod.Get, $"/v1/users?createdBefore={cutoff}");
        using var one = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{Id(ada)}");
        using var unknown = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{Guid.CreateVersion7()}");
        using var otherProjects = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{Id(elsewhere)}");
        using var notAnId = await api.AsServerAsync(HttpMethod.Get, "/v1/users/nope");

        Assert.Equal(HttpStatusCode.Created, ada.Status);
        Assert.Equal("Ada", ada.Body.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, ada.Body.GetProperty("lastSignInAt").ValueKind);
        Assert.Equal([Id(bob), Id(ab), Id(adam), Id(ada)], Ids(all));
        Assert.Equal([Id(bob), Id(ab), Id(adam), Id(ada)], Ids(firstPage).Concat(Ids(secondPage)));
        Assert.Equal([Id(adam), Id(ada)], Ids(prefix));
        Assert.Empty(Ids(underscore)); // _ is a character, not a wildcard
        Assert.Empty(Ids(blocked));
        Assert.Equal("invalid_request", badStatus.Code);
        Assert.Equal([Id(bob), Id(ab)], Ids(after));
        Assert.Equal([Id(ada)], Ids(before));
        Assert.Equal("ada@x.com", one.Body.GetProperty("email").GetString());
        Assert.Equal("user_not_found", unknown.Code);
        Assert.Equal("user_not_found", otherProjects.Code);
        Assert.Equal("user_not_found", notAnId.Code);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_sessions"));
        Assert.Equal(4L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.created' AND project_id = @p AND payload->'actor'->>'type' = 'apiKey'", ("p", AuthApi.Project)));
    }

    [Fact]
    public async Task Create_follows_the_sign_up_rules()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");

        using var taken = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "ADA@x.com", password = "correct horse battery" });
        using var weak = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "bo@x.com", password = "short" });
        using var badEmail = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "nope", password = "correct horse battery" });

        Assert.Equal("user_already_exists", taken.Code);
        Assert.Equal("invalid_password", weak.Code);
        Assert.Equal("invalid_request", badEmail.Code);
    }

    [Fact]
    public async Task A_key_must_be_of_the_project_and_hold_the_scope()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");

        using var none = await api.SendAsync(HttpMethod.Get, "/v1/users");
        using var bearer = await api.SendAsync(HttpMethod.Get, "/v1/users", bearer: AuthApi.AccessToken(signUp));
        using var wrongProject = await api.AsServerAsync(HttpMethod.Get, "/v1/users", key: AuthApi.OtherProjectKey);
        using var madeUp = await api.AsServerAsync(HttpMethod.Get, "/v1/users", key: "orv_sk_" + new string('Z', 43));
        using var readOnlyReads = await api.AsServerAsync(HttpMethod.Get, "/v1/users", key: AuthApi.ReadKey);
        using var readOnlyWrites = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{AuthApi.UserId(signUp)}/block", key: AuthApi.ReadKey);
        using var console = await api.AsServerAsync(HttpMethod.Get, "/v1/users", project: "console");

        foreach (var refused in new[] { none, bearer, wrongProject, madeUp }) Assert.Equal("invalid_api_key", refused.Code);
        Assert.Equal(HttpStatusCode.OK, readOnlyReads.Status);
        Assert.Equal(HttpStatusCode.Forbidden, readOnlyWrites.Status);
        Assert.Equal("insufficient_scope", readOnlyWrites.Code);
        Assert.Equal("project_not_found", console.Code);
    }

    [Fact]
    public async Task Blocking_ends_every_session_and_refuses_sign_in_and_unblocking_lets_them_back()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var laptop = await api.SignUpAsync("ada@x.com");
        using var phone = await api.SignInAsync("ada@x.com");
        using var before = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(phone));
        var user = AuthApi.UserId(laptop);

        using var block = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{user}/block");
        using var blockAgain = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{user}/block");
        using var after = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(phone));
        using var refresh = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(laptop) });
        using var rightPassword = await api.SignInAsync("ada@x.com");
        using var wrongPassword = await api.SignInAsync("ada@x.com", "wrong horse battery");
        using var unblock = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{user}/unblock");
        using var unblockAgain = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{user}/unblock");
        using var signIn = await api.SignInAsync("ada@x.com");
        using var oldSession = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(phone));
        using var unknown = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{Guid.CreateVersion7()}/block");

        Assert.Equal(HttpStatusCode.OK, before.Status);
        Assert.Equal("blocked", block.Body.GetProperty("status").GetString());
        Assert.Equal("blocked", blockAgain.Body.GetProperty("status").GetString());
        Assert.Equal("invalid_token", after.Code);
        Assert.Equal("invalid_refresh_token", refresh.Code);
        Assert.Equal("user_blocked", rightPassword.Code);
        Assert.Equal("invalid_credentials", wrongPassword.Code);
        Assert.Equal("active", unblock.Body.GetProperty("status").GetString());
        Assert.Equal("active", unblockAgain.Body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        Assert.Equal("invalid_token", oldSession.Code); // old sessions stay ended
        Assert.Equal("user_not_found", unknown.Code);
        Assert.Equal(2L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_sessions WHERE end_reason = 'user_blocked'"));
        Assert.Equal(["auth.user.blocked", "auth.user.unblocked"], await TestDatabase.ScalarAsync<string[]>(api.Database.Superuser,
            "SELECT array_agg(type ORDER BY id) FROM orvano.events WHERE type IN ('auth.user.blocked', 'auth.user.unblocked') AND payload->'actor'->>'type' = 'apiKey'"));
    }

    [Fact]
    public async Task A_server_deletes_a_user_and_their_tokens_stop()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        using var before = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(signUp));
        var user = AuthApi.UserId(signUp);

        using var deleted = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{user}");
        using var again = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{user}");
        using var after = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(signUp));

        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal("user_not_found", again.Code);
        Assert.Equal("invalid_token", after.Code);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_users WHERE project_id = @p", ("p", AuthApi.Project)));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.deleted' AND payload->'actor'->>'type' = 'apiKey'"));
    }

    [Fact]
    public async Task A_server_lists_and_ends_a_users_sessions()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var laptop = await api.SignUpAsync("ada@x.com");
        using var phone = await api.SignInAsync("ada@x.com");
        using var tablet = await api.SignInAsync("ada@x.com");
        using var grace = await api.SignUpAsync("grace@x.com");
        var user = AuthApi.UserId(laptop);

        using var listed = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{user}/sessions");
        using var endOne = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{user}/sessions/{Sid(phone)}");
        using var othersSession = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{user}/sessions/{Sid(grace)}");
        using var afterOne = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{user}/sessions");
        using var endAll = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{user}/sessions");
        using var afterAll = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{user}/sessions");
        using var unknown = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{Guid.CreateVersion7()}/sessions");
        using var graceStill = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(grace));

        Assert.Equal([Sid(tablet), Sid(phone), Sid(laptop)], listed.Body.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.All(listed.Body.GetProperty("items").EnumerateArray(), s => Assert.False(s.GetProperty("current").GetBoolean()));
        Assert.Equal(HttpStatusCode.NoContent, endOne.Status);
        Assert.Equal("session_not_found", othersSession.Code);
        Assert.Equal(2, afterOne.Body.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, endAll.Status);
        Assert.Equal(0, afterAll.Body.GetProperty("items").GetArrayLength());
        Assert.Equal("user_not_found", unknown.Code);
        Assert.Equal(HttpStatusCode.OK, graceStill.Status);
    }

    [Fact]
    public async Task A_browser_origin_must_be_a_web_platform_of_the_project()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var platform = await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/platforms", new { type = "web", name = "Site", identifier = "*.example.com" });

        using var allowed = await SignUpFrom(api, "https://app.example.com", "ada@x.com");
        using var refused = await SignUpFrom(api, "https://evil.test", "eve@x.com");
        using var bare = await SignUpFrom(api, "https://example.com", "bare@x.com");
        using var nullOrigin = await SignUpFrom(api, "null", "null@x.com");
        using var server = await api.SignUpAsync("server@x.com"); // no Origin: not checked
        using var otherProject = await SignUpFrom(api, "https://app.example.com", "ada@x.com", AuthApi.OtherProject);

        Assert.Equal(HttpStatusCode.Created, platform.Status);
        Assert.Equal(HttpStatusCode.Created, allowed.Status);
        Assert.Equal("https://app.example.com", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("X-Request-Id", allowed.Headers.GetValues("Access-Control-Expose-Headers").Single(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Equal("origin_not_allowed", refused.Code);
        Assert.Equal("https://evil.test", refused.Headers.GetValues("Access-Control-Allow-Origin").Single()); // so the SDK reads the refusal
        Assert.Equal("origin_not_allowed", bare.Code);
        Assert.Equal("origin_not_allowed", nullOrigin.Code);
        Assert.Equal(HttpStatusCode.Created, server.Status);
        Assert.Equal("origin_not_allowed", otherProject.Code);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_users WHERE project_id = @p AND email <> ALL (ARRAY['ada@x.com', 'server@x.com'])", ("p", AuthApi.Project)));
    }

    [Fact]
    public async Task A_preflight_answers_with_the_requests_origin_and_console_routes_get_no_cors()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/v1/account");
        preflight.Headers.Add("Origin", "https://app.example.com");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type, x-orvano-project, authorization");
        using var answer = await api.Http.SendAsync(preflight, TestContext.Current.CancellationToken);
        var consoleHeaders = await api.ConsoleHeadersAsync();
        consoleHeaders["Origin"] = "https://app.example.com";
        using var console = await api.SendAsync(HttpMethod.Get, "/v1/console/orgs", project: null, headers: consoleHeaders);

        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);
        Assert.Equal("https://app.example.com", answer.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("PATCH", answer.Headers.GetValues("Access-Control-Allow-Methods").Single(), StringComparison.Ordinal);
        Assert.Equal("content-type, x-orvano-project, authorization", answer.Headers.GetValues("Access-Control-Allow-Headers").Single());
        Assert.Equal("600", answer.Headers.GetValues("Access-Control-Max-Age").Single());
        Assert.False(console.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static Task<Reply> SignUpFrom(AuthApi api, string origin, string email, string project = AuthApi.Project) =>
        api.SendAsync(HttpMethod.Post, "/v1/account", new { email, password = "correct horse battery" }, project,
            headers: new Dictionary<string, string> { ["Origin"] = origin });

    private static string Id(Reply user) => user.Body.GetProperty("id").GetString()!;

    private static string Sid(Reply signedIn) => signedIn.Body.GetProperty("session").GetProperty("sessionId").GetString()!;

    private static IEnumerable<string?> Ids(Reply page) => page.Body.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetString());
}

// The Test fixtures' users (with passwords) and platforms, seeded at api start (spec 0004, value sourcing).
public class AuthFixturesTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Fixture_users_sign_in_and_fixture_web_platforms_allow_their_origin()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var signIn = await api.SignInAsync(AuthApi.FixtureUser, "fixture horse battery", AuthApi.OtherProject);
        using var fromLocalhost = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = "local@x.com", password = "correct horse battery" },
            AuthApi.OtherProject, headers: new Dictionary<string, string> { ["Origin"] = "http://localhost:5173" });
        using var fromElsewhere = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = "else@x.com", password = "correct horse battery" },
            AuthApi.OtherProject, headers: new Dictionary<string, string> { ["Origin"] = "https://elsewhere.test" });

        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        Assert.Equal("Fixture User", signIn.Body.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Created, fromLocalhost.Status);
        Assert.Equal("origin_not_allowed", fromElsewhere.Code);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.created' AND payload->'actor'->>'type' = 'system'"));
    }

    [Theory]
    [InlineData("users:\n  - { project: nope, email: a@x.com, password: long enough pw }", "users project 'nope'")]
    [InlineData("users:\n  - { project: authproject0001, email: nope, password: long enough pw }", "is not an email address")]
    [InlineData("users:\n  - { project: authproject0001, email: a@x.com, password: short }", "8 to 256 characters")]
    [InlineData("platforms:\n  - { project: authproject0001, type: fridge, identifier: x }", "platform type 'fridge'")]
    [InlineData("platforms:\n  - { project: authproject0001, type: web, identifier: '*' }", "platform '*'")]
    public void Bad_fixture_users_or_platforms_keep_the_api_from_starting(string entry, string problem)
    {
        var path = Path.Combine(Path.GetTempPath(), $"orvano-bad-fixtures-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, $"""
            consoleUsers: [{"{"} email: owner@x.com, password: owner horse battery {"}"}]
            projects:
              - {"{"} id: authproject0001, name: Auth {"}"}
            {entry}

            """);
        var environment = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Test" };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ORVANO_TEST_FIXTURES"] = path })
            .Build();

        var fixtures = Orvano.Server.Hosting.TestFixtures.Load(environment, config);

        Assert.Contains(problem, fixtures.Problem, StringComparison.Ordinal);
    }
}
