using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;
using static Orvano.Server.Tests.Auth.PasskeyTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 build task 4 over HTTP against the real binary: a server or the console reads and resets a user's MFA and
// manages their passkeys (AC-27), User.mfaEnabled and the mfa filter (AC-39, AC-5), and `orvano mfa reset` (AC-28).
public class MfaResetTests(PostgresFixture postgres)
{
    private const string MethodsUrl = "/v1/console/project/auth/methods";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_server_reset_removes_the_factor_ends_every_session_and_keeps_passkeys()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var ada = await EnrollAsync(api, "ada@x.com");
        var bearer = await StrongBearerAsync(api, "ada@x.com", ada.Secret);
        await RegisterAsync(api, bearer);

        using var reset = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{ada.UserId}/mfa/reset");

        Assert.Equal(HttpStatusCode.NoContent, reset.Status);
        using var refused = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: bearer);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        using var signedIn = await api.SignInAsync("ada@x.com");
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        Assert.Equal(JsonValueKind.Null, signedIn.Body.GetProperty("mfa").ValueKind);
        Assert.False(signedIn.Body.GetProperty("user").GetProperty("mfaEnabled").GetBoolean());
        Assert.Equal(0L, await CountAsync(api, "SELECT count(*) FROM orvano.auth_recovery_codes"));
        Assert.Equal(0L, await CountAsync(api, "SELECT count(*) FROM orvano.auth_totp_factors"));
        Assert.Equal(1L, await CountAsync(api, "SELECT count(*) FROM orvano.auth_passkeys"));
        Assert.Equal(0L, await CountAsync(api, $"SELECT count(*) FROM orvano.auth_sessions WHERE user_id = '{ada.UserId}' AND ended_at IS NULL AND id <> '{Sid(signedIn)}'"));
        Assert.True(await CountAsync(api, "SELECT count(*) FROM orvano.auth_sessions WHERE end_reason = 'mfa_reset'") >= 2);

        using var events = await EventsAsync(api, "auth.mfa.reset");
        var reasonless = Assert.Single(events.RootElement.EnumerateArray());
        Assert.Equal("apiKey", reasonless.GetProperty("actor").GetProperty("type").GetString());
        Assert.Equal(ada.UserId, reasonless.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task A_reset_of_a_user_without_mfa_changes_nothing_and_an_unknown_user_is_404()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var bob = await api.SignUpAsync("bob@x.com");

        using var reset = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{AuthApi.UserId(bob)}/mfa/reset");
        using var unknown = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{Guid.CreateVersion7()}/mfa/reset");
        using var junk = await api.AsServerAsync(HttpMethod.Post, "/v1/users/nope/mfa/reset");
        using var readOnly = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{AuthApi.UserId(bob)}/mfa/reset", key: AuthApi.ReadKey);
        using var elsewhere = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{AuthApi.UserId(bob)}/mfa/reset",
            key: AuthApi.OtherProjectKey, project: AuthApi.OtherProject);

        Assert.Equal(HttpStatusCode.NoContent, reset.Status);
        Assert.Equal(HttpStatusCode.OK, (await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(bob))).Status);
        Assert.Equal(0L, await CountAsync(api, "SELECT count(*) FROM orvano.events WHERE type IN ('auth.mfa.reset', 'auth.session.ended')"));
        Assert.Equal((HttpStatusCode.NotFound, "user_not_found"), (unknown.Status, unknown.Code));
        Assert.Equal("user_not_found", junk.Code);
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.Status);
        Assert.Equal("user_not_found", elsewhere.Code);
    }

    [Fact]
    public async Task MfaEnabled_and_the_mfa_filter_follow_the_confirmed_factor_and_the_totp_switch()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var ada = await EnrollAsync(api, "ada@x.com");
        // Bob started an authenticator app but never confirmed it: a pending factor is not MFA.
        var bobBearer = await VerifiedUserAsync(api, "bob@x.com");
        using (var pending = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", bearer: bobBearer))
            Assert.Equal(HttpStatusCode.Created, pending.Status);
        var bobId = (await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: bobBearer)).Body.GetProperty("id").GetString()!;

        Assert.Equal([ada.UserId], await IdsAsync(api, "/v1/users?mfa=on"));
        Assert.Equal([bobId], await IdsAsync(api, "/v1/users?mfa=off"));
        Assert.Equal([ada.UserId], await IdsAsync(api, "/v1/console/project/users?mfa=on", console: true));
        Assert.True((await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{ada.UserId}")).Body.GetProperty("mfaEnabled").GetBoolean());
        Assert.False((await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{bobId}")).Body.GetProperty("mfaEnabled").GetBoolean());
        using var bad = await api.AsServerAsync(HttpMethod.Get, "/v1/users?mfa=maybe");
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_request"), (bad.Status, bad.Code));

        using var off = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { totpEnabled = false });
        Assert.Equal(HttpStatusCode.OK, off.Status);

        Assert.Empty(await IdsAsync(api, "/v1/users?mfa=on"));
        Assert.Equal([bobId, ada.UserId], await IdsAsync(api, "/v1/users?mfa=off"));
        Assert.False((await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{ada.UserId}")).Body.GetProperty("mfaEnabled").GetBoolean());
    }

    [Fact]
    public async Task A_server_lists_and_removes_a_users_passkeys_but_never_another_users()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        using var ada = await api.SignUpAsync("ada@x.com");
        var adaId = AuthApi.UserId(ada);
        await VerifyEmailAsync(api, adaId);
        var adaKey = await RegisterAsync(api, AuthApi.AccessToken(ada));
        var bobBearer = await VerifiedUserAsync(api, "bob@x.com");
        var bobKey = await RegisterAsync(api, bobBearer);

        using var listed = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{adaId}/passkeys");
        var passkeyId = adaKey.Passkey.GetProperty("id").GetString();
        using var notHers = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{adaId}/passkeys/{bobKey.Passkey.GetProperty("id").GetString()}");
        using var removed = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{adaId}/passkeys/{passkeyId}");
        using var again = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{adaId}/passkeys/{passkeyId}");
        using var noUser = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{Guid.CreateVersion7()}/passkeys");

        Assert.Equal(passkeyId, Assert.Single(listed.Body.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal((HttpStatusCode.NotFound, "passkey_not_found"), (notHers.Status, notHers.Code));
        Assert.Equal(HttpStatusCode.NoContent, removed.Status);
        Assert.Equal("passkey_not_found", again.Code);
        Assert.Equal((HttpStatusCode.NotFound, "user_not_found"), (noUser.Status, noUser.Code));
        Assert.Equal(HttpStatusCode.OK, (await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(ada))).Status);
        Assert.Equal(1L, await CountAsync(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.passkey.removed' AND payload->>'reason' = 'server'"));
    }

    [Fact]
    public async Task Viewers_read_a_users_passkeys_and_developers_reset_and_remove_them()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var ada = await EnrollAsync(api, "ada@x.com");
        var registered = await RegisterAsync(api, await StrongBearerAsync(api, "ada@x.com", ada.Secret));
        var passkey = registered.Passkey.GetProperty("id").GetString();
        var (developer, viewer) = (AuthApi.OtherConsoleUsers[0], AuthApi.OtherConsoleUsers[1]);
        await api.GrantAsync(developer, "developer");
        await api.GrantAsync(viewer, "viewer");
        var users = $"/v1/console/project/users/{ada.UserId}";

        using var viewerList = await api.AsConsoleAsync(HttpMethod.Get, $"{users}/passkeys", account: viewer);
        using var viewerReset = await api.AsConsoleAsync(HttpMethod.Post, $"{users}/mfa/reset", account: viewer);
        using var viewerRemove = await api.AsConsoleAsync(HttpMethod.Delete, $"{users}/passkeys/{passkey}", account: viewer);
        Assert.Equal(HttpStatusCode.OK, viewerList.Status);
        Assert.Single(viewerList.Body.GetProperty("items").EnumerateArray());
        Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), (viewerReset.Status, viewerReset.Code));
        Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), (viewerRemove.Status, viewerRemove.Code));
        Assert.Equal(1L, await CountAsync(api, "SELECT count(*) FROM orvano.auth_totp_factors"));

        using var reset = await api.AsConsoleAsync(HttpMethod.Post, $"{users}/mfa/reset", account: developer);
        using var remove = await api.AsConsoleAsync(HttpMethod.Delete, $"{users}/passkeys/{passkey}", account: developer);
        using var unknown = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/project/users/{Guid.CreateVersion7()}/mfa/reset", account: developer);

        Assert.Equal(HttpStatusCode.NoContent, reset.Status);
        Assert.Equal(HttpStatusCode.NoContent, remove.Status);
        Assert.Equal("user_not_found", unknown.Code);
        using var resetEvents = await EventsAsync(api, "auth.mfa.reset");
        Assert.Equal("user", Assert.Single(resetEvents.RootElement.EnumerateArray()).GetProperty("actor").GetProperty("type").GetString());
        Assert.Equal(1L, await CountAsync(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.passkey.removed' AND payload->>'reason' = 'console'"));
    }

    [Fact]
    public async Task A_server_and_every_console_role_read_a_users_mfa_state_as_the_user_does()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var ada = await EnrollAsync(api, "ada@x.com");
        using (var stepOne = await api.SignInAsync("ada@x.com"))
        using (var stepTwo = await StepTwoAsync(api, Ticket(stepOne), recoveryCode: ada.RecoveryCodes[0]))
            Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        var viewer = AuthApi.OtherConsoleUsers[1];
        await api.GrantAsync(viewer, "viewer");

        using var server = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{ada.UserId}/mfa", key: AuthApi.ReadKey);
        using var console = await api.AsConsoleAsync(HttpMethod.Get, $"/v1/console/project/users/{ada.UserId}/mfa", account: viewer);
        using var unknown = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{Guid.CreateVersion7()}/mfa");
        using var junk = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/users/nope/mfa");
        using var elsewhere = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{ada.UserId}/mfa",
            key: AuthApi.OtherProjectKey, project: AuthApi.OtherProject);

        Assert.Equal(HttpStatusCode.OK, server.Status);
        Assert.True(server.Body.GetProperty("mfaEnabled").GetBoolean());
        Assert.True(server.Body.GetProperty("totpConfirmed").GetBoolean());
        Assert.Equal(JsonValueKind.String, server.Body.GetProperty("totpConfirmedAt").ValueKind);
        Assert.Equal(9, server.Body.GetProperty("recoveryCodesRemaining").GetInt32());
        Assert.Equal(server.Body.GetRawText(), console.Body.GetRawText());
        Assert.Equal((HttpStatusCode.NotFound, "user_not_found"), (unknown.Status, unknown.Code));
        Assert.Equal((HttpStatusCode.NotFound, "user_not_found"), (junk.Status, junk.Code));
        Assert.Equal("user_not_found", elsewhere.Code);
        // Reading writes no event: the one auth.mfa event is the enrollment's.
        Assert.Equal(1L, await CountAsync(api, "SELECT count(*) FROM orvano.events WHERE type LIKE 'auth.mfa%'"));

        using var off = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { totpEnabled = false });
        Assert.Equal(HttpStatusCode.OK, off.Status);
        using var switchedOff = await api.AsConsoleAsync(HttpMethod.Get, $"/v1/console/project/users/{ada.UserId}/mfa", account: viewer);
        Assert.False(switchedOff.Body.GetProperty("mfaEnabled").GetBoolean());
        Assert.True(switchedOff.Body.GetProperty("totpConfirmed").GetBoolean());
    }

    [Fact]
    public async Task Orvano_mfa_reset_lets_the_install_admin_sign_in_with_a_password_alone()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        await SeedConsoleFactorAsync(api, AuthApi.ConsoleUser);
        Assert.Equal(HttpStatusCode.Forbidden, (await ConsoleSignInAsync(api, AuthApi.ConsoleUser)).StatusCode);

        await using var unknown = await MfaResetAsync(api, "--email", "nobody@x.com");
        await using var badArgs = await MfaResetAsync(api, "--email");
        await using var noEmail = await MfaResetAsync(api, "--passkeys");
        await using var reset = await MfaResetAsync(api, "--email", AuthApi.ConsoleUser.ToUpperInvariant(), "--passkeys");
        await using var twice = await MfaResetAsync(api, "--email", AuthApi.ConsoleUser);

        Assert.Equal(2, unknown.ExitCode);
        Assert.Contains("not found", unknown.Output, StringComparison.Ordinal);
        Assert.Equal(64, badArgs.ExitCode);
        Assert.Equal(64, noEmail.ExitCode);
        Assert.Equal(0, reset.ExitCode);
        Assert.Equal("reset", reset.Output.Trim());
        Assert.Equal(0, twice.ExitCode);
        Assert.Equal("no factor", twice.Output.Trim());

        using var signedIn = await ConsoleSignInAsync(api, AuthApi.ConsoleUser);
        Assert.Equal(HttpStatusCode.Created, signedIn.StatusCode);
        using var events = await EventsAsync(api, "auth.mfa.reset");
        var actor = Assert.Single(events.RootElement.EnumerateArray()).GetProperty("actor");
        Assert.Equal("system", actor.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, actor.GetProperty("id").ValueKind);
    }

    private static Task<OrvanoProcess> MfaResetAsync(AuthApi api, params string[] args) =>
        OrvanoProcess.RunAsync(["mfa", "reset", .. args], new Dictionary<string, string> { ["ORVANO_DB_URL"] = api.Database.AppUrl });

    private static async Task<HttpResponseMessage> ConsoleSignInAsync(AuthApi api, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/console/account/session")
        {
            Content = JsonContent.Create(new { email, password = ConsoleSignIn.Password }),
        };
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        return await api.Http.SendAsync(request, Ct);
    }

    /// <summary>A confirmed TOTP factor and one recovery code for a console account; only their existence matters here.</summary>
    private static Task SeedConsoleFactorAsync(AuthApi api, string email) =>
        TestDatabase.ExecuteAsync(api.Database.Superuser, """
            WITH account AS (SELECT id FROM orvano.auth_users WHERE project_id = 'console' AND lower(email) = @email),
                 factor AS (
                   INSERT INTO orvano.auth_totp_factors (user_id, project_id, secret_ciphertext, confirmed_at)
                   SELECT id, 'console', '\x00'::bytea, now() FROM account)
            INSERT INTO orvano.auth_recovery_codes (project_id, user_id, code_mac, mac_key_id)
            SELECT 'console', id, '\x00'::bytea, 'k' FROM account
            """, ("email", email));

    private static Task<long> CountAsync(AuthApi api, string sql) => TestDatabase.ScalarAsync<long>(api.Database.Superuser, sql);

    private static async Task<JsonDocument> EventsAsync(AuthApi api, string type) =>
        JsonDocument.Parse(await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT coalesce(json_agg(payload ORDER BY id), '[]')::text FROM orvano.events WHERE type = @type", ("type", type)));

    private static async Task<string[]> IdsAsync(AuthApi api, string url, bool console = false)
    {
        using var page = console ? await api.AsConsoleAsync(HttpMethod.Get, url) : await api.AsServerAsync(HttpMethod.Get, url);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        return [.. page.Body.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetString()!)];
    }
}
