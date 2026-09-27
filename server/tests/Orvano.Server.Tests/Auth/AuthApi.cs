using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

/// <summary>A JSON reply: the status, the headers, and the parsed body when there is one.</summary>
public sealed record Reply(HttpStatusCode Status, HttpResponseHeaders Headers, JsonDocument? Document) : IDisposable
{
    public JsonElement Body => Document!.RootElement;

    public string? Code => Document?.RootElement.TryGetProperty("code", out var code) == true ? code.GetString() : null;

    public void Dispose() => Document?.Dispose();
}

/// <summary>
/// The real api binary in the Test environment (every response checked against the contract), with a fixture console
/// account that owns the active fixture projects. Spec 0004's HTTP tests call it as an app, a server, or the console would.
/// </summary>
public sealed class AuthApi : IAsyncDisposable
{
    public const string ConsoleUser = "auth-api-tests@x.com";

    /// <summary>More console accounts, with personal orgs only; tests give them roles in the Fixtures org.</summary>
    public static readonly string[] OtherConsoleUsers = ["developer@x.com", "viewer@x.com", "stranger@x.com"];
    public const string Project = "authproject0001";
    public const string OtherProject = "authproject0002";
    public const string ServerKey = "orv_sk_authAuthAuthAuthAuthAuthAuthAuthAuthAuthAut";
    public const string ReadKey = "orv_sk_readReadReadReadReadReadReadReadReadReadRea";
    public const string FixtureUser = "fixture@x.com";
    public const string OtherProjectKey = "orv_sk_otherOtherOtherOtherOtherOtherOtherOtherOth";

    private AuthApi(OrvanoProcess process, TestDatabase database)
    {
        Process = process;
        Database = database;
        Http = process.Http();
    }

    public OrvanoProcess Process { get; }

    public TestDatabase Database { get; }

    public HttpClient Http { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Starts the api on a fresh database, with two active fixture projects owned by a fixture console account, or,
    /// with <paramref name="fixtures"/> false, with no fixtures at all (an install that waits for its first admin).
    /// </summary>
    public static async Task<AuthApi> StartAsync(PostgresFixture postgres, IReadOnlyDictionary<string, string>? env = null, bool fixtures = true)
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var fixturesPath = Path.Combine(Path.GetTempPath(), $"orvano-auth-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(fixturesPath, $"""
            {ConsoleSignIn.Fixtures([ConsoleUser, .. OtherConsoleUsers])}projects:
              - id: {Project}
                name: Auth project
              - id: {OtherProject}
                name: Other auth project
            apiKeys:
              - project: {Project}
                secret: {ServerKey}
                scopes: [users.read, users.write]
              - project: {Project}
                secret: {ReadKey}
                scopes: [users.read]
              - project: {OtherProject}
                secret: {OtherProjectKey}
                scopes: [users.read, users.write]
            platforms:
              - project: {OtherProject}
                type: web
                identifier: localhost
            users:
              - project: {OtherProject}
                email: {FixtureUser}
                password: fixture horse battery
                name: Fixture User

            """, Ct);

        var settings = new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
        };
        if (fixtures) settings["ORVANO_TEST_FIXTURES"] = fixturesPath;
        foreach (var (key, value) in env ?? new Dictionary<string, string>()) settings[key] = value;

        var process = OrvanoProcess.Start(["api"], settings, listen: true);
        try
        {
            await process.WaitUntilListeningAsync();
        }
        catch
        {
            await process.DisposeAsync();
            await database.DisposeAsync();
            throw;
        }

        // No worker runs here, so stand in for its provisioning of the fixture projects.
        await TestDatabase.ExecuteAsync(database.Superuser, "UPDATE orvano.platform_projects SET status = 'active' WHERE kind = 'app'");
        return new AuthApi(process, database);
    }

    public async Task<Reply> SendAsync(
        HttpMethod method, string url, object? body = null, string? project = Project, string? bearer = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (project is not null) request.Headers.Add("X-Orvano-Project", project);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(name, value);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await Http.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return new Reply(response.StatusCode, response.Headers, text.Length > 0 ? JsonDocument.Parse(text) : null);
    }

    /// <summary>Signs a new user up and returns the reply (201 with the user and session when it works).</summary>
    public Task<Reply> SignUpAsync(string email, string password = "correct horse battery", string? project = Project, object? extra = null) =>
        SendAsync(HttpMethod.Post, "/v1/account", extra ?? new { email, password }, project);

    public Task<Reply> SignInAsync(string email, string password = "correct horse battery", string? project = Project) =>
        SendAsync(HttpMethod.Post, "/v1/account/sessions/password", new { email, password }, project);

    /// <summary>Calls a <c>users</c> operation with an API key (the full scope server key by default).</summary>
    public Task<Reply> AsServerAsync(HttpMethod method, string url, object? body = null, string key = ServerKey, string? project = Project) =>
        SendAsync(method, url, body, project, headers: new Dictionary<string, string> { ["X-Orvano-Key"] = key });

    /// <summary>Calls a console operation as a fixture console account (the owner by default), signed in for real.</summary>
    public async Task<Reply> AsConsoleAsync(HttpMethod method, string url, object? body = null, string? project = Project, string account = ConsoleUser) =>
        await SendAsync(method, url, body, project, headers: await ConsoleHeadersAsync(account));

    /// <summary>A fixture console account's cookie and the same origin header a console call carries.</summary>
    public async Task<Dictionary<string, string>> ConsoleHeadersAsync(string account = ConsoleUser) => new()
    {
        ["Cookie"] = $"orvano_console={await ConsoleSignIn.CookieAsync(Http, account, Ct)}",
        ["Sec-Fetch-Site"] = "same-origin",
    };

    /// <summary>Gives a fixture console account a role in the org of the fixture projects.</summary>
    public Task GrantAsync(string account, string role) =>
        TestDatabase.ExecuteAsync(Database.Superuser, """
            INSERT INTO orvano.platform_memberships (org_id, user_id, role)
            SELECT p.org_id, u.id, @role
            FROM orvano.platform_projects p, orvano.auth_users u
            WHERE p.id = @project AND u.project_id = 'console' AND lower(u.email) = @email
            """, ("role", role), ("project", Project), ("email", account));

    public static string AccessToken(Reply signedIn) => signedIn.Body.GetProperty("session").GetProperty("accessToken").GetString()!;

    public static string RefreshToken(Reply signedIn) => signedIn.Body.GetProperty("session").GetProperty("refreshToken").GetString()!;

    public static string UserId(Reply signedIn) => signedIn.Body.GetProperty("user").GetProperty("id").GetString()!;

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await Process.DisposeAsync();
        await Database.DisposeAsync();
    }
}
