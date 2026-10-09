using System.Net;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;
using static Orvano.Server.Tests.Auth.PasskeyTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 AC-31 over HTTP against the real binary: every security change queues its alert email in its own
// transaction, worded through the security_alert template; no SMTP or no email means no alert and the change still
// commits; claiming an account sends none; console accounts get theirs through the install SMTP.
public class SecurityAlertTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_change_queues_its_own_alert_for_the_user()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        await EnablePasskeysAsync(api);
        var ada = await EnrollAsync(api, "ada@x.com");
        using var stepOne = await api.SignInAsync("ada@x.com");
        using var stepTwo = await StepTwoAsync(api, Ticket(stepOne), recoveryCode: ada.RecoveryCodes[0]);
        var bearer = AuthApi.AccessToken(stepTwo);
        using var codes = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/recovery-codes", bearer: bearer);
        var passkey = await RegisterAsync(api, bearer);
        using var removed = await api.SendAsync(HttpMethod.Delete, $"/v1/account/passkeys/{passkey.Passkey.GetProperty("id").GetString()}", bearer: bearer);
        using var off = await api.SendAsync(HttpMethod.Delete, "/v1/account/mfa/totp", bearer: bearer);

        Assert.Equal(HttpStatusCode.Created, codes.Status);
        Assert.Equal(HttpStatusCode.NoContent, removed.Status);
        Assert.Equal(HttpStatusCode.NoContent, off.Status);
        Assert.Equal(
            [
                "Two step verification is on", "A recovery code was used", "New recovery codes were made", "A passkey was added",
                "A passkey was removed", "Two step verification is off",
            ],
            await AlertsAsync(api, "ada@x.com"));
        var latest = await api.LatestEmailAsync("ada@x.com");
        Assert.Equal(("security_alert", "Security alert for Auth project"), (latest!.Template, latest.Subject));
        Assert.Contains("If it wasn't, sign in to Auth project now", latest.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reset_tells_the_user_and_a_step_up_with_a_recovery_code_does_too()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var ada = await EnrollAsync(api, "ada@x.com");
        using var signUp = await api.SignInAsync("ada@x.com");
        using var stepTwo = await StepTwoAsync(api, Ticket(signUp), totpCode: CodeAt(ada.Secret, 1));
        using var stepUp = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify", new { recoveryCode = ada.RecoveryCodes[1] },
            bearer: AuthApi.AccessToken(stepTwo));

        using var reset = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{ada.UserId}/mfa/reset");
        using var again = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{ada.UserId}/mfa/reset");

        Assert.Equal(HttpStatusCode.OK, stepUp.Status);
        Assert.Equal(HttpStatusCode.NoContent, again.Status);
        Assert.Equal(["Two step verification is on", "A recovery code was used", "Two step verification is off"], await AlertsAsync(api, "ada@x.com"));
    }

    [Fact]
    public async Task Without_smtp_the_change_commits_and_no_email_is_queued()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);

        var ada = await EnrollAsync(api, "ada@x.com");

        Assert.Equal(10, ada.RecoveryCodes.Count);
        Assert.Equal(0L, await api.QueuedEmailCountAsync());
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.mfa.enabled'"));
    }

    [Fact]
    public async Task A_claim_removes_factors_without_an_alert()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var ada = await EnrollAsync(api, "ada@x.com");
        using (var unverified = await api.AsServerAsync(HttpMethod.Put, $"/v1/users/{ada.UserId}/email-verification", new { verified = false }))
            Assert.Equal(HttpStatusCode.OK, unverified.Status);
        var before = await AlertsAsync(api, "ada@x.com");

        using var magic = await api.SendAsync(HttpMethod.Post, "/v1/account/magic-link",
            new { email = "ada@x.com", redirectUrl = "https://app.example.com/done" });
        Assert.Equal(HttpStatusCode.Accepted, magic.Status);
        var link = (await api.LatestEmailAsync("ada@x.com"))!;
        using var redeemed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = link.Token });

        Assert.Equal(HttpStatusCode.Created, redeemed.Status);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_totp_factors"));
        Assert.Equal(before, await AlertsAsync(api, "ada@x.com"));
    }

    [Fact]
    public async Task A_console_account_gets_its_alert_through_the_install_smtp()
    {
        await using var api = await AuthApi.StartAsync(postgres, smtp: true);
        var cookie = $"orvano_console={await ConsoleSignIn.CookieAsync(api.Http, AuthApi.ConsoleUser, Ct)}";
        var headers = new Dictionary<string, string> { ["Cookie"] = cookie, ["Sec-Fetch-Site"] = "same-origin" };

        using var setup = await api.SendAsync(HttpMethod.Post, "/v1/console/account/mfa/totp", new { password = ConsoleSignIn.Password }, project: null, headers: headers);
        using var confirmed = await api.SendAsync(HttpMethod.Post, "/v1/console/account/mfa/totp/confirm",
            new { code = CodeAt(setup.Body.GetProperty("secret").GetString()!, 0) }, project: null, headers: headers);

        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        Assert.Equal(["Two step verification is on"], await AlertsAsync(api, AuthApi.ConsoleUser));
        Assert.Equal("Security alert for Orvano", (await api.LatestEmailAsync(AuthApi.ConsoleUser))!.Subject);
    }

    /// <summary>The headings of every security alert queued for <paramref name="to"/>, oldest first.</summary>
    private static async Task<string[]> AlertsAsync(AuthApi api, string to)
    {
        var secrets = new Orvano.Core.Secrets.SecretBox(Orvano.Core.Secrets.MasterKeys.Parse(OrvanoProcess.MasterKeys));
        await using var conn = await api.Database.Superuser.OpenConnectionAsync(Ct);
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT id, content_ciphertext FROM orvano.messaging_emails WHERE template = 'security_alert' ORDER BY created_at, id", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        var headings = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            var email = Orvano.Messaging.Application.EmailSealer.Open(secrets, reader.GetGuid(0), reader.GetFieldValue<byte[]>(1));
            if (string.Equals(email.To, to, StringComparison.OrdinalIgnoreCase)) headings.Add(email.Text.Split('\n')[0]);
        }

        return [.. headings];
    }
}
