using System.Net;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0012 build tasks 1 and 2 over HTTP against the real binary and the fake provider: redirect sign in with every
// provider, the handoff code's binding, lost states, provider errors, the verified email rules, and claiming.
// AC-4 to AC-8, AC-10 to AC-12, AC-16, AC-27.
public class OAuthTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Google_signs_a_new_user_in_and_a_second_sign_in_finds_the_same_user()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var ada = new { sub = "google-ada", email = "Ada@Example.com", emailVerified = true, name = "Ada Lovelace" };

        using var first = await OAuthDriver.SignInAsync(api, "google", ada);
        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.True(first.Body.GetProperty("isNewUser").GetBoolean());
        var user = first.Body.GetProperty("user");
        Assert.Equal("Ada@Example.com", user.GetProperty("email").GetString());
        Assert.True(user.GetProperty("emailVerified").GetBoolean());
        Assert.Equal("Ada Lovelace", user.GetProperty("name").GetString());

        using var second = await OAuthDriver.SignInAsync(api, "google", ada with { email = "ada@example.com" });
        Assert.Equal(HttpStatusCode.Created, second.Status);
        Assert.False(second.Body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(AuthApi.UserId(first), AuthApi.UserId(second));

        using var sessions = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions", bearer: AuthApi.AccessToken(second));
        Assert.All(sessions.Body.GetProperty("items").EnumerateArray(), s =>
        {
            Assert.Equal("oauth", s.GetProperty("method").GetString());
            Assert.Equal("google", s.GetProperty("provider").GetString());
        });

        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.auth_identities WHERE provider = 'google' AND subject = 'google-ada' AND email = 'ada@example.com'"));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.created' AND payload->>'method' = 'oauth' AND payload->>'provider' = 'google'"));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.identity.linked' AND payload->>'source' = 'sign_in'"));
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_oauth_flows"));
    }

    [Fact]
    public async Task The_code_works_once_only_with_its_verifier_and_only_for_sign_in()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var (start, verifier) = await OAuthDriver.StartAsync(api, "github");
        using (start)
        {
            var back = await OAuthDriver.FollowAsync(api, start.Body.GetProperty("url").GetString()!, new { sub = "4242", email = "gh@x.com", emailVerified = true });
            Assert.Equal("oauth", OAuthDriver.Param(back, "orvano_type"));
            var code = OAuthDriver.Param(back, "orvano_code")!;

            using var wrong = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code, codeVerifier = OAuthDriver.Pkce().Verifier });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
            Assert.Equal("invalid_oauth_code", wrong.Code);

            using var right = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code, codeVerifier = verifier });
            Assert.Equal(HttpStatusCode.Created, right.Status);

            using var again = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code, codeVerifier = verifier });
            Assert.Equal("invalid_oauth_code", again.Code);

            using var malformed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code = "orv_oc_short", codeVerifier = verifier });
            Assert.Equal("invalid_request", malformed.Code);
        }

        // A code past its 2 minutes.
        var (late, lateVerifier) = await OAuthDriver.StartAsync(api, "google");
        using (late)
        {
            var back = await OAuthDriver.FollowAsync(api, late.Body.GetProperty("url").GetString()!, new { sub = "late", email = "late@x.com", emailVerified = true });
            await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_oauth_flows SET expires_at = now() - interval '1 second'");
            using var expired = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth",
                new { code = OAuthDriver.Param(back, "orvano_code"), codeVerifier = lateVerifier });
            Assert.Equal("invalid_oauth_code", expired.Code);
        }
    }

    [Fact]
    public async Task A_lost_state_gets_the_static_page_and_no_redirect()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = api.Http.BaseAddress };

        var (start, _) = await OAuthDriver.StartAsync(api, "google");
        string state;
        using (start) state = OAuthDriver.Param(new Uri(start.Body.GetProperty("url").GetString()!), "state")!;

        foreach (var url in new[]
                 {
                     $"/v1/projects/{AuthApi.Project}/oauth/google/callback?state=unknown&code=x",
                     $"/v1/projects/{AuthApi.Project}/oauth/github/callback?state={Uri.EscapeDataString(state)}&code=x",
                     $"/v1/projects/{AuthApi.OtherProject}/oauth/google/callback?state={Uri.EscapeDataString(state)}&code=x",
                     $"/v1/projects/{AuthApi.Project}/oauth/google/callback",
                 })
        {
            using var page = await http.GetAsync(url, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, page.StatusCode);
            Assert.Null(page.Headers.Location);
            Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
            Assert.Equal("default-src 'none'; style-src 'unsafe-inline'", page.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal("noindex", page.Headers.GetValues("X-Robots-Tag").Single());
            Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
            Assert.Contains("Go back to the app", await page.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        // The right state on its own path still works once, then is spent.
        using var cancelled = await http.GetAsync($"/v1/projects/{AuthApi.Project}/oauth/google/callback?state={Uri.EscapeDataString(state)}&error=access_denied", Ct);
        Assert.Equal(HttpStatusCode.Found, cancelled.StatusCode);
        Assert.Equal("oauth_access_denied", OAuthDriver.Param(cancelled.Headers.Location!, "orvano_error"));
        Assert.Equal("no-store", cancelled.Headers.CacheControl!.ToString());
        using var spent = await http.GetAsync($"/v1/projects/{AuthApi.Project}/oauth/google/callback?state={Uri.EscapeDataString(state)}&code=x", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, spent.StatusCode);
    }

    [Fact]
    public async Task Start_checks_the_body_the_redirect_and_the_provider_in_order()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var (_, challenge) = OAuthDriver.Pkce();

        using var badChallenge = await api.SendAsync(HttpMethod.Post, "/v1/account/oauth/flows", new { provider = "google", redirectUrl = OAuthDriver.Redirect, codeChallenge = "short" });
        Assert.Equal("invalid_request", badChallenge.Code);
        using var badProvider = await api.SendAsync(HttpMethod.Post, "/v1/account/oauth/flows", new { provider = "yahoo", redirectUrl = OAuthDriver.Redirect, codeChallenge = challenge });
        Assert.Equal(HttpStatusCode.BadRequest, badProvider.Status);
        using var badRedirect = await api.SendAsync(HttpMethod.Post, "/v1/account/oauth/flows", new { provider = "google", redirectUrl = "https://evil.example/cb", codeChallenge = challenge });
        Assert.Equal("redirect_url_not_allowed", badRedirect.Code);

        // A custom scheme naming a registered app is allowed here, unlike for magic links.
        using var app = await api.SendAsync(HttpMethod.Post, "/v1/account/oauth/flows", new { provider = "google", redirectUrl = "com.acme.app://auth", codeChallenge = challenge });
        Assert.Equal(HttpStatusCode.OK, app.Status);
        var url = new Uri(app.Body.GetProperty("url").GetString()!);
        Assert.Equal(AuthApi.GoogleWebClient, OAuthDriver.Param(url, "client_id"));
        Assert.Equal("openid email profile", OAuthDriver.Param(url, "scope"));
        Assert.Equal("S256", OAuthDriver.Param(url, "code_challenge_method"));
        Assert.NotNull(OAuthDriver.Param(url, "nonce"));
        Assert.EndsWith($"/v1/projects/{AuthApi.Project}/oauth/google/callback", OAuthDriver.Param(url, "redirect_uri"), StringComparison.Ordinal);

        using var otherProject = await api.SendAsync(HttpMethod.Post, "/v1/account/oauth/flows",
            new { provider = "google", redirectUrl = "http://localhost:3000/cb", codeChallenge = challenge }, project: AuthApi.OtherProject);
        Assert.Equal("provider_not_enabled", otherProject.Code);

        // No row holds a usable secret.
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_oauth_flows WHERE octet_length(state_hash) <> 32 OR octet_length(nonce_hash) <> 32"));
    }

    [Fact]
    public async Task Apple_posts_the_form_and_its_first_name_sets_the_user_name()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        using var first = await OAuthDriver.SignInAsync(api, "apple", new { sub = "apple-001", email = "relay@privaterelay.appleid.com", emailVerified = true, name = "Grace Hopper" });
        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal("Grace Hopper", first.Body.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal("relay@privaterelay.appleid.com", first.Body.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.auth_identities WHERE provider = 'apple' AND provider_refresh_ciphertext IS NOT NULL"));
        var sealedBefore = await TestDatabase.ScalarAsync<byte[]>(api.Database.Superuser, "SELECT provider_refresh_ciphertext FROM orvano.auth_identities WHERE provider = 'apple'");

        using var second = await OAuthDriver.SignInAsync(api, "apple", new { sub = "apple-001", email = "relay@privaterelay.appleid.com", emailVerified = true, name = "Someone Else" });
        Assert.Equal("Grace Hopper", second.Body.GetProperty("user").GetProperty("name").GetString());
        var sealedAfter = await TestDatabase.ScalarAsync<byte[]>(api.Database.Superuser, "SELECT provider_refresh_ciphertext FROM orvano.auth_identities WHERE provider = 'apple'");
        Assert.NotEqual(sealedBefore, sealedAfter);
    }

    [Fact]
    public async Task Only_verified_emails_find_or_create_a_user()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        using var owner = await api.SignUpAsync("owner@x.com");
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET email_verified_at = now() WHERE email = 'owner@x.com'");

        // GitHub with no verified primary email: a user with no email.
        using var github = await OAuthDriver.SignInAsync(api, "github", new { sub = "777", email = "owner@x.com", emailVerified = false, name = "Octo" });
        Assert.Equal(HttpStatusCode.Created, github.Status);
        Assert.True(github.Body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(JsonValueKind.Null, github.Body.GetProperty("user").GetProperty("email").ValueKind);

        // Microsoft without xms_edov never links by email.
        using var microsoft = await OAuthDriver.SignInAsync(api, "microsoft", new { sub = "oid-1", email = "owner@x.com", tid = "9188040d-6c67-4c5b-b112-36a304b66dad" });
        Assert.True(microsoft.Body.GetProperty("isNewUser").GetBoolean());
        Assert.NotEqual(AuthApi.UserId(owner), AuthApi.UserId(microsoft));

        // Microsoft with xms_edov links to the verified owner, and its subject is tid:oid.
        using var edov = await OAuthDriver.SignInAsync(api, "microsoft", new { sub = "oid-2", email = "owner@x.com", tid = "9188040d-6c67-4c5b-b112-36a304b66dad", xmsEdov = true });
        Assert.False(edov.Body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(AuthApi.UserId(owner), AuthApi.UserId(edov));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.auth_identities WHERE subject = '9188040d-6c67-4c5b-b112-36a304b66dad:oid-2'"));

        // Google with email_verified false creates an email less user.
        using var google = await OAuthDriver.SignInAsync(api, "google", new { sub = "g-unverified", email = "owner@x.com", emailVerified = false });
        Assert.True(google.Body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(JsonValueKind.Null, google.Body.GetProperty("user").GetProperty("email").ValueKind);

        // A verified owner whose Google account changed subject is refused, not relinked.
        using var first = await OAuthDriver.SignInAsync(api, "google", new { sub = "g-owner-1", email = "owner@x.com", emailVerified = true });
        Assert.Equal(AuthApi.UserId(owner), AuthApi.UserId(first));
        using var replaced = await OAuthDriver.SignInAsync(api, "google", new { sub = "g-owner-2", email = "owner@x.com", emailVerified = true });
        Assert.Equal("provider_already_linked", replaced.Code);
    }

    [Fact]
    public async Task A_verified_sign_in_claims_an_impostors_unverified_account()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        using var impostor = await api.SignUpAsync("victim@x.com");
        // The impostor links GitHub to the unverified account by signing in with an account that has no verified email.
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            $"INSERT INTO orvano.auth_identities (project_id, user_id, provider, subject, email_verified) VALUES ('{AuthApi.Project}', '{AuthApi.UserId(impostor)}', 'github', '666', false)");

        using var victim = await OAuthDriver.SignInAsync(api, "google", new { sub = "victim-google", email = "victim@x.com", emailVerified = true });
        Assert.Equal(HttpStatusCode.Created, victim.Status);
        Assert.False(victim.Body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(AuthApi.UserId(impostor), AuthApi.UserId(victim));
        Assert.True(victim.Body.GetProperty("user").GetProperty("emailVerified").GetBoolean());

        Assert.Equal(0L, await Count(api, $"SELECT count(*) FROM orvano.auth_passwords WHERE user_id = '{AuthApi.UserId(victim)}'"));
        Assert.Equal(["google"], await Strings(api, $"SELECT provider FROM orvano.auth_identities WHERE user_id = '{AuthApi.UserId(victim)}'"));
        using var oldSession = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(impostor));
        Assert.Equal("invalid_token", oldSession.Code);
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.identity.unlinked' AND payload->>'reason' = 'claimed'"));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.password.removed'"));
    }

    [Fact]
    public async Task Recovering_an_unverified_account_drops_an_identity_planted_on_it()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true, email: true, smtp: true);
        using var impostor = await api.SignUpAsync("victim@x.com");
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            $"INSERT INTO orvano.auth_identities (project_id, user_id, provider, subject, email_verified) VALUES ('{AuthApi.Project}', '{AuthApi.UserId(impostor)}', 'github', '666', false)");

        using var request = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "victim@x.com", redirectUrl = OAuthDriver.Redirect });
        Assert.Equal(HttpStatusCode.Accepted, request.Status);
        var email = await api.LatestEmailAsync("victim@x.com");
        using var reset = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token = email!.Token, password = "another horse battery" });
        Assert.Equal(HttpStatusCode.Created, reset.Status);

        Assert.Equal(0L, await Count(api, $"SELECT count(*) FROM orvano.auth_identities WHERE user_id = '{AuthApi.UserId(reset)}'"));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.identity.unlinked' AND payload->>'reason' = 'claimed'"));
        using var oldSession = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(impostor));
        Assert.Equal("invalid_token", oldSession.Code);
        using var newSession = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(reset));
        Assert.Equal(HttpStatusCode.OK, newSession.Status);
    }

    [Fact]
    public async Task Verifying_an_unverified_account_drops_an_identity_planted_on_it_and_keeps_the_password()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true, email: true, smtp: true);
        using var impostor = await api.SignUpAsync("victim@x.com", extra: new { email = "victim@x.com", password = "correct horse battery", verificationRedirectUrl = OAuthDriver.Redirect });
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            $"INSERT INTO orvano.auth_identities (project_id, user_id, provider, subject, email_verified) VALUES ('{AuthApi.Project}', '{AuthApi.UserId(impostor)}', 'github', '666', false)");

        var email = await api.LatestEmailAsync("victim@x.com");
        using var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = email!.Token });
        Assert.Equal(HttpStatusCode.OK, verified.Status);

        Assert.Equal(0L, await Count(api, $"SELECT count(*) FROM orvano.auth_identities WHERE user_id = '{AuthApi.UserId(impostor)}'"));
        Assert.Equal(1L, await Count(api, $"SELECT count(*) FROM orvano.auth_passwords WHERE user_id = '{AuthApi.UserId(impostor)}'"));
        using var oldSession = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(impostor));
        Assert.Equal("invalid_token", oldSession.Code);
    }

    [Fact]
    public async Task A_blocked_user_is_refused_and_the_code_still_works_until_it_expires()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var user = new { sub = "blocked-1", email = "blocked@x.com", emailVerified = true };
        using var first = await OAuthDriver.SignInAsync(api, "google", user);
        using (await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{AuthApi.UserId(first)}/block")) { }

        using var refused = await OAuthDriver.SignInAsync(api, "google", user);
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Equal("user_blocked", refused.Code);
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.auth_oauth_flows WHERE code_hash IS NOT NULL"));
    }

    [Fact]
    public async Task Twenty_parallel_redemptions_of_one_code_give_one_session()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var (start, verifier) = await OAuthDriver.StartAsync(api, "google");
        using (start)
        {
            var back = await OAuthDriver.FollowAsync(api, start.Body.GetProperty("url").GetString()!, new { sub = "race", email = "race@x.com", emailVerified = true });
            var code = OAuthDriver.Param(back, "orvano_code");
            var replies = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
                api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code, codeVerifier = verifier })));
            Assert.Single(replies, r => r.Status == HttpStatusCode.Created);
            Assert.All(replies.Where(r => r.Status != HttpStatusCode.Created), r => Assert.Equal("invalid_oauth_code", r.Code));
            foreach (var reply in replies) reply.Dispose();
        }

        // Two first sign ins of one provider account at once: one user, one identity, never a 500.
        var both = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => OAuthDriver.SignInAsync(api, "github", new { sub = "9001", email = "twin@x.com", emailVerified = true })));
        Assert.All(both, r => Assert.Equal(HttpStatusCode.Created, r.Status));
        Assert.Equal(AuthApi.UserId(both[0]), AuthApi.UserId(both[1]));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.auth_identities WHERE subject = '9001'"));
        foreach (var reply in both) reply.Dispose();
    }

    [Fact]
    public async Task A_slow_provider_is_unavailable_and_an_error_in_a_200_is_a_provider_error()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);

        var (slow, _) = await OAuthDriver.StartAsync(api, "google");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var hung = await OAuthDriver.FollowAsync(api, slow.Body.GetProperty("url").GetString()!, new { sub = "slow", hang = "token" });
        Assert.Equal("provider_unavailable", OAuthDriver.Param(hung, "orvano_error"));
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(16));
        slow.Dispose();

        var (bad, _) = await OAuthDriver.StartAsync(api, "github");
        var refused = await OAuthDriver.FollowAsync(api, bad.Body.GetProperty("url").GetString()!, new { sub = "1", tokenError = "bad_verification_code" });
        Assert.Equal("provider_error", OAuthDriver.Param(refused, "orvano_error"));
        Assert.Equal("oauth", OAuthDriver.Param(refused, "orvano_type"));
        bad.Dispose();

        // A failed flow row is gone.
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_oauth_flows"));
    }

    [Fact]
    public async Task Sixty_failed_redemptions_from_one_ip_refuse_even_a_valid_code()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var (start, verifier) = await OAuthDriver.StartAsync(api, "github");
        var back = await OAuthDriver.FollowAsync(api, start.Body.GetProperty("url").GetString()!, new { sub = "60", email = "limit@x.com", emailVerified = true });
        start.Dispose();

        for (var i = 0; i < 60; i++)
        {
            using var wrong = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth",
                new { code = "orv_oc_" + new string((char)('A' + (i % 26)), 43), codeVerifier = verifier });
            Assert.Equal("invalid_oauth_code", wrong.Code);
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code = OAuthDriver.Param(back, "orvano_code"), codeVerifier = verifier });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.Equal("rate_limited", limited.Code);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    private static Task<long> Count(AuthApi api, string sql) => TestDatabase.ScalarAsync<long>(api.Database.Superuser, sql);

    private static async Task<string[]> Strings(AuthApi api, string sql)
    {
        await using var cmd = api.Database.Superuser.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        var found = new List<string>();
        while (await reader.ReadAsync(Ct)) found.Add(reader.GetString(0));
        return [.. found];
    }
}
