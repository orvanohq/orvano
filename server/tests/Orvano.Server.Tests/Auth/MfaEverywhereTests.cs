using System.Net;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 build task 2 over HTTP against the real binary: the challenge in every step one, recovery deferred to step
// two, turning MFA off, new recovery codes, step up, and claiming. AC-6, AC-8, AC-14, AC-15, AC-17 to AC-19, AC-29.
public class MfaEverywhereTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth/callback";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Magic_link_and_email_code_challenge_a_user_with_mfa_and_the_link_stays_used()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var ada = await EnrollAsync(api, "ada@x.com");

        var link = await PasswordlessTests.MagicLinkAsync(api, "ada@x.com", Redirect);
        using var byLink = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = link });
        AssertChallenged(byLink);
        using var again = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = link });
        Assert.Equal("invalid_email_token", again.Code);

        using var stepTwo = await StepTwoAsync(api, Ticket(byLink), recoveryCode: ada.RecoveryCodes[0]);
        Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        Assert.Equal(["email", "mfa", "rec"], Amr(stepTwo));
        Assert.Equal("magic_link", await SessionMethodAsync(api, Sid(stepTwo)));

        var code = await PasswordlessTests.EmailCodeAsync(api, "ada@x.com");
        using var byCode = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "ada@x.com", code });
        AssertChallenged(byCode);
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_email_tokens WHERE kind = 'email_code'"));
    }

    [Fact]
    public async Task A_magic_link_that_creates_the_user_is_never_challenged()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var link = await PasswordlessTests.MagicLinkAsync(api, "new@x.com", Redirect);

        using var signedIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = link });

        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        Assert.True(signedIn.Body.GetProperty("isNewUser").GetBoolean());
        Assert.Equal(JsonValueKind.Null, signedIn.Body.GetProperty("mfa").ValueKind);
    }

    [Fact]
    public async Task Provider_sign_in_challenges_a_user_with_mfa_and_step_two_keeps_the_provider()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var ada = new { sub = "google-ada", email = "ada@example.com", emailVerified = true, name = "Ada" };
        using var first = await OAuthDriver.SignInAsync(api, "google", ada);
        Assert.Equal(HttpStatusCode.Created, first.Status);
        var bearer = AuthApi.AccessToken(first);
        // A user with no password enrolls on a fresh session alone (AC-17).
        using var setup = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { }, bearer: bearer);
        var secret = setup.Body.GetProperty("secret").GetString()!;
        using var confirmed = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp/confirm", new { code = CodeAt(secret, 0) }, bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);

        using var second = await OAuthDriver.SignInAsync(api, "google", ada);
        AssertChallenged(second);
        using var stepTwo = await StepTwoAsync(api, Ticket(second), totpCode: CodeAt(secret, 1));

        Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        Assert.Equal(["fed", "mfa", "otp"], Amr(stepTwo));
        using var sessions = await api.SendAsync(HttpMethod.Get, "/v1/account/sessions", bearer: AuthApi.AccessToken(stepTwo));
        var current = sessions.Body.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("current").GetBoolean());
        Assert.Equal("oauth", current.GetProperty("method").GetString());
        Assert.Equal("google", current.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Recovery_for_a_user_with_mfa_changes_nothing_until_step_two()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var ada = await EnrollAsync(api, "ada@x.com");
        using var stepOne = await api.SignInAsync("ada@x.com");
        using var signedIn = await StepTwoAsync(api, Ticket(stepOne), recoveryCode: ada.RecoveryCodes[0]);
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);

        var token = await RecoveryTests.RequestTokenAsync(api, "ada@x.com");
        using var reset = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = "another horse battery" });
        AssertChallenged(reset);

        // Only the inbox: the old password still works, the new one does not, and the session stays.
        AssertChallenged(await api.SignInAsync("ada@x.com"));
        Assert.Equal("invalid_credentials", (await api.SignInAsync("ada@x.com", "another horse battery")).Code);
        using var stillThere = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(signedIn));
        Assert.Equal(HttpStatusCode.OK, stillThere.Status);
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.password.reset'"));

        using var stepTwo = await StepTwoAsync(api, Ticket(reset), recoveryCode: ada.RecoveryCodes[1]);

        Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        Assert.Equal(["mfa", "pwd", "rec"], Amr(stepTwo));
        using var ended = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(signedIn));
        Assert.Equal(HttpStatusCode.Unauthorized, ended.Status);
        Assert.Equal("password_reset", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT end_reason FROM orvano.auth_sessions WHERE id = @id", ("id", Guid.Parse(Sid(signedIn)))));
        Assert.Equal("invalid_credentials", (await api.SignInAsync("ada@x.com")).Code);
        AssertChallenged(await api.SignInAsync("ada@x.com", "another horse battery"));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.password.reset'"));
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_mfa_tickets WHERE pending_password_hash IS NOT NULL"));
    }

    [Fact]
    public async Task Turning_mfa_off_needs_a_recent_second_factor_and_keeps_sessions()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var ada = await EnrollAsync(api, "ada@x.com");
        var bearer = await SignInWithCodeAsync(api, "ada@x.com", ada.RecoveryCodes[0]);
        await AgeStrongCheckAsync(api, bearer);

        using var stale = await api.SendAsync(HttpMethod.Delete, "/v1/account/mfa/totp", bearer: bearer);
        Assert.Equal(HttpStatusCode.Forbidden, stale.Status);
        Assert.Equal("mfa_verification_required", stale.Code);

        using var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify", new { recoveryCode = ada.RecoveryCodes[1] }, bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, verified.Status);
        Assert.Equal(["mfa", "pwd", "rec"], Claims(verified.Body.GetProperty("accessToken").GetString()!).GetProperty("amr").EnumerateArray().Select(a => a.GetString()));

        using var off = await api.SendAsync(HttpMethod.Delete, "/v1/account/mfa/totp", bearer: bearer);
        Assert.Equal(HttpStatusCode.NoContent, off.Status);
        using var status = await api.SendAsync(HttpMethod.Get, "/v1/account/mfa", bearer: bearer);
        Assert.False(status.Body.GetProperty("mfaEnabled").GetBoolean());
        Assert.Equal(0, status.Body.GetProperty("recoveryCodesRemaining").GetInt32());
        Assert.Equal(HttpStatusCode.Created, (await api.SignInAsync("ada@x.com")).Status);
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.mfa.disabled' AND payload->>'reason' = 'user'"));

        using var twice = await api.SendAsync(HttpMethod.Delete, "/v1/account/mfa/totp", bearer: bearer);
        Assert.Equal("mfa_not_enabled", twice.Code);
    }

    [Fact]
    public async Task New_recovery_codes_replace_the_old_ones_after_a_step_up()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var plain = await api.SignUpAsync("bob@x.com");
        using var noMfa = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/recovery-codes", bearer: AuthApi.AccessToken(plain));
        Assert.Equal("mfa_not_enabled", noMfa.Code);

        var ada = await EnrollAsync(api, "ada@x.com");
        var bearer = await SignInWithCodeAsync(api, "ada@x.com", ada.RecoveryCodes[0]);

        using var created = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/recovery-codes", bearer: bearer);

        Assert.Equal(HttpStatusCode.Created, created.Status);
        var codes = created.Body.GetProperty("codes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(10, codes.Count);
        using var oldOne = await api.SignInAsync("ada@x.com");
        Assert.Equal("invalid_mfa_code", (await StepTwoAsync(api, Ticket(oldOne), recoveryCode: ada.RecoveryCodes[1])).Code);
        Assert.Equal(HttpStatusCode.Created, (await StepTwoAsync(api, Ticket(oldOne), recoveryCode: codes[0])).Status);
        Assert.Equal(2L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.recovery_codes.created'"));
    }

    [Fact]
    public async Task Step_up_guards_password_change_and_delete_before_any_password_check()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var ada = await EnrollAsync(api, "ada@x.com");
        var bearer = await SignInWithCodeAsync(api, "ada@x.com", ada.RecoveryCodes[0]);
        await AgeStrongCheckAsync(api, bearer);

        using var password = await api.SendAsync(HttpMethod.Put, "/v1/account/password",
            new { currentPassword = "wrong", newPassword = "another horse battery" }, bearer: bearer);
        Assert.Equal("mfa_verification_required", password.Code);
        using var delete = await api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { password = "wrong" }, bearer: bearer);
        Assert.Equal("mfa_verification_required", delete.Code);

        using var wrongFactor = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify", new { totpCode = WrongCode(ada.Secret) }, bearer: bearer);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongFactor.Status);
        Assert.Equal("invalid_mfa_code", wrongFactor.Code);
        using var both = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify",
            new { totpCode = "123456", recoveryCode = ada.RecoveryCodes[1] }, bearer: bearer);
        Assert.Equal("invalid_request", both.Code);

        using var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify", new { totpCode = CodeAt(ada.Secret, 1) }, bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, verified.Status);
        using var stillWrong = await api.SendAsync(HttpMethod.Put, "/v1/account/password",
            new { currentPassword = "wrong", newPassword = "another horse battery" }, bearer: bearer);
        Assert.Equal("invalid_credentials", stillWrong.Code);
        using var changed = await api.SendAsync(HttpMethod.Put, "/v1/account/password",
            new { currentPassword = "correct horse battery", newPassword = "another horse battery" }, bearer: bearer);
        Assert.Equal(HttpStatusCode.NoContent, changed.Status);
    }

    [Fact]
    public async Task Step_up_for_a_user_without_mfa_refuses_codes()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("bob@x.com");

        using var verify = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify", new { recoveryCode = "AAAAA-AAAAA" }, bearer: AuthApi.AccessToken(signUp));

        Assert.Equal(HttpStatusCode.Conflict, verify.Status);
        Assert.Equal("factor_not_enabled", verify.Code);
    }

    [Fact]
    public async Task The_link_operations_need_a_recent_second_factor_from_a_user_with_mfa()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var ada = await EnrollAsync(api, "ada@x.com");
        var bearer = await SignInWithCodeAsync(api, "ada@x.com", ada.RecoveryCodes[0]);
        await AgeStrongCheckAsync(api, bearer);

        var (link, _) = await OAuthDriver.StartAsync(api, "github", bearer: bearer, path: "/v1/account/identities/oauth/flows");
        using var _ = link;

        Assert.Equal(HttpStatusCode.Forbidden, link.Status);
        Assert.Equal("mfa_verification_required", link.Code);
    }

    [Fact]
    public async Task Claiming_an_account_removes_its_factors_and_is_never_challenged()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var ada = await EnrollAsync(api, "ada@x.com");
        using var unverified = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{ada.UserId}/email-verification", new { verified = false });
        Assert.Equal(HttpStatusCode.OK, unverified.Status);

        var link = await PasswordlessTests.MagicLinkAsync(api, "ada@x.com", Redirect);
        using var claimed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = link });

        Assert.Equal(HttpStatusCode.Created, claimed.Status);
        Assert.Equal(JsonValueKind.Null, claimed.Body.GetProperty("mfa").ValueKind);
        Assert.Equal(1, Claims(AuthApi.AccessToken(claimed)).GetProperty("aal").GetInt32());
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_totp_factors"));
        Assert.Equal(0L, await Count(api, "SELECT count(*) FROM orvano.auth_recovery_codes"));
        Assert.Equal(1L, await Count(api, "SELECT count(*) FROM orvano.events WHERE type = 'auth.mfa.disabled' AND payload->>'reason' = 'claimed'"));
    }

    /// <summary>Signs in with the password and a recovery code; answers the level 2 session's access token.</summary>
    private static async Task<string> SignInWithCodeAsync(AuthApi api, string email, string recoveryCode)
    {
        using var stepOne = await api.SignInAsync(email);
        using var stepTwo = await StepTwoAsync(api, Ticket(stepOne), recoveryCode: recoveryCode);
        Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        return AuthApi.AccessToken(stepTwo);
    }

    /// <summary>Moves the session's last strong check 11 minutes back, past the step up window.</summary>
    private static Task AgeStrongCheckAsync(AuthApi api, string bearer) =>
        TestDatabase.ExecuteAsync(api.Database.Superuser,
            $"UPDATE orvano.auth_sessions SET strong_auth_at = now() - interval '11 minutes', created_at = now() - interval '11 minutes' WHERE id = '{Claims(bearer).GetProperty("sid").GetString()}'");

    private static void AssertChallenged(Reply reply)
    {
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty("session").ValueKind);
        Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty("user").ValueKind);
        Assert.Matches("^orv_mt_", reply.Body.GetProperty("mfa").GetProperty("ticket").GetString());
    }

    private static IEnumerable<string?> Amr(Reply signedIn) =>
        Claims(AuthApi.AccessToken(signedIn)).GetProperty("amr").EnumerateArray().Select(a => a.GetString());

    private static Task<string> SessionMethodAsync(AuthApi api, string sessionId) =>
        TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT method FROM orvano.auth_sessions WHERE id = @id", ("id", Guid.Parse(sessionId)));

    private static Task<long> Count(AuthApi api, string sql) => TestDatabase.ScalarAsync<long>(api.Database.Superuser, sql);
}
