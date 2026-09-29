using System.Net;
using System.Net.Http.Json;
using Orvano.Contract;
using Orvano.Core;
using Orvano.Platform.Contracts;
using Orvano.Server.Tests.Infrastructure;
using Orvano.Server.Tests.Platform;

namespace Orvano.Server.Tests.Install;

// Spec 0006: the first admin gate on the server (AC-19 to AC-22, AC-24).
public class SetupTokenTests(PostgresFixture postgres)
{
    private const string Token = "ost_right-token-0000000000000000000000000000000";
    private const string Wrong = "ost_wrong-token-0000000000000000000000000000000";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_account_needs_the_setup_token_and_later_ones_ignore_it()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, setupToken: Token);
        var setup = p.Get<IInstallSetupState>();
        Assert.True(await setup.IsSetupRequiredAsync(Ct));

        Assert.IsType<SignupAdmission.SetupTokenInvalid>((await p.SignUpAsync("a@example.com")).Admission);
        Assert.IsType<SignupAdmission.SetupTokenInvalid>((await p.SignUpAsync("a@example.com", setupToken: Wrong)).Admission);
        Assert.IsType<SignupAdmission.SetupTokenInvalid>((await p.SignUpAsync("a@example.com", setupToken: Token[..^1])).Admission);
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_install_admins"));
        Assert.Equal(0L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_orgs"));
        Assert.True(await setup.IsSetupRequiredAsync(Ct));

        var (_, admitted) = await p.SignUpAsync("admin@example.com", setupToken: Token);
        Assert.Equal(new SignupAdmission.Admitted(IsFirstAccount: true), admitted);
        Assert.False(await setup.IsSetupRequiredAsync(Ct));

        // After the first admin, spec 0003's invite rules apply and the token admits no one.
        Assert.IsType<SignupAdmission.Refused>((await p.SignUpAsync("second@example.com", setupToken: Token)).Admission);
    }

    [Fact]
    public async Task Racing_first_sign_ups_with_the_token_give_exactly_one_admin()
    {
        await using var p = await PlatformHarness.StartAsync(postgres, setupToken: Token);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => p.SignUpAsync($"racer{i}@example.com", setupToken: Token), Ct)));

        Assert.Equal(1, results.Count(r => r.Admission is SignupAdmission.Admitted { IsFirstAccount: true }));
        Assert.Equal(1L, await p.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_install_admins"));
    }

    [Fact]
    public async Task Without_a_configured_token_the_first_sign_up_stays_open()
    {
        await using var p = await PlatformHarness.StartAsync(postgres);

        Assert.Equal(new SignupAdmission.Admitted(IsFirstAccount: true), (await p.SignUpAsync("admin@example.com", setupToken: Wrong)).Admission);
    }

    [Theory]
    [InlineData("ost_short")]
    [InlineData("xyz_right-token-0000000000000000000000000000000")]
    [InlineData("ost_right+token-0000000000000000000000000000000")]
    public async Task A_malformed_token_is_refused_at_start(string token)
    {
        var ex = await Assert.ThrowsAsync<OrvanoConfigException>(() => PlatformHarness.StartAsync(postgres, setupToken: token));

        Assert.Equal("ORVANO_SETUP_TOKEN must be ost_ followed by 43 base64url characters. Run the installer, or generate one as .env.example shows.", ex.Message);
        Assert.DoesNotContain(token, ex.Message);
    }

    [Fact]
    public async Task GetSetup_needs_no_session_and_setup_status_reads_it()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await using var api = await StartApiAsync(database, ("ORVANO_SETUP_TOKEN", Token));
        using var http = api.Http();

        using var response = await http.GetAsync("/v1/console/install/setup", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new InstallSetup(SetupRequired: true, SignupOpen: false), await response.Content.ReadFromJsonAsync<InstallSetup>(Ct));
        Assert.Equal("required", (await SetupStatusAsync(api)).Trim());

        await AddInstallAdminAsync(database);
        Assert.Equal(new InstallSetup(SetupRequired: false, SignupOpen: false), await http.GetFromJsonAsync<InstallSetup>("/v1/console/install/setup", Ct));
        Assert.Equal("done", (await SetupStatusAsync(api)).Trim());

        // Every other console route still needs a session.
        using var settings = await http.GetAsync("/v1/console/install/settings", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, settings.StatusCode);
        using var post = new HttpRequestMessage(HttpMethod.Post, "/v1/console/install/setup");
        post.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var posted = await http.SendAsync(post, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, posted.StatusCode);
    }

    [Fact]
    public async Task GetSetup_allows_60_calls_a_minute_from_one_address_then_answers_429()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await using var api = await StartApiAsync(database, ("ORVANO_SETUP_TOKEN", Token));
        using var http = api.Http();

        for (var i = 0; i < 60; i++)
        {
            using var allowed = await http.GetAsync("/v1/console/install/setup", Ct);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await http.GetAsync("/v1/console/install/setup", Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        var problem = await limited.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        Assert.Equal("rate_limited", problem.GetProperty("code").GetString());
        var retryAfter = int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);
    }

    [Fact]
    public async Task Setup_status_exits_1_when_the_api_does_not_answer()
    {
        await using var status = await OrvanoProcess.RunAsync(["setup-status"],
            new Dictionary<string, string> { ["ASPNETCORE_HTTP_PORTS"] = OrvanoProcess.FreePort().ToString(System.Globalization.CultureInfo.InvariantCulture) });

        Assert.Equal(1, status.ExitCode);
        Assert.Contains("The api did not answer the setup status.", status.Output);
    }

    [Fact]
    public async Task The_api_refuses_to_start_in_Production_without_a_token_while_no_admin_exists()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await using (var refused = OrvanoProcess.Start(["api"], new Dictionary<string, string> { ["ORVANO_DB_URL"] = database.AppUrl }, listen: true))
        {
            await refused.WaitForExitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(1, refused.ExitCode);
            Assert.Contains("ORVANO_SETUP_TOKEN is not set and this install has no admin yet", refused.Output);
        }

        // Once an admin exists, the token is no longer needed.
        await AddInstallAdminAsync(database);
        await using var api = await StartApiAsync(database);
    }

    [Fact]
    public async Task The_api_refuses_a_malformed_token_in_every_environment()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await using var api = OrvanoProcess.Start(["api"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ORVANO_SETUP_TOKEN"] = "not-a-token",
        }, listen: true);
        await api.WaitForExitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, api.ExitCode);
        Assert.Contains("ORVANO_SETUP_TOKEN must be ost_ followed by 43 base64url characters.", api.Output);
    }

    [Fact]
    public async Task Development_starts_without_a_token()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await using var api = await StartApiAsync(database, ("ASPNETCORE_ENVIRONMENT", "Development"));
    }

    private static async Task<OrvanoProcess> StartApiAsync(TestDatabase database, params (string Key, string Value)[] extra)
    {
        var env = new Dictionary<string, string> { ["ORVANO_DB_URL"] = database.AppUrl };
        foreach (var (key, value) in extra) env[key] = value;
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

    private static async Task<string> SetupStatusAsync(OrvanoProcess api)
    {
        await using var status = await OrvanoProcess.RunAsync(["setup-status"],
            new Dictionary<string, string> { ["ASPNETCORE_HTTP_PORTS"] = api.Port!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        Assert.Equal(0, status.ExitCode);
        return status.Output;
    }

    private static Task AddInstallAdminAsync(TestDatabase database) =>
        TestDatabase.ExecuteAsync(database.Superuser, "INSERT INTO orvano.platform_install_admins (user_id) VALUES (@id)", ("id", Guid.CreateVersion7()));
}
