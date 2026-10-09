using System.Net;
using System.Text;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;
using static Orvano.Server.Tests.Auth.PasskeyTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 AC-34 over HTTP against the real binary: after TOTP enrollment, step two with a code and a recovery code,
// refused factors and tickets, passkey registration and sign in, step up, and the alert emails, no log line, event,
// job payload, or problem body carries a TOTP secret, an otpauth URI, a recovery code, a ticket, a WebAuthn challenge,
// a credential's JSON, or an email address, and the tables hold the secret only sealed.
public class MfaLeakTests(PostgresFixture postgres)
{
    [Fact]
    public async Task No_record_carries_a_secret_a_code_a_ticket_a_challenge_or_a_credential()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        await EnablePasskeysAsync(api);
        var problems = new StringBuilder();
        var ada = await EnrollAsync(api, "ada@x.com");

        using var stepOne = await api.SignInAsync("ada@x.com");
        var ticket = Ticket(stepOne);
        using (var wrong = await StepTwoAsync(api, ticket, totpCode: WrongCode(ada.Secret))) problems.Append(wrong.Body.GetRawText());
        using (var badTicket = await StepTwoAsync(api, ticket[..^2] + "AA", totpCode: CodeAt(ada.Secret, 1))) problems.Append(badTicket.Body.GetRawText());
        using var stepTwo = await StepTwoAsync(api, ticket, recoveryCode: ada.RecoveryCodes[0]);
        Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        var bearer = AuthApi.AccessToken(stepTwo);
        using (var reused = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify", new { recoveryCode = ada.RecoveryCodes[0] }, bearer: bearer))
            problems.Append(reused.Body.GetRawText());

        using var registration = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", bearer: bearer);
        var challenge = registration.Body.GetProperty("options").GetProperty("challenge").GetString()!;
        var credential = await CreateCredentialAsync(api, registration.Body.GetProperty("options"));
        using (var made = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys",
                   new { challengeId = registration.Body.GetProperty("challengeId").GetString(), credential }, bearer: bearer))
            Assert.Equal(HttpStatusCode.Created, made.Status);
        var credentialId = credential.GetProperty("id").GetString()!;
        using var signedIn = await PasskeySignInAsync(api, credentialId);
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        using (var forged = await PasskeySignInAsync(api, credentialId, new { origin = "https://evil.example.com" })) problems.Append(forged.Body.GetRawText());

        string[] secrets =
        [
            ada.Secret, "otpauth://", ticket, ticket["orv_mt_".Length..], challenge, credentialId,
            credential.GetProperty("response").GetProperty("clientDataJSON").GetString()!,
            credential.GetProperty("response").GetProperty("attestationObject").GetString()!,
            .. ada.RecoveryCodes, .. ada.RecoveryCodes.Select(c => c.Replace("-", "", StringComparison.Ordinal)), "@x.com",
        ];
        var events = await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT coalesce(string_agg(payload::text, ' '), '') FROM orvano.events");
        var jobs = await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT coalesce(string_agg(payload::text, ' '), '') FROM orvano.jobs");
        var log = api.Process.Output;
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, events, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, jobs, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, problems.ToString(), StringComparison.Ordinal);
        }

        foreach (var code in new[] { "invalid_mfa_code", "invalid_mfa_ticket", "invalid_passkey" })
            Assert.Contains(code, problems.ToString(), StringComparison.Ordinal);
        // The alert emails were queued, so the scan covered their jobs too.
        Assert.True(await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.messaging_emails WHERE template = 'security_alert'") >= 3);
        // Codes are kept only as MACs, tickets and challenges only as SHA-256.
        foreach (var code in ada.RecoveryCodes.Select(c => c.Replace("-", "", StringComparison.Ordinal)))
        {
            Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
                "SELECT count(*) FROM orvano.auth_recovery_codes WHERE position(convert_to(@code, 'UTF8') IN code_mac) > 0", ("code", code)));
        }

        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_webauthn_challenges WHERE octet_length(challenge_hash) <> 32"));
    }
}
