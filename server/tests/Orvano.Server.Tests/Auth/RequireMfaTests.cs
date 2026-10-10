using System.Net;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0014 build task 6 over HTTP against the real binary: the require MFA switch and its rules, the enrollment
// challenge at step one, the four enrollment operations with the ticket, and the step two challenge of a user with a
// factor but not MFA on (amended 2026-10-10). AC-2, AC-27.
public class RequireMfaTests(PostgresFixture postgres)
{
    private const string MethodsUrl = "/v1/console/project/auth/methods";
    private const string Redirect = "https://app.example.com/auth/callback";

    [Fact]
    public async Task The_switch_needs_a_factor_users_can_enroll_and_an_email_server()
    {
        await using (var noSmtp = await AuthApi.StartAsync(postgres))
        {
            using var refused = await noSmtp.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { mfaRequired = true });
            Assert.Equal((HttpStatusCode.Conflict, "email_not_configured"), (refused.Status, refused.Code));
        }

        await using var api = await AuthApi.StartAsync(postgres, smtp: true);
        using (var bothOff = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { totpEnabled = false, mfaRequired = true }))
            Assert.Equal((HttpStatusCode.BadRequest, "invalid_request"), (bothOff.Status, bothOff.Code));

        using var on = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { mfaRequired = true });
        Assert.Equal(HttpStatusCode.OK, on.Status);
        Assert.True(on.Body.GetProperty("mfaRequired").GetBoolean());
        // Counted on a read only.
        Assert.Equal(JsonValueKind.Null, on.Body.GetProperty("activeUsersWithoutMfa").ValueKind);

        // Turning the last factor off while it is on is refused too.
        using (var lastOff = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { totpEnabled = false }))
            Assert.Equal("invalid_request", lastOff.Code);
        Assert.Contains("mfaRequired", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT payload::text FROM orvano.events WHERE type = 'auth.method_settings.updated' ORDER BY id DESC LIMIT 1"));
    }

    [Fact]
    public async Task A_password_user_without_a_factor_enrolls_totp_with_the_ticket_and_gets_a_level_2_session()
    {
        await using var api = await AuthApi.StartAsync(postgres, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var userId = AuthApi.UserId(signUp);
        await VerifyEmailAsync(api, userId);
        await RequireAsync(api);

        // A session made before the switch keeps working, and the card counts its user.
        using (var refreshed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(signUp) }))
            Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.Equal(1, await WithoutMfaAsync(api));

        using var stepOne = await api.SignInAsync("ada@x.com");
        AssertCreated(stepOne);
        AssertEnrollment(stepOne, ["totp"]);
        var ticket = Ticket(stepOne);
        var expiresIn = stepOne.Body.GetProperty("mfa").GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow;
        Assert.InRange(expiresIn.TotalMinutes, 14, 15.1);

        // An enrollment ticket is not a step two ticket.
        using (var stepTwo = await StepTwoAsync(api, ticket, totpCode: "123456"))
            Assert.Equal((HttpStatusCode.Unauthorized, "invalid_mfa_ticket"), (stepTwo.Status, stepTwo.Code));

        using var setup = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/enrollment/totp", new { ticket });
        AssertCreated(setup);
        var secret = setup.Body.GetProperty("secret").GetString()!;

        using (var wrong = await CompleteTotpAsync(api, ticket, WrongCode(secret)))
            Assert.Equal((HttpStatusCode.Unauthorized, "invalid_mfa_code"), (wrong.Status, wrong.Code));

        using var done = await CompleteTotpAsync(api, ticket, CodeAt(secret, 0));
        AssertCreated(done);
        Assert.Equal(10, done.Body.GetProperty("recoveryCodes").GetArrayLength());
        var auth = done.Body.GetProperty("auth");
        Assert.Equal(userId, auth.GetProperty("user").GetProperty("id").GetString());
        Assert.False(auth.GetProperty("verificationRequired").GetBoolean());
        var claims = Claims(auth.GetProperty("session").GetProperty("accessToken").GetString()!);
        Assert.Equal(2, claims.GetProperty("aal").GetInt32());
        Assert.Equal(["mfa", "otp", "pwd"], claims.GetProperty("amr").EnumerateArray().Select(a => a.GetString()!));
        Assert.Equal("password", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT method FROM orvano.auth_sessions WHERE id = @id", ("id", Guid.Parse(auth.GetProperty("session").GetProperty("sessionId").GetString()!))));

        // Turning MFA on ended the older session (spec 0013, AC-13), the ticket is spent, and the card's count drops.
        using (var old = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(signUp) }))
            Assert.Equal("invalid_refresh_token", old.Code);
        using (var again = await CompleteTotpAsync(api, ticket, CodeAt(secret, 1)))
            Assert.Equal("invalid_mfa_ticket", again.Code);
        Assert.Equal(0, await WithoutMfaAsync(api));

        // The next sign in is an ordinary step two, whose ticket the enrollment operations refuse.
        using var next = await api.SignInAsync("ada@x.com");
        Assert.False(next.Body.GetProperty("mfa").GetProperty("enrollmentRequired").GetBoolean());
        using (var misuse = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/enrollment/totp", new { ticket = Ticket(next) }))
            Assert.Equal("invalid_mfa_ticket", misuse.Code);
    }

    [Fact]
    public async Task Five_wrong_codes_end_the_ticket_and_an_unverified_user_is_sent_to_verify_first()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using (var signUp = await api.SignUpAsync("ada@x.com")) await VerifyEmailAsync(api, AuthApi.UserId(signUp));
        using (await api.SignUpAsync("bob@x.com")) { }
        await RequireAsync(api);

        using var stepOne = await api.SignInAsync("ada@x.com");
        var ticket = Ticket(stepOne);
        using var setup = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/enrollment/totp", new { ticket });
        var secret = setup.Body.GetProperty("secret").GetString()!;
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await CompleteTotpAsync(api, ticket, WrongCode(secret));
            Assert.Equal("invalid_mfa_code", wrong.Code);
        }

        using (var gone = await CompleteTotpAsync(api, ticket, CodeAt(secret, 0)))
            Assert.Equal("invalid_mfa_ticket", gone.Code);

        // Spec 0013 enrolls only verified emails: the right password gets the verified email refusal, never a ticket.
        using var unverified = await api.SignInAsync("bob@x.com");
        Assert.Equal((HttpStatusCode.Forbidden, "email_verification_required"), (unverified.Status, unverified.Code));
        using var wrongPassword = await api.SignInAsync("bob@x.com", "not the password");
        Assert.Equal("invalid_credentials", wrongPassword.Code);

        // Sign up follows the verified email flow while the switch is on (AC-12).
        using var pending = await api.SendAsync(HttpMethod.Post, "/v1/account",
            new { email = "new@x.com", password = "correct horse battery", verificationRedirectUrl = Redirect });
        AssertCreated(pending);
        Assert.True(pending.Body.GetProperty("verificationRequired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, pending.Body.GetProperty("session").ValueKind);
    }

    [Fact]
    public async Task A_new_magic_link_user_and_a_recovery_are_sent_to_enrollment_and_a_passkey_enrolls_too()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        await PasskeyTests.EnablePasskeysAsync(api);
        using (var signUp = await api.SignUpAsync("ada@x.com")) await VerifyEmailAsync(api, AuthApi.UserId(signUp));
        await RequireAsync(api);

        // A user the magic link creates enrolls before their first session.
        var token = await PasswordlessTests.MagicLinkAsync(api, "neo@x.com", Redirect);
        using var created = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        AssertCreated(created);
        AssertEnrollment(created, ["totp", "passkey"]);

        using var options = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/enrollment/passkey", new { ticket = Ticket(created) });
        AssertCreated(options);
        var credential = await PasskeyTests.CreateCredentialAsync(api, options.Body.GetProperty("options"));
        var challengeId = options.Body.GetProperty("challengeId").GetString();
        using var done = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/enrollment/passkey/confirm",
            new { ticket = Ticket(created), challengeId, credential, name = "Laptop" });
        AssertCreated(done);
        Assert.Equal(JsonValueKind.Null, done.Body.GetProperty("recoveryCodes").ValueKind);
        var claims = Claims(done.Body.GetProperty("auth").GetProperty("session").GetProperty("accessToken").GetString()!);
        Assert.Equal(2, claims.GetProperty("aal").GetInt32());
        Assert.Equal(["email", "hwk", "mfa", "user"], claims.GetProperty("amr").EnumerateArray().Select(a => a.GetString()!));

        // A recovery's new password waits on the enrollment, as it waits on step two (spec 0013, AC-6).
        using (var sent = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "ada@x.com", redirectUrl = Redirect }))
            Assert.Equal(HttpStatusCode.Accepted, sent.Status);
        var recovery = (await api.LatestEmailAsync("ada@x.com"))!.Token!;
        using var reset = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token = recovery, password = "brand new battery" });
        AssertEnrollment(reset, ["totp", "passkey"]);
        using (var oldStill = await api.SignInAsync("ada@x.com"))
            Assert.True(oldStill.Body.GetProperty("mfa").GetProperty("enrollmentRequired").GetBoolean());

        using var setup = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/enrollment/totp", new { ticket = Ticket(reset) });
        using var enrolled = await CompleteTotpAsync(api, Ticket(reset), CodeAt(setup.Body.GetProperty("secret").GetString()!, 0));
        AssertCreated(enrolled);
        using var newPassword = await api.SignInAsync("ada@x.com", "brand new battery");
        AssertCreated(newPassword);
        Assert.False(newPassword.Body.GetProperty("mfa").GetProperty("enrollmentRequired").GetBoolean());
    }

    [Fact]
    public async Task A_user_with_a_factor_but_not_mfa_on_is_challenged_for_the_passkey_except_at_passkey_sign_in()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        await PasskeyTests.EnablePasskeysAsync(api);

        // Ada's only factor is a passkey; Bob has a confirmed TOTP factor and a passkey, and TOTP is turned off below.
        var ada = await PasskeyTests.RegisterAsync(api, await PasskeyTests.VerifiedUserAsync(api, "ada@x.com"));
        var bob = await EnrollAsync(api, "bob@x.com");
        await PasskeyTests.RegisterAsync(api, await PasskeyTests.StrongBearerAsync(api, "bob@x.com", bob.Secret));

        // With the switch off, Ada's password still opens a session at level 1, as before.
        using (var before = await api.SignInAsync("ada@x.com"))
        {
            AssertCreated(before);
            Assert.Equal(JsonValueKind.Null, before.Body.GetProperty("mfa").ValueKind);
            Assert.Equal(1, Claims(AuthApi.AccessToken(before)).GetProperty("aal").GetInt32());
        }

        await RequireAsync(api);

        // Password and magic link both stop at a step two challenge for the passkey: no session, no enrollment.
        using var password = await api.SignInAsync("ada@x.com");
        AssertChallenge(password, ["passkey"]);
        var token = await PasswordlessTests.MagicLinkAsync(api, "ada@x.com", Redirect);
        using (var magic = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token }))
            AssertChallenge(magic, ["passkey"]);

        // The passkey answers step two at level 2.
        var ticket = Ticket(password);
        using var challenge = await PasskeyTests.MfaChallengeAsync(api, ticket);
        using var stepTwo = await PasskeyTests.StepTwoWithPasskeyAsync(api, ticket, challenge, ada.CredentialId);
        AssertCreated(stepTwo);
        var bearer = AuthApi.AccessToken(stepTwo);
        Assert.Equal(2, Claims(bearer).GetProperty("aal").GetInt32());

        // Passkey sign in is never challenged, and MFA status stays TOTP defined: Ada reads as MFA off.
        using (var passkey = await PasskeyTests.PasskeySignInAsync(api, ada.CredentialId))
        {
            AssertCreated(passkey);
            Assert.Equal(JsonValueKind.Null, passkey.Body.GetProperty("mfa").ValueKind);
        }

        using (var status = await api.SendAsync(HttpMethod.Get, "/v1/account/mfa", bearer: bearer))
            Assert.False(status.Body.GetProperty("mfaEnabled").GetBoolean());

        // Bob, with TOTP off, has MFA off but still a factor, so he is challenged for the passkey alone.
        using (var off = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { totpEnabled = false }))
            Assert.Equal(HttpStatusCode.OK, off.Status);
        using (var bobStepOne = await api.SignInAsync("bob@x.com")) AssertChallenge(bobStepOne, ["passkey"]);
        using (var bobStatus = await api.AsServerAsync(HttpMethod.Get, $"/v1/users/{bob.UserId}/mfa"))
            Assert.False(bobStatus.Body.GetProperty("mfaEnabled").GetBoolean());
    }

    private static void AssertCreated(Reply reply) => Assert.True(reply.Status == HttpStatusCode.Created, $"{reply.Status}: {reply.Body}");

    private static async Task RequireAsync(AuthApi api)
    {
        using var on = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { mfaRequired = true });
        Assert.Equal(HttpStatusCode.OK, on.Status);
    }

    private static async Task<int> WithoutMfaAsync(AuthApi api)
    {
        using var read = await api.AsConsoleAsync(HttpMethod.Get, MethodsUrl);
        return read.Body.GetProperty("activeUsersWithoutMfa").GetInt32();
    }

    private static Task<Reply> CompleteTotpAsync(AuthApi api, string ticket, string code) =>
        api.SendAsync(HttpMethod.Post, "/v1/account/mfa/enrollment/totp/confirm", new { ticket, code });

    /// <summary>A step one that ended in enrollment: no user, no session, and the factors the user may enroll.</summary>
    private static void AssertEnrollment(Reply stepOne, string[] factors)
    {
        Assert.True(stepOne.Status == HttpStatusCode.Created, $"{stepOne.Status}: {stepOne.Body}");
        Assert.Equal(JsonValueKind.Null, stepOne.Body.GetProperty("user").ValueKind);
        Assert.Equal(JsonValueKind.Null, stepOne.Body.GetProperty("session").ValueKind);
        Assert.False(stepOne.Body.GetProperty("verificationRequired").GetBoolean());
        var mfa = stepOne.Body.GetProperty("mfa");
        Assert.True(mfa.GetProperty("enrollmentRequired").GetBoolean());
        Assert.Equal(factors, mfa.GetProperty("factors").EnumerateArray().Select(f => f.GetString()!));
    }

    /// <summary>A step one that ended in a step two challenge: no user, no session, and the factors to answer with.</summary>
    private static void AssertChallenge(Reply stepOne, string[] factors)
    {
        Assert.True(stepOne.Status == HttpStatusCode.Created, $"{stepOne.Status}: {stepOne.Body}");
        Assert.Equal(JsonValueKind.Null, stepOne.Body.GetProperty("session").ValueKind);
        var mfa = stepOne.Body.GetProperty("mfa");
        Assert.False(mfa.GetProperty("enrollmentRequired").GetBoolean());
        Assert.Equal(factors, mfa.GetProperty("factors").EnumerateArray().Select(f => f.GetString()!));
    }
}
