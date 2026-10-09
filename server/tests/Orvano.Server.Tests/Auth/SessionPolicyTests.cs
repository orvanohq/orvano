using System.Net;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0014 build task 5 over HTTP against the real binary: per project token and session lifetimes, and the cap on
// live sessions per user under the user lock. AC-25, AC-26.
public class SessionPolicyTests(PostgresFixture postgres)
{
    private const string PoliciesUrl = AuthPolicyTests.PoliciesUrl;

    [Fact]
    public async Task Lifetimes_come_from_the_project_for_new_tokens_and_sessions()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using (var set = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { accessTokenSeconds = 300, sessionIdleSeconds = 3600, sessionAbsoluteSeconds = 86400 }))
            Assert.Equal(HttpStatusCode.OK, set.Status);

        using var signUp = await api.SignUpAsync("ada@x.com");
        var claims = Claims(AuthApi.AccessToken(signUp));
        Assert.Equal(300, claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64());

        var sessionId = Guid.Parse(Sid(signUp));
        Assert.Equal(86400d, await SecondsAsync(api, "expires_at - created_at", sessionId), 1d);
        Assert.Equal(3600d, await SecondsAsync(api, "idle_expires_at - created_at", sessionId), 1d);

        // A refresh carries the project's access lifetime and moves the idle expiry, never past expires_at.
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET expires_at = now() + interval '10 minutes', idle_expires_at = now() + interval '10 minutes' WHERE id = @id", ("id", sessionId));
        using var refreshed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(signUp) });
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        var next = Claims(refreshed.Body.GetProperty("accessToken").GetString()!);
        Assert.Equal(300, next.GetProperty("exp").GetInt64() - next.GetProperty("iat").GetInt64());
        Assert.Equal(0d, await SecondsAsync(api, "idle_expires_at - expires_at", sessionId), 1d);

        // Past its absolute end, the session no longer refreshes.
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET expires_at = now() - interval '1 second', idle_expires_at = now() - interval '1 second' WHERE id = @id", ("id", sessionId));
        using var ended = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = refreshed.Body.GetProperty("refreshToken").GetString() });
        Assert.Equal("invalid_refresh_token", ended.Code);

        // An existing session keeps its expires_at when the rules change.
        using var other = await api.SignInAsync("ada@x.com");
        var before = await SecondsAsync(api, "expires_at - created_at", Guid.Parse(Sid(other)));
        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { sessionAbsoluteSeconds = 172800 })) { }
        Assert.Equal(before, await SecondsAsync(api, "expires_at - created_at", Guid.Parse(Sid(other))), 1d);
    }

    [Fact]
    public async Task A_cap_ends_the_least_recently_used_session_which_then_fails_at_the_api()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using (await api.SignUpAsync("ada@x.com")) { }
        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { maxSessionsPerUser = 2 })) { }

        using var first = await api.SignInAsync("ada@x.com");
        using var second = await api.SignInAsync("ada@x.com");
        // The first (sign up's) session and this one are older than the next; refreshing second makes it the most recent.
        using (var refresh = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(second) }))
            Assert.Equal(HttpStatusCode.OK, refresh.Status);
        using var third = await api.SignInAsync("ada@x.com");

        Assert.Equal(2L, await LiveAsync(api));
        Assert.Equal("session_limit", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT end_reason FROM orvano.auth_sessions WHERE id = @id", ("id", Guid.Parse(Sid(first)))));
        using var me = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(first));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_token"), (me.Status, me.Code));
        using var stale = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(first) });
        Assert.Equal("invalid_refresh_token", stale.Code);
        Assert.True(await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.ended' AND payload->>'reason' = 'session_limit'") >= 1);

        // Ten sign ins at once still leave exactly two live sessions.
        var replies = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => api.SignInAsync("ada@x.com")));
        Assert.All(replies, r => Assert.Equal(HttpStatusCode.Created, r.Status));
        foreach (var reply in replies) reply.Dispose();
        Assert.Equal(2L, await LiveAsync(api));

        // Lowering the cap ends nothing until the next sign in; null means no limit.
        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { maxSessionsPerUser = 1 })) { }
        Assert.Equal(2L, await LiveAsync(api));
        using (await api.SignInAsync("ada@x.com")) { }
        Assert.Equal(1L, await LiveAsync(api));
        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { maxSessionsPerUser = (int?)null })) { }
        for (var i = 0; i < 3; i++) using (await api.SignInAsync("ada@x.com")) { }
        Assert.Equal(4L, await LiveAsync(api));
    }

    private static Task<double> SecondsAsync(AuthApi api, string interval, Guid sessionId) =>
        TestDatabase.ScalarAsync<double>(api.Database.Superuser,
            $"SELECT extract(epoch FROM {interval})::float8 FROM orvano.auth_sessions WHERE id = @id", ("id", sessionId));

    private static Task<long> LiveAsync(AuthApi api) =>
        TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_sessions WHERE project_id = @p AND ended_at IS NULL AND idle_expires_at > now() AND expires_at > now()",
            ("p", AuthApi.Project));
}
