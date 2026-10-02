using System.Net;
using System.Text;
using System.Text.Json;
using Orvano.Server.Hosting;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0004 build task 3, the thin thread, over HTTP against the real binary: sign up, sign in, the current user, and
// the JWKS. AC-1 to AC-7, AC-12, AC-20, AC-21, AC-30, AC-34.
public class SignUpSignInTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sign_up_creates_the_user_their_password_and_a_session_and_signs_them_in()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var reply = await api.SignUpAsync("  Ada@Example.com ", extra: new { email = "  Ada@Example.com ", password = "correct horse battery", name = "Ada" });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        var user = reply.Body.GetProperty("user");
        Assert.Equal("Ada@Example.com", user.GetProperty("email").GetString());
        Assert.Equal("Ada", user.GetProperty("name").GetString());
        Assert.Equal("active", user.GetProperty("status").GetString());
        Assert.False(user.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(JsonValueKind.Object, user.GetProperty("metadata").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, user.GetProperty("lastSignInAt").ValueKind);

        var session = reply.Body.GetProperty("session");
        Assert.StartsWith("orv_rt_", session.GetProperty("refreshToken").GetString());
        var accessExpires = session.GetProperty("accessTokenExpiresAt").GetDateTimeOffset();
        Assert.InRange(accessExpires - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(850), TimeSpan.FromSeconds(901));
        Assert.InRange(session.GetProperty("refreshTokenExpiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromDays(29.9), TimeSpan.FromDays(30.1));

        var userId = Guid.Parse(AuthApi.UserId(reply));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.auth_users WHERE id = @id", userId));
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT hash FROM orvano.auth_passwords WHERE user_id = @id", ("id", userId)));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.auth_sessions WHERE user_id = @id AND ended_at IS NULL", userId));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.created' AND subject = @id::text", userId));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.created' AND subject = @id::text", userId));
    }

    [Theory]
    [InlineData("not an email", "correct horse battery", "invalid_request")]
    [InlineData("ada@example.com", "short", "invalid_password")]
    [InlineData("ada@example.com", "", "invalid_password")]
    public async Task Sign_up_refuses_a_bad_email_or_password(string email, string password, string code)
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var reply = await api.SignUpAsync(email, password);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal(code, reply.Code);
    }

    [Fact]
    public async Task Sign_up_refuses_an_email_of_the_project_ignoring_case_but_not_one_of_another_project()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var first = await api.SignUpAsync("ada@x.com");
        using var again = await api.SignUpAsync("ADA@x.com");
        using var elsewhere = await api.SignUpAsync("ada@x.com", project: AuthApi.OtherProject);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        Assert.Equal("user_already_exists", again.Code);
        Assert.Equal(HttpStatusCode.Created, elsewhere.Status);
    }

    [Fact]
    public async Task Two_racing_sign_ups_with_one_email_make_exactly_one_user()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        var replies = await Task.WhenAll(api.SignUpAsync("Race@x.com"), api.SignUpAsync("race@x.com"));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], replies.Select(r => r.Status).Order());
        Assert.Equal("user_already_exists", replies.Single(r => r.Status == HttpStatusCode.Conflict).Code);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_users WHERE lower(email) = 'race@x.com'"));
        foreach (var r in replies) r.Dispose();
    }

    [Fact]
    public async Task Sign_in_opens_a_new_session_and_a_wrong_password_and_an_unknown_email_answer_alike()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");

        using var signIn = await api.SignInAsync("ADA@x.com");
        using var wrong = await api.SignInAsync("ada@x.com", "wrong horse battery");
        using var unknown = await api.SignInAsync("nobody@x.com");
        using var malformed = await api.SignInAsync("nobody", "x");
        using var overlong = await api.SignInAsync("ada@x.com", new string('a', 1_000_000));

        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        Assert.NotEqual(AuthApi.RefreshToken(signUp), AuthApi.RefreshToken(signIn));
        Assert.Equal(2L, await Count(api, "SELECT count(*) FROM orvano.auth_sessions WHERE user_id = @id", Guid.Parse(AuthApi.UserId(signUp))));
        foreach (var refused in new[] { wrong, unknown, malformed, overlong })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
            Assert.Equal("invalid_credentials", refused.Code);
            Assert.Equal(Without(wrong.Body, "requestId"), Without(refused.Body, "requestId"));
        }
    }

    [Fact]
    public async Task A_body_over_1_MiB_is_refused_with_413_before_any_work()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var tooBig = await api.SignUpAsync("ada@x.com", new string('a', (int)ServerRole.MaxRequestBodyBytes));

        using var signUp = await api.SignUpAsync("ada@x.com");

        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "invalid_request"), (tooBig.Status, tooBig.Code));
        Assert.Equal(HttpStatusCode.Created, signUp.Status);
    }

    [Fact]
    public async Task A_blocked_user_learns_it_only_with_the_right_password()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET status = 'blocked'");

        using var right = await api.SignInAsync("ada@x.com");
        using var wrong = await api.SignInAsync("ada@x.com", "wrong horse battery");

        Assert.Equal((HttpStatusCode.Forbidden, "user_blocked"), (right.Status, right.Code));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_credentials"), (wrong.Status, wrong.Code));
    }

    [Fact]
    public async Task The_access_token_is_an_ES256_JWT_with_ids_only()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var reply = await api.SignUpAsync("ada@x.com", extra: new { email = "ada@x.com", password = "correct horse battery", name = "Ada" });

        var parts = AuthApi.AccessToken(reply).Split('.');
        using var header = JsonDocument.Parse(Base64Url(parts[0]));
        using var claims = JsonDocument.Parse(Base64Url(parts[1]));

        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal(22, header.RootElement.GetProperty("kid").GetString()!.Length);
        Assert.Equal(["aud", "email_verified", "exp", "iat", "iss", "sid", "sub"], claims.RootElement.EnumerateObject().Select(p => p.Name).Order());
        // Spec 0010 AC-14: a boolean read from the user row when the token is issued.
        Assert.Equal(System.Text.Json.JsonValueKind.False, claims.RootElement.GetProperty("email_verified").ValueKind);
        Assert.Equal($"{OrvanoProcess.PublicUrl}/v1/projects/{AuthApi.Project}", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal(AuthApi.Project, claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(AuthApi.UserId(reply), claims.RootElement.GetProperty("sub").GetString());
        Assert.Equal(reply.Body.GetProperty("session").GetProperty("sessionId").GetString(), claims.RootElement.GetProperty("sid").GetString());
        Assert.Equal(900, claims.RootElement.GetProperty("exp").GetInt64() - claims.RootElement.GetProperty("iat").GetInt64());
        Assert.DoesNotContain("ada@x.com", Encoding.UTF8.GetString(Base64Url(parts[1])));
    }

    [Fact]
    public async Task The_current_user_needs_a_valid_bearer_of_the_project_whose_session_is_active()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var token = AuthApi.AccessToken(signUp);

        using var me = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: token);
        using var none = await api.SendAsync(HttpMethod.Get, "/v1/account");
        using var garbage = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: "not.a.token");
        using var otherProject = await api.SendAsync(HttpMethod.Get, "/v1/account", project: AuthApi.OtherProject, bearer: token);

        Assert.Equal(HttpStatusCode.OK, me.Status);
        Assert.Equal(AuthApi.UserId(signUp), me.Body.GetProperty("id").GetString());
        Assert.Equal("ada@x.com", me.Body.GetProperty("email").GetString());
        Assert.Equal((HttpStatusCode.Unauthorized, "session_required"), (none.Status, none.Code));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_token"), (garbage.Status, garbage.Code));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_token"), (otherProject.Status, otherProject.Code));
    }

    [Fact]
    public async Task A_token_whose_session_ended_or_whose_user_is_blocked_is_refused()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var ended = await api.SignUpAsync("ada@x.com");
        using var blocked = await api.SignUpAsync("bob@x.com");
        // Before either token is ever used, so no cached answer stands in the way.
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET ended_at = now(), end_reason = 'revoked' WHERE user_id = @id", ("id", Guid.Parse(AuthApi.UserId(ended))));
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_users SET status = 'blocked' WHERE id = @id", ("id", Guid.Parse(AuthApi.UserId(blocked))));

        using var a = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(ended));
        using var b = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(blocked));

        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_token"), (a.Status, a.Code));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_token"), (b.Status, b.Code));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.BadRequest, "invalid_request")]
    [InlineData("nosuchproject", HttpStatusCode.NotFound, "project_not_found")]
    [InlineData("console", HttpStatusCode.NotFound, "project_not_found")]
    public async Task Account_calls_need_a_servable_project(string? project, HttpStatusCode status, string code)
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var reply = await api.SignUpAsync("ada@x.com", project: project);

        Assert.Equal((status, code), (reply.Status, reply.Code));
    }

    // Spec 0003 AC-4: provisioning and failed are not ready yet; deleting is gone at once.
    [Theory]
    [InlineData("provisioning", HttpStatusCode.Conflict, "project_not_ready")]
    [InlineData("failed", HttpStatusCode.Conflict, "project_not_ready")]
    [InlineData("deleting", HttpStatusCode.NotFound, "project_not_found")]
    public async Task A_project_that_is_not_active_is_not_served(string status, HttpStatusCode expected, string code)
    {
        await using var api = await AuthApi.StartAsync(postgres);
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.platform_projects SET status = @s, deleted_at = CASE WHEN @s = 'deleting' THEN now() END, " +
            "purge_after = CASE WHEN @s = 'deleting' THEN now() + interval '7 days' END WHERE id = @id",
            ("s", status), ("id", AuthApi.OtherProject));

        using var reply = await api.SignUpAsync("ada@x.com", project: AuthApi.OtherProject);

        Assert.Equal((expected, code), (reply.Status, reply.Code));
    }

    [Fact]
    public async Task The_JWKS_and_discovery_list_the_projects_public_keys_with_a_public_cache()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var empty = await api.SendAsync(HttpMethod.Get, $"/v1/projects/{AuthApi.Project}/.well-known/jwks.json", project: null);
        using var signUp = await api.SignUpAsync("ada@x.com");

        using var jwks = await api.SendAsync(HttpMethod.Get, $"/v1/projects/{AuthApi.Project}/.well-known/jwks.json", project: null);
        using var discovery = await api.SendAsync(HttpMethod.Get, $"/v1/projects/{AuthApi.Project}/.well-known/openid-configuration", project: null);
        using var console = await api.SendAsync(HttpMethod.Get, "/v1/projects/console/.well-known/jwks.json", project: null);

        Assert.Equal(0, empty.Body.GetProperty("keys").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, jwks.Status);
        Assert.Equal("public, max-age=300", jwks.Headers.CacheControl?.ToString());
        var key = Assert.Single(jwks.Body.GetProperty("keys").EnumerateArray());
        Assert.Equal(("EC", "P-256", "ES256", "sig"), (key.GetProperty("kty").GetString(), key.GetProperty("crv").GetString(), key.GetProperty("alg").GetString(), key.GetProperty("use").GetString()));
        Assert.False(key.TryGetProperty("d", out _));
        using var header = JsonDocument.Parse(Base64Url(AuthApi.AccessToken(signUp).Split('.')[0]));
        Assert.Equal(header.RootElement.GetProperty("kid").GetString(), key.GetProperty("kid").GetString());

        var issuer = $"{OrvanoProcess.PublicUrl}/v1/projects/{AuthApi.Project}";
        Assert.Equal(issuer, discovery.Body.GetProperty("issuer").GetString());
        Assert.Equal(issuer + "/.well-known/jwks.json", discovery.Body.GetProperty("jwks_uri").GetString());
        Assert.Equal((HttpStatusCode.NotFound, "project_not_found"), (console.Status, console.Code));
    }

    [Fact]
    public async Task Racing_first_sign_ins_of_a_project_share_one_signing_key_and_every_token_works()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "INSERT INTO orvano.auth_users (project_id, email) SELECT @p, 'u' || g || '@x.com' FROM generate_series(1, 20) g", ("p", AuthApi.Project));
        var hash = await CurrentHashAsync(api);
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "INSERT INTO orvano.auth_passwords (user_id, project_id, hash) SELECT id, project_id, @h FROM orvano.auth_users WHERE project_id = @p", ("h", hash), ("p", AuthApi.Project));

        var replies = await Task.WhenAll(Enumerable.Range(1, 20).Select(i => api.SignInAsync($"u{i}@x.com")));

        Assert.All(replies, r => Assert.Equal(HttpStatusCode.Created, r.Status));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_signing_keys WHERE project_id = @p", ("p", AuthApi.Project)));
        foreach (var r in replies)
        {
            using var me = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(r));
            Assert.Equal(HttpStatusCode.OK, me.Status);
            r.Dispose();
        }
    }

    [Fact]
    public async Task The_eleventh_sign_in_for_one_email_within_15_minutes_is_limited_but_other_emails_are_not()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        for (var i = 0; i < 10; i++)
        {
            using var attempt = await api.SignInAsync("target@x.com", "wrong horse battery");
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.Status);
        }

        using var limited = await api.SignInAsync("TARGET@x.com");
        using var other = await api.SignInAsync("other@x.com");

        Assert.Equal((HttpStatusCode)429, limited.Status);
        Assert.Equal("rate_limited", limited.Code);
        Assert.InRange(int.Parse(limited.Headers.GetValues("Retry-After").Single()), 1, 900);
        Assert.Equal(HttpStatusCode.Unauthorized, other.Status);
    }

    [Fact]
    public async Task The_61st_sign_up_from_one_ip_within_an_hour_is_limited()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var ip = new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.50" };

        for (var i = 0; i < 60; i++)
        {
            // An invalid password: no hash is spent, but the attempt still counts.
            using var attempt = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = $"u{i}@x.com", password = "short" }, headers: ip);
            Assert.Equal(HttpStatusCode.BadRequest, attempt.Status);
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = "last@x.com", password = "correct horse battery" }, headers: ip);
        using var otherIp = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = "last@x.com", password = "correct horse battery" },
            headers: new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.51" });

        Assert.Equal((HttpStatusCode)429, limited.Status);
        Assert.Equal(HttpStatusCode.Created, otherIp.Status);
    }

    [Fact]
    public async Task App_sign_ups_and_server_created_users_share_one_limit_per_ip()
    {
        // AC-30: account.create and users.create count against the same 60 per hour of one connection IP.
        await using var api = await AuthApi.StartAsync(postgres);
        var ip = new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.52" };
        var serverIp = new Dictionary<string, string>(ip) { ["X-Orvano-Key"] = AuthApi.ServerKey };

        for (var i = 0; i < 30; i++)
        {
            // Invalid passwords: no hash is spent, but each attempt still counts.
            using var app = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = $"a{i}@x.com", password = "short" }, headers: ip);
            using var server = await api.SendAsync(HttpMethod.Post, "/v1/users", new { email = $"s{i}@x.com", password = "short" }, headers: serverIp);
            Assert.Equal(HttpStatusCode.BadRequest, app.Status);
            Assert.Equal(HttpStatusCode.BadRequest, server.Status);
        }

        using var limitedServer = await api.SendAsync(HttpMethod.Post, "/v1/users", new { email = "last@x.com", password = "correct horse battery" }, headers: serverIp);
        using var limitedApp = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = "last@x.com", password = "correct horse battery" }, headers: ip);

        Assert.Equal((HttpStatusCode)429, limitedServer.Status);
        Assert.Equal("rate_limited", limitedServer.Code);
        Assert.Equal((HttpStatusCode)429, limitedApp.Status);
    }

    [Fact]
    public async Task The_database_holds_no_plain_refresh_token_or_private_key()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var refresh = AuthApi.RefreshToken(signUp);

        var ciphertext = await TestDatabase.ScalarAsync<byte[]>(api.Database.Superuser, "SELECT refresh_ciphertext FROM orvano.auth_sessions");
        var privateKey = await TestDatabase.ScalarAsync<byte[]>(api.Database.Superuser, "SELECT private_key_ciphertext FROM orvano.auth_signing_keys");
        var dump = await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT string_agg(row_to_json(s)::text, '') FROM orvano.auth_sessions s");

        Assert.DoesNotContain(refresh, Encoding.ASCII.GetString(ciphertext), StringComparison.Ordinal);
        Assert.DoesNotContain(refresh.Split('.')[1], dump, StringComparison.Ordinal);
        Assert.Equal(1, privateKey[0]); // the envelope's format version, not a DER sequence (0x30)
        Assert.DoesNotContain("orv_rt_", api.Process.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@x.com", api.Process.Output, StringComparison.Ordinal);
    }

    private static Task<long> Count(AuthApi api, string sql, Guid id) =>
        TestDatabase.ScalarAsync<long>(api.Database.Superuser, sql, ("id", id));

    private static async Task<string> CurrentHashAsync(AuthApi api)
    {
        using var hasher = new Orvano.Auth.Domain.PasswordHasher();
        return (await hasher.TryHashAsync("correct horse battery", Ct))!;
    }

    private static byte[] Base64Url(string text) => System.Buffers.Text.Base64Url.DecodeFromChars(text);

    private static string Without(JsonElement body, string property) =>
        JsonSerializer.Serialize(body.EnumerateObject().Where(p => p.Name != property).ToDictionary(p => p.Name, p => p.Value));
}
