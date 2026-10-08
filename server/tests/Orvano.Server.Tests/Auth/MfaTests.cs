using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using Orvano.Auth.Domain;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 build task 1 over HTTP against the real binary: TOTP enrollment, the challenge at password sign in, the
// second step with a TOTP or recovery code, and the aal and amr claims. AC-5 to AC-10, AC-12, AC-13, AC-16, AC-17,
// AC-25, AC-26, AC-33, AC-34. Codes come from the returned secret and the real clock, like an authenticator app's.
public class MfaTests(PostgresFixture postgres)
{
    private const string Password = "correct horse battery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Turning_on_totp_challenges_password_sign_in_and_the_second_step_signs_in_at_level_2()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollAsync(api, "ada@x.com");

        using var stepOne = await api.SignInAsync("ada@x.com");
        Assert.Equal(HttpStatusCode.Created, stepOne.Status);
        Assert.Equal(JsonValueKind.Null, stepOne.Body.GetProperty("user").ValueKind);
        Assert.Equal(JsonValueKind.Null, stepOne.Body.GetProperty("session").ValueKind);
        Assert.False(stepOne.Body.GetProperty("isNewUser").GetBoolean());
        var mfa = stepOne.Body.GetProperty("mfa");
        Assert.Matches("^orv_mt_[A-Za-z0-9_-]{43}$", mfa.GetProperty("ticket").GetString());
        Assert.Equal(["totp", "recovery_code"], mfa.GetProperty("factors").EnumerateArray().Select(f => f.GetString()));
        Assert.InRange(mfa.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4.9), TimeSpan.FromMinutes(5.1));
        Assert.Equal(enrolled.LastSignInAt, await LastSignInAsync(api, enrolled.UserId));

        // The confirm used this step's code, so the next one (one step of drift) answers the second step.
        using var stepTwo = await StepTwoAsync(api, Ticket(stepOne), totpCode: CodeAt(enrolled.Secret, +1));

        Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        Assert.Equal(JsonValueKind.Null, stepTwo.Body.GetProperty("mfa").ValueKind);
        Assert.Equal(enrolled.UserId, AuthApi.UserId(stepTwo));
        var claims = Claims(AuthApi.AccessToken(stepTwo));
        Assert.Equal(2, claims.GetProperty("aal").GetInt32());
        Assert.Equal(["mfa", "otp", "pwd"], claims.GetProperty("amr").EnumerateArray().Select(a => a.GetString()));
        Assert.True(await LastSignInAsync(api, enrolled.UserId) > enrolled.LastSignInAt);

        using var sessions = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions", bearer: AuthApi.AccessToken(stepTwo));
        var current = sessions.Body.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("current").GetBoolean());
        Assert.Equal("password", current.GetProperty("method").GetString());
        Assert.Equal(2, current.GetProperty("aal").GetInt32());
        Assert.Equal(["mfa", "otp", "pwd"], current.GetProperty("amr").EnumerateArray().Select(a => a.GetString()));

        // A refresh keeps the claims, read from the session row.
        using var refreshed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(stepTwo) });
        Assert.Equal(2, Claims(refreshed.Body.GetProperty("accessToken").GetString()!).GetProperty("aal").GetInt32());
        Assert.Equal("2", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT payload->>'aal' FROM orvano.events WHERE type = 'auth.session.created' ORDER BY id DESC LIMIT 1"));
    }

    [Fact]
    public async Task A_user_without_mfa_signs_in_as_before_at_level_1()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        using var signIn = await api.SignInAsync("ada@x.com");

        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        Assert.Equal(JsonValueKind.Null, signIn.Body.GetProperty("mfa").ValueKind);
        var claims = Claims(AuthApi.AccessToken(signIn));
        Assert.Equal(1, claims.GetProperty("aal").GetInt32());
        Assert.Equal(["pwd"], claims.GetProperty("amr").EnumerateArray().Select(a => a.GetString()));
        using var status = await api.SendAsync(HttpMethod.Get, "/v1/account/mfa", bearer: AuthApi.AccessToken(signIn));
        Assert.False(status.Body.GetProperty("mfaEnabled").GetBoolean());
        Assert.False(status.Body.GetProperty("totpConfirmed").GetBoolean());
        Assert.Equal(["totp"], status.Body.GetProperty("factorsAvailable").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task Confirming_totp_answers_10_codes_raises_the_session_and_ends_every_other_session()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        await VerifyEmailAsync(api, AuthApi.UserId(signUp));
        using var other = await api.SignInAsync("ada@x.com");
        var bearer = AuthApi.AccessToken(signUp);

        using var setup = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, setup.Status);
        var secret = setup.Body.GetProperty("secret").GetString()!;
        Assert.Matches("^[A-Z2-7]{32}$", secret);
        Assert.Equal(
            $"otpauth://totp/Auth%20project:ada%40x.com?secret={secret}&issuer=Auth%20project&algorithm=SHA1&digits=6&period=30",
            setup.Body.GetProperty("uri").GetString());

        using var wrong = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp/confirm", new { code = WrongCode(secret) }, bearer: bearer);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal("invalid_mfa_code", wrong.Code);

        using var confirmed = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp/confirm", new { code = CodeAt(secret, 0) }, bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        var codes = confirmed.Body.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(10, codes.Count);
        Assert.All(codes, c => Assert.Matches("^[A-Z2-7]{5}-[A-Z2-7]{5}$", c));
        var session = confirmed.Body.GetProperty("session");
        Assert.Equal(AuthApi.RefreshToken(signUp), session.GetProperty("refreshToken").GetString());
        Assert.Equal(Sid(signUp), session.GetProperty("sessionId").GetString());
        var claims = Claims(session.GetProperty("accessToken").GetString()!);
        Assert.Equal(2, claims.GetProperty("aal").GetInt32());
        Assert.Equal(["mfa", "otp", "pwd"], claims.GetProperty("amr").EnumerateArray().Select(a => a.GetString()));

        using var otherSession = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(other));
        Assert.Equal(HttpStatusCode.Unauthorized, otherSession.Status);
        Assert.Equal("mfa_enabled", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT end_reason FROM orvano.auth_sessions WHERE id = @id", ("id", Guid.Parse(Sid(other)))));
        using var stillMine = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, stillMine.Status);

        using var status = await api.SendAsync(HttpMethod.Get, "/v1/account/mfa", bearer: bearer);
        Assert.True(status.Body.GetProperty("mfaEnabled").GetBoolean());
        Assert.Equal(10, status.Body.GetProperty("recoveryCodesRemaining").GetInt32());
        using var again = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp/confirm", new { code = CodeAt(secret, 1) }, bearer: bearer);
        Assert.Equal("totp_not_pending", again.Code);

        foreach (var type in new[] { "auth.mfa.enabled", "auth.recovery_codes.created" })
        {
            Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = @type", ("type", type)));
        }
    }

    [Fact]
    public async Task Enrollment_needs_a_verified_email_a_fresh_session_and_mfa_off()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var bearer = AuthApi.AccessToken(signUp);

        using var unverified = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", bearer: bearer);
        Assert.Equal(HttpStatusCode.Conflict, unverified.Status);
        Assert.Equal("email_not_verified", unverified.Code);

        await VerifyEmailAsync(api, AuthApi.UserId(signUp));
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET created_at = now() - interval '11 minutes' WHERE id = @id", ("id", Guid.Parse(Sid(signUp))));
        using var stale = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", bearer: bearer);
        Assert.Equal(HttpStatusCode.Forbidden, stale.Status);
        Assert.Equal("reauthentication_required", stale.Code);

        var enrolled = await EnrollAsync(api, "grace@x.com");
        using var signIn = await StepTwoAsync(api, Ticket(await api.SignInAsync("grace@x.com")), totpCode: CodeAt(enrolled.Secret, +1));
        using var already = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", bearer: AuthApi.AccessToken(signIn));
        Assert.Equal(HttpStatusCode.Conflict, already.Status);
        Assert.Equal("mfa_already_enabled", already.Code);
    }

    [Fact]
    public async Task A_code_works_once_and_five_wrong_factors_end_the_ticket()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollAsync(api, "ada@x.com");

        // The confirm's own code can't answer again.
        using var first = await api.SignInAsync("ada@x.com");
        var usedStep = await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT last_used_step FROM orvano.auth_totp_factors");
        using var reused = await StepTwoAsync(api, Ticket(first), totpCode: Totp.Code(DecodeBase32(enrolled.Secret), usedStep));
        Assert.Equal(HttpStatusCode.Unauthorized, reused.Status);
        Assert.Equal("invalid_mfa_code", reused.Code);

        for (var i = 0; i < 4; i++)
        {
            using var wrong = await StepTwoAsync(api, Ticket(first), totpCode: WrongCode(enrolled.Secret));
            Assert.Equal("invalid_mfa_code", wrong.Code);
        }

        using var gone = await StepTwoAsync(api, Ticket(first), totpCode: CodeAt(enrolled.Secret, +1));
        Assert.Equal(HttpStatusCode.Unauthorized, gone.Status);
        Assert.Equal("invalid_mfa_ticket", gone.Code);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_mfa_tickets"));
    }

    [Fact]
    public async Task A_recovery_code_answers_once_in_any_case_and_spacing()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollAsync(api, "ada@x.com");
        var code = enrolled.RecoveryCodes[3];

        using var first = await api.SignInAsync("ada@x.com");
        using var signedIn = await StepTwoAsync(api, Ticket(first), recoveryCode: " " + code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        Assert.Equal(["mfa", "pwd", "rec"], Claims(AuthApi.AccessToken(signedIn)).GetProperty("amr").EnumerateArray().Select(a => a.GetString()));

        using var second = await api.SignInAsync("ada@x.com");
        using var reused = await StepTwoAsync(api, Ticket(second), recoveryCode: code);
        Assert.Equal("invalid_mfa_code", reused.Code);
        using var status = await api.SendAsync(HttpMethod.Get, "/v1/account/mfa", bearer: AuthApi.AccessToken(signedIn));
        Assert.Equal(9, status.Body.GetProperty("recoveryCodesRemaining").GetInt32());
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.recovery_code.used'"));
    }

    [Fact]
    public async Task Two_parallel_right_codes_give_one_session()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollAsync(api, "ada@x.com");
        using var stepOne = await api.SignInAsync("ada@x.com");
        var ticket = Ticket(stepOne);

        var replies = await Task.WhenAll(
            StepTwoAsync(api, ticket, recoveryCode: enrolled.RecoveryCodes[0]),
            StepTwoAsync(api, ticket, recoveryCode: enrolled.RecoveryCodes[1]));

        Assert.Single(replies, r => r.Status == HttpStatusCode.Created);
        Assert.Single(replies, r => r.Code == "invalid_mfa_ticket");
        foreach (var reply in replies) reply.Dispose();
    }

    [Fact]
    public async Task A_ticket_works_only_under_its_project_and_in_its_format()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollAsync(api, "ada@x.com");
        using var stepOne = await api.SignInAsync("ada@x.com");

        using var otherProject = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa",
            new { ticket = Ticket(stepOne), totpCode = CodeAt(enrolled.Secret, +1) }, project: AuthApi.OtherProject);
        Assert.Equal("invalid_mfa_ticket", otherProject.Code);

        using var malformed = await StepTwoAsync(api, "orv_mt_short", totpCode: "123456");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.Status);
        using var both = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa",
            new { ticket = Ticket(stepOne), totpCode = "123456", recoveryCode = enrolled.RecoveryCodes[0] });
        Assert.Equal("invalid_request", both.Code);
        using var expired = await api.SignInAsync("ada@x.com");
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_mfa_tickets SET expires_at = now() - interval '1 second'");
        using var late = await StepTwoAsync(api, Ticket(expired), totpCode: CodeAt(enrolled.Secret, +1));
        Assert.Equal("invalid_mfa_ticket", late.Code);
    }

    [Fact]
    public async Task A_sixth_ticket_deletes_the_users_oldest()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollAsync(api, "ada@x.com");
        var tickets = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            using var stepOne = await api.SignInAsync("ada@x.com");
            tickets.Add(Ticket(stepOne));
        }

        Assert.Equal(5L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_mfa_tickets"));
        using var oldest = await StepTwoAsync(api, tickets[0], totpCode: CodeAt(enrolled.Secret, +1));
        Assert.Equal("invalid_mfa_ticket", oldest.Code);
        using var newest = await StepTwoAsync(api, tickets[5], totpCode: CodeAt(enrolled.Secret, +1));
        Assert.Equal(HttpStatusCode.Created, newest.Status);
    }

    [Fact]
    public async Task With_totp_off_for_the_project_nobody_is_challenged_and_nobody_enrolls()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollAsync(api, "ada@x.com");
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "INSERT INTO orvano.auth_method_settings (project_id, totp_enabled) VALUES (@project, false)", ("project", AuthApi.Project));

        using var signIn = await api.SignInAsync("ada@x.com");
        Assert.Equal(JsonValueKind.Null, signIn.Body.GetProperty("mfa").ValueKind);
        Assert.Equal(1, Claims(AuthApi.AccessToken(signIn)).GetProperty("aal").GetInt32());
        using var status = await api.SendAsync(HttpMethod.Get, "/v1/account/mfa", bearer: AuthApi.AccessToken(signIn));
        Assert.False(status.Body.GetProperty("mfaEnabled").GetBoolean());
        Assert.True(status.Body.GetProperty("totpConfirmed").GetBoolean());

        using var grace = await api.SignUpAsync("grace@x.com");
        await VerifyEmailAsync(api, AuthApi.UserId(grace));
        using var refused = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", bearer: AuthApi.AccessToken(grace));
        Assert.Equal("factor_not_enabled", refused.Code);
        Assert.NotEmpty(enrolled.Secret);
    }

    [Fact]
    public async Task The_database_and_events_hold_no_secret_code_or_ticket()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollAsync(api, "ada@x.com");
        using var stepOne = await api.SignInAsync("ada@x.com");
        var ticket = Ticket(stepOne);

        var events = await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT coalesce(string_agg(payload::text, ''), '') FROM orvano.events");
        Assert.DoesNotContain(ticket, events, StringComparison.Ordinal);
        Assert.DoesNotContain(enrolled.Secret, events, StringComparison.Ordinal);
        Assert.DoesNotContain(enrolled.RecoveryCodes[0], events, StringComparison.Ordinal);
        var secretBytes = DecodeBase32(enrolled.Secret);
        Assert.False(await TestDatabase.ScalarAsync<bool>(api.Database.Superuser,
            "SELECT position(@secret IN secret_ciphertext) > 0 FROM orvano.auth_totp_factors", ("secret", secretBytes)));
        Assert.Equal(32, await TestDatabase.ScalarAsync<int>(api.Database.Superuser, "SELECT octet_length(ticket_hash) FROM orvano.auth_mfa_tickets"));
    }

    private sealed record Enrolled(string UserId, string Secret, IReadOnlyList<string> RecoveryCodes, DateTime LastSignInAt);

    /// <summary>Signs a user up, verifies their email, and turns on TOTP with this step's code.</summary>
    private static async Task<Enrolled> EnrollAsync(AuthApi api, string email)
    {
        using var signUp = await api.SignUpAsync(email);
        var userId = AuthApi.UserId(signUp);
        await VerifyEmailAsync(api, userId);
        var bearer = AuthApi.AccessToken(signUp);
        using var setup = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, setup.Status);
        var secret = setup.Body.GetProperty("secret").GetString()!;
        using var confirmed = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp/confirm", new { code = CodeAt(secret, 0) }, bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        var codes = confirmed.Body.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList();
        return new Enrolled(userId, secret, codes, await LastSignInAsync(api, userId));
    }

    private static async Task VerifyEmailAsync(AuthApi api, string userId)
    {
        using var marked = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{userId}/email-verification", new { verified = true });
        Assert.Equal(HttpStatusCode.OK, marked.Status);
    }

    private static Task<Reply> StepTwoAsync(AuthApi api, string ticket, string? totpCode = null, string? recoveryCode = null) =>
        api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa", new Dictionary<string, string?>
        {
            ["ticket"] = ticket,
            ["totpCode"] = totpCode,
            ["recoveryCode"] = recoveryCode,
        }.Where(p => p.Value is not null).ToDictionary());

    private static string Ticket(Reply stepOne) => stepOne.Body.GetProperty("mfa").GetProperty("ticket").GetString()!;

    private static string Sid(Reply signedIn) => signedIn.Body.GetProperty("session").GetProperty("sessionId").GetString()!;

    private static Task<DateTime> LastSignInAsync(AuthApi api, string userId) =>
        TestDatabase.ScalarAsync<DateTime>(api.Database.Superuser, "SELECT last_sign_in_at FROM orvano.auth_users WHERE id = @id", ("id", Guid.Parse(userId)));

    /// <summary>The code an authenticator app shows for this step plus <paramref name="offset"/> steps.</summary>
    private static string CodeAt(string base32Secret, int offset) =>
        Totp.Code(DecodeBase32(base32Secret), Totp.StepAt(DateTimeOffset.UtcNow) + offset);

    /// <summary>A code that matches none of the three accepted steps.</summary>
    private static string WrongCode(string base32Secret)
    {
        var secret = DecodeBase32(base32Secret);
        var step = Totp.StepAt(DateTimeOffset.UtcNow);
        var taken = Enumerable.Range(-2, 5).Select(o => Totp.Code(secret, step + o)).ToHashSet();
        return Enumerable.Range(0, 1_000_000).Select(n => n.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)).First(c => !taken.Contains(c));
    }

    private static byte[] DecodeBase32(string value)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in value)
        {
            buffer = (buffer << 5) | Base32.Alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits < 8) continue;
            bytes.Add((byte)(buffer >> (bits - 8)));
            bits -= 8;
        }

        return [.. bytes];
    }

    private static JsonElement Claims(string accessToken) =>
        JsonDocument.Parse(Base64Url.DecodeFromChars(accessToken.Split('.')[1])).RootElement.Clone();
}
