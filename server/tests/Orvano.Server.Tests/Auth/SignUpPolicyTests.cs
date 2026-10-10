using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0014 build task 4 over HTTP against the real binary: closed sign ups, the email domain rule, require verified
// email with the hidden sign up and the sign in refusal, the reject link, provider sign ups under the switches, and the
// tightened last sign in method rule, and the claim a second hidden sign up makes (amended 2026-10-10). AC-8 to AC-15, AC-33.
public class SignUpPolicyTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth/callback";
    private const string Password = "correct horse battery";

    [Fact]
    public async Task Closed_sign_ups_refuse_every_client_path_that_creates_a_user_but_not_existing_users_or_servers()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using (var existing = await api.SignUpAsync("ada@x.com")) Assert.Equal(HttpStatusCode.Created, existing.Status);
        var token = await PasswordlessTests.MagicLinkAsync(api, "new@x.com");
        await SetAsync(api, new { signUpsEnabled = false });

        using var signUp = await api.SignUpAsync("bob@x.com");
        Assert.Equal(HttpStatusCode.Forbidden, signUp.Status);
        Assert.Equal("sign_up_disabled", signUp.Code);
        // Decided before the account is read: a taken email gets the same answer.
        using var taken = await api.SignUpAsync("ada@x.com");
        Assert.Equal("sign_up_disabled", taken.Code);

        // A magic link for a new email is refused at redemption and stays unspent.
        using var link = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        Assert.Equal("sign_up_disabled", link.Code);

        using var signIn = await api.SignInAsync("ada@x.com");
        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        using var created = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "server@x.com", password = Password });
        Assert.Equal(HttpStatusCode.Created, created.Status);

        await SetAsync(api, new { signUpsEnabled = true });
        using var later = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        Assert.Equal(HttpStatusCode.Created, later.Status);
        Assert.True(later.Body.GetProperty("isNewUser").GetBoolean());
    }

    [Fact]
    public async Task The_domain_rule_applies_to_every_path_that_sets_an_email_and_never_to_current_emails()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var ada = await api.SignUpAsync("ada@example.com");
        using var bob = await api.SignUpAsync("bob@x.com");
        var token = await PasswordlessTests.MagicLinkAsync(api, "neo@mail.example.com");
        await SetAsync(api, new { blockedEmailDomains = new[] { "example.com" } });

        using var signUp = await api.SignUpAsync("a@mail.example.com");
        Assert.Equal(HttpStatusCode.Forbidden, signUp.Status);
        Assert.Equal("email_domain_not_allowed", signUp.Code);
        using var server = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "s@example.com", password = Password });
        Assert.Equal("email_domain_not_allowed", server.Code);
        using var moved = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{AuthApi.UserId(bob)}/email", new { email = "bob@example.com" });
        Assert.Equal("email_domain_not_allowed", moved.Code);
        using var change = await api.SendAsync(HttpMethod.Put, "/v1/account/email",
            new { email = "bob@sub.example.com", redirectUrl = Redirect, password = Password }, bearer: AuthApi.AccessToken(bob));
        Assert.Equal("email_domain_not_allowed", change.Code);
        using var link = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        Assert.Equal("email_domain_not_allowed", link.Code);

        // An existing @example.com user still signs in, by password and by magic link.
        using var signIn = await api.SignInAsync("ada@example.com");
        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        var adaLink = await PasswordlessTests.MagicLinkAsync(api, "ada@example.com");
        using var adaLinked = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = adaLink });
        Assert.Equal(HttpStatusCode.Created, adaLinked.Status);

        // An allowed list admits only its domains (after IDN conversion) and skips the other two checks.
        await SetAsync(api, new { blockedEmailDomains = Array.Empty<string>(), allowedEmailDomains = new[] { "xn--bcher-kva.example" }, blockDisposableEmails = true });
        using var idn = await api.SignUpAsync("a@bücher.example");
        Assert.Equal(HttpStatusCode.Created, idn.Status);
        using var outside = await api.SignUpAsync("c@x.com");
        Assert.Equal("email_domain_not_allowed", outside.Code);

        // Disposable addresses, with no allowed list.
        await SetAsync(api, new { allowedEmailDomains = Array.Empty<string>() });
        using var disposable = await api.SignUpAsync("someone@mailinator.com");
        Assert.Equal("email_domain_not_allowed", disposable.Code);
        using var fine = await api.SignUpAsync("someone@x.com");
        Assert.Equal(HttpStatusCode.Created, fine.Status);
    }

    [Fact]
    public async Task Require_verified_email_needs_smtp_and_then_hides_which_emails_have_accounts()
    {
        await using (var noSmtp = await AuthApi.StartAsync(postgres, email: true))
        {
            using var refused = await noSmtp.AsConsoleAsync(HttpMethod.Patch, AuthPolicyTests.PoliciesUrl, new { requireVerifiedEmail = true });
            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Equal("email_not_configured", refused.Code);
        }

        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using (var owner = await api.SignUpAsync("owner@x.com")) Assert.Equal(HttpStatusCode.Created, owner.Status);
        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_users SET email_verified_at = now() WHERE lower(email) = 'owner@x.com'");
        // Listing the test client as an app server lets a request name another limit IP, so a resend isn't held back
        // by the one a minute recipient limit of the sign up's own email.
        await SetAsync(api, new { requireVerifiedEmail = true, trustedServerCidrs = new[] { "127.0.0.1/32", "::1/128" } });

        using var missing = await api.SignUpAsync("x@x.com");
        Assert.Equal(HttpStatusCode.BadRequest, missing.Status);
        Assert.Equal("invalid_request", missing.Code);

        var clock = Stopwatch.StartNew();
        using var fresh = await PendingSignUpAsync(api, "new@x.com");
        Assert.True(clock.ElapsedMilliseconds >= 450, $"took {clock.ElapsedMilliseconds} ms");
        clock.Restart();
        using var known = await PendingSignUpAsync(api, "OWNER@x.com", name: "Someone Else");
        Assert.True(clock.ElapsedMilliseconds >= 450, $"took {clock.ElapsedMilliseconds} ms");

        // Byte identical bodies, no cookie, no echoed name.
        Assert.Equal(fresh.Document!.RootElement.GetRawText(), known.Document!.RootElement.GetRawText());
        Assert.True(known.Body.GetProperty("verificationRequired").GetBoolean());
        foreach (var field in new[] { "user", "session", "mfa", "verificationEmail" })
            Assert.Equal(JsonValueKind.Null, known.Body.GetProperty(field).ValueKind);
        Assert.False(known.Body.GetProperty("isNewUser").GetBoolean());
        Assert.False(known.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain("Someone", known.Document.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_sessions s JOIN orvano.auth_users u ON u.id = s.user_id WHERE lower(u.email) = 'new@x.com'"));

        // The new email got a verification email with both links; the owner got the alert, and their row is unchanged.
        var verification = await api.LatestEmailAsync("new@x.com");
        Assert.Equal("verification", verification!.Template);
        Assert.Contains("orvano_type=verification_reject", verification.Text, StringComparison.Ordinal);
        var alert = await api.LatestEmailAsync("owner@x.com");
        Assert.Equal("security_alert", alert!.Template);
        Assert.Contains("tried to sign up", alert.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_users WHERE name = 'Someone Else'"));

        // The new user's right password gets 403 email_verification_required; a wrong one 401.
        using var wrong = await api.SignInAsync("new@x.com", "wrong horse battery");
        Assert.Equal("invalid_credentials", wrong.Code);
        using var unverified = await api.SignInAsync("new@x.com");
        Assert.Equal(HttpStatusCode.Forbidden, unverified.Status);
        Assert.Equal("email_verification_required", unverified.Code);

        // With a redirect, the refused sign in sends a fresh link, and the answer is the same.
        var before = await api.QueuedEmailCountAsync();
        using var resend = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/password",
            new { email = "new@x.com", password = Password, verificationRedirectUrl = Redirect }, headers: From("198.51.100.7"));
        Assert.Equal("email_verification_required", resend.Code);
        Assert.Equal(before + 1, await api.QueuedEmailCountAsync());

        var link = await api.LatestEmailAsync("new@x.com");
        using (var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = link!.Token }))
            Assert.Equal(HttpStatusCode.OK, verified.Status);
        using var signedIn = await api.SignInAsync("new@x.com");
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        Assert.False(signedIn.Body.GetProperty("verificationRequired").GetBoolean());

        // The verified owner's link is spent: its reject link fails too.
        using var rejectAfterVerify = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/reject", new { token = link.Token });
        Assert.Equal("invalid_email_token", rejectAfterVerify.Code);
    }

    [Fact]
    public async Task The_reject_link_claims_an_impostors_pre_registration_for_the_inbox_owner()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);

        // The impostor signs up with Ada's email before the switch, and holds a session.
        using var impostor = await api.SignUpAsync("ada@x.com", extra: new { email = "ada@x.com", password = Password, verificationRedirectUrl = Redirect });
        Assert.Equal(HttpStatusCode.Created, impostor.Status);
        var userId = AuthApi.UserId(impostor);
        await SetAsync(api, new { requireVerifiedEmail = true, trustedServerCidrs = new[] { "127.0.0.1/32", "::1/128" } });

        var email = await api.LatestEmailAsync("ada@x.com");
        using var rejected = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/reject", new { token = email!.Token });
        Assert.Equal(HttpStatusCode.NoContent, rejected.Status);

        // The password and sessions are gone, the user ID stays, and the impostor's token fails.
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_passwords WHERE user_id = @u::uuid", ("u", userId)));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_users WHERE id = @u::uuid AND email_verified_at IS NULL", ("u", userId)));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_sessions WHERE user_id = @u::uuid AND end_reason = 'account_claimed'", ("u", userId)));
        using var stale = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(impostor));
        Assert.Equal(HttpStatusCode.Unauthorized, stale.Status);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.password.removed'"));

        // The token works once; a verify with it fails too.
        using var again = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/reject", new { token = email.Token });
        Assert.Equal("invalid_email_token", again.Code);
        using var verify = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = email.Token });
        Assert.Equal("invalid_email_token", verify.Code);
        using var malformed = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/reject", new { token = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.Status);

        // A second hidden sign up for the still unverified email sends Ada a fresh link.
        var before = await api.QueuedEmailCountAsync();
        using (var second = await PendingSignUpAsync(api, "ada@x.com", from: "198.51.100.7")) Assert.True(second.Body.GetProperty("verificationRequired").GetBoolean());
        Assert.Equal(before + 1, await api.QueuedEmailCountAsync());
        Assert.Equal("verification", (await api.LatestEmailAsync("ada@x.com"))!.Type);

        // Ada signs in by magic link, which verifies the same user.
        var token = await PasswordlessTests.MagicLinkAsync(api, "ada@x.com");
        using var ada = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        Assert.Equal(HttpStatusCode.Created, ada.Status);
        Assert.Equal(userId, AuthApi.UserId(ada));
        Assert.True(ada.Body.GetProperty("user").GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task The_reject_link_carries_the_verification_token_on_the_same_redirect_and_an_email_change_has_none()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com", extra: new { email = "ada@x.com", password = Password, verificationRedirectUrl = Redirect });
        Assert.Equal(HttpStatusCode.Created, signUp.Status);

        // AC-15: both links hold the same token on the same redirect URL; only orvano_type tells them apart.
        var links = Links((await api.LatestEmailAsync("ada@x.com"))!.Text);
        Assert.Equal(["verification", "verification_reject"], links.Select(l => l.Type).Order());
        Assert.Single(links.Select(l => l.Token).Distinct());
        Assert.All(links, l => Assert.Equal(Redirect, l.Target));

        // An email change email carries only its own link, never a reject link.
        using (var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = links[0].Token }))
            Assert.Equal(HttpStatusCode.OK, verified.Status);
        using var signedIn = await api.SignInAsync("ada@x.com");
        using var change = await api.SendAsync(HttpMethod.Put, "/v1/account/email",
            new { email = "ada.new@x.com", redirectUrl = Redirect, password = Password }, bearer: AuthApi.AccessToken(signedIn));
        Assert.True(change.Status == HttpStatusCode.Accepted, $"{change.Status}: {change.Code}");
        var changeEmail = (await api.LatestEmailAsync("ada.new@x.com"))!;
        Assert.Equal(["email_change"], Links(changeEmail.Text).Select(l => l.Type));
        Assert.DoesNotContain("verification_reject", changeEmail.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sign_up_attempt_alert_is_the_security_alert_template_and_keeps_a_projects_own_words()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using (var owner = await api.SignUpAsync("owner@x.com")) await MfaTests.VerifyEmailAsync(api, AuthApi.UserId(owner));
        using (var edited = await api.AsConsoleAsync(HttpMethod.Put, "/v1/console/project/email/templates/security_alert",
            new { subject = "Heads up from {{ project.name }}", html = "<p>Our words: {{ alert }}</p>", text = "Our words: {{ alert }}" }))
            Assert.True(edited.Status == HttpStatusCode.OK, edited.Document?.RootElement.ToString());
        await SetAsync(api, new { requireVerifiedEmail = true });

        using (var hidden = await PendingSignUpAsync(api, "owner@x.com")) { }

        // AC-12: the alert renders the project's edited security_alert, with alert sign_up_attempt.
        var alert = (await api.LatestEmailAsync("owner@x.com"))!;
        Assert.Equal("security_alert", alert.Template);
        Assert.Equal("Heads up from Auth project", alert.Subject);
        Assert.Equal("Our words: sign_up_attempt", alert.Text.Trim());
    }

    [Fact]
    public async Task A_second_hidden_sign_up_claims_the_unverified_account_so_no_earlier_password_survives()
    {
        const string Other = "other horse battery";
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);

        // The impostor signs up with Ada's email and password P before the switch, holds a session, and links GitHub.
        using var impostor = await api.SignUpAsync("ada@x.com");
        Assert.Equal(HttpStatusCode.Created, impostor.Status);
        var userId = AuthApi.UserId(impostor);
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            $"INSERT INTO orvano.auth_identities (project_id, user_id, provider, subject, email_verified) VALUES ('{AuthApi.Project}', '{userId}', 'github', '666', false)");
        await SetAsync(api, new { requireVerifiedEmail = true, trustedServerCidrs = new[] { "127.0.0.1/32", "::1/128" } });

        // Ada signs up with password Q: the same answer, in the same time, as a sign up for a free email.
        var clock = Stopwatch.StartNew();
        using var ada = await PendingSignUpAsync(api, "ada@x.com", from: "198.51.100.7", password: Other);
        Assert.True(clock.ElapsedMilliseconds >= 450, $"took {clock.ElapsedMilliseconds} ms");
        using var free = await PendingSignUpAsync(api, "free@x.com", from: "198.51.100.8");
        Assert.Equal(free.Document!.RootElement.GetRawText(), ada.Document!.RootElement.GetRawText());
        Assert.False(ada.Headers.Contains("Set-Cookie"));

        // The account is claimed at once: no password, identity, or session, with the claim's events.
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_passwords WHERE user_id = @u::uuid", ("u", userId)));
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_identities WHERE user_id = @u::uuid", ("u", userId)));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_sessions WHERE user_id = @u::uuid AND end_reason = 'account_claimed'", ("u", userId)));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.password.removed'"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.identity.unlinked'"));
        using (var stale = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(impostor)))
            Assert.Equal(HttpStatusCode.Unauthorized, stale.Status);

        // P now gets 401 at once, not the 403 that would resend a link to the impostor.
        var before = await api.QueuedEmailCountAsync();
        using (var p = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/password",
            new { email = "ada@x.com", password = Password, verificationRedirectUrl = Redirect }, headers: From("198.51.100.9")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, p.Status);
            Assert.Equal("invalid_credentials", p.Code);
        }

        Assert.Equal(before, await api.QueuedEmailCountAsync());

        // Ada's fresh link verifies the same user, and Q was never kept either.
        var link = await api.LatestEmailAsync("ada@x.com");
        Assert.Equal("verification", link!.Type);
        using (var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = link.Token }))
            Assert.Equal(HttpStatusCode.OK, verified.Status);
        using (var q = await api.SignInAsync("ada@x.com", Other)) Assert.Equal("invalid_credentials", q.Code);

        var token = await PasswordlessTests.MagicLinkAsync(api, "ada@x.com");
        using var signedIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token });
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        Assert.Equal(userId, AuthApi.UserId(signedIn));

        // A first sign up whose link is verified with no later sign up keeps its password.
        var own = await api.LatestEmailAsync("free@x.com");
        using (var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = own!.Token }))
            Assert.Equal(HttpStatusCode.OK, verified.Status);
        using var kept = await api.SignInAsync("free@x.com");
        Assert.Equal(HttpStatusCode.Created, kept.Status);
    }

    [Fact]
    public async Task Provider_sign_ups_follow_closed_sign_ups_the_domain_rule_and_require_verified_email()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true, smtp: true);
        using (var linked = await OAuthDriver.SignInAsync(api, "github", new { sub = "linked", email = "linked@x.com", emailVerified = true }))
            Assert.Equal(HttpStatusCode.Created, linked.Status);

        await SetAsync(api, new { requireVerifiedEmail = true });
        using var unverified = await OAuthDriver.SignInAsync(api, "github", new { sub = "gh-new", email = "gh-new@x.com", emailVerified = false });
        Assert.Equal("email_verification_required", unverified.Code);
        using var again = await OAuthDriver.SignInAsync(api, "github", new { sub = "linked", email = "linked@x.com", emailVerified = true });
        Assert.Equal(HttpStatusCode.Created, again.Status);

        await SetAsync(api, new { requireVerifiedEmail = false, allowedEmailDomains = new[] { "x.com" } });
        using var noEmail = await OAuthDriver.SignInAsync(api, "github", new { sub = "gh-none", emailVerified = false });
        Assert.Equal("email_domain_not_allowed", noEmail.Code);

        await SetAsync(api, new { allowedEmailDomains = Array.Empty<string>(), signUpsEnabled = false });
        using var closed = await OAuthDriver.SignInAsync(api, "google", new { sub = "g-new", email = "g-new@x.com", emailVerified = true });
        Assert.Equal("sign_up_disabled", closed.Code);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_users WHERE lower(email) LIKE 'g%new@x.com'"));
    }

    [Fact]
    public async Task Without_smtp_a_verified_email_is_no_way_in_so_the_last_identity_stays()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        using var user = await OAuthDriver.SignInAsync(api, "google", new { sub = "only-google", email = "only@x.com", emailVerified = true });
        var bearer = AuthApi.AccessToken(user);
        using var list = await api.SendAsync(HttpMethod.Get, "/v1/account/identities", bearer: bearer);
        var only = list.Body.GetProperty("items")[0].GetProperty("id").GetString();

        using var refused = await api.SendAsync(HttpMethod.Delete, $"/v1/account/identities/{only}", bearer: bearer);
        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("last_sign_in_method", refused.Code);
    }

    private static async Task SetAsync(AuthApi api, object change)
    {
        using var saved = await api.AsConsoleAsync(HttpMethod.Patch, AuthPolicyTests.PoliciesUrl, change);
        Assert.True(saved.Status == HttpStatusCode.OK, saved.Document?.RootElement.ToString());
    }

    private static Dictionary<string, string> From(string ip) => new() { ["X-Orvano-Client-IP"] = ip };

    /// <summary>Every distinct emailed link in a text body: its URL before the query, its <c>orvano_type</c>, and its token.</summary>
    private static List<(string Target, string Type, string Token)> Links(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"(?<target>https?://[^\s?""<>()]+)\?orvano_type=(?<type>[a-z_]+)&orvano_token=(?<token>[A-Za-z0-9_\-]+)")
            .Select(m => (m.Groups["target"].Value, m.Groups["type"].Value, m.Groups["token"].Value))
            .Distinct()
            .ToList();

    private static async Task<Reply> PendingSignUpAsync(AuthApi api, string email, string? name = null, string? from = null, string password = Password)
    {
        var reply = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email, password, name, verificationRedirectUrl = Redirect },
            headers: from is null ? null : From(from));
        Assert.True(reply.Status == HttpStatusCode.Created, reply.Document?.RootElement.ToString());
        return reply;
    }
}
