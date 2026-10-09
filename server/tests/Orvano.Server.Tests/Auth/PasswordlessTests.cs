using System.Diagnostics;
using System.Net;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 build task 3 over HTTP against the real binary: magic link and email code sign in, attempts, races, and
// account claiming. AC-3, AC-5, AC-8, AC-15, AC-16, AC-19, AC-28, AC-30, AC-32.
public class PasswordlessTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth/callback";

    [Fact]
    public async Task A_magic_link_for_a_new_email_creates_a_verified_user_and_a_code_signs_them_in_again()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);

        var token = await MagicLinkAsync(api, "Neo@X.com");
        using var first = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.True(first.Body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal("Neo@X.com", first.Body.GetProperty("user").GetProperty("email").GetString());
        Assert.True(first.Body.GetProperty("user").GetProperty("emailVerified").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.Body.GetProperty("user").GetProperty("name").ValueKind);
        Assert.True(VerificationTests.Claim(AuthApi.AccessToken(first)));

        using var reused = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        Assert.Equal("invalid_email_token", reused.Code);

        var code = await EmailCodeAsync(api, "neo@x.com");
        using var second = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "NEO@x.com", code });
        Assert.Equal(HttpStatusCode.Created, second.Status);
        Assert.False(second.Body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(AuthApi.UserId(first), AuthApi.UserId(second));

        using var sessions = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions", bearer: AuthApi.AccessToken(second));
        Assert.Equal(["email_code", "magic_link"], sessions.Body.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("method").GetString()));

        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.created' AND payload->>'method' = 'magic_link'"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.email_token.created' AND payload->>'kind' = 'magic_link' AND payload->'userId' = 'null'::jsonb AND payload->'actor'->'id' = 'null'::jsonb"));

        // A user without a password: password sign in fails like a wrong password does.
        var clock = Stopwatch.StartNew();
        using var noPassword = await api.SignInAsync("neo@x.com");
        var took = clock.Elapsed;
        Assert.Equal("invalid_credentials", noPassword.Code);
        Assert.True(took > TimeSpan.FromMilliseconds(5), "checked against the dummy hash");
    }

    [Fact]
    public async Task CreateUser_false_a_blocked_user_and_a_known_user_get_the_same_answer()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var known = await api.SignUpAsync("known@x.com");
        using var blocked = await api.SignUpAsync("blocked@x.com");
        using (await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{AuthApi.UserId(blocked)}/block")) { }

        foreach (var (email, createUser) in new[] { ("known@x.com", false), ("blocked@x.com", true), ("nobody@x.com", false) })
        {
            using var link = await api.SendAsync(HttpMethod.Post, "/v1/account/magic-link", new { email, redirectUrl = Redirect, createUser });
            using var code = await api.SendAsync(HttpMethod.Post, "/v1/account/email-code", new { email, createUser });
            Assert.Equal(HttpStatusCode.Accepted, link.Status);
            Assert.Equal(HttpStatusCode.Accepted, code.Status);
        }

        Assert.Equal(2L, await api.QueuedEmailCountAsync()); // known@x.com's link and code; createUser only matters for new emails
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens WHERE user_id IS NULL"));

        using var customScheme = await api.SendAsync(HttpMethod.Post, "/v1/account/magic-link", new { email = "known@x.com", redirectUrl = "com.acme.app://auth" });
        Assert.Equal("redirect_url_not_allowed", customScheme.Code);
    }

    [Fact]
    public async Task Four_wrong_codes_leave_the_right_one_working_and_five_from_one_address_are_limited()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);

        var four = await EmailCodeAsync(api, "four@x.com");
        for (var i = 0; i < 4; i++)
        {
            using var wrong = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "four@x.com", code = Wrong(four, i) });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
            Assert.Equal("invalid_code", wrong.Code);
        }

        using (var right = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "four@x.com", code = four }))
            Assert.Equal(HttpStatusCode.Created, right.Status);

        // Spec 0014, AC-19: five wrong codes from one address limit that address, and the code survives them (it goes
        // only at the 10th wrong guess, which AbuseLimitTests covers from several addresses).
        var five = await EmailCodeAsync(api, "five@x.com");
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "five@x.com", code = Wrong(five, i) });
            Assert.Equal("invalid_code", wrong.Code);
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "five@x.com", code = five });
        Assert.Equal("rate_limited", limited.Code);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens WHERE kind = 'email_code'"));

        // Malformed input is a 400 that counts nothing, and an unknown email answers like a wrong code.
        using var shortCode = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "four@x.com", code = "12345" });
        using var badEmail = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "nope", code = "123456" });
        using var unknown = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "nobody@x.com", code = "123456" });
        Assert.Equal("invalid_request", shortCode.Code);
        Assert.Equal("invalid_request", badEmail.Code);
        Assert.Equal("invalid_code", unknown.Code);
    }

    [Fact]
    public async Task Ten_parallel_wrong_guesses_from_one_address_reach_the_code_at_most_five_times_and_an_expired_code_counts_none()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var code = await EmailCodeAsync(api, "race@x.com");

        // Spec 0014, AC-19: reservations let at most five of them reach the code; the rest answer 429.
        var replies = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "race@x.com", code = Wrong(code, i) })));
        Assert.InRange(replies.Count(r => r.Code == "invalid_code"), 1, 5);
        Assert.All(replies, r => Assert.Contains(r.Code, new[] { "invalid_code", "rate_limited" }));
        foreach (var reply in replies) reply.Dispose();

        Assert.InRange(await TestDatabase.ScalarAsync<short>(api.Database.Superuser, "SELECT attempts FROM orvano.auth_email_tokens"), (short)1, (short)5);
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "DELETE FROM orvano.auth_email_tokens");

        var expiring = await EmailCodeAsync(api, "late@x.com");
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_email_tokens SET expires_at = now() - interval '1 second'");
        using var late = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "late@x.com", code = expiring });
        Assert.Equal("invalid_code", late.Code);
        Assert.Equal(0, await TestDatabase.ScalarAsync<short>(api.Database.Superuser, "SELECT attempts FROM orvano.auth_email_tokens"));
    }

    [Fact]
    public async Task Twenty_parallel_redemptions_of_one_link_give_one_session()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var token = await MagicLinkAsync(api, "ada@x.com");

        var replies = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token })));
        try
        {
            Assert.Single(replies, r => r.Status == HttpStatusCode.Created);
            Assert.Equal(19, replies.Count(r => r.Code == "invalid_email_token"));
        }
        finally
        {
            foreach (var reply in replies) reply.Dispose();
        }
    }

    [Fact]
    public async Task A_link_and_a_code_for_one_new_email_redeemed_together_make_one_user_with_two_sessions()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var token = await MagicLinkAsync(api, "twin@x.com");
        var code = await EmailCodeAsync(api, "twin@x.com");

        var replies = await Task.WhenAll(
            api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token }),
            api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "twin@x.com", code }));
        try
        {
            Assert.All(replies, r => Assert.Equal(HttpStatusCode.Created, r.Status));
            Assert.Equal(AuthApi.UserId(replies[0]), AuthApi.UserId(replies[1]));
            Assert.Single(replies, r => r.Body.GetProperty("isNewUser").GetBoolean());
            Assert.Equal(2L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_sessions"));
        }
        finally
        {
            foreach (var reply in replies) reply.Dispose();
        }
    }

    [Fact]
    public async Task The_inbox_owner_signing_in_by_link_removes_an_early_sign_ups_password_and_sessions()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var attacker = await api.SignUpAsync("victim@x.com", "attacker horse battery");

        var token = await MagicLinkAsync(api, "victim@x.com");
        using var victim = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        Assert.Equal(HttpStatusCode.Created, victim.Status);
        Assert.False(victim.Body.GetProperty("isNewUser").GetBoolean());
        Assert.True(victim.Body.GetProperty("user").GetProperty("emailVerified").GetBoolean());

        using var attackerRefresh = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(attacker) });
        using var attackerAccess = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(attacker));
        using var attackerSignIn = await api.SignInAsync("victim@x.com", "attacker horse battery");
        using var victimAccess = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(victim));
        Assert.Equal("invalid_refresh_token", attackerRefresh.Code);
        Assert.Equal("invalid_token", attackerAccess.Code);
        Assert.Equal("invalid_credentials", attackerSignIn.Code);
        Assert.Equal(HttpStatusCode.OK, victimAccess.Status);

        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.password.removed'"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.ended' AND payload->>'reason' = 'account_claimed'"));
    }

    [Fact]
    public async Task A_verified_user_signing_in_by_link_keeps_their_password_and_sessions()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET email_verified_at = now()");

        var token = await MagicLinkAsync(api, "ada@x.com");
        using var link = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        using var refresh = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(signUp) });
        using var password = await api.SignInAsync("ada@x.com");

        Assert.Equal(HttpStatusCode.Created, link.Status);
        Assert.Equal(HttpStatusCode.OK, refresh.Status);
        Assert.Equal(HttpStatusCode.Created, password.Status);
    }

    [Fact]
    public async Task A_blocked_user_gets_403_and_the_link_still_works_after_unblocking()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var token = await MagicLinkAsync(api, "ada@x.com");
        // Blocking deletes the user's live tokens (AC-29), so put the row back as it was to test the 403.
        var hash = await TestDatabase.ScalarAsync<byte[]>(api.Database.Superuser, "SELECT secret_hash FROM orvano.auth_email_tokens");
        using (await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{AuthApi.UserId(signUp)}/block")) { }
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens"));
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "INSERT INTO orvano.auth_email_tokens (project_id, kind, user_id, email, secret_hash, expires_at) VALUES (@p, 'magic_link', @u::uuid, 'ada@x.com', @h, now() + interval '15 minutes')",
            ("p", AuthApi.Project), ("u", AuthApi.UserId(signUp)), ("h", hash));

        using (var blocked = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, blocked.Status);
            Assert.Equal("user_blocked", blocked.Code);
        }

        using (await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{AuthApi.UserId(signUp)}/unblock")) { }
        using var works = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        Assert.Equal(HttpStatusCode.Created, works.Status);
    }

    [Fact]
    public async Task Creating_a_user_by_link_takes_the_sign_up_limit_and_a_limited_redemption_leaves_the_link_working()
    {
        // AC-15: a new user takes spec 0004's auth.sign_up.ip limit; over it, 429 rolled back with the token intact.
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var full = new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.60" };
        for (var i = 0; i < 60; i++)
        {
            // An invalid password: no hash is spent, but the attempt still counts against the IP.
            using var attempt = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = $"u{i}@x.com", password = "short" }, headers: full);
            Assert.Equal(HttpStatusCode.BadRequest, attempt.Status);
        }

        using var signUp = await api.SignUpAsync("known@x.com");
        var newEmail = await MagicLinkAsync(api, "late@x.com");
        var knownEmail = await MagicLinkAsync(api, "known@x.com");

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = newEmail }, headers: full);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.Equal("rate_limited", limited.Code);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_users WHERE email = 'late@x.com'"));

        // Signing in to a user who exists creates no one, so the full IP is no obstacle.
        using var existing = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = knownEmail }, headers: full);
        Assert.Equal(HttpStatusCode.Created, existing.Status);
        Assert.False(existing.Body.GetProperty("isNewUser").GetBoolean());

        using var elsewhere = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = newEmail },
            headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.61" });
        Assert.Equal(HttpStatusCode.Created, elsewhere.Status);
        Assert.True(elsewhere.Body.GetProperty("isNewUser").GetBoolean());
    }

    [Fact]
    public async Task Wrong_codes_for_an_email_from_one_address_are_limited_after_five_even_when_right()
    {
        // Spec 0014, AC-19: auth.email_code_failed.recipient_ip, 5 failures per 15 minutes per project, lowercased email,
        // and limit IP; a right code never counts.
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        for (var i = 0; i < 3; i++)
        {
            // No code is live yet: the same 401 as a wrong code, and it counts as a failure.
            using var early = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = i % 2 == 0 ? "ADA@x.com" : "ada@x.com", code = "000000" });
            Assert.Equal("invalid_code", early.Code);
        }

        var code = await EmailCodeAsync(api, "ada@x.com");
        for (var i = 0; i < 2; i++)
        {
            using var wrong = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "ada@x.com", code = Wrong(code, i) });
            Assert.Equal("invalid_code", wrong.Code);
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "Ada@X.com", code });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.Equal("rate_limited", limited.Code);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens WHERE kind = 'email_code'"));

        var other = await EmailCodeAsync(api, "bob@x.com");
        using var otherEmail = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "bob@x.com", code = other });
        Assert.Equal(HttpStatusCode.Created, otherEmail.Status);
    }

    internal static async Task<string> MagicLinkAsync(AuthApi api, string email, string redirect = Redirect)
    {
        using var reply = await api.SendAsync(HttpMethod.Post, "/v1/account/magic-link", new { email, redirectUrl = redirect });
        Assert.Equal(HttpStatusCode.Accepted, reply.Status);
        var sent = await api.LatestEmailAsync(email);
        Assert.Equal("magic_link", sent!.Type);
        return sent.Token!;
    }

    internal static async Task<string> EmailCodeAsync(AuthApi api, string email)
    {
        using var reply = await api.SendAsync(HttpMethod.Post, "/v1/account/email-code", new { email });
        Assert.Equal(HttpStatusCode.Accepted, reply.Status);
        var sent = await api.LatestEmailAsync(email);
        Assert.Equal("email_code", sent!.Template);
        return sent.Code!;
    }

    private static string Wrong(string code, int i) => ((int.Parse(code, System.Globalization.CultureInfo.InvariantCulture) + 1 + i) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}
