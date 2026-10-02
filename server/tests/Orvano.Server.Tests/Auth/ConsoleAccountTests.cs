using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Auth;
using Orvano.Auth.Application;
using Orvano.Auth.Contracts;
using Orvano.Auth.Jobs;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Http;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Secrets;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0004 build task 8 over HTTP against the real binary: console accounts sign up through the install's gate, sign
// in, refresh, and sign out with two HttpOnly SameSite=Strict cookies, and every unsafe console request passes the
// CSRF rule. AC-27, AC-28, AC-35; spec 0003 AC-6 to AC-8, AC-14; spec 0006 AC-20.
public class ConsoleAccountTests(PostgresFixture postgres)
{
    private const string SetupToken = "ost_consoleAccountTestsSetupToken00000000000000";
    private const string Password = "console horse battery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_account_needs_the_setup_token_becomes_the_install_admin_and_later_ones_meet_the_policy()
    {
        await using var api = await StartAsync();
        using var http = api.Process.Http();

        using var noToken = await SignUpAsync(http, "ada@x.com", setupToken: null);
        using var wrong = await SignUpAsync(http, "ada@x.com", setupToken: "ost_" + new string('x', 43));
        using var first = await SignUpAsync(http, "ada@x.com", setupToken: SetupToken);
        using var second = await SignUpAsync(http, "grace@x.com", setupToken: SetupToken);
        using var me = await SendAsync(http, HttpMethod.Get, "/v1/console/account", cookies: Cookies(first));
        using var orgs = await SendAsync(http, HttpMethod.Get, "/v1/console/orgs", cookies: Cookies(first));
        using var settings = await SendAsync(http, HttpMethod.Get, "/v1/console/install/settings", cookies: Cookies(first));

        Assert.Equal((HttpStatusCode.Forbidden, "setup_token_invalid"), (noToken.Status, noToken.Code));
        Assert.Equal("setup_token_invalid", wrong.Code);
        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal("ada@x.com", first.Body.GetProperty("email").GetString());
        Assert.Equal((HttpStatusCode.Forbidden, "signup_closed"), (second.Status, second.Code));
        Assert.Equal(first.Body.GetProperty("id").GetString(), me.Body.GetProperty("id").GetString());
        Assert.Equal(1, orgs.Body.GetProperty("items").GetArrayLength()); // the personal org
        Assert.Equal(HttpStatusCode.OK, settings.Status); // an install admin
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_users WHERE project_id = 'console' AND lower(email) = 'ada@x.com'"));
    }

    [Fact]
    public async Task Sign_in_sets_two_HttpOnly_Strict_host_only_cookies_the_refresh_one_scoped_to_the_session_path()
    {
        await using var api = await StartAsync(publicUrl: "https://console.example.com");
        using var http = api.Process.Http();
        await SeedAdminAsync(http, "https://console.example.com");

        using var signIn = await SendAsync(http, HttpMethod.Post, "/v1/console/account/session", new { email = "ADA@x.com", password = Password },
            origin: "https://console.example.com");

        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        var access = SetCookie(signIn, OrvanoHeaders.ConsoleCookie);
        var refresh = SetCookie(signIn, OrvanoHeaders.ConsoleRefreshCookie);
        foreach (var cookie in new[] { access, refresh })
        {
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("path=/;", access + ";", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/console/account/session", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("orv_rt_", Value(refresh), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_access_cookie_outlives_the_access_token_so_an_expired_token_is_refreshed_not_a_missing_cookie_signed_out()
    {
        await using var api = await StartAsync(publicUrl: "https://console.example.com");
        using var http = api.Process.Http();
        await SeedAdminAsync(http, "https://console.example.com");

        using var signIn = await SendAsync(http, HttpMethod.Post, "/v1/console/account/session", new { email = "ADA@x.com", password = Password },
            origin: "https://console.example.com");

        // A browser deletes an expired cookie, and a request without one answers console_session_required, which the console never refreshes.
        var access = Expires(SetCookie(signIn, OrvanoHeaders.ConsoleCookie));
        Assert.InRange(access - DateTimeOffset.UtcNow, TimeSpan.FromDays(29), TimeSpan.FromDays(31));
        Assert.Equal(Expires(SetCookie(signIn, OrvanoHeaders.ConsoleRefreshCookie)), access);
    }

    [Fact]
    public async Task Cookies_skip_Secure_only_when_the_public_url_is_http_localhost()
    {
        await using var api = await StartAsync();
        using var http = api.Process.Http();

        using var first = await SignUpAsync(http, "ada@x.com", SetupToken);

        Assert.DoesNotContain("secure", SetCookie(first, OrvanoHeaders.ConsoleCookie), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cross-site", null, HttpStatusCode.Forbidden)]
    [InlineData("same-site", null, HttpStatusCode.Forbidden)]
    [InlineData("none", null, HttpStatusCode.Forbidden)]
    [InlineData(null, null, HttpStatusCode.Forbidden)]
    [InlineData(null, "https://evil.test", HttpStatusCode.Forbidden)]
    [InlineData(null, "http://localhost:8080", HttpStatusCode.Unauthorized)] // the public origin: through to sign in
    [InlineData("same-origin", "https://evil.test", HttpStatusCode.Unauthorized)] // Sec-Fetch-Site decides when sent
    public async Task An_unsafe_console_request_needs_same_origin_fetch_metadata_or_the_public_origin(string? site, string? origin, HttpStatusCode expected)
    {
        await using var api = await StartAsync();
        using var http = api.Process.Http();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/console/account/session")
        {
            Content = JsonContent.Create(new { email = "nobody@x.com", password = "wrong horse battery" }),
        };
        if (site is not null) request.Headers.Add("Sec-Fetch-Site", site);
        if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await http.SendAsync(request, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.Forbidden ? "csrf_rejected" : "invalid_credentials", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_safe_console_request_needs_no_fetch_metadata()
    {
        await using var api = await StartAsync();
        using var http = api.Process.Http();

        using var response = await http.GetAsync("/v1/console/install/setup", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_trades_the_refresh_cookie_for_new_cookies_and_a_refused_one_clears_them()
    {
        await using var api = await StartAsync();
        using var http = api.Process.Http();
        using var first = await SignUpAsync(http, "ada@x.com", SetupToken);
        var oldRefresh = Value(SetCookie(first, OrvanoHeaders.ConsoleRefreshCookie));

        using var refreshed = await SendAsync(http, HttpMethod.Post, "/v1/console/account/session/refresh",
            cookies: $"{OrvanoHeaders.ConsoleRefreshCookie}={oldRefresh}");
        var newAccess = Value(SetCookie(refreshed, OrvanoHeaders.ConsoleCookie));
        using var me = await SendAsync(http, HttpMethod.Get, "/v1/console/account", cookies: $"{OrvanoHeaders.ConsoleCookie}={newAccess}");
        using var none = await SendAsync(http, HttpMethod.Post, "/v1/console/account/session/refresh");

        Assert.Equal(HttpStatusCode.NoContent, refreshed.Status);
        Assert.NotEqual(oldRefresh, Value(SetCookie(refreshed, OrvanoHeaders.ConsoleRefreshCookie)));
        Assert.Equal(HttpStatusCode.OK, me.Status);
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_refresh_token"), (none.Status, none.Code));
        Assert.Contains("expires=thu, 01 jan 1970", SetCookie(none, OrvanoHeaders.ConsoleCookie), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sign_out_ends_the_session_clears_both_cookies_and_works_with_only_the_refresh_cookie()
    {
        await using var api = await StartAsync();
        using var http = api.Process.Http();
        using var first = await SignUpAsync(http, "ada@x.com", SetupToken);
        using var second = await SendAsync(http, HttpMethod.Post, "/v1/console/account/session", new { email = "ada@x.com", password = Password });

        using var signOut = await SendAsync(http, HttpMethod.Delete, "/v1/console/account/session", cookies: Cookies(first));
        using var after = await SendAsync(http, HttpMethod.Get, "/v1/console/account", cookies: Cookies(first));
        using var byRefresh = await SendAsync(http, HttpMethod.Delete, "/v1/console/account/session",
            cookies: $"{OrvanoHeaders.ConsoleRefreshCookie}={Value(SetCookie(second, OrvanoHeaders.ConsoleRefreshCookie))}");
        using var secondAfter = await SendAsync(http, HttpMethod.Get, "/v1/console/account", cookies: Cookies(second));

        Assert.Equal(HttpStatusCode.NoContent, signOut.Status);
        Assert.Contains("expires=thu, 01 jan 1970", SetCookie(signOut, OrvanoHeaders.ConsoleRefreshCookie), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("console_session_required", after.Code);
        Assert.Equal(HttpStatusCode.NoContent, byRefresh.Status);
        Assert.Equal("console_session_required", secondAfter.Code);
        Assert.Equal(2L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_sessions WHERE project_id = 'console' AND end_reason = 'sign_out'"));
    }

    [Fact]
    public async Task An_app_users_token_is_no_console_session_and_a_console_token_is_no_app_session()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var http = api.Process.Http();
        var consoleToken = await ConsoleSignIn.CookieAsync(http, AuthApi.ConsoleUser, Ct);
        using var app = await api.SignUpAsync("user@x.com");

        using var appAsConsole = await SendAsync(http, HttpMethod.Get, "/v1/console/account",
            cookies: $"{OrvanoHeaders.ConsoleCookie}={AuthApi.AccessToken(app)}");
        using var consoleAsApp = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: consoleToken);
        using var consoleProject = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/password",
            new { email = AuthApi.ConsoleUser, password = ConsoleSignIn.Password }, project: "console");

        Assert.Equal("console_session_required", appAsConsole.Code);
        Assert.Equal("invalid_token", consoleAsApp.Code);
        Assert.Equal("project_not_found", consoleProject.Code);
    }

    [Fact]
    public async Task An_expired_console_token_answers_token_expired_so_the_console_refreshes()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var keys = new SigningKeys(database.App, new SecretBox(MasterKeys.Parse(OrvanoProcess.MasterKeys)), clock);
        var tokens = new AccessTokens(keys, PublicUrl.Parse(OrvanoProcess.PublicUrl), clock);
        var issued = await tokens.IssueAsync(ConsoleProject.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), emailVerified: false, Ct);
        var checks = new ConsoleSessionChecks(tokens, null!);

        clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(ConsoleSessionStatus.Expired, (await checks.CheckAsync(issued.Token, Ct)).Status);
        Assert.Equal(ConsoleSessionStatus.Missing, (await checks.CheckAsync(null, Ct)).Status);
        Assert.Equal(ConsoleSessionStatus.Invalid, (await checks.CheckAsync("a.b.c", Ct)).Status);
    }

    [Fact]
    public async Task A_purged_project_loses_its_users_sessions_passwords_and_keys_through_the_purge_job()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var shop = await api.SignUpAsync("ada@x.com");
        using var other = await api.SignUpAsync("ada@x.com", project: AuthApi.OtherProject);
        // Spec 0010 AC-29: email tokens go too, including those for emails with no user.
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            """
            INSERT INTO orvano.auth_email_tokens (project_id, kind, email, secret_hash, expires_at)
            VALUES (@p, 'magic_link', 'new@x.com', decode(repeat('aa', 32), 'hex'), now() + interval '15 minutes'),
                   (@o, 'magic_link', 'new@x.com', decode(repeat('bb', 32), 'hex'), now() + interval '15 minutes')
            """, ("p", AuthApi.Project), ("o", AuthApi.OtherProject));
        var work = new WorkRegistry();
        new AuthModule().RegisterWork(work);
        var purged = new OutboxEvent(1, AuthApi.Project, AuthJobs.ProjectPurgedEvent, AuthApi.Project, "{}", DateTimeOffset.UtcNow);

        var jobs = work.ConsumersFor(purged).Single(c => c.Name == "auth.purge_users").Consumer(purged).ToList();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddKeyedSingleton<NpgsqlDataSource>(OrvanoDb.App, (_, _) => NpgsqlDataSource.Create(api.Database.AppUrl))
            .BuildServiceProvider();
        var job = Assert.Single(jobs);
        var handler = work.HandlerFor(job.Kind)!;
        for (var run = 0; run < 2; run++) // idempotent
            await handler(new JobContext(new ClaimedJob(1, job.Queue, job.Kind, AuthApi.Project, job.PayloadJson, 1, 10), services), Ct);

        Assert.Equal("auth", job.Queue);
        foreach (var table in new[] { "auth_users", "auth_passwords", "auth_sessions", "auth_signing_keys", "auth_email_tokens" })
        {
            Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, $"SELECT count(*) FROM orvano.{table} WHERE project_id = @p", ("p", AuthApi.Project)));
            Assert.NotEqual(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, $"SELECT count(*) FROM orvano.{table} WHERE project_id = @p", ("p", AuthApi.OtherProject)));
        }
    }

    /// <summary>An install with no fixtures: it waits for its first admin, who needs <see cref="SetupToken"/>.</summary>
    private Task<AuthApi> StartAsync(string? publicUrl = null)
    {
        var env = new Dictionary<string, string> { ["ORVANO_SETUP_TOKEN"] = SetupToken };
        if (publicUrl is not null) env["ORVANO_PUBLIC_URL"] = publicUrl;
        return AuthApi.StartAsync(postgres, env, fixtures: false);
    }

    private static async Task SeedAdminAsync(HttpClient http, string origin)
    {
        using var reply = await SendAsync(http, HttpMethod.Post, "/v1/console/account", new { email = "ada@x.com", password = Password, setupToken = SetupToken },
            origin: origin);
        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    private static Task<Reply> SignUpAsync(HttpClient http, string email, string? setupToken) =>
        SendAsync(http, HttpMethod.Post, "/v1/console/account", new { email, password = Password, setupToken });

    /// <summary>A console call as the console's own page would send it: <c>Sec-Fetch-Site: same-origin</c>, unless an <paramref name="origin"/> is given.</summary>
    private static async Task<Reply> SendAsync(HttpClient http, HttpMethod method, string url, object? body = null, string? cookies = null, string? origin = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (origin is null) request.Headers.Add("Sec-Fetch-Site", "same-origin");
        else request.Headers.Add("Origin", origin);
        if (cookies is not null) request.Headers.Add("Cookie", cookies);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return new Reply(response.StatusCode, response.Headers, text.Length > 0 ? JsonDocument.Parse(text) : null);
    }

    private static string SetCookie(Reply reply, string name) =>
        reply.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(name + "=", StringComparison.Ordinal));

    private static DateTimeOffset Expires(string setCookie) => DateTimeOffset.Parse(
        setCookie.Split(';').Single(part => part.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)).Split('=', 2)[1].Trim());

    private static string Value(string setCookie) => setCookie.Split(';')[0].Split('=', 2)[1];

    private static string Cookies(Reply signedIn) =>
        $"{OrvanoHeaders.ConsoleCookie}={Value(SetCookie(signedIn, OrvanoHeaders.ConsoleCookie))}; " +
        $"{OrvanoHeaders.ConsoleRefreshCookie}={Value(SetCookie(signedIn, OrvanoHeaders.ConsoleRefreshCookie))}";
}
