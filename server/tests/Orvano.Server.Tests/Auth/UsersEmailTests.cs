using System.Net;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 build task 5 over HTTP against the real binary: a server reads, sets, and triggers a user's verification,
// sends a reset, and changes an email. AC-21, AC-29.
public class UsersEmailTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth/callback";

    [Fact]
    public async Task A_server_filters_by_verification_imports_verified_users_and_marks_them_by_hand()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var imported = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "imported@x.com", password = "correct horse battery", emailVerified = true });
        using var plain = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "plain@x.com", password = "correct horse battery" });
        Assert.True(imported.Body.GetProperty("emailVerified").GetBoolean());
        Assert.False(plain.Body.GetProperty("emailVerified").GetBoolean());

        using var verified = await api.AsServerAsync(HttpMethod.Get, "/v1/users?emailVerified=true");
        using var unverified = await api.AsServerAsync(HttpMethod.Get, "/v1/users?emailVerified=false");
        Assert.Equal(["imported@x.com"], Emails(verified));
        Assert.Equal(["plain@x.com"], Emails(unverified));

        var id = plain.Body.GetProperty("id").GetString();
        using var marked = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{id}/email-verification", new { verified = true });
        var at = marked.Body.GetProperty("emailVerifiedAt").GetString();
        using var again = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{id}/email-verification", new { verified = true });
        Assert.True(marked.Body.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(at, again.Body.GetProperty("emailVerifiedAt").GetString()); // an existing date stays

        using var unmarked = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{id}/email-verification", new { verified = false });
        Assert.Equal(JsonValueKind.Null, unmarked.Body.GetProperty("emailVerifiedAt").ValueKind);
        Assert.Equal(2L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.updated' AND payload->'actor'->>'type' = 'apiKey'"));

        using var readOnly = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{id}/email-verification", new { verified = true }, key: AuthApi.ReadKey);
        using var unknown = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{Guid.CreateVersion7()}/email-verification", new { verified = true });
        Assert.Equal("insufficient_scope", readOnly.Code);
        Assert.Equal("user_not_found", unknown.Code);
    }

    [Fact]
    public async Task A_server_sends_verification_and_reset_links_with_the_same_limits_and_refusals()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var user = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "ada@x.com", password = "correct horse battery" });
        var id = user.Body.GetProperty("id").GetString();

        using var verify = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{id}/verification", new { redirectUrl = Redirect });
        Assert.Equal(HttpStatusCode.Accepted, verify.Status);
        var verification = await api.LatestEmailAsync("ada@x.com");
        Assert.Equal("verification", verification!.Type);

        using var reset = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{id}/recovery", new { redirectUrl = Redirect });
        Assert.Equal(HttpStatusCode.Accepted, reset.Status);
        Assert.Equal("recovery", (await api.LatestEmailAsync("ada@x.com"))!.Type);
        using var tooSoon = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{id}/recovery", new { redirectUrl = Redirect });
        Assert.Equal("rate_limited", tooSoon.Code);

        // Marking verified by hand deletes the live verification link.
        using (await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{id}/email-verification", new { verified = true })) { }
        using var stale = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = verification.Token });
        Assert.Equal("invalid_email_token", stale.Code);
        using var verifiedAlready = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{id}/verification", new { redirectUrl = Redirect });
        Assert.Equal("email_already_verified", verifiedAlready.Code);

        using (await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{id}/block")) { }
        using var blocked = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{id}/recovery", new { redirectUrl = Redirect });
        using var badRedirect = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{id}/recovery", new { redirectUrl = "https://evil.example" });
        using var unknown = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{Guid.CreateVersion7()}/recovery", new { redirectUrl = Redirect });
        Assert.Equal("user_blocked", blocked.Code);
        Assert.Equal("redirect_url_not_allowed", badRedirect.Code);
        Assert.Equal("user_not_found", unknown.Code);
    }

    [Fact]
    public async Task A_server_changes_an_email_at_once_deleting_live_tokens_and_keeping_sessions()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        using var other = await api.SignUpAsync("bob@x.com");
        var id = AuthApi.UserId(signUp);
        var resetToken = await RecoveryTests.RequestTokenAsync(api, "ada@x.com");

        using var moved = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{id}/email", new { email = "ada.king@x.com" });
        Assert.Equal("ada.king@x.com", moved.Body.GetProperty("email").GetString());
        Assert.False(moved.Body.GetProperty("emailVerified").GetBoolean());
        using var verified = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{id}/email", new { email = "ada.k@x.com", emailVerified = true });
        Assert.True(verified.Body.GetProperty("emailVerified").GetBoolean());

        using var reset = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token = resetToken, password = "another horse battery" });
        using var session = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(signUp));
        using var taken = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{id}/email", new { email = "BOB@x.com" });
        using var invalid = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{id}/email", new { email = "nope" });
        Assert.Equal("invalid_email_token", reset.Code);
        Assert.Equal(HttpStatusCode.OK, session.Status);
        Assert.Equal("email_already_in_use", taken.Code);
        Assert.Equal("invalid_request", invalid.Code);
    }

    private static string[] Emails(Reply page) =>
        [.. page.Body.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("email").GetString()!)];
}
