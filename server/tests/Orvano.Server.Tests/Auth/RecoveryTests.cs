using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 build task 1 over HTTP against the real binary: password reset. AC-2, AC-3, AC-6 to AC-10, AC-20, AC-28,
// AC-30.
public class RecoveryTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth?next=%2Fhome";
    private const string NewPassword = "another horse battery";

    [Fact]
    public async Task A_reset_signs_the_user_in_ends_every_other_session_and_works_once()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        using var second = await api.SignInAsync("ada@x.com");

        using var request = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = " ADA@x.com ", redirectUrl = Redirect });
        Assert.Equal(HttpStatusCode.Accepted, request.Status);
        Assert.Null(request.Document);

        var email = await api.LatestEmailAsync("ada@x.com");
        Assert.NotNull(email);
        Assert.Equal("recovery", email.Template);
        Assert.Equal("recovery", email.Type);
        Assert.StartsWith("https://app.example.com/auth?next=%2Fhome&orvano_type=recovery&orvano_token=orv_el_", email.Url, StringComparison.Ordinal);
        var token = email.Token!;

        // AC-1: only the hash is stored.
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_email_tokens WHERE kind = 'recovery' AND secret_hash = @hash AND expires_at BETWEEN now() + interval '59 minutes' AND now() + interval '61 minutes'",
            ("hash", SHA256.HashData(Encoding.UTF8.GetBytes(token)))));

        using var weak = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.Status);
        Assert.Equal("invalid_password", weak.Code);

        using var reset = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = NewPassword });
        Assert.Equal(HttpStatusCode.Created, reset.Status);
        Assert.True(reset.Body.GetProperty("user").GetProperty("emailVerified").GetBoolean());
        Assert.False(reset.Body.GetProperty("isNewUser").GetBoolean());

        // Every session from before ends at once; the new one works and records how it began.
        using var oldRefresh = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(signUp) });
        using var oldAccess = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(second));
        using var sessions = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions", bearer: AuthApi.AccessToken(reset));
        Assert.Equal("invalid_refresh_token", oldRefresh.Code);
        Assert.Equal("invalid_token", oldAccess.Code);
        var only = Assert.Single(sessions.Body.GetProperty("items").EnumerateArray());
        Assert.Equal("recovery", only.GetProperty("method").GetString());

        using var oldPassword = await api.SignInAsync("ada@x.com");
        using var newPassword = await api.SignInAsync("ada@x.com", NewPassword);
        Assert.Equal("invalid_credentials", oldPassword.Code);
        Assert.Equal(HttpStatusCode.Created, newPassword.Status);

        // AC-3: the same link again.
        using var again = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = NewPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, again.Status);
        Assert.Equal("invalid_email_token", again.Code);

        // AC-30: the records, with no email, token, or URL in them.
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.password.reset'"));
        Assert.Equal(2L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.ended' AND payload->>'reason' = 'password_reset'"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.email_token.created' AND payload->>'kind' = 'recovery' AND payload->>'userId' = @user",
            ("user", AuthApi.UserId(signUp))));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.created' AND payload->>'method' = 'recovery'"));
        var payloads = await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT string_agg(payload::text, ' ') FROM orvano.events");
        Assert.DoesNotContain("ada@x.com", payloads, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orv_el_", payloads, StringComparison.Ordinal);
        Assert.DoesNotContain("app.example.com", payloads, StringComparison.Ordinal);
        Assert.DoesNotContain("orv_el_", api.Process.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_request_answers_the_same_for_a_known_a_blocked_and_an_unknown_email()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var known = await api.SignUpAsync("known@x.com");
        using var blockedUser = await api.SignUpAsync("blocked@x.com");
        using var block = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{AuthApi.UserId(blockedUser)}/block");

        var replies = new List<(Reply Reply, TimeSpan Took)>();
        foreach (var address in new[] { "known@x.com", "blocked@x.com", "nobody@x.com" })
        {
            var clock = Stopwatch.StartNew();
            var reply = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = address, redirectUrl = Redirect });
            replies.Add((reply, clock.Elapsed));
        }

        try
        {
            foreach (var (reply, took) in replies)
            {
                Assert.Equal(HttpStatusCode.Accepted, reply.Status);
                Assert.Null(reply.Document);
                Assert.True(took >= TimeSpan.FromMilliseconds(490), $"answered after {took.TotalMilliseconds} ms");
                Assert.Equal(HeaderNames(replies[0].Reply), HeaderNames(reply));
            }

            Assert.Equal(1L, await api.QueuedEmailCountAsync());
            Assert.NotNull(await api.LatestEmailAsync("known@x.com"));
        }
        finally
        {
            foreach (var (reply, _) in replies) reply.Dispose();
        }
    }

    [Fact]
    public async Task Without_an_smtp_server_every_request_gets_email_not_configured()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        using var known = await api.SignUpAsync("known@x.com");

        foreach (var address in new[] { "known@x.com", "nobody@x.com" })
        {
            using var reply = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = address, redirectUrl = Redirect });
            Assert.Equal(HttpStatusCode.Conflict, reply.Status);
            Assert.Equal("email_not_configured", reply.Code);
        }
    }

    [Fact]
    public async Task At_the_install_cap_every_request_gets_email_rate_limited()
    {
        await using var api = await AuthApi.StartAsync(postgres, new Dictionary<string, string> { ["ORVANO_EMAIL_INSTALL_HOURLY_LIMIT"] = "1" }, email: true, smtp: true);
        using var known = await api.SignUpAsync("known@x.com");
        using var other = await api.SignUpAsync("other@x.com");
        using var first = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "known@x.com", redirectUrl = Redirect });
        Assert.Equal(HttpStatusCode.Accepted, first.Status);

        foreach (var address in new[] { "other@x.com", "nobody@x.com" })
        {
            using var reply = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = address, redirectUrl = Redirect });
            Assert.Equal(HttpStatusCode.TooManyRequests, reply.Status);
            Assert.Equal("email_rate_limited", reply.Code);
            Assert.NotNull(reply.Headers.RetryAfter);
        }
    }

    [Fact]
    public async Task A_redirect_outside_the_project_platforms_is_refused_before_anything_happens()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var known = await api.SignUpAsync("known@x.com");

        string[] refused =
        [
            "https://evil.example/cb",
            "http://app.example.com/cb", // http only on localhost
            "javascript:alert(1)",
            "https://known:pw@app.example.com/cb",
            "https://evil.example\\@app.example.com",
            "https://app.example.com./cb",
            "https://sub.app.example.com/cb", // the platform is not a wildcard
            "com.acme.app://auth", // a custom scheme never carries a reset link
            "com.unknown.app://auth",
            "not a url",
        ];
        foreach (var redirect in refused)
        {
            using var reply = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "known@x.com", redirectUrl = redirect });
            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal("redirect_url_not_allowed", reply.Code);
        }

        using var badEmail = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "not-an-email", redirectUrl = "https://evil.example" });
        Assert.Equal("invalid_request", badEmail.Code);
        Assert.Equal(0L, await api.QueuedEmailCountAsync());

        using var localhost = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "known@x.com", redirectUrl = "http://localhost:3000/cb?orvano_token=stale" });
        Assert.Equal(HttpStatusCode.Accepted, localhost.Status);
        var email = await api.LatestEmailAsync("known@x.com");
        Assert.StartsWith("http://localhost:3000/cb?orvano_type=recovery&orvano_token=orv_el_", email!.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_request_within_a_minute_is_limited_with_retry_after()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var known = await api.SignUpAsync("known@x.com");

        using var first = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "known@x.com", redirectUrl = Redirect });
        using var second = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "KNOWN@x.com", redirectUrl = Redirect });
        using var unknown = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "nobody@x.com", redirectUrl = Redirect });

        Assert.Equal(HttpStatusCode.Accepted, first.Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.Status);
        Assert.Equal("rate_limited", second.Code);
        Assert.InRange(second.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        Assert.Equal(HttpStatusCode.Accepted, unknown.Status);
    }

    [Fact]
    public async Task A_reset_token_fails_when_expired_blocked_or_the_email_changed_and_a_failure_leaves_it_usable()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var user = AuthApi.UserId(signUp);
        var token = await RequestTokenAsync(api, "ada@x.com");

        // A blocked user: 403, and the token still works once they are unblocked. Blocking deletes the user's live
        // tokens (AC-29), so the row is put back as it was to reach the 403 a racing block could cause.
        var hash = await TestDatabase.ScalarAsync<byte[]>(api.Database.Superuser, "SELECT secret_hash FROM orvano.auth_email_tokens");
        using (await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{user}/block")) { }
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens"));
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "INSERT INTO orvano.auth_email_tokens (project_id, kind, user_id, email, secret_hash, expires_at) VALUES (@p, 'recovery', @u::uuid, 'ada@x.com', @h, now() + interval '1 hour')",
            ("p", AuthApi.Project), ("u", user), ("h", hash));
        using (var blocked = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = NewPassword }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, blocked.Status);
            Assert.Equal("user_blocked", blocked.Code);
        }

        using (await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{user}/unblock")) { }

        // The account's email moved since the link was sent.
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET email = 'moved@x.com' WHERE id = @id::uuid", ("id", user));
        using (var moved = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = NewPassword }))
            Assert.Equal("invalid_email_token", moved.Code);
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET email = 'ada@x.com' WHERE id = @id::uuid", ("id", user));

        // Expiry is the database clock's call.
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_email_tokens SET expires_at = now() - interval '1 second'");
        using (var expired = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = NewPassword }))
            Assert.Equal("invalid_email_token", expired.Code);
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_email_tokens SET expires_at = now() + interval '1 hour'");

        using var works = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = NewPassword });
        Assert.Equal(HttpStatusCode.Created, works.Status);

        foreach (var junk in new[] { "", "orv_el_short", "orv_rt_" + new string('A', 43), LinkToken() })
        {
            using var reply = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token = junk, password = NewPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
            Assert.Equal("invalid_email_token", reply.Code);
        }
    }

    [Fact]
    public async Task After_sixty_failed_redemptions_from_one_ip_even_a_valid_token_is_limited()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var token = await RequestTokenAsync(api, "ada@x.com");

        for (var i = 0; i < 60; i++)
        {
            using var junk = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token = LinkToken(), password = NewPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, junk.Status);
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = NewPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.Equal("rate_limited", limited.Code);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    internal static async Task<string> RequestTokenAsync(AuthApi api, string email, string redirect = Redirect)
    {
        using var reply = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email, redirectUrl = redirect });
        Assert.Equal(HttpStatusCode.Accepted, reply.Status);
        return (await api.LatestEmailAsync(email))!.Token!;
    }

    internal static string LinkToken() => "orv_el_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string[] HeaderNames(Reply reply) =>
        [.. reply.Headers.Select(h => h.Key).Where(name => name is not ("Date" or "X-Request-Id" or "traceparent")).Order(StringComparer.Ordinal)];
}
