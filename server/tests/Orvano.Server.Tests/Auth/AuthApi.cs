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
/// session that owns one active project. Spec 0004's HTTP tests call it as an app, a server, or the console would.
/// </summary>
public sealed class AuthApi : IAsyncDisposable
{
    public const string ConsoleSession = "auth-api-tests";
    public const string Project = "authproject0001";
    public const string OtherProject = "authproject0002";
    public const string ServerKey = "orv_sk_authAuthAuthAuthAuthAuthAuthAuthAuthAuthAut";

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

    /// <summary>Starts the api on a fresh database, with two active fixture projects.</summary>
    public static async Task<AuthApi> StartAsync(PostgresFixture postgres, IReadOnlyDictionary<string, string>? env = null)
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var fixtures = Path.Combine(Path.GetTempPath(), $"orvano-auth-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(fixtures, $"""
            consoleSessions:
              - {ConsoleSession}
            projects:
              - id: {Project}
                name: Auth project
              - id: {OtherProject}
                name: Other auth project
            apiKeys:
              - project: {Project}
                secret: {ServerKey}
                scopes: [users.read, users.write]

            """, Ct);

        var settings = new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["ORVANO_TEST_FIXTURES"] = fixtures,
        };
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
