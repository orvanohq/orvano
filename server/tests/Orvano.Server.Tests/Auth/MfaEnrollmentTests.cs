using System.Net;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;
using static Orvano.Server.Tests.Auth.PasskeyTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 AC-13, AC-17, AC-19, AC-32 over HTTP, as the review of 2026-10-08 changed them: an operation that adds a
// way in (createTotp, createPasskeyRegistration, createOAuthLinkFlow, createIdTokenIdentity) needs, from a user with MFA
// off, a strong check within 10 minutes or else the current password; a user without a password keeps the 10 minute
// session rule. A leaked access token alone, however fresh, never adds a factor. confirmTotp and verifyMfa answer a
// RaisedSession, never the refresh token, and every password sent counts against auth.password_check.user.
public class MfaEnrollmentTests(PostgresFixture postgres)
{
    private const string LinkFlows = "/v1/account/identities/oauth/flows";
    private const string Nonce = "nonce-0123456789abcdef";

    // The review's first Major finding: a fresh access token without the password adds nothing.
    [Fact]
    public async Task A_fresh_access_token_alone_never_adds_a_way_in_to_an_account_with_a_password()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        await EnablePasskeysAsync(api);
        var bearer = await VerifiedUserAsync(api, "ada@x.com");

        using var totp = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { }, bearer: bearer);
        using var passkey = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", new { }, bearer: bearer);
        var (link, _) = await OAuthDriver.StartAsync(api, "github", bearer: bearer, path: LinkFlows);
        using var linkReply = link;
        using var native = await api.SendAsync(HttpMethod.Post, "/v1/account/identities/id-token",
            new { provider = "google", idToken = "not.a.token", nonce = Nonce }, bearer: bearer);

        foreach (var (name, reply) in new[] { ("createTotp", totp), ("createPasskeyRegistration", passkey), ("createOAuthLinkFlow", link), ("createIdTokenIdentity", native) })
        {
            Assert.True(reply.Status == HttpStatusCode.Unauthorized, name);
            Assert.Equal("invalid_credentials", reply.Code);
        }

        Assert.Equal(0L, await CountAsync(api, "SELECT count(*) FROM orvano.auth_totp_factors"));
        Assert.Equal(0L, await CountAsync(api, "SELECT count(*) FROM orvano.auth_webauthn_challenges"));
        Assert.Equal(0L, await CountAsync(api, "SELECT count(*) FROM orvano.auth_oauth_flows"));
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_the_right_one_enrolls_on_a_session_older_than_10_minutes()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        await EnablePasskeysAsync(api);
        var bearer = await VerifiedUserAsync(api, "ada@x.com");
        var wrong = new { password = "wrong horse battery" };

        using var wrongTotp = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", wrong, bearer: bearer);
        using var wrongPasskey = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", wrong, bearer: bearer);
        var (wrongLink, _) = await OAuthDriver.StartAsync(api, "github", bearer: bearer, path: LinkFlows, password: "wrong horse battery");
        using var wrongLinkReply = wrongLink;
        foreach (var reply in new[] { wrongTotp, wrongPasskey, wrongLink })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
            Assert.Equal("invalid_credentials", reply.Code);
        }

        await AgeSessionsAsync(api);
        using var totp = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", WithPassword, bearer: bearer);
        using var passkey = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: bearer);
        var (link, _) = await OAuthDriver.StartAsync(api, "github", bearer: bearer, path: LinkFlows, password: Password);
        using var linkReply = link;

        Assert.Equal(HttpStatusCode.Created, totp.Status);
        Assert.Equal(HttpStatusCode.OK, passkey.Status);
        Assert.Equal(HttpStatusCode.OK, link.Status);
    }

    // AC-19: a passkey step up is the strong check, so the same calls then need no password, even on a session created
    // over 10 minutes ago, and verifyMfa answers a new access token without the refresh token.
    [Fact]
    public async Task A_strong_check_enrolls_without_the_password_and_verify_answers_no_refresh_token()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        await EnablePasskeysAsync(api);
        using var signUp = await api.SignUpAsync("ada@x.com");
        await VerifyEmailAsync(api, AuthApi.UserId(signUp));
        var bearer = AuthApi.AccessToken(signUp);
        await RegisterAsync(api, bearer);
        await AgeSessionsAsync(api);

        using var before = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { }, bearer: bearer);
        Assert.Equal("invalid_credentials", before.Code);

        using var challenge = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/passkey-challenge", bearer: bearer);
        var options = challenge.Body.GetProperty("options");
        var answer = await AssertAsync(api, options, options.GetProperty("allowCredentials")[0].GetProperty("id").GetString()!);
        using var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify",
            new { passkey = new { challengeId = challenge.Body.GetProperty("challengeId").GetString(), credential = answer } }, bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, verified.Status);
        Assert.Equal(["accessToken", "accessTokenExpiresAt", "sessionId"], verified.Body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Sid(signUp), verified.Body.GetProperty("sessionId").GetString());
        Assert.Equal(2, Claims(verified.Body.GetProperty("accessToken").GetString()!).GetProperty("aal").GetInt32());
        await AssertRefreshCarriesAal2Async(api, AuthApi.RefreshToken(signUp));

        using var totp = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { }, bearer: bearer);
        using var passkey = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", new { }, bearer: bearer);
        var (link, _) = await OAuthDriver.StartAsync(api, "github", bearer: bearer, path: LinkFlows);
        using var linkReply = link;

        Assert.Equal(HttpStatusCode.Created, totp.Status);
        Assert.Equal(HttpStatusCode.OK, passkey.Status);
        Assert.Equal(HttpStatusCode.OK, link.Status);
    }

    [Fact]
    public async Task A_user_without_a_password_keeps_the_10_minute_session_rule()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        await EnablePasskeysAsync(api);
        using var user = await OAuthDriver.SignInAsync(api, "google", new { sub = "google-only", email = "only@x.com", emailVerified = true, name = "Only" });
        var bearer = AuthApi.AccessToken(user);

        using var fresh = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { }, bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, fresh.Status);

        await AgeSessionsAsync(api);
        using var stale = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { }, bearer: bearer);
        using var guessed = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { password = "any password at all" }, bearer: bearer);
        using var passkey = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", new { }, bearer: bearer);

        foreach (var reply in new[] { stale, guessed, passkey })
        {
            Assert.Equal(HttpStatusCode.Forbidden, reply.Status);
            Assert.Equal("reauthentication_required", reply.Code);
        }
    }

    // AC-13: the pending secret is the proof, so the confirm works on a session past 10 minutes, and answers a
    // RaisedSession: the refresh token the client holds keeps working and its next refresh carries aal 2.
    [Fact]
    public async Task Confirming_totp_works_after_the_session_turns_10_minutes_old_and_answers_no_refresh_token()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        await VerifyEmailAsync(api, AuthApi.UserId(signUp));
        var bearer = AuthApi.AccessToken(signUp);
        using var setup = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", WithPassword, bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, setup.Status);
        await AgeSessionsAsync(api);

        using var confirmed = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp/confirm",
            new { code = CodeAt(setup.Body.GetProperty("secret").GetString()!, 0) }, bearer: bearer);

        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        var session = confirmed.Body.GetProperty("session");
        Assert.Equal(["accessToken", "accessTokenExpiresAt", "sessionId"], session.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2, Claims(session.GetProperty("accessToken").GetString()!).GetProperty("aal").GetInt32());
        await AssertRefreshCarriesAal2Async(api, AuthApi.RefreshToken(signUp));
    }

    // AC-32: auth.password_check.user allows 10 passwords per user in 15 minutes, across every operation that takes one.
    // The link flow has no enrollment limit, so it fills the window; then every enrollment call that sends a password
    // answers 429 with Retry-After, and another user is not affected.
    [Fact]
    public async Task The_11th_password_sent_to_an_enrollment_operation_answers_429()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        await EnablePasskeysAsync(api);
        var ada = await VerifiedUserAsync(api, "ada@x.com");
        var grace = await VerifiedUserAsync(api, "grace@x.com");
        for (var i = 0; i < 10; i++)
        {
            var (wrong, _) = await OAuthDriver.StartAsync(api, "github", bearer: ada, path: LinkFlows, password: "wrong horse battery");
            using var _ = wrong;
            Assert.Equal("invalid_credentials", wrong.Code);
        }

        var (link, _) = await OAuthDriver.StartAsync(api, "github", bearer: ada, path: LinkFlows, password: Password);
        using var linkReply = link;
        using var totp = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", WithPassword, bearer: ada);
        using var passkey = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: ada);
        using var native = await api.SendAsync(HttpMethod.Post, "/v1/account/identities/id-token",
            new { provider = "google", idToken = "not.a.token", nonce = Nonce, password = Password }, bearer: ada);
        var (other, _) = await OAuthDriver.StartAsync(api, "github", bearer: grace, path: LinkFlows, password: Password);
        using var otherReply = other;

        foreach (var (name, reply) in new[] { ("createOAuthLinkFlow", link), ("createTotp", totp), ("createPasskeyRegistration", passkey), ("createIdTokenIdentity", native) })
        {
            Assert.True(reply.Status == (HttpStatusCode)429, name);
            Assert.Equal("rate_limited", reply.Code);
            Assert.NotNull(reply.Headers.RetryAfter);
        }

        Assert.Equal(HttpStatusCode.OK, other.Status);
        Assert.Equal(0L, await CountAsync(api, "SELECT count(*) FROM orvano.auth_totp_factors"));
    }

    private static Task<long> CountAsync(AuthApi api, string sql) => TestDatabase.ScalarAsync<long>(api.Database.Superuser, sql);

    /// <summary>Moves every session's creation and strong check 11 minutes back, past the 10 minute window.</summary>
    private static Task AgeSessionsAsync(AuthApi api) =>
        TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET created_at = created_at - interval '11 minutes', strong_auth_at = strong_auth_at - interval '11 minutes'");
}
