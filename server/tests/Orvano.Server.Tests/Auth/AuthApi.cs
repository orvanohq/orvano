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
    /// <remarks>
    /// <paramref name="email"/> (spec 0010) adds platforms to <see cref="Project"/> (web <c>localhost</c> and
    /// <c>app.example.com</c>, iOS <c>com.acme.app</c>), and <paramref name="smtp"/> an install SMTP server, so auth
    /// emails queue. No worker runs, so they are never sent: <see cref="LatestEmailAsync"/> opens the queued row.
    /// </remarks>
    /// <remarks>
    /// <paramref name="oauth"/> (spec 0012) also turns on the fake sign in provider under the process's own
    /// <c>/v1/test/oauth</c>, makes the public URL the process's address so the provider's callback reaches it, adds
    /// <paramref name="email"/>'s platforms, and seeds all four providers for <see cref="Project"/> (<see cref="OAuthFixtures"/>).
    /// </remarks>
    public static async Task<AuthApi> StartAsync(
        PostgresFixture postgres, IReadOnlyDictionary<string, string>? env = null, bool fixtures = true, bool email = false, bool smtp = false,
        bool oauth = false, bool pwned = false)
    {
        email |= oauth;
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
            {(email ? EmailPlatforms : "")}users:
              - project: {OtherProject}
                email: {FixtureUser}
                password: fixture horse battery
                name: Fixture User
            {(smtp ? InstallSmtp : "")}
            {(oauth ? OAuthFixtures : "")}
            """, Ct);

        var settings = new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
        };
        if (fixtures) settings["ORVANO_TEST_FIXTURES"] = fixturesPath;

        int? port = oauth || pwned ? OrvanoProcess.FreePort() : null;
        // Spec 0014, AC-6: the breached password check calls the process's own fake range API, never the real one.
        if (pwned) settings["ORVANO_TEST_HIBP_URL"] = $"http://127.0.0.1:{port}/v1/test/pwned";
        if (oauth)
        {
            settings["ORVANO_PUBLIC_URL"] = $"http://127.0.0.1:{port}";
            settings["ORVANO_TEST_OAUTH_PROVIDER_URL"] = $"http://127.0.0.1:{port}/v1/test/oauth";
        }

        foreach (var (key, value) in env ?? new Dictionary<string, string>()) settings[key] = value;
        var process = OrvanoProcess.Start(["api"], settings, listen: true, port: port);
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

    private const string EmailPlatforms = $"""
          - project: {Project}
            type: web
            identifier: localhost
          - project: {Project}
            type: web
            identifier: app.example.com
          - project: {Project}
            type: ios
            identifier: com.acme.app

        """;

    /// <summary>The Google web client ID of the OAuth fixtures; its native one is <see cref="GoogleNativeClient"/>.</summary>
    public const string GoogleWebClient = "google-web-client";
    public const string GoogleNativeClient = "google-ios-client";
    public const string AppleServicesId = "com.acme.app.web";
    public const string AppleBundleId = "com.acme.app";

    /// <summary>A throwaway Sign in with Apple key: an EC P-256 PKCS#8 PEM made for these tests only.</summary>
    public const string ApplePrivateKey = """
        -----BEGIN PRIVATE KEY-----
        MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg7h8OMosJFfkuDZUw
        uypojpDZR8j94JL8++F84SXKmkqhRANCAASj9wKnLhJvcOx0l5eonieys8K0DKhy
        gL94OCq2BNXGvQqVSPln9CgpoQc0KRMmWCJsrVzsiOG0tIPdF/9WTisT
        -----END PRIVATE KEY-----
        """;

    private const string OAuthFixtures = $"""
        oauthProviders:
          - project: {Project}
            provider: google
            enabled: true
            clientId: {GoogleWebClient}
            clientSecret: google-secret-0001
            clientIdsExtra: [{GoogleNativeClient}]
          - project: {Project}
            provider: apple
            enabled: true
            clientId: {AppleServicesId}
            clientIdsExtra: [{AppleBundleId}]
            appleTeamId: TEAM123456
            appleKeyId: KEY1234567
            applePrivateKey: |
              -----BEGIN PRIVATE KEY-----
              MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg7h8OMosJFfkuDZUw
              uypojpDZR8j94JL8++F84SXKmkqhRANCAASj9wKnLhJvcOx0l5eonieys8K0DKhy
              gL94OCq2BNXGvQqVSPln9CgpoQc0KRMmWCJsrVzsiOG0tIPdF/9WTisT
              -----END PRIVATE KEY-----
          - project: {Project}
            provider: github
            enabled: true
            clientId: github-client
            clientSecret: github-secret-0001
          - project: {Project}
            provider: microsoft
            enabled: true
            clientId: microsoft-client
            clientSecret: microsoft-secret-0001

        """;

    private const string InstallSmtp = """
        installSmtp:
          host: smtp.example.com
          port: 587
          security: starttls
          fromEmail: auth@example.com

        """;

    /// <summary>
    /// The newest queued auth email to <paramref name="to"/>, opened with the test master key, or null when none was
    /// queued. Only <c>messaging_emails</c> rows exist here, since no worker sends them.
    /// </summary>
    public async Task<SealedEmailView?> LatestEmailAsync(string to)
    {
        var secrets = new Orvano.Core.Secrets.SecretBox(Orvano.Core.Secrets.MasterKeys.Parse(OrvanoProcess.MasterKeys));
        await using var conn = await Database.Superuser.OpenConnectionAsync(Ct);
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT id, template, content_ciphertext FROM orvano.messaging_emails ORDER BY created_at DESC, id DESC", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            var id = reader.GetGuid(0);
            var email = Orvano.Messaging.Application.EmailSealer.Open(secrets, id, reader.GetFieldValue<byte[]>(2));
            if (string.Equals(email.To, to, StringComparison.OrdinalIgnoreCase)) return SealedEmailView.From(reader.GetString(1), email.Subject, email.Text);
        }

        return null;
    }

    /// <summary>How many auth emails were queued in all.</summary>
    public Task<long> QueuedEmailCountAsync() => TestDatabase.ScalarAsync<long>(Database.Superuser, "SELECT count(*) FROM orvano.messaging_emails");

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

/// <summary>A queued auth email as a test reads it: the template, subject, link (with its type and token), or code.</summary>
public sealed record SealedEmailView(string Template, string Subject, string Text, string? Url, string? Type, string? Token, string? Code)
{
    public static SealedEmailView From(string template, string subject, string text)
    {
        var link = System.Text.RegularExpressions.Regex.Matches(text, @"[A-Za-z][A-Za-z0-9+.\-]*://\S+")
            .Select(m => m.Value).FirstOrDefault(v => v.Contains("orvano_token", StringComparison.Ordinal));
        if (link is null)
        {
            var code = System.Text.RegularExpressions.Regex.Match(text, @"(?<!\d)\d{6}(?!\d)");
            return new SealedEmailView(template, subject, text, null, null, null, code.Success ? code.Value : null);
        }

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);
        return new SealedEmailView(template, subject, text, link, query["orvano_type"], query["orvano_token"], null);
    }
}
