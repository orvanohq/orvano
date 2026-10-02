using System.Net;
using System.Text;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 build task 2 over HTTP against the real binary: email verification at sign up and on request, the
// email_verified claim. AC-6, AC-11 to AC-14, AC-20.
public class VerificationTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth/callback";

    [Fact]
    public async Task A_sign_up_link_verifies_the_email_once_and_the_next_token_carries_the_claim()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com", extra: new { email = "ada@x.com", password = "correct horse battery", verificationRedirectUrl = Redirect });

        Assert.Equal(HttpStatusCode.Created, signUp.Status);
        Assert.Equal("queued", signUp.Body.GetProperty("verificationEmail").GetString());
        Assert.True(signUp.Body.GetProperty("isNewUser").GetBoolean());
        Assert.False(signUp.Body.GetProperty("user").GetProperty("emailVerified").GetBoolean());
        Assert.Equal(JsonValueKind.Null, signUp.Body.GetProperty("user").GetProperty("emailVerifiedAt").ValueKind);
        Assert.False(Claim(AuthApi.AccessToken(signUp)));

        var email = await api.LatestEmailAsync("ada@x.com");
        Assert.Equal("verification", email!.Template);
        Assert.Equal("verification", email.Type);

        using var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = email.Token });
        Assert.Equal(HttpStatusCode.OK, verified.Status);
        Assert.True(verified.Body.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(JsonValueKind.String, verified.Body.GetProperty("emailVerifiedAt").ValueKind);

        // AC-14: the claim is read at every refresh, so the next token says true.
        using var refreshed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(signUp) });
        Assert.True(Claim(refreshed.Body.GetProperty("accessToken").GetString()!));
        using var signIn = await api.SignInAsync("ada@x.com");
        Assert.True(Claim(AuthApi.AccessToken(signIn)));

        using var again = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = email.Token });
        Assert.Equal("invalid_email_token", again.Code);

        using var resend = await api.SendAsync(HttpMethod.Post, "/v1/account/verification", new { redirectUrl = Redirect }, bearer: AuthApi.AccessToken(signIn));
        Assert.Equal(HttpStatusCode.Conflict, resend.Status);
        Assert.Equal("email_already_verified", resend.Code);

        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.updated' AND payload->'changed' = '[\"emailVerified\"]'::jsonb"));
    }

    [Fact]
    public async Task Sign_up_reports_what_happened_to_the_verification_email_and_always_creates_the_user()
    {
        await using var noSmtp = await AuthApi.StartAsync(postgres, email: true);
        using var notConfigured = await noSmtp.SignUpAsync("ada@x.com", extra: new { email = "ada@x.com", password = "correct horse battery", verificationRedirectUrl = Redirect });
        using var none = await noSmtp.SignUpAsync("bob@x.com");

        Assert.Equal(HttpStatusCode.Created, notConfigured.Status);
        Assert.Equal("not_configured", notConfigured.Body.GetProperty("verificationEmail").GetString());
        Assert.Equal(JsonValueKind.Null, none.Body.GetProperty("verificationEmail").ValueKind);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(noSmtp.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens"));
    }

    [Fact]
    public async Task A_bad_redirect_is_refused_before_a_taken_email_and_before_anything_is_created()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var first = await api.SignUpAsync("ada@x.com");

        using var both = await api.SignUpAsync("ada@x.com", extra: new { email = "ada@x.com", password = "correct horse battery", verificationRedirectUrl = "https://evil.example/cb" });
        using var fresh = await api.SignUpAsync("bob@x.com", extra: new { email = "bob@x.com", password = "correct horse battery", verificationRedirectUrl = "https://evil.example/cb" });

        Assert.Equal("redirect_url_not_allowed", both.Code);
        Assert.Equal("redirect_url_not_allowed", fresh.Code);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_users WHERE project_id = @p", ("p", AuthApi.Project)));
    }

    [Fact]
    public async Task A_verification_link_may_open_in_the_app_and_a_resend_shares_the_recipient_limits()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com", extra: new { email = "ada@x.com", password = "correct horse battery", verificationRedirectUrl = "com.acme.app://auth" });
        var bearer = AuthApi.AccessToken(signUp);

        Assert.Equal("queued", signUp.Body.GetProperty("verificationEmail").GetString());
        Assert.StartsWith("com.acme.app://auth/?orvano_type=verification&orvano_token=orv_el_", (await api.LatestEmailAsync("ada@x.com"))!.Url, StringComparison.Ordinal);

        // The sign up's email took this minute's slot.
        using var tooSoon = await api.SendAsync(HttpMethod.Post, "/v1/account/verification", new { redirectUrl = Redirect }, bearer: bearer);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.Status);
        Assert.Equal("rate_limited", tooSoon.Code);
        Assert.NotNull(tooSoon.Headers.RetryAfter);

        using var badRedirect = await api.SendAsync(HttpMethod.Post, "/v1/account/verification", new { redirectUrl = "com.unknown.app://auth" }, bearer: bearer);
        using var noSession = await api.SendAsync(HttpMethod.Post, "/v1/account/verification", new { redirectUrl = Redirect });
        Assert.Equal("redirect_url_not_allowed", badRedirect.Code);
        Assert.Equal("session_required", noSession.Code);
    }

    [Fact]
    public async Task A_verification_link_for_an_email_the_account_no_longer_has_fails_and_stays()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com", extra: new { email = "ada@x.com", password = "correct horse battery", verificationRedirectUrl = Redirect });
        var token = (await api.LatestEmailAsync("ada@x.com"))!.Token;

        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET email = 'eve@x.com' WHERE email = 'ada@x.com'");
        using var moved = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token });
        Assert.Equal("invalid_email_token", moved.Code);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens"));

        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET email = 'Ada@X.com' WHERE email = 'eve@x.com'");
        using var back = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token });
        Assert.Equal(HttpStatusCode.OK, back.Status);
    }

    internal static bool Claim(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return claims.RootElement.GetProperty("email_verified").GetBoolean();
    }
}
