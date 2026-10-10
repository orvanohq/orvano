using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Orvano.Auth.Application;
using Orvano.Auth.Jobs;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0014 build task 7 over HTTP against the real binary: guest sign in, what a guest may not do, the upgrade in both
// modes and by provider link, the users filter, the guest settings, the guest limits, and retention. AC-2, AC-28 to AC-32.
public class AnonymousUserTests(PostgresFixture postgres)
{
    private const string MethodsUrl = "/v1/console/project/auth/methods";
    private const string Redirect = "https://app.example.com/auth/callback";
    private const string Password = "correct horse battery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_guest_signs_in_only_when_the_project_allows_it_and_gets_a_level_1_session_with_no_amr()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using (var off = await GuestAsync(api))
            Assert.Equal((HttpStatusCode.Forbidden, "anonymous_disabled"), (off.Status, off.Code));

        await EnableAsync(api);
        await SetPoliciesAsync(api, new { signUpsEnabled = false });
        using (var closed = await GuestAsync(api))
            Assert.Equal((HttpStatusCode.Forbidden, "sign_up_disabled"), (closed.Status, closed.Code));
        await SetPoliciesAsync(api, new { signUpsEnabled = true });

        using var guest = await GuestAsync(api);
        Assert.Equal(HttpStatusCode.Created, guest.Status);
        Assert.True(guest.Body.GetProperty("isNewUser").GetBoolean());
        var user = guest.Body.GetProperty("user");
        Assert.True(user.GetProperty("isAnonymous").GetBoolean());
        foreach (var field in new[] { "email", "name" }) Assert.Equal(JsonValueKind.Null, user.GetProperty(field).ValueKind);
        Assert.False(user.GetProperty("hasPassword").GetBoolean());

        var claims = Claims(AuthApi.AccessToken(guest));
        Assert.True(claims.GetProperty("is_anonymous").GetBoolean());
        Assert.Equal(1, claims.GetProperty("aal").GetInt32());
        Assert.Equal(0, claims.GetProperty("amr").GetArrayLength());

        using var sessions = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions", bearer: AuthApi.AccessToken(guest));
        Assert.Equal("anonymous", sessions.Body.GetProperty("items")[0].GetProperty("method").GetString());
        Assert.Equal(1L, await CountAsync(api,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.created' AND payload->>'method' = 'anonymous'"));

        // A permanent user's token says false.
        using var ada = await api.SignUpAsync("ada@x.com");
        Assert.False(Claims(AuthApi.AccessToken(ada)).GetProperty("is_anonymous").GetBoolean());
        Assert.False(ada.Body.GetProperty("user").GetProperty("isAnonymous").GetBoolean());
    }

    [Fact]
    public async Task A_guest_reads_and_deletes_their_account_but_gets_no_password_email_or_factor()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        await PasskeyTests.EnablePasskeysAsync(api);
        await EnableAsync(api);
        using var guest = await GuestAsync(api);
        var bearer = AuthApi.AccessToken(guest);

        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Put, "/v1/account/password", new { newPassword = "another horse battery" }),
                     (HttpMethod.Put, "/v1/account/email", new { email = "ada@x.com", redirectUrl = Redirect }),
                     (HttpMethod.Post, "/v1/account/verification", new { redirectUrl = Redirect }),
                     (HttpMethod.Post, "/v1/account/mfa/totp", new { }),
                     (HttpMethod.Post, "/v1/account/passkeys/registration", new { }),
                 })
        {
            using var refused = await api.SendAsync(method, url, body, bearer: bearer);
            Assert.True((HttpStatusCode.Forbidden, "anonymous_not_allowed") == (refused.Status, refused.Code), $"{url}: {refused.Status} {refused.Code}");
        }

        using (var me = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: bearer))
            Assert.True(me.Body.GetProperty("isAnonymous").GetBoolean());

        // No password asked, though the session is not fresh.
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_sessions SET created_at = now() - interval '1 hour'");
        using var deleted = await api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { }, bearer: bearer);
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal(0L, await CountAsync(api, "SELECT count(*) FROM orvano.auth_users WHERE is_anonymous"));
    }

    [Fact]
    public async Task An_upgrade_keeps_the_user_id_and_the_next_refresh_says_the_user_is_permanent()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        await EnableAsync(api);
        using (await api.SignUpAsync("taken@x.com")) { }
        using var guest = await GuestAsync(api);
        var bearer = AuthApi.AccessToken(guest);
        var userId = AuthApi.UserId(guest);

        // Only a guest can upgrade, and the email and password rules hold.
        using (var notGuest = await UpgradeAsync(api, AuthApi.AccessToken(await api.SignInAsync("taken@x.com")), "x@x.com"))
            Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), (notGuest.Status, notGuest.Code));
        using (var common = await UpgradeAsync(api, bearer, "ada@x.com", password: "password123"))
            Assert.Equal("password_too_common", common.Code);
        using (var taken = await UpgradeAsync(api, bearer, "TAKEN@x.com"))
            Assert.Equal((HttpStatusCode.Conflict, "user_already_exists"), (taken.Status, taken.Code));

        using var upgraded = await UpgradeAsync(api, bearer, "ada@x.com", name: "Ada", redirect: Redirect);
        Assert.Equal(HttpStatusCode.OK, upgraded.Status);
        Assert.False(upgraded.Body.GetProperty("verificationRequired").GetBoolean());
        Assert.Equal("queued", upgraded.Body.GetProperty("verificationEmail").GetString());
        var user = upgraded.Body.GetProperty("user");
        Assert.Equal(userId, user.GetProperty("id").GetString());
        Assert.False(user.GetProperty("isAnonymous").GetBoolean());
        Assert.Equal("ada@x.com", user.GetProperty("email").GetString());
        Assert.False(user.GetProperty("emailVerified").GetBoolean());
        Assert.Equal("Ada", user.GetProperty("name").GetString());
        Assert.True(user.GetProperty("hasPassword").GetBoolean());
        Assert.Equal("verification", (await api.LatestEmailAsync("ada@x.com"))!.Template);

        // The session in hand keeps working; its next refresh carries false.
        using var refreshed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(guest) });
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.False(Claims(refreshed.Body.GetProperty("accessToken").GetString()!).GetProperty("is_anonymous").GetBoolean());
        using (var signIn = await api.SignInAsync("ada@x.com"))
            Assert.Equal(userId, AuthApi.UserId(signIn));
        using (var again = await UpgradeAsync(api, bearer, "ada2@x.com"))
            Assert.Equal("forbidden", again.Code);
        Assert.Equal(1L, await CountAsync(api,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.upgraded' AND payload->>'method' = 'password' AND payload->>'userId' = @id",
            ("id", userId)));
    }

    [Fact]
    public async Task Under_the_verified_email_rule_an_upgrade_waits_for_its_link_and_never_says_whether_the_email_is_taken()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        await EnableAsync(api);
        using (await api.SignUpAsync("owner@x.com")) { }
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET email_verified_at = now() WHERE email = 'owner@x.com'");
        // Listing the test client as an app server lets the last upgrade name another limit IP, so the one a minute
        // recipient limit of the first link doesn't hold its email back.
        await SetPoliciesAsync(api, new { requireVerifiedEmail = true, trustedServerCidrs = new[] { "127.0.0.1/32", "::1/128" } });
        using var guest = await GuestAsync(api);
        var bearer = AuthApi.AccessToken(guest);
        var userId = AuthApi.UserId(guest);

        using (var missing = await UpgradeAsync(api, bearer, "ada@x.com"))
            Assert.Equal("invalid_request", missing.Code);

        var clock = Stopwatch.StartNew();
        using var free = await UpgradeAsync(api, bearer, "ada@x.com", name: "Ada", redirect: Redirect);
        Assert.True(clock.ElapsedMilliseconds >= 450, $"took {clock.ElapsedMilliseconds} ms");
        clock.Restart();
        using var taken = await UpgradeAsync(api, bearer, "OWNER@x.com", redirect: Redirect);
        Assert.True(clock.ElapsedMilliseconds >= 450, $"took {clock.ElapsedMilliseconds} ms");
        Assert.Equal(HttpStatusCode.OK, taken.Status);
        Assert.Equal(free.Document!.RootElement.GetRawText(), taken.Document!.RootElement.GetRawText());
        Assert.True(taken.Body.GetProperty("verificationRequired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, taken.Body.GetProperty("user").ValueKind);
        Assert.Equal("security_alert", (await api.LatestEmailAsync("owner@x.com"))!.Template);

        // The later call voided the earlier link; a fresh free upgrade sends a new one. Still a guest meanwhile, named.
        var voided = (await api.LatestEmailAsync("ada@x.com"))!;
        Assert.Equal("email_change", voided.Type);
        using (var stale = await api.SendAsync(HttpMethod.Post, "/v1/account/email/confirm", new { token = voided.Token }))
            Assert.Equal("invalid_email_token", stale.Code);
        using (await UpgradeAsync(api, bearer, "ada@x.com", redirect: Redirect, ip: "198.51.100.9")) { }
        Assert.True(await TestDatabase.ScalarAsync<bool>(api.Database.Superuser, "SELECT is_anonymous FROM orvano.auth_users WHERE id = @id", ("id", Guid.Parse(userId))));
        Assert.Equal("Ada", await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT name FROM orvano.auth_users WHERE id = @id", ("id", Guid.Parse(userId))));

        var link = (await api.LatestEmailAsync("ada@x.com"))!;
        using var confirmed = await api.SendAsync(HttpMethod.Post, "/v1/account/email/confirm", new { token = link.Token });
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        Assert.False(confirmed.Body.GetProperty("isAnonymous").GetBoolean());
        Assert.True(confirmed.Body.GetProperty("emailVerified").GetBoolean());
        using (var signIn = await api.SignInAsync("ada@x.com"))
            Assert.Equal(userId, AuthApi.UserId(signIn));
        using (var refreshed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(guest) }))
            Assert.False(Claims(refreshed.Body.GetProperty("accessToken").GetString()!).GetProperty("is_anonymous").GetBoolean());
    }

    [Fact]
    public async Task Under_required_mfa_the_upgrade_ends_the_guest_sessions_and_the_next_sign_in_enrolls()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        await EnableAsync(api);
        using (var on = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { mfaRequired = true })) Assert.Equal(HttpStatusCode.OK, on.Status);

        // A guest is never challenged.
        using var guest = await GuestAsync(api);
        Assert.Equal(HttpStatusCode.Created, guest.Status);
        Assert.Equal(JsonValueKind.Null, guest.Body.GetProperty("mfa").ValueKind);

        using var pending = await UpgradeAsync(api, AuthApi.AccessToken(guest), "ada@x.com", redirect: Redirect);
        Assert.True(pending.Body.GetProperty("verificationRequired").GetBoolean());
        using (var confirmed = await api.SendAsync(HttpMethod.Post, "/v1/account/email/confirm", new { token = (await api.LatestEmailAsync("ada@x.com"))!.Token }))
            Assert.Equal(HttpStatusCode.OK, confirmed.Status);

        using (var ended = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(guest) }))
            Assert.Equal("invalid_refresh_token", ended.Code);
        Assert.Equal("mfa_required", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT end_reason FROM orvano.auth_sessions WHERE user_id = @id", ("id", Guid.Parse(AuthApi.UserId(guest)))));
        using var next = await api.SignInAsync("ada@x.com");
        Assert.True(next.Body.GetProperty("mfa").GetProperty("enrollmentRequired").GetBoolean());
    }

    [Fact]
    public async Task Linking_a_provider_makes_a_guest_permanent_with_the_provider_email_when_it_is_free_and_allowed()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        await EnableAsync(api);

        // A blocked domain refuses the link and changes nothing.
        await SetPoliciesAsync(api, new { blockedEmailDomains = new[] { "blocked.com" } });
        using var refusedGuest = await GuestAsync(api);
        using (var refused = await LinkGitHubAsync(api, AuthApi.AccessToken(refusedGuest), "1", "gh@blocked.com"))
            Assert.Equal((HttpStatusCode.Forbidden, "email_domain_not_allowed"), (refused.Status, refused.Code));
        Assert.True(await TestDatabase.ScalarAsync<bool>(api.Database.Superuser,
            "SELECT is_anonymous FROM orvano.auth_users WHERE id = @id", ("id", Guid.Parse(AuthApi.UserId(refusedGuest)))));

        // No password or recent session asked: the guest's session is their only credential.
        using var guest = await GuestAsync(api);
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_sessions SET created_at = now() - interval '1 hour'");
        using var linked = await LinkGitHubAsync(api, AuthApi.AccessToken(guest), "2", "gh@x.com");
        Assert.Equal(HttpStatusCode.Created, linked.Status);
        using var me = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(guest));
        Assert.False(me.Body.GetProperty("isAnonymous").GetBoolean());
        Assert.Equal("gh@x.com", me.Body.GetProperty("email").GetString());
        Assert.True(me.Body.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(1L, await CountAsync(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.upgraded' AND payload->>'method' = 'oauth'"));
    }

    [Fact]
    public async Task The_settings_rules_the_list_filter_and_the_guest_limit()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using (var bad = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { anonymousIdleDays = 0 }))
            Assert.Equal("invalid_request", bad.Code);
        using var saved = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { anonymousEnabled = true, anonymousIdleDays = 7 });
        Assert.True(saved.Body.GetProperty("anonymousEnabled").GetBoolean());
        Assert.Equal(7, saved.Body.GetProperty("anonymousIdleDays").GetInt32());
        var changed = await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT payload::text FROM orvano.events WHERE type = 'auth.method_settings.updated' ORDER BY id DESC LIMIT 1");
        Assert.Contains("anonymousEnabled", changed, StringComparison.Ordinal);
        Assert.Contains("anonymousIdleDays", changed, StringComparison.Ordinal);

        using var guest = await GuestAsync(api);
        using (await api.SignUpAsync("ada@x.com")) { }
        using var guests = await api.AsServerAsync(HttpMethod.Get, "/v1/users?anonymous=true");
        Assert.Equal([AuthApi.UserId(guest)], guests.Body.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetString()));
        using var permanent = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/users?anonymous=false");
        Assert.Equal(["ada@x.com"], permanent.Body.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("email").GetString()));

        // AC-21: the project's editable limit, counted afresh from the change.
        await SetPoliciesAsync(api, new { anonymousPerIp = 1 });
        using (var first = await GuestAsync(api)) Assert.Equal(HttpStatusCode.Created, first.Status);
        using var limited = await GuestAsync(api);
        Assert.Equal((HttpStatusCode.TooManyRequests, "rate_limited"), (limited.Status, limited.Code));
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Retention_deletes_guests_idle_past_their_project_days_and_keeps_active_ones()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using (var settings = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { anonymousEnabled = true, anonymousIdleDays = 30 }))
            Assert.Equal(HttpStatusCode.OK, settings.Status);
        using var idle = await GuestAsync(api);
        using var active = await GuestAsync(api);
        using var permanent = await api.SignUpAsync("ada@x.com");
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_users SET created_at = now() - interval '40 days'");
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET created_at = now() - interval '40 days', last_refreshed_at = now() - interval '31 days'");
        // The active guest refreshed yesterday.
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET last_refreshed_at = now() - interval '1 day' WHERE user_id = @id", ("id", Guid.Parse(AuthApi.UserId(active))));

        await using var app = Npgsql.NpgsqlDataSource.Create(api.Database.AppUrl);
        Assert.Equal(1, await AuthRetention.DeleteIdleGuestsAsync(new AuthStore(app), Ct));

        var left = await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_users WHERE id = ANY(@ids)", ("ids", new[] { Guid.Parse(AuthApi.UserId(active)), Guid.Parse(AuthApi.UserId(permanent)) }));
        Assert.Equal(2L, left);
        Assert.Equal(1L, await CountAsync(api,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.deleted' AND payload->>'reason' = 'anonymous_idle' AND payload->>'userId' = @id",
            ("id", AuthApi.UserId(idle))));
        Assert.Equal(0, await AuthRetention.DeleteIdleGuestsAsync(new AuthStore(app), Ct));
    }

    private static async Task EnableAsync(AuthApi api)
    {
        using var on = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { anonymousEnabled = true });
        Assert.Equal(HttpStatusCode.OK, on.Status);
    }

    private static async Task SetPoliciesAsync(AuthApi api, object change)
    {
        using var saved = await api.AsConsoleAsync(HttpMethod.Patch, AuthPolicyTests.PoliciesUrl, change);
        Assert.True(saved.Status == HttpStatusCode.OK, $"{saved.Status}: {saved.Code}");
    }

    private static Task<Reply> GuestAsync(AuthApi api) => api.SendAsync(HttpMethod.Post, "/v1/account/sessions/anonymous");

    private static Task<Reply> UpgradeAsync(
        AuthApi api, string bearer, string email, string password = Password, string? name = null, string? redirect = null, string? ip = null)
    {
        var body = new Dictionary<string, string?> { ["email"] = email, ["password"] = password, ["name"] = name, ["verificationRedirectUrl"] = redirect }
            .Where(p => p.Value is not null).ToDictionary();
        return api.SendAsync(HttpMethod.Post, "/v1/account/anonymous/upgrade", body, bearer: bearer,
            headers: ip is null ? null : new Dictionary<string, string> { ["X-Orvano-Client-IP"] = ip });
    }

    private static async Task<Reply> LinkGitHubAsync(AuthApi api, string bearer, string sub, string email)
    {
        var (start, verifier) = await OAuthDriver.StartAsync(api, "github", bearer: bearer, path: "/v1/account/identities/oauth/flows");
        using (start)
        {
            Assert.True(start.Status == HttpStatusCode.OK, $"{start.Status}: {start.Code}");
            var back = await OAuthDriver.FollowAsync(api, start.Body.GetProperty("url").GetString()!, new { sub, email, emailVerified = true });
            return await api.SendAsync(HttpMethod.Post, "/v1/account/identities/oauth", new { code = OAuthDriver.Param(back, "orvano_code"), codeVerifier = verifier }, bearer: bearer);
        }
    }

    private static Task<long> CountAsync(AuthApi api, string sql, params (string Name, object Value)[] parameters) =>
        TestDatabase.ScalarAsync<long>(api.Database.Superuser, sql, parameters);
}
