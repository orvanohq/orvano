using System.Net;
using System.Text.Json;
using Orvano.Core.Http;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;
using static Orvano.Server.Tests.Auth.PasskeyTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 build task 5 over HTTP against the real binary: console accounts enroll TOTP and passkeys with an
// unverified email (AC-12, AC-20), console sign in answers ConsoleAuthResult and keeps the ticket in the
// orvano_console_mfa cookie (AC-41), the second step and passkey sign in set the session cookies, and the console step
// up and confirm set a new orvano_console cookie (AC-42).
public class ConsoleMfaTests(PostgresFixture postgres)
{
    // A fixture console account no other test here signs in, so its cached cookie is always its first session.
    private const string Account = "stranger@x.com";
    private const string ConsoleOrigin = OrvanoProcess.PublicUrl;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_console_account_with_an_unverified_email_enrolls_totp_and_a_passkey()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_users WHERE project_id = 'console' AND lower(email) = @email AND email_verified_at IS NOT NULL",
            ("email", Account)));

        var enrolled = await EnrollConsoleAsync(api);
        using var status = await AsAccountAsync(api, HttpMethod.Get, "/v1/console/account/mfa");
        var passkey = await RegisterConsolePasskeyAsync(api);

        Assert.Equal(10, enrolled.RecoveryCodes.Count);
        Assert.True(status.Body.GetProperty("mfaEnabled").GetBoolean());
        Assert.Equal(10, status.Body.GetProperty("recoveryCodesRemaining").GetInt32());
        Assert.Equal(["totp", "passkey"], status.Body.GetProperty("factorsAvailable").EnumerateArray().Select(f => f.GetString()));
        Assert.True(passkey.Passkey.GetProperty("active").GetBoolean());
        using var listed = await AsAccountAsync(api, HttpMethod.Get, "/v1/console/account/passkeys");
        Assert.Single(listed.Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Sign_in_with_mfa_sets_only_the_ticket_cookie_and_the_second_step_sets_the_session_cookies()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollConsoleAsync(api);

        using var stepOne = await SignInAsync(api);
        var ticketCookie = SetCookie(stepOne, OrvanoHeaders.ConsoleMfaCookie);
        using var noCookie = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/mfa", new { totpCode = CodeAt(enrolled.Secret, 1) });
        using var stepTwo = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/mfa", new { totpCode = CodeAt(enrolled.Secret, 1) },
            cookie: Pair(ticketCookie));

        Assert.Equal(HttpStatusCode.Created, stepOne.Status);
        Assert.Equal(JsonValueKind.Null, stepOne.Body.GetProperty("account").ValueKind);
        Assert.Equal("", stepOne.Body.GetProperty("mfa").GetProperty("ticket").GetString());
        Assert.Equal(["totp", "recovery_code"], stepOne.Body.GetProperty("mfa").GetProperty("factors").EnumerateArray().Select(f => f.GetString()));
        Assert.Null(SetCookieOrNull(stepOne, OrvanoHeaders.ConsoleCookie));
        Assert.Null(SetCookieOrNull(stepOne, OrvanoHeaders.ConsoleRefreshCookie));
        Assert.StartsWith("orv_mt_", Value(ticketCookie), StringComparison.Ordinal);
        Assert.Contains("httponly", ticketCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", ticketCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/console/account/session", ticketCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=300", ticketCookie, StringComparison.OrdinalIgnoreCase);
        // The public URL is plain http://localhost here, the one case the console cookies leave Secure off.
        Assert.DoesNotContain("secure", ticketCookie, StringComparison.OrdinalIgnoreCase);

        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_mfa_ticket"), (noCookie.Status, noCookie.Code));
        Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        Assert.Equal(Account, stepTwo.Body.GetProperty("email").GetString());
        Assert.True(Cleared(SetCookie(stepTwo, OrvanoHeaders.ConsoleMfaCookie)));
        var access = Value(SetCookie(stepTwo, OrvanoHeaders.ConsoleCookie));
        Assert.Equal(2, Claims(access).GetProperty("aal").GetInt32());
        Assert.StartsWith("orv_rt_", Value(SetCookie(stepTwo, OrvanoHeaders.ConsoleRefreshCookie)), StringComparison.Ordinal);
        using var me = await ConsoleAsync(api, HttpMethod.Get, "/v1/console/account", cookie: $"{OrvanoHeaders.ConsoleCookie}={access}");
        Assert.Equal(HttpStatusCode.OK, me.Status);
    }

    [Fact]
    public async Task The_fifth_wrong_code_ends_the_ticket_and_clears_its_cookie()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollConsoleAsync(api);
        using var stepOne = await SignInAsync(api);
        var cookie = Pair(SetCookie(stepOne, OrvanoHeaders.ConsoleMfaCookie));
        var wrong = new { totpCode = WrongCode(enrolled.Secret) };

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var refused = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/mfa", wrong, cookie);
            Assert.Equal((HttpStatusCode.Unauthorized, "invalid_mfa_code"), (refused.Status, refused.Code));
            Assert.Null(SetCookieOrNull(refused, OrvanoHeaders.ConsoleMfaCookie));
        }

        using var fifth = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/mfa", wrong, cookie);
        using var sixth = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/mfa", new { totpCode = CodeAt(enrolled.Secret, 1) }, cookie);

        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_mfa_code"), (fifth.Status, fifth.Code));
        Assert.True(Cleared(SetCookie(fifth, OrvanoHeaders.ConsoleMfaCookie)));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_mfa_ticket"), (sixth.Status, sixth.Code));
        Assert.True(Cleared(SetCookie(sixth, OrvanoHeaders.ConsoleMfaCookie)));
    }

    [Fact]
    public async Task A_console_passkey_signs_in_at_level_2_and_answers_the_second_step()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        // TOTP turns MFA on, so the password sign in below is challenged.
        await EnrollConsoleAsync(api);
        var passkey = await RegisterConsolePasskeyAsync(api);

        using var challenge = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/passkey-challenge");
        var assertion = await AssertAsync(api, challenge.Body.GetProperty("options"), passkey.CredentialId, new { origin = ConsoleOrigin });
        using var signedIn = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/passkey",
            new { challengeId = challenge.Body.GetProperty("challengeId").GetString(), credential = assertion });

        Assert.Equal("localhost", challenge.Body.GetProperty("options").GetProperty("rpId").GetString());
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        Assert.Equal(Account, signedIn.Body.GetProperty("email").GetString());
        Assert.Equal(2, Claims(Value(SetCookie(signedIn, OrvanoHeaders.ConsoleCookie))).GetProperty("aal").GetInt32());

        using var stepOne = await SignInAsync(api);
        var cookie = Pair(SetCookie(stepOne, OrvanoHeaders.ConsoleMfaCookie));
        using var mfaChallenge = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/mfa/passkey-challenge", cookie: cookie);
        var answer = await AssertAsync(api, mfaChallenge.Body.GetProperty("options"), passkey.CredentialId, new { origin = ConsoleOrigin });
        using var stepTwo = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/mfa",
            new { passkey = new { challengeId = mfaChallenge.Body.GetProperty("challengeId").GetString(), credential = answer } }, cookie);
        using var withoutTicket = await ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session/mfa/passkey-challenge");

        Assert.Equal(["totp", "recovery_code", "passkey"], stepOne.Body.GetProperty("mfa").GetProperty("factors").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        Assert.Contains("hwk", Claims(Value(SetCookie(stepTwo, OrvanoHeaders.ConsoleCookie))).GetProperty("amr").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_mfa_ticket"), (withoutTicket.Status, withoutTicket.Code));
    }

    [Fact]
    public async Task Step_up_sets_a_new_console_cookie_and_unlocks_turning_totp_off()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var enrolled = await EnrollConsoleAsync(api);
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET created_at = created_at - interval '11 minutes', strong_auth_at = strong_auth_at - interval '11 minutes'");

        using var stale = await AsAccountAsync(api, HttpMethod.Delete, "/v1/console/account/mfa/totp");
        using var wrong = await AsAccountAsync(api, HttpMethod.Post, "/v1/console/account/mfa/verify", new { totpCode = WrongCode(enrolled.Secret) });
        using var verified = await AsAccountAsync(api, HttpMethod.Post, "/v1/console/account/mfa/verify", new { recoveryCode = enrolled.RecoveryCodes[0] });
        using var off = await AsAccountAsync(api, HttpMethod.Delete, "/v1/console/account/mfa/totp");
        using var status = await AsAccountAsync(api, HttpMethod.Get, "/v1/console/account/mfa");

        Assert.Equal((HttpStatusCode.Forbidden, "mfa_verification_required"), (stale.Status, stale.Code));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_mfa_code"), (wrong.Status, wrong.Code));
        Assert.Equal(HttpStatusCode.NoContent, verified.Status);
        Assert.Contains("rec", Claims(Value(SetCookie(verified, OrvanoHeaders.ConsoleCookie))).GetProperty("amr").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(HttpStatusCode.NoContent, off.Status);
        Assert.False(status.Body.GetProperty("mfaEnabled").GetBoolean());
        using var signIn = await SignInAsync(api);
        Assert.Equal(JsonValueKind.Object, signIn.Body.GetProperty("account").ValueKind);
    }

    private sealed record ConsoleEnrolled(string Secret, IReadOnlyList<string> RecoveryCodes);

    /// <summary>Turns on TOTP for <see cref="Account"/> through the console twins; the confirm sets a new cookie.</summary>
    private static async Task<ConsoleEnrolled> EnrollConsoleAsync(AuthApi api)
    {
        using var setup = await AsAccountAsync(api, HttpMethod.Post, "/v1/console/account/mfa/totp");
        Assert.Equal(HttpStatusCode.Created, setup.Status);
        var secret = setup.Body.GetProperty("secret").GetString()!;
        Assert.Contains("otpauth://totp/Orvano:", setup.Body.GetProperty("uri").GetString(), StringComparison.Ordinal);
        using var confirmed = await AsAccountAsync(api, HttpMethod.Post, "/v1/console/account/mfa/totp/confirm", new { code = CodeAt(secret, 0) });
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        Assert.Equal(2, Claims(Value(SetCookie(confirmed, OrvanoHeaders.ConsoleCookie))).GetProperty("aal").GetInt32());
        Assert.False(confirmed.Body.TryGetProperty("session", out _));
        return new ConsoleEnrolled(secret, [.. confirmed.Body.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!)]);
    }

    private static async Task<Registered> RegisterConsolePasskeyAsync(AuthApi api)
    {
        using var options = await AsAccountAsync(api, HttpMethod.Post, "/v1/console/account/passkeys/registration");
        Assert.Equal(HttpStatusCode.OK, options.Status);
        Assert.Equal("Orvano", options.Body.GetProperty("options").GetProperty("rp").GetProperty("name").GetString());
        var credential = await CreateCredentialAsync(api, options.Body.GetProperty("options"), ConsoleOrigin);
        using var made = await AsAccountAsync(api, HttpMethod.Post, "/v1/console/account/passkeys",
            new { challengeId = options.Body.GetProperty("challengeId").GetString(), credential, name = "Laptop" });
        Assert.Equal(HttpStatusCode.Created, made.Status);
        return new Registered(credential.GetProperty("id").GetString()!, made.Body.Clone());
    }

    /// <summary>A console call as <see cref="Account"/>, through its first (cached) session.</summary>
    private static async Task<Reply> AsAccountAsync(AuthApi api, HttpMethod method, string url, object? body = null) =>
        await ConsoleAsync(api, method, url, body, $"{OrvanoHeaders.ConsoleCookie}={await ConsoleSignIn.CookieAsync(api.Http, Account, Ct)}");

    private static Task<Reply> SignInAsync(AuthApi api) =>
        ConsoleAsync(api, HttpMethod.Post, "/v1/console/account/session", new { email = Account, password = ConsoleSignIn.Password });

    private static Task<Reply> ConsoleAsync(AuthApi api, HttpMethod method, string url, object? body = null, string? cookie = null)
    {
        var headers = new Dictionary<string, string> { ["Sec-Fetch-Site"] = "same-origin" };
        if (cookie is not null) headers["Cookie"] = cookie;
        return api.SendAsync(method, url, body, project: null, headers: headers);
    }

    private static string? SetCookieOrNull(Reply reply, string name) =>
        reply.Headers.TryGetValues("Set-Cookie", out var values) ? values.SingleOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal)) : null;

    private static string SetCookie(Reply reply, string name) => SetCookieOrNull(reply, name) ?? throw new InvalidOperationException($"No {name} cookie was set.");

    private static string Pair(string setCookie) => setCookie.Split(';')[0];

    private static string Value(string setCookie) => Pair(setCookie)[(setCookie.IndexOf('=', StringComparison.Ordinal) + 1)..];

    private static bool Cleared(string setCookie) => Value(setCookie).Length == 0 && setCookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
}
