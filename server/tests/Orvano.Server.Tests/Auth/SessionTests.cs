using System.Net;
using System.Text.Json;
using Orvano.Auth;
using Orvano.Auth.Jobs;
using Orvano.Core.Modules;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0004 build task 4 over HTTP against the real binary: refresh with rotation, grace, and reuse detection, sign
// out, listing and ending sessions, session records, and the refresh limits. AC-8 to AC-11, AC-16, AC-30 to AC-33.
// Time moves by editing the session row, since the database clock decides.
public class SessionTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_refresh_rotates_the_pair_and_the_previous_token_replays_it_within_the_grace()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var first = AuthApi.RefreshToken(signUp);

        using var rotated = await RefreshAsync(api, first);
        using var replayed = await RefreshAsync(api, first);

        Assert.Equal(HttpStatusCode.OK, rotated.Status);
        var second = rotated.Body.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first, second);
        Assert.Equal(Sid(signUp), rotated.Body.GetProperty("sessionId").GetString());
        Assert.StartsWith(first[..29], second, StringComparison.Ordinal); // the same session ID inside
        Assert.InRange(rotated.Body.GetProperty("refreshTokenExpiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromDays(29.9), TimeSpan.FromDays(30.1));

        Assert.Equal(HttpStatusCode.OK, replayed.Status);
        Assert.Equal(second, replayed.Body.GetProperty("refreshToken").GetString());
        using var me = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: replayed.Body.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, me.Status);

        Assert.True(await TestDatabase.ScalarAsync<bool>(api.Database.Superuser,
            "SELECT previous_refresh_hash IS NOT NULL AND rotated_at IS NOT NULL AND last_refreshed_at > created_at FROM orvano.auth_sessions WHERE id = @id",
            ("id", Guid.Parse(Sid(signUp)))));
    }

    [Fact]
    public async Task Two_parallel_refreshes_with_one_token_get_the_same_pair()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");

        var replies = await Task.WhenAll(RefreshAsync(api, AuthApi.RefreshToken(signUp)), RefreshAsync(api, AuthApi.RefreshToken(signUp)));

        Assert.All(replies, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(replies[0].Body.GetProperty("refreshToken").GetString(), replies[1].Body.GetProperty("refreshToken").GetString());
        foreach (var reply in replies) reply.Dispose();
    }

    [Fact]
    public async Task The_previous_token_after_the_grace_ends_the_session_as_reuse()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        using var rotated = await RefreshAsync(api, AuthApi.RefreshToken(signUp));
        var sessionId = Guid.Parse(Sid(signUp));
        var access = rotated.Body.GetProperty("accessToken").GetString();
        using var before = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: access);
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_sessions SET rotated_at = now() - interval '11 seconds' WHERE id = @id", ("id", sessionId));

        using var reused = await RefreshAsync(api, AuthApi.RefreshToken(signUp));
        using var current = await RefreshAsync(api, rotated.Body.GetProperty("refreshToken").GetString()!);
        using var after = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: access);

        Assert.Equal(HttpStatusCode.OK, before.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.Status);
        Assert.Equal("invalid_refresh_token", reused.Code);
        Assert.Equal("invalid_refresh_token", current.Code);
        Assert.Equal("invalid_token", after.Code); // evicted at once on the instance that ended it
        Assert.Equal("reuse_detected", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT end_reason FROM orvano.auth_sessions WHERE id = @id", ("id", sessionId)));
        var payload = await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT payload::text FROM orvano.events WHERE type = 'auth.session.ended'");
        using var ended = JsonDocument.Parse(payload);
        Assert.Equal("reuse_detected", ended.RootElement.GetProperty("reason").GetString());
        Assert.Equal(sessionId.ToString(), ended.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal("system", ended.RootElement.GetProperty("actor").GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_made_up_secret_or_an_older_token_is_refused_and_changes_nothing()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var oldest = AuthApi.RefreshToken(signUp);
        using var second = await RefreshAsync(api, oldest);
        using var third = await RefreshAsync(api, second.Body.GetProperty("refreshToken").GetString()!);
        var madeUp = oldest[..30] + new string('A', 43);

        using var guessed = await RefreshAsync(api, madeUp);
        using var older = await RefreshAsync(api, oldest); // neither current nor previous
        using var stillWorks = await RefreshAsync(api, third.Body.GetProperty("refreshToken").GetString()!);

        Assert.Equal("invalid_refresh_token", guessed.Code);
        Assert.Equal("invalid_refresh_token", older.Code);
        Assert.Equal(HttpStatusCode.OK, stillWorks.Status);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_sessions WHERE ended_at IS NOT NULL"));
    }

    [Theory]
    [InlineData("idle_expires_at = now() - interval '1 second'")]
    [InlineData("expires_at = now() - interval '1 second', idle_expires_at = now() - interval '1 second'")]
    public async Task An_expired_session_refuses_to_refresh(string expiry)
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        await TestDatabase.ExecuteAsync(api.Database.Superuser, $"UPDATE orvano.auth_sessions SET {expiry}");

        using var reply = await RefreshAsync(api, AuthApi.RefreshToken(signUp));

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.Equal("invalid_refresh_token", reply.Code);
    }

    [Fact]
    public async Task A_refresh_after_30_days_idle_would_fail_but_each_refresh_moves_the_idle_expiry_up_to_the_absolute_one()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var sessionId = Guid.Parse(Sid(signUp));
        // Created 350 days ago: the next idle expiry would be 30 days out, past the absolute one 15 days out.
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET created_at = now() - interval '350 days', expires_at = now() + interval '15 days', idle_expires_at = now() + interval '1 day' WHERE id = @id",
            ("id", sessionId));

        using var reply = await RefreshAsync(api, AuthApi.RefreshToken(signUp));

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.InRange(reply.Body.GetProperty("refreshTokenExpiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromDays(14.9), TimeSpan.FromDays(15.1));
        Assert.True(await TestDatabase.ScalarAsync<bool>(api.Database.Superuser,
            "SELECT idle_expires_at = expires_at FROM orvano.auth_sessions WHERE id = @id", ("id", sessionId)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("orv_rt_nope")]
    [InlineData("eyJhbGciOiJFUzI1NiJ9.e30.c2ln")]
    public async Task A_malformed_refresh_token_is_refused(string token)
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var reply = await RefreshAsync(api, token);

        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.Equal("invalid_refresh_token", reply.Code);
    }

    [Fact]
    public async Task A_refresh_token_of_one_project_is_refused_in_another()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");

        using var reply = await RefreshAsync(api, AuthApi.RefreshToken(signUp), project: AuthApi.OtherProject);

        Assert.Equal("invalid_refresh_token", reply.Code);
    }

    [Fact]
    public async Task Sign_out_ends_the_session_so_its_refresh_and_access_tokens_stop_at_once()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var access = AuthApi.AccessToken(signUp);
        using var before = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: access); // caches the session as active

        using var signOut = await api.SendAsync(HttpMethod.Delete, "/v1/account/sessions/current", bearer: access);
        using var refresh = await RefreshAsync(api, AuthApi.RefreshToken(signUp));
        using var after = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: access);

        Assert.Equal(HttpStatusCode.NoContent, signOut.Status);
        Assert.Equal("invalid_refresh_token", refresh.Code);
        Assert.Equal("invalid_token", after.Code);
        Assert.Equal("sign_out", await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT end_reason FROM orvano.auth_sessions"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.ended' AND payload->>'reason' = 'sign_out' AND payload->'actor'->>'type' = 'user'"));
    }

    [Fact]
    public async Task A_user_holds_many_sessions_listed_newest_first_with_what_signed_in()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        using var second = await api.SignInAsync("ada@x.com");
        using var third = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/password", new { email = "ada@x.com", password = "correct horse battery" },
            headers: new Dictionary<string, string>
            {
                ["X-Orvano-Client-IP"] = "203.0.113.7",
                ["X-Orvano-Client-UA"] = "Browser/1.0",
                ["X-Orvano-SDK"] = "orvano-nextjs/0.1.0",
            });
        var bearer = AuthApi.AccessToken(third);

        using var firstPage = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions?limit=2", bearer: bearer);
        using var secondPage = await api.SendAsync(HttpMethod.Get,
            $"/v1/account/sessions?limit=2&cursor={Uri.EscapeDataString(firstPage.Body.GetProperty("nextCursor").GetString()!)}", bearer: bearer);
        using var badCursor = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions?cursor=nope", bearer: bearer);

        var listed = firstPage.Body.GetProperty("items").EnumerateArray().Concat(secondPage.Body.GetProperty("items").EnumerateArray()).ToList();
        Assert.Equal([Sid(third), Sid(second), Sid(signUp)], listed.Select(s => s.GetProperty("id").GetString()));
        Assert.Equal([true, false, false], listed.Select(s => s.GetProperty("current").GetBoolean()));
        Assert.Equal("203.0.113.7", listed[0].GetProperty("ipAddress").GetString());
        Assert.Equal("Browser/1.0", listed[0].GetProperty("userAgent").GetString());
        Assert.Equal("orvano-nextjs/0.1.0", listed[0].GetProperty("sdk").GetString());
        Assert.Equal(JsonValueKind.Null, secondPage.Body.GetProperty("nextCursor").ValueKind);
        Assert.Equal("invalid_cursor", badCursor.Code);
    }

    [Fact]
    public async Task A_refresh_records_the_last_ip()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");

        using var refresh = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(signUp) },
            headers: new Dictionary<string, string> { ["X-Orvano-Client-IP"] = "198.51.100.9" });
        using var list = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions", bearer: refresh.Body.GetProperty("accessToken").GetString());

        Assert.Equal("198.51.100.9", list.Body.GetProperty("items")[0].GetProperty("ipAddress").GetString());
        Assert.Equal("127.0.0.1", await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT host(ip_created) FROM orvano.auth_sessions"));
    }

    [Fact]
    public async Task A_user_ends_one_of_their_own_sessions_but_never_another_users()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var ada = await api.SignUpAsync("ada@x.com");
        using var adaPhone = await api.SignInAsync("ada@x.com");
        using var grace = await api.SignUpAsync("grace@x.com");
        var bearer = AuthApi.AccessToken(ada);

        using var ended = await api.SendAsync(HttpMethod.Delete, $"/v1/account/sessions/{Sid(adaPhone)}", bearer: bearer);
        using var again = await api.SendAsync(HttpMethod.Delete, $"/v1/account/sessions/{Sid(adaPhone)}", bearer: bearer);
        using var others = await api.SendAsync(HttpMethod.Delete, $"/v1/account/sessions/{Sid(grace)}", bearer: bearer);
        using var unknown = await api.SendAsync(HttpMethod.Delete, $"/v1/account/sessions/{Guid.CreateVersion7()}", bearer: bearer);
        using var notAnId = await api.SendAsync(HttpMethod.Delete, "/v1/account/sessions/nope", bearer: bearer);
        using var phone = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(adaPhone));
        using var graceStill = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(grace));

        Assert.Equal(HttpStatusCode.NoContent, ended.Status);
        Assert.Equal(HttpStatusCode.NoContent, again.Status);
        Assert.Equal("session_not_found", others.Code);
        Assert.Equal("session_not_found", unknown.Code);
        Assert.Equal("session_not_found", notAnId.Code);
        Assert.Equal("invalid_token", phone.Code);
        Assert.Equal(HttpStatusCode.OK, graceStill.Status);
        Assert.Equal("revoked", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT end_reason FROM orvano.auth_sessions WHERE id = @id", ("id", Guid.Parse(Sid(adaPhone)))));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.ended'"));
    }

    [Fact]
    public async Task Ending_the_other_sessions_keeps_only_the_current_one()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var laptop = await api.SignUpAsync("ada@x.com");
        using var phone = await api.SignInAsync("ada@x.com");
        using var tablet = await api.SignInAsync("ada@x.com");
        using var phoneBefore = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(phone));

        using var ended = await api.SendAsync(HttpMethod.Delete, "/v1/account/sessions", bearer: AuthApi.AccessToken(laptop));
        using var list = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions", bearer: AuthApi.AccessToken(laptop));
        using var phoneAfter = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(phone));
        using var tabletRefresh = await RefreshAsync(api, AuthApi.RefreshToken(tablet));

        Assert.Equal(HttpStatusCode.NoContent, ended.Status);
        Assert.Equal([Sid(laptop)], list.Body.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.OK, phoneBefore.Status);
        Assert.Equal("invalid_token", phoneAfter.Code);
        Assert.Equal("invalid_refresh_token", tabletRefresh.Code);
        Assert.Equal(2L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.ended' AND payload->>'reason' = 'revoked'"));
    }

    [Fact]
    public async Task Session_operations_need_a_bearer_token()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var list = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions");
        using var signOut = await api.SendAsync(HttpMethod.Delete, "/v1/account/sessions/current");

        Assert.Equal("session_required", list.Code);
        Assert.Equal("session_required", signOut.Code);
    }

    [Fact]
    public async Task The_61st_failed_refresh_from_one_ip_is_limited_but_good_refreshes_never_count()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var token = AuthApi.RefreshToken(signUp);
        var ip = new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.60" };

        for (var i = 0; i < 59; i++)
        {
            using var good = await RefreshAsync(api, token, headers: ip);
            Assert.Equal(HttpStatusCode.OK, good.Status);
            token = good.Body.GetProperty("refreshToken").GetString()!;
        }

        for (var i = 0; i < 60; i++)
        {
            using var bad = await RefreshAsync(api, $"orv_rt_{RandomId()}.{new string('A', 43)}", headers: ip);
            Assert.Equal("invalid_refresh_token", bad.Code);
        }

        using var limited = await RefreshAsync(api, token, headers: ip);
        using var otherIp = await RefreshAsync(api, token, headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.61" });

        Assert.Equal((HttpStatusCode)429, limited.Status);
        Assert.Equal("rate_limited", limited.Code);
        Assert.InRange(int.Parse(limited.Headers.GetValues("Retry-After").Single()), 1, 900);
        Assert.Equal(HttpStatusCode.OK, otherIp.Status); // the 60th refresh of this session, still within its own limit
    }

    [Fact]
    public async Task A_forwarded_client_ip_is_only_recorded_and_never_moves_a_limit()
    {
        // AC-31: X-Orvano-Client-IP is shown only. Changing it on every call must not dodge the connection IP's limit.
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var token = AuthApi.RefreshToken(signUp);

        for (var i = 0; i < 60; i++)
        {
            using var bad = await RefreshAsync(api, $"orv_rt_{RandomId()}.{new string('A', 43)}",
                headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.62", ["X-Orvano-Client-IP"] = $"198.51.100.{i}" });
            Assert.Equal("invalid_refresh_token", bad.Code);
        }

        using var limited = await RefreshAsync(api, token,
            headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.62", ["X-Orvano-Client-IP"] = "198.51.100.200" });

        Assert.Equal((HttpStatusCode)429, limited.Status);
        Assert.Equal("rate_limited", limited.Code);
    }

    [Fact]
    public async Task The_61st_refresh_of_one_session_within_15_minutes_is_limited()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var token = AuthApi.RefreshToken(signUp);

        for (var i = 0; i < 60; i++)
        {
            using var good = await RefreshAsync(api, token);
            Assert.Equal(HttpStatusCode.OK, good.Status);
            token = good.Body.GetProperty("refreshToken").GetString()!;
        }

        using var limited = await RefreshAsync(api, token);

        Assert.Equal((HttpStatusCode)429, limited.Status);
    }

    [Fact]
    public async Task Retention_deletes_sessions_30_days_after_they_end_or_expire_and_retired_keys()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var user = await TestDatabase.ScalarAsync<Guid>(database.Superuser,
            "INSERT INTO orvano.auth_users (project_id, email) VALUES ('shop', 'ada@x.com') RETURNING id");
        var endedLongAgo = await InsertSessionAsync(database, user, "ended_at = now() - interval '31 days', end_reason = 'sign_out'");
        var endedRecently = await InsertSessionAsync(database, user, "ended_at = now() - interval '29 days', end_reason = 'revoked'");
        var expiredLongAgo = await InsertSessionAsync(database, user, "idle_expires_at = now() - interval '31 days'");
        var expiredRecently = await InsertSessionAsync(database, user, "idle_expires_at = now() - interval '29 days'");
        var active = await InsertSessionAsync(database, user, "ended_at = NULL");
        await InsertKeyAsync(database, "retiredKeyAAAAAAAAAAAA", "retiring", "now() - interval '1 minute'");
        await InsertKeyAsync(database, "retiringKeyAAAAAAAAAAA", "retiring", "now() + interval '1 hour'");
        await InsertKeyAsync(database, "activeKeyAAAAAAAAAAAAA", "active", "NULL");

        await using var app = Npgsql.NpgsqlDataSource.Create(database.AppUrl);
        // Spec 0010 AC-29: email tokens go once past their expiry.
        await TestDatabase.ExecuteAsync(database.Superuser,
            """
            INSERT INTO orvano.auth_email_tokens (project_id, kind, email, secret_hash, mac_key_id, expires_at)
            VALUES ('shop', 'email_code', 'old@x.com', decode(repeat('aa', 32), 'hex'), 'k', now() - interval '1 second'),
                   ('shop', 'email_code', 'new@x.com', decode(repeat('bb', 32), 'hex'), 'k', now() + interval '10 minutes')
            """);
        var (sessions, keys, tokens) = await AuthRetention.RunAsync(app, Ct);

        Assert.Equal((2, 1, 1), (sessions, keys, tokens));
        Assert.Equal(["new@x.com"], await TestDatabase.ScalarAsync<string[]>(database.Superuser, "SELECT array_agg(email) FROM orvano.auth_email_tokens"));
        var left = await TestDatabase.ScalarAsync<Guid[]>(database.Superuser, "SELECT array_agg(id) FROM orvano.auth_sessions");
        Assert.Equal(new[] { endedRecently, expiredRecently, active }.Order(), left.Order());
        Assert.DoesNotContain(endedLongAgo, left);
        Assert.DoesNotContain(expiredLongAgo, left);
        Assert.Equal(["activeKeyAAAAAAAAAAAAA", "retiringKeyAAAAAAAAAAA"],
            await TestDatabase.ScalarAsync<string[]>(database.Superuser, "SELECT array_agg(id ORDER BY id) FROM orvano.auth_signing_keys"));
    }

    [Fact]
    public void The_retention_schedule_runs_hourly()
    {
        var work = new WorkRegistry();
        new AuthModule().RegisterWork(work);

        Assert.Equal(TimeSpan.FromHours(1), work.Schedules.Single(s => s.Name == "auth.retention").Interval);
    }

    private static Task<Reply> RefreshAsync(AuthApi api, string token, string project = AuthApi.Project, IReadOnlyDictionary<string, string>? headers = null) =>
        api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = token }, project, headers: headers);

    private static string Sid(Reply signedIn) => signedIn.Body.GetProperty("session").GetProperty("sessionId").GetString()!;

    private static string RandomId() => System.Buffers.Text.Base64Url.EncodeToString(Guid.CreateVersion7().ToByteArray(bigEndian: true));

    private static async Task<Guid> InsertSessionAsync(TestDatabase database, Guid user, string set)
    {
        var id = await TestDatabase.ScalarAsync<Guid>(database.Superuser, """
            INSERT INTO orvano.auth_sessions (project_id, user_id, refresh_hash, refresh_ciphertext, idle_expires_at, expires_at, created_at, last_refreshed_at)
            VALUES ('shop', @user, sha256('x'), '\x00', now() + interval '1 day', now() + interval '300 days', now() - interval '60 days', now() - interval '60 days')
            RETURNING id
            """, ("user", user));
        await TestDatabase.ExecuteAsync(database.Superuser, $"UPDATE orvano.auth_sessions SET {set} WHERE id = @id", ("id", id));
        return id;
    }

    private static Task InsertKeyAsync(TestDatabase database, string kid, string status, string retireAfter) =>
        TestDatabase.ExecuteAsync(database.Superuser, $"""
            INSERT INTO orvano.auth_signing_keys (id, project_id, alg, public_jwk, private_key_ciphertext, status, retire_after)
            VALUES (@kid, 'shop', 'ES256', jsonb_build_object(), '\x00', @status, {retireAfter})
            """, ("kid", kid), ("status", status));
}
