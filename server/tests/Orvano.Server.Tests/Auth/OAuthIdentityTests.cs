using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0012 build tasks 3, 4, and 6 over HTTP: native ID token sign in, linking and unlinking, Apple's revoke on
// deletion, the users' providers, and the console's provider settings. AC-1 to AC-3, AC-9, AC-13 to AC-16, AC-25.
public class OAuthIdentityTests(PostgresFixture postgres)
{
    private const string Nonce = "nonce-0123456789abcdef";

    [Fact]
    public async Task Native_google_and_apple_tokens_sign_in_once_each()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);

        var google = await MintAsync(api, "google", AuthApi.GoogleNativeClient, "g-native", "native@x.com");
        using var signedIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new { provider = "google", idToken = google.IdToken, nonce = Nonce });
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        Assert.True(signedIn.Body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(["google"], signedIn.Body.GetProperty("user").GetProperty("providers").EnumerateArray().Select(p => p.GetString()));
        Assert.False(signedIn.Body.GetProperty("user").GetProperty("hasPassword").GetBoolean());

        using var replay = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new { provider = "google", idToken = google.IdToken, nonce = Nonce });
        Assert.Equal("invalid_id_token", replay.Code);

        // The web client ID is an audience too (Android's Credential Manager tokens carry it).
        var web = await MintAsync(api, "google", AuthApi.GoogleWebClient, "g-native", "native@x.com");
        using var again = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new { provider = "google", idToken = web.IdToken, nonce = Nonce });
        Assert.False(again.Body.GetProperty("isNewUser").GetBoolean());

        var apple = await MintAsync(api, "apple", AuthApi.AppleBundleId, "a-native", "relay@privaterelay.appleid.com");
        using var noCode = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new { provider = "apple", idToken = apple.IdToken, nonce = Nonce });
        Assert.Equal("invalid_request", noCode.Code);
        using var appleIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token",
            new { provider = "apple", idToken = apple.IdToken, nonce = Nonce, authorizationCode = apple.Code, name = "Grace Hopper" });
        Assert.Equal(HttpStatusCode.Created, appleIn.Status);
        Assert.Equal("Grace Hopper", appleIn.Body.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.auth_identities WHERE provider = 'apple' AND provider_refresh_ciphertext IS NOT NULL"));
        using var sessions = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions", bearer: AuthApi.AccessToken(appleIn));
        Assert.Equal("id_token", sessions.Body.GetProperty("items")[0].GetProperty("method").GetString());
        Assert.Equal("apple", sessions.Body.GetProperty("items")[0].GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Native_tokens_for_the_wrong_audience_nonce_or_time_are_refused_alike()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);

        foreach (var (provider, aud, expiresIn, nonce) in new[]
                 {
                     ("google", "someone-elses-client", 600, Nonce),
                     ("apple", AuthApi.AppleServicesId, 600, Nonce),
                     ("google", AuthApi.GoogleNativeClient, -120, Nonce),
                     ("google", AuthApi.GoogleNativeClient, 600, "another-nonce-0123456789"),
                 })
        {
            var minted = await MintAsync(api, provider, aud, "s-" + aud, null, expiresIn);
            using var refused = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token",
                new { provider, idToken = minted.IdToken, nonce, authorizationCode = minted.Code ?? "unused-code" });
            Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
            Assert.Equal("invalid_id_token", refused.Code);
        }

        using var garbage = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new { provider = "google", idToken = "not.a.token", nonce = Nonce });
        Assert.Equal("invalid_id_token", garbage.Code);
        using var shortNonce = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new { provider = "google", idToken = "x", nonce = "short" });
        Assert.Equal("invalid_request", shortNonce.Code);
        using var github = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new { provider = "github", idToken = "x", nonce = Nonce });
        Assert.Equal(HttpStatusCode.BadRequest, github.Status);
        using var notEnabled = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new { provider = "google", idToken = "x", nonce = Nonce },
            project: AuthApi.OtherProject);
        Assert.Equal("provider_not_enabled", notEnabled.Code);
    }

    [Fact]
    public async Task A_fresh_session_links_by_redirect_and_natively_and_unlinking_keeps_a_way_in()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        using var user = await OAuthOnlyUserAsync(api, "only-google", emailVerified: false);
        var bearer = AuthApi.AccessToken(user);

        // GitHub by redirect.
        var (start, verifier) = await OAuthDriver.StartAsync(api, "github", bearer: bearer, path: "/v1/account/identities/oauth/flows");
        Assert.Equal(HttpStatusCode.OK, start.Status);
        var back = await OAuthDriver.FollowAsync(api, start.Body.GetProperty("url").GetString()!, new { sub = "5150", email = "gh@x.com", emailVerified = true });
        Assert.Equal("oauth_link", OAuthDriver.Param(back, "orvano_type"));
        var code = OAuthDriver.Param(back, "orvano_code");
        using (var asSignIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code, codeVerifier = verifier }))
            Assert.Equal("invalid_oauth_code", asSignIn.Code);
        using var linked = await api.SendAsync(HttpMethod.Post, "/v1/account/identities/oauth", new { code, codeVerifier = verifier }, bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, linked.Status);
        Assert.Equal("github", linked.Body.GetProperty("provider").GetString());
        Assert.Equal("5150", linked.Body.GetProperty("subject").GetString());
        start.Dispose();

        // Apple natively.
        var apple = await MintAsync(api, "apple", AuthApi.AppleBundleId, "a-link", null);
        using var appleLinked = await api.SendAsync(HttpMethod.Post, "/v1/account/identities/id-token",
            new { provider = "apple", idToken = apple.IdToken, nonce = Nonce, authorizationCode = apple.Code }, bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, appleLinked.Status);

        using var list = await api.SendAsync(HttpMethod.Get, "/v1/account/identities", bearer: bearer);
        Assert.Equal(["google", "github", "apple"], list.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("provider").GetString()));
        using var me = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: bearer);
        Assert.Equal(["apple", "github", "google"], me.Body.GetProperty("providers").EnumerateArray().Select(p => p.GetString()));

        // Unlink down to one; the last is refused, for the user, a server, and the console alike.
        var ids = list.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToArray();
        using (var first = await api.SendAsync(HttpMethod.Delete, $"/v1/account/identities/{ids[0]}", bearer: bearer)) Assert.Equal(HttpStatusCode.NoContent, first.Status);
        using (var second = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{AuthApi.UserId(user)}/identities/{ids[1]}")) Assert.Equal(HttpStatusCode.NoContent, second.Status);
        using var last = await api.AsConsoleAsync(HttpMethod.Delete, $"/v1/console/project/users/{AuthApi.UserId(user)}/identities/{ids[2]}");
        Assert.Equal("last_sign_in_method", last.Code);
        using var other = await api.SendAsync(HttpMethod.Delete, $"/v1/account/identities/{Guid.NewGuid()}", bearer: bearer);
        Assert.Equal("identity_not_found", other.Code);

        // The refused unlink left the Apple identity, so nothing was revoked; one unlink was by a server.
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.jobs WHERE kind = 'auth.apple.revoke'"));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.identity.unlinked' AND payload->>'reason' = 'server'"));
    }

    [Fact]
    public async Task A_verified_email_or_an_email_with_a_password_is_another_way_in_but_an_unverified_email_alone_is_not()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);

        // A verified email from the provider: the user may unlink their only identity (AC-14, verify.md check 12).
        using var verified = await OAuthOnlyUserAsync(api, "verified-google", emailVerified: true);
        var verifiedBearer = AuthApi.AccessToken(verified);
        using var verifiedList = await api.SendAsync(HttpMethod.Get, "/v1/account/identities", bearer: verifiedBearer);
        var only = verifiedList.Body.GetProperty("items")[0].GetProperty("id").GetString();
        using (var unlinked = await api.SendAsync(HttpMethod.Delete, $"/v1/account/identities/{only}", bearer: verifiedBearer))
            Assert.Equal(HttpStatusCode.NoContent, unlinked.Status);

        // An unverified email with a password: unlinking the only identity is allowed.
        using var signedUp = await api.SignUpAsync("password-user@x.com");
        var bearer = AuthApi.AccessToken(signedUp);
        var (start, verifier) = await OAuthDriver.StartAsync(api, "github", bearer: bearer, path: "/v1/account/identities/oauth/flows", password: MfaTests.Password);
        var back = await OAuthDriver.FollowAsync(api, start.Body.GetProperty("url").GetString()!, new { sub = "4242", email = "gh-pw@x.com", emailVerified = true });
        start.Dispose();
        using var linked = await api.SendAsync(HttpMethod.Post, "/v1/account/identities/oauth",
            new { code = OAuthDriver.Param(back, "orvano_code"), codeVerifier = verifier }, bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, linked.Status);
        var github = linked.Body.GetProperty("id").GetString();
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET email_verified_at = NULL WHERE lower(email) = 'password-user@x.com'");

        // The same user without the password: the unverified email alone is no way in, so the unlink is refused.
        var hash = await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT hash FROM orvano.auth_passwords WHERE user_id = (SELECT id FROM orvano.auth_users WHERE lower(email) = 'password-user@x.com')");
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "DELETE FROM orvano.auth_passwords WHERE user_id = (SELECT id FROM orvano.auth_users WHERE lower(email) = 'password-user@x.com')");
        using var refused = await api.SendAsync(HttpMethod.Delete, $"/v1/account/identities/{github}", bearer: bearer);
        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("last_sign_in_method", refused.Code);
        using var stillThere = await api.SendAsync(HttpMethod.Get, "/v1/account/identities", bearer: bearer);
        Assert.Single(stillThere.Body.GetProperty("items").EnumerateArray());

        // With the password back, the same unlink goes through.
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            """
            INSERT INTO orvano.auth_passwords (user_id, project_id, hash)
            SELECT id, project_id, @hash FROM orvano.auth_users WHERE lower(email) = 'password-user@x.com'
            """, ("hash", hash));
        using var allowed = await api.SendAsync(HttpMethod.Delete, $"/v1/account/identities/{github}", bearer: bearer);
        Assert.Equal(HttpStatusCode.NoContent, allowed.Status);
    }

    [Fact]
    public async Task Linking_needs_a_fresh_session_its_own_flow_and_a_free_provider_account()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        using var victim = await OAuthOnlyUserAsync(api, "victim-google", emailVerified: true);
        using var attacker = await OAuthOnlyUserAsync(api, "attacker-google", emailVerified: true);
        using var holder = await OAuthDriver.SignInAsync(api, "github", new { sub = "777", email = "holder@x.com", emailVerified = true });

        // The attacker starts a link flow; the victim redeems the code: refused, because the flow is the attacker's.
        var (start, verifier) = await OAuthDriver.StartAsync(api, "github", bearer: AuthApi.AccessToken(attacker), path: "/v1/account/identities/oauth/flows");
        var back = await OAuthDriver.FollowAsync(api, start.Body.GetProperty("url").GetString()!, new { sub = "888", email = "a@x.com", emailVerified = true });
        using var stolen = await api.SendAsync(HttpMethod.Post, "/v1/account/identities/oauth",
            new { code = OAuthDriver.Param(back, "orvano_code"), codeVerifier = verifier }, bearer: AuthApi.AccessToken(victim));
        Assert.Equal("invalid_oauth_code", stolen.Code);
        start.Dispose();

        // A provider account linked to someone else.
        var (taken, takenVerifier) = await OAuthDriver.StartAsync(api, "github", bearer: AuthApi.AccessToken(victim), path: "/v1/account/identities/oauth/flows");
        var takenBack = await OAuthDriver.FollowAsync(api, taken.Body.GetProperty("url").GetString()!, new { sub = "777", email = "holder@x.com", emailVerified = true });
        using var conflict = await api.SendAsync(HttpMethod.Post, "/v1/account/identities/oauth",
            new { code = OAuthDriver.Param(takenBack, "orvano_code"), codeVerifier = takenVerifier }, bearer: AuthApi.AccessToken(victim));
        Assert.Equal("identity_already_linked", conflict.Code);
        taken.Dispose();

        // A provider the user already has.
        var (twice, _) = await OAuthDriver.StartAsync(api, "google", bearer: AuthApi.AccessToken(victim), path: "/v1/account/identities/oauth/flows");
        Assert.Equal("provider_already_linked", twice.Code);
        twice.Dispose();

        // An 11 minute old session.
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_sessions SET created_at = now() - interval '11 minutes'");
        var (stale, _) = await OAuthDriver.StartAsync(api, "microsoft", bearer: AuthApi.AccessToken(victim), path: "/v1/account/identities/oauth/flows");
        Assert.Equal("reauthentication_required", stale.Code);
        stale.Dispose();
    }

    [Fact]
    public async Task Linking_apple_natively_keeps_the_name_apple_sent_once()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        using var user = await OAuthOnlyUserAsync(api, "nameless-google", emailVerified: false);
        var apple = await MintAsync(api, "apple", AuthApi.AppleBundleId, "a-named", null);

        using var linked = await api.SendAsync(HttpMethod.Post, "/v1/account/identities/id-token",
            new { provider = "apple", idToken = apple.IdToken, nonce = Nonce, authorizationCode = apple.Code, name = "Grace Hopper" }, bearer: AuthApi.AccessToken(user));

        Assert.Equal(HttpStatusCode.Created, linked.Status);
        Assert.Equal("Grace Hopper", await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT name FROM orvano.auth_users WHERE id = @id::uuid", ("id", AuthApi.UserId(user))));
    }

    [Fact]
    public async Task Deleting_a_user_queues_apple_revokes_before_the_cascade()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var apple = await MintAsync(api, "apple", AuthApi.AppleBundleId, "a-delete", "del@privaterelay.appleid.com");
        using var signedIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token",
            new { provider = "apple", idToken = apple.IdToken, nonce = Nonce, authorizationCode = apple.Code });
        using (var deleted = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{AuthApi.UserId(signedIn)}")) Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.jobs WHERE kind = 'auth.apple.revoke' AND queue = 'auth' AND max_attempts = 8"));
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.jobs WHERE kind = 'auth.apple.revoke' AND payload::text LIKE '%fake-refresh%'"));
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_identities"));
    }

    [Fact]
    public async Task The_console_saves_settings_by_the_rules_and_never_returns_a_secret()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        await api.GrantAsync(AuthApi.OtherConsoleUsers[1], "viewer");

        using var list = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/auth/providers");
        Assert.Equal(["google", "apple", "github", "microsoft"], list.Body.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("provider").GetString()));
        var google = list.Body.GetProperty("items")[0];
        Assert.Equal("0001", google.GetProperty("clientSecretHint").GetString());
        Assert.True(google.GetProperty("redirectReady").GetBoolean());
        Assert.EndsWith($"/v1/projects/{AuthApi.Project}/oauth/google/callback", google.GetProperty("callbackUrl").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("google-secret", list.Body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", list.Body.GetRawText(), StringComparison.Ordinal);

        // Leaving the secret out keeps it; null clears it, which an enabled GitHub can't survive.
        using var kept = await api.AsConsoleAsync(HttpMethod.Put, "/v1/console/project/auth/providers/github", new { enabled = true, clientId = "github-client-2" });
        Assert.Equal(HttpStatusCode.OK, kept.Status);
        Assert.Equal("0001", kept.Body.GetProperty("clientSecretHint").GetString());
        using var cleared = await api.AsConsoleAsync(HttpMethod.Put, "/v1/console/project/auth/providers/github", new { enabled = true, clientId = "github-client-2", clientSecret = (string?)null });
        Assert.Equal("invalid_request", cleared.Code);
        using var off = await api.AsConsoleAsync(HttpMethod.Put, "/v1/console/project/auth/providers/github", new { enabled = false, clientId = "github-client-2", clientSecret = (string?)null });
        Assert.False(off.Body.GetProperty("clientSecretSet").GetBoolean());

        using var appleNoKey = await api.AsConsoleAsync(HttpMethod.Put, "/v1/console/project/auth/providers/apple",
            new { enabled = true, clientId = "svc", appleTeamId = "TEAM123456", appleKeyId = "KEY1234567", applePrivateKey = (string?)null });
        Assert.Equal("invalid_request", appleNoKey.Code);
        using var badKey = await api.AsConsoleAsync(HttpMethod.Put, "/v1/console/project/auth/providers/apple",
            new { enabled = false, applePrivateKey = "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----" });
        Assert.Equal("invalid_request", badKey.Code);

        using var viewer = await api.AsConsoleAsync(HttpMethod.Put, "/v1/console/project/auth/providers/google", new { enabled = false }, account: AuthApi.OtherConsoleUsers[1]);
        Assert.Equal(HttpStatusCode.Forbidden, viewer.Status);
        using var viewerList = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/auth/providers", account: AuthApi.OtherConsoleUsers[1]);
        Assert.Equal(HttpStatusCode.OK, viewerList.Status);

        using (var deleted = await api.AsConsoleAsync(HttpMethod.Delete, "/v1/console/project/auth/providers/microsoft")) Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.provider.deleted'"));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.provider.updated' AND payload->'changed' ? 'clientSecret' AND payload->'actor'->>'type' = 'user'"));
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_oauth_providers WHERE position('secret'::bytea in coalesce(client_secret_ciphertext, ''::bytea)) > 0 OR position('PRIVATE'::bytea in coalesce(apple_private_key_ciphertext, ''::bytea)) > 0"));
    }

    [Fact]
    public async Task A_provider_ready_only_one_way_is_not_configured_for_the_other()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);

        // Google with its web client ID and no secret: native ready, not redirect ready.
        using (var google = await api.AsConsoleAsync(HttpMethod.Put, "/v1/console/project/auth/providers/google",
                   new { enabled = true, clientId = AuthApi.GoogleWebClient, clientSecret = (string?)null, clientIdsExtra = Array.Empty<string>() }))
        {
            Assert.False(google.Body.GetProperty("redirectReady").GetBoolean());
            Assert.True(google.Body.GetProperty("nativeReady").GetBoolean());
        }

        var (start, _) = await OAuthDriver.StartAsync(api, "google");
        using (start)
        {
            Assert.Equal(HttpStatusCode.Conflict, start.Status);
            Assert.Equal("provider_not_configured", start.Code);
        }

        var web = await MintAsync(api, "google", AuthApi.GoogleWebClient, "g-web-only", "web-only@x.com");
        using (var native = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new { provider = "google", idToken = web.IdToken, nonce = Nonce }))
            Assert.Equal(HttpStatusCode.Created, native.Status);

        // Apple with its Services ID and key but no bundle ID: redirect ready, not native ready.
        using (var apple = await api.AsConsoleAsync(HttpMethod.Put, "/v1/console/project/auth/providers/apple",
                   new { enabled = true, clientId = AuthApi.AppleServicesId, clientIdsExtra = Array.Empty<string>(), appleTeamId = "TEAM123456", appleKeyId = "KEY1234567" }))
        {
            Assert.True(apple.Body.GetProperty("redirectReady").GetBoolean());
            Assert.False(apple.Body.GetProperty("nativeReady").GetBoolean());
        }

        var token = await MintAsync(api, "apple", AuthApi.AppleBundleId, "a-no-bundle", "no-bundle@x.com");
        using var refused = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token",
            new { provider = "apple", idToken = token.IdToken, nonce = Nonce, authorizationCode = token.Code });
        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("provider_not_configured", refused.Code);

        var (appleStart, _) = await OAuthDriver.StartAsync(api, "apple");
        using (appleStart) Assert.Equal(HttpStatusCode.OK, appleStart.Status);
    }

    [Fact]
    public async Task A_redirect_and_a_native_first_sign_in_for_one_new_email_at_once_give_one_user_with_two_identities()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);

        for (var round = 0; round < 3; round++)
        {
            var email = $"both-{round}@x.com";
            var apple = await MintAsync(api, "apple", AuthApi.AppleBundleId, $"a-both-{round}", email);
            var replies = await Task.WhenAll(
                OAuthDriver.SignInAsync(api, "google", new { sub = $"g-both-{round}", email, emailVerified = true }),
                api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token",
                    new { provider = "apple", idToken = apple.IdToken, nonce = Nonce, authorizationCode = apple.Code }));

            Assert.All(replies, r => Assert.Equal(HttpStatusCode.Created, r.Status));
            Assert.Equal(AuthApi.UserId(replies[0]), AuthApi.UserId(replies[1]));
            Assert.Single(replies, r => r.Body.GetProperty("isNewUser").GetBoolean());
            Assert.Equal(1L, await Count(api, $"SELECT count(*) FROM orvano.auth_users WHERE lower(email) = '{email}'"));
            Assert.Equal(2L, await Count(api, $"SELECT count(*) FROM orvano.auth_identities i JOIN orvano.auth_users u ON u.id = i.user_id WHERE lower(u.email) = '{email}'"));
            foreach (var reply in replies) reply.Dispose();
        }
    }

    [Fact]
    public async Task A_key_set_that_hangs_makes_native_sign_in_unavailable_within_the_timeout()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        using (var hang = await api.SendAsync(HttpMethod.Post, "/v1/test/oauth/keys/hang")) Assert.Equal(HttpStatusCode.NoContent, hang.Status);

        var google = await MintAsync(api, "google", AuthApi.GoogleNativeClient, "g-slow", "slow@x.com");
        // The shared client gives up after 5 seconds, so this call waits on its own.
        using var http = new HttpClient { BaseAddress = api.Http.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account/sessions/id-token")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { provider = "google", idToken = google.IdToken, nonce = Nonce }),
        };
        request.Headers.Add("X-Orvano-Project", AuthApi.Project);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var slow = await http.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, slow.StatusCode);
        using var problem = JsonDocument.Parse(await slow.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("provider_unavailable", problem.RootElement.GetProperty("code").GetString());
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(16));
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_users WHERE email = 'slow@x.com'"));
    }

    /// <summary>A user whose only way in is a Google identity, with an unverified email unless <paramref name="emailVerified"/>.</summary>
    private static Task<Reply> OAuthOnlyUserAsync(AuthApi api, string sub, bool emailVerified) =>
        OAuthDriver.SignInAsync(api, "google", new { sub, email = $"{sub}@x.com", emailVerified });

    private sealed record Minted(string IdToken, string? Code);

    private static async Task<Minted> MintAsync(AuthApi api, string provider, string aud, string sub, string? email, int expiresIn = 600)
    {
        var hashed = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Nonce)));
        using var minted = await api.SendAsync(HttpMethod.Post, "/v1/test/oauth/id-tokens",
            new { provider, aud, sub, email, emailVerified = email is not null, nonce = hashed, expiresIn });
        Assert.Equal(HttpStatusCode.OK, minted.Status);
        var code = minted.Body.GetProperty("authorizationCode");
        return new Minted(minted.Body.GetProperty("idToken").GetString()!, code.ValueKind == JsonValueKind.String ? code.GetString() : null);
    }

    private static Task<long> Count(AuthApi api, string sql) => TestDatabase.ScalarAsync<long>(api.Database.Superuser, sql);
}
