using System.Net;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 build task 4 over HTTP against the real binary: email change and self service for users without a
// password. AC-17 to AC-19, AC-29.
public class EmailChangeTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth/callback";
    private const string Password = "correct horse battery";

    [Fact]
    public async Task An_email_change_waits_for_the_new_inbox_and_then_swaps_and_verifies_keeping_sessions()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var bearer = AuthApi.AccessToken(signUp);
        var resetToken = await RecoveryTests.RequestTokenAsync(api, "ada@x.com");

        using var request = await api.SendAsync(HttpMethod.Put, "/v1/account/email", new { email = "ada.king@x.com", redirectUrl = Redirect, password = Password }, bearer: bearer);
        Assert.Equal(HttpStatusCode.Accepted, request.Status);
        using (var unchanged = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: bearer))
            Assert.Equal("ada@x.com", unchanged.Body.GetProperty("email").GetString());

        var email = await api.LatestEmailAsync("ada.king@x.com");
        Assert.Equal("verification", email!.Template);
        Assert.Equal("email_change", email.Type);

        using var confirm = await api.SendAsync(HttpMethod.Post, "/v1/account/email/confirm", new { token = email.Token });
        Assert.Equal(HttpStatusCode.OK, confirm.Status);
        Assert.Equal("ada.king@x.com", confirm.Body.GetProperty("email").GetString());
        Assert.True(confirm.Body.GetProperty("emailVerified").GetBoolean());

        using var stillSignedIn = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.Status);
        using var again = await api.SendAsync(HttpMethod.Post, "/v1/account/email/confirm", new { token = email.Token });
        Assert.Equal("invalid_email_token", again.Code);

        // The other live tokens went with the old address.
        using var oldReset = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token = resetToken, password = "another horse battery" });
        Assert.Equal("invalid_email_token", oldReset.Code);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.updated' AND payload->'changed' = '[\"email\", \"emailVerified\"]'::jsonb"));
    }

    [Fact]
    public async Task An_email_change_checks_the_body_then_the_password_then_the_address()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        using var other = await api.SignUpAsync("bob@x.com");
        var bearer = AuthApi.AccessToken(signUp);

        async Task<string?> CodeAsync(object body)
        {
            using var reply = await api.SendAsync(HttpMethod.Put, "/v1/account/email", body, bearer: bearer);
            return reply.Code;
        }

        Assert.Equal("invalid_request", await CodeAsync(new { email = "nope", redirectUrl = Redirect, password = "wrong horse battery" }));
        Assert.Equal("invalid_request", await CodeAsync(new { email = "ADA@x.com", redirectUrl = Redirect, password = Password }));
        Assert.Equal("redirect_url_not_allowed", await CodeAsync(new { email = "new@x.com", redirectUrl = "https://evil.example", password = "wrong horse battery" }));
        Assert.Equal("invalid_credentials", await CodeAsync(new { email = "bob@x.com", redirectUrl = Redirect, password = "wrong horse battery" }));
        Assert.Equal("invalid_credentials", await CodeAsync(new { email = "new@x.com", redirectUrl = Redirect }));
        Assert.Equal("email_already_in_use", await CodeAsync(new { email = "BOB@x.com", redirectUrl = Redirect, password = Password }));
        Assert.Equal(0L, await api.QueuedEmailCountAsync());
    }

    [Fact]
    public async Task A_confirmation_fails_and_stays_when_someone_took_the_address_in_between()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        using var request = await api.SendAsync(HttpMethod.Put, "/v1/account/email", new { email = "new@x.com", redirectUrl = Redirect, password = Password }, bearer: AuthApi.AccessToken(signUp));
        var token = (await api.LatestEmailAsync("new@x.com"))!.Token;
        using var thief = await api.SignUpAsync("NEW@x.com");

        using var taken = await api.SendAsync(HttpMethod.Post, "/v1/account/email/confirm", new { token });
        Assert.Equal(HttpStatusCode.Conflict, taken.Status);
        Assert.Equal("email_already_in_use", taken.Code);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens WHERE kind = 'email_change'"));

        using (await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{AuthApi.UserId(thief)}")) { }
        using var works = await api.SendAsync(HttpMethod.Post, "/v1/account/email/confirm", new { token });
        Assert.Equal(HttpStatusCode.OK, works.Status);
    }

    [Fact]
    public async Task A_user_without_a_password_changes_sensitive_things_only_from_a_fresh_session()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var token = await PasswordlessTests.MagicLinkAsync(api, "neo@x.com");
        using var signedIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        var bearer = AuthApi.AccessToken(signedIn);
        var session = signedIn.Body.GetProperty("session").GetProperty("sessionId").GetString()!;

        // An 11 minute old session must sign in again first.
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_sessions SET created_at = now() - interval '11 minutes' WHERE id = @id::uuid", ("id", session));
        using (var stale = await api.SendAsync(HttpMethod.Put, "/v1/account/email", new { email = "new@x.com", redirectUrl = Redirect }, bearer: bearer))
            Assert.Equal("reauthentication_required", stale.Code);
        using (var stalePassword = await api.SendAsync(HttpMethod.Put, "/v1/account/password", new { newPassword = Password }, bearer: bearer))
            Assert.Equal("reauthentication_required", stalePassword.Code);
        using (var staleDelete = await api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { }, bearer: bearer))
            Assert.Equal("reauthentication_required", staleDelete.Code);

        // A fresh one may: any password sent is ignored.
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_sessions SET created_at = now() WHERE id = @id::uuid", ("id", session));
        using (var change = await api.SendAsync(HttpMethod.Put, "/v1/account/email", new { email = "new@x.com", redirectUrl = Redirect, password = "whatever horse" }, bearer: bearer))
            Assert.Equal(HttpStatusCode.Accepted, change.Status);

        var code = await PasswordlessTests.EmailCodeAsync(api, "neo@x.com");
        using var other = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "neo@x.com", code });
        using (var first = await api.SendAsync(HttpMethod.Put, "/v1/account/password", new { newPassword = Password }, bearer: bearer))
            Assert.Equal(HttpStatusCode.NoContent, first.Status);

        // A first password ends the other sessions, and from now on the password is required.
        using var otherAccess = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(other));
        using var signIn = await api.SignInAsync("neo@x.com", Password);
        using var deleteWithout = await api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { }, bearer: bearer);
        using var deleteWith = await api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { password = Password }, bearer: bearer);
        Assert.Equal("invalid_token", otherAccess.Code);
        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        Assert.Equal("invalid_credentials", deleteWithout.Code);
        Assert.Equal(HttpStatusCode.NoContent, deleteWith.Status);
    }

    [Fact]
    public async Task A_password_change_deletes_live_reset_links()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var resetToken = await RecoveryTests.RequestTokenAsync(api, "ada@x.com");

        using var change = await api.SendAsync(HttpMethod.Put, "/v1/account/password", new { currentPassword = Password, newPassword = "another horse battery" }, bearer: AuthApi.AccessToken(signUp));
        using var reset = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token = resetToken, password = "third horse battery" });

        Assert.Equal(HttpStatusCode.NoContent, change.Status);
        Assert.Equal("invalid_email_token", reset.Code);
    }
}
