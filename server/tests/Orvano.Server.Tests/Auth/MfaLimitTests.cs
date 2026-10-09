using System.Net;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.PasskeyTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 AC-32 over HTTP: each rate limit of the Rate limits table is wired to its operations. Over the limit, the
// call answers 429 rate_limited with Retry-After, while another IP or another user is not affected. The test runner
// connects from 127.0.0.1, a trusted proxy by default, so X-Forwarded-For picks the client IP.
// (auth.mfa_failed.user is MfaTests.The_11th_wrong_factor_window_of_one_user_answers_429_and_counts_nothing, and
// auth.password_check.user is MfaEnrollmentTests.The_11th_password_sent_to_an_enrollment_operation_answers_429.)
public class MfaLimitTests(PostgresFixture postgres)
{
    // auth.mfa_ticket_failed.ip: 60 unknown tickets per IP in 15 minutes, at step two and at its passkey challenge.
    [Fact]
    public async Task The_61st_unknown_ticket_from_one_ip_answers_429()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var ip = Ip("203.0.113.70");
        for (var i = 0; i < 60; i++)
        {
            using var unknown = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa", new { ticket = UnknownTicket(), totpCode = "123456" }, headers: ip);
            Assert.Equal("invalid_mfa_ticket", unknown.Code);
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa", new { ticket = UnknownTicket(), totpCode = "123456" }, headers: ip);
        using var challenge = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa/passkey-challenge", new { ticket = UnknownTicket() }, headers: ip);
        using var otherIp = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa", new { ticket = UnknownTicket(), totpCode = "123456" },
            headers: Ip("203.0.113.71"));

        AssertLimited(limited);
        AssertLimited(challenge);
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_mfa_ticket"), (otherIp.Status, otherIp.Code));
    }

    // auth.passkey.ip: 300 passkey calls per IP in 15 minutes.
    [Fact]
    public async Task The_301st_passkey_call_from_one_ip_answers_429()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var ip = Ip("203.0.113.72");
        for (var i = 0; i < 300; i++)
        {
            using var challenge = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey-challenge", headers: ip);
            Assert.Equal(HttpStatusCode.OK, challenge.Status);
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey-challenge", headers: ip);
        using var otherIp = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey-challenge", headers: Ip("203.0.113.73"));

        AssertLimited(limited);
        Assert.Equal(HttpStatusCode.OK, otherIp.Status);
    }

    // auth.passkey_challenge.user: 30 challenges per user in 15 minutes; the registration took one.
    [Fact]
    public async Task The_31st_passkey_challenge_of_one_user_answers_429()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var ada = await VerifiedUserAsync(api, "ada@x.com");
        var bob = await VerifiedUserAsync(api, "bob@x.com");
        await RegisterAsync(api, ada);
        await RegisterAsync(api, bob);
        for (var i = 0; i < 29; i++)
        {
            using var challenge = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/passkey-challenge", bearer: ada);
            Assert.Equal(HttpStatusCode.OK, challenge.Status);
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/passkey-challenge", bearer: ada);
        using var otherUser = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/passkey-challenge", bearer: bob);

        AssertLimited(limited);
        Assert.Equal(HttpStatusCode.OK, otherUser.Status);
    }

    // auth.passkey_failed.ip: 60 failed passkey sign ins per IP in 15 minutes.
    [Fact]
    public async Task The_61st_failed_passkey_sign_in_from_one_ip_answers_429()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var ip = Ip("203.0.113.74");
        for (var i = 0; i < 60; i++)
        {
            using var refused = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey", FailedSignIn(), headers: ip);
            Assert.Equal((HttpStatusCode.Unauthorized, "invalid_passkey"), (refused.Status, refused.Code));
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey", FailedSignIn(), headers: ip);
        using var otherIp = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey", FailedSignIn(), headers: Ip("203.0.113.75"));

        AssertLimited(limited);
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_passkey"), (otherIp.Status, otherIp.Code));
    }

    // auth.mfa_enroll.user: 10 enrollment calls per user in 15 minutes, shared by createTotp and
    // createPasskeyRegistration. A user without a password on a fresh session, so no password check counts.
    [Fact]
    public async Task The_11th_enrollment_call_of_one_user_answers_429()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        await EnablePasskeysAsync(api);
        using var ada = await OAuthDriver.SignInAsync(api, "google", new { sub = "google-ada", email = "ada@example.com", emailVerified = true, name = "Ada" });
        using var grace = await OAuthDriver.SignInAsync(api, "google", new { sub = "google-grace", email = "grace@example.com", emailVerified = true, name = "Grace" });
        var bearer = AuthApi.AccessToken(ada);
        for (var i = 0; i < 10; i++)
        {
            using var setup = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { }, bearer: bearer);
            Assert.Equal(HttpStatusCode.Created, setup.Status);
        }

        using var limited = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { }, bearer: bearer);
        using var passkey = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", new { }, bearer: bearer);
        using var otherUser = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/totp", new { }, bearer: AuthApi.AccessToken(grace));

        AssertLimited(limited);
        AssertLimited(passkey);
        Assert.Equal(HttpStatusCode.Created, otherUser.Status);
    }

    private static Dictionary<string, string> Ip(string ip) => new() { ["X-Forwarded-For"] = ip };

    /// <summary>A well formed ticket nobody issued.</summary>
    private static string UnknownTicket() =>
        "orv_mt_" + System.Buffers.Text.Base64Url.EncodeToString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>A well formed passkey answer to a challenge nobody made.</summary>
    private static object FailedSignIn() => new
    {
        challengeId = Guid.NewGuid().ToString(),
        credential = new
        {
            id = "AAAA",
            rawId = "AAAA",
            type = "public-key",
            response = new { clientDataJSON = "AAAA", authenticatorData = "AAAA", signature = "AAAA" },
        },
    };

    private static void AssertLimited(Reply reply)
    {
        Assert.Equal((HttpStatusCode)429, reply.Status);
        Assert.Equal("rate_limited", reply.Code);
        Assert.NotNull(reply.Headers.RetryAfter);
    }
}
