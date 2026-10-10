using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Orvano.Platform.Jobs;
using Orvano.Server.Tests.Auth;
using Orvano.Server.Tests.Infrastructure;
using Orvano.Server.Tests.Platform;

namespace Orvano.Server.Tests.Databases;

/// <summary>
/// The real api binary in the Test environment (every response checked against the contract) with one fixture
/// project provisioned for real by the Platform job, so its role <c>p_&lt;id&gt;</c> and schema exist. Roles are
/// cluster wide, so every harness gets its own project ID. Spec 0015's HTTP tests call it as a server, an app, and
/// the console would.
/// </summary>
public sealed class DataApi : IAsyncDisposable
{
    public const string Owner = "data-owner@x.com";
    public const string Developer = "data-developer@x.com";
    public const string Viewer = "data-viewer@x.com";
    public const string Stranger = "data-stranger@x.com";
    public const string AppUser = "data-user@x.com";
    public const string AppUserPassword = "fixture horse battery";

    /// <summary>Every data scope.</summary>
    public static readonly string FullKey = "orv_sk_" + new string('f', 43);

    /// <summary><c>tables.read</c> and <c>rows.read</c> only.</summary>
    public static readonly string ReadKey = "orv_sk_" + new string('r', 43);

    /// <summary><c>users.read</c> only.</summary>
    public static readonly string UsersKey = "orv_sk_" + new string('u', 43);

    private DataApi(OrvanoProcess process, TestDatabase database, string project)
    {
        Process = process;
        Database = database;
        Project = project;
        Http = process.Http();
    }

    public OrvanoProcess Process { get; }

    public TestDatabase Database { get; }

    /// <summary>The fixture project's ID; its main schema is <see cref="Schema"/>.</summary>
    public string Project { get; }

    public string Schema => "p_" + Project;

    public HttpClient Http { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<DataApi> StartAsync(PostgresFixture postgres)
    {
        var project = "data" + Guid.NewGuid().ToString("N")[..12];
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var fixturesPath = Path.Combine(Path.GetTempPath(), $"orvano-data-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(fixturesPath, $"""
            {ConsoleSignIn.Fixtures(Owner, Developer, Viewer, Stranger)}projects:
              - id: {project}
                name: Data project
            apiKeys:
              - project: {project}
                secret: {FullKey}
                scopes: [tables.read, tables.write, rows.read, rows.write]
              - project: {project}
                secret: {ReadKey}
                scopes: [tables.read, rows.read]
              - project: {project}
                secret: {UsersKey}
                scopes: [users.read]
            platforms:
              - project: {project}
                type: web
                identifier: localhost
            users:
              - project: {project}
                email: {AppUser}
                password: {AppUserPassword}

            """, Ct);

        var process = OrvanoProcess.Start(["api"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["ORVANO_TEST_FIXTURES"] = fixturesPath,
        }, listen: true);
        try
        {
            await process.WaitUntilListeningAsync();
            // The worker's job, run here: the project's role and schema exist for real.
            await using var platform = PlatformHarness.Over(database);
            await platform.RunJobsAsync(PlatformJobs.ProvisionProject);
        }
        catch
        {
            await process.DisposeAsync();
            await database.DisposeAsync();
            throw;
        }

        var api = new DataApi(process, database, project);
        await api.GrantAsync(Developer, "developer");
        await api.GrantAsync(Viewer, "viewer");
        return api;
    }

    /// <summary>Sends a request with the project header; <paramref name="body"/> is JSON, or raw text when it is a string.</summary>
    public async Task<Reply> SendAsync(
        HttpMethod method, string url, object? body = null, string? key = null, string? bearer = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Orvano-Project", Project);
        if (key is not null) request.Headers.Add("X-Orvano-Key", key);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(name, value);
        request.Content = body switch
        {
            null => null,
            string raw => new StringContent(raw, Encoding.UTF8, "application/json"),
            _ => JsonContent.Create(body),
        };
        using var response = await Http.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return new Reply(response.StatusCode, response.Headers, text.Length > 0 ? JsonDocument.Parse(text) : null);
    }

    /// <summary>Calls a public operation with an API key (every data scope by default).</summary>
    public Task<Reply> AsServerAsync(HttpMethod method, string url, object? body = null, string? key = null) =>
        SendAsync(method, url, body, key: key ?? FullKey);

    /// <summary>Calls a console operation as a fixture console account (the owner by default), signed in for real.</summary>
    public async Task<Reply> AsConsoleAsync(HttpMethod method, string url, object? body = null, string account = Owner) =>
        await SendAsync(method, url, body, headers: new Dictionary<string, string>
        {
            ["Cookie"] = $"orvano_console={await ConsoleSignIn.CookieAsync(Http, account, Ct)}",
            ["Sec-Fetch-Site"] = "same-origin",
        });

    /// <summary>The fixture app user's access token.</summary>
    public async Task<string> AppUserTokenAsync()
    {
        using var signedIn = await SendAsync(HttpMethod.Post, "/v1/account/sessions/password", new { email = AppUser, password = AppUserPassword });
        return AuthApi.AccessToken(signedIn);
    }

    /// <summary>Creates a table through the API and fails the test unless it answers 201.</summary>
    public async Task CreateTableAsync(string name, params object[] columns)
    {
        using var created = await AsServerAsync(HttpMethod.Post, "/v1/databases/main/tables", new { name, columns });
        Assert.True(created.Status == System.Net.HttpStatusCode.Created, created.Document?.RootElement.ToString());
    }

    /// <summary>Runs SQL in the project's main schema as the project role, as a developer in <c>psql</c> would.</summary>
    public Task<int> ExecuteAsProjectAsync(string sql) =>
        TestDatabase.ExecuteAsync(Database.Superuser, $"SET ROLE \"{Schema}\"; SET search_path = \"{Schema}\"; {sql}; RESET ROLE; RESET search_path;");

    public Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
        TestDatabase.ScalarAsync<T>(Database.Superuser, sql, parameters);

    /// <summary>Gives a fixture console account a role in the org of the fixture project.</summary>
    public Task GrantAsync(string account, string role) =>
        TestDatabase.ExecuteAsync(Database.Superuser, """
            INSERT INTO orvano.platform_memberships (org_id, user_id, role)
            SELECT p.org_id, u.id, @role
            FROM orvano.platform_projects p, orvano.auth_users u
            WHERE p.id = @project AND u.project_id = 'console' AND lower(u.email) = @email
            """, ("role", role), ("project", Project), ("email", account));

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await Process.DisposeAsync();
        await Database.DisposeAsync();
    }
}
