using System.Net;
using System.Security.Cryptography;
using System.Text;
using Orvano.Auth.Application;
using Orvano.Auth.Jobs;
using Orvano.Server.Modules;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0014 AC-39 over HTTP against the real binary: after saving rules with domains and CIDRs, refused domains and
// passwords (the breached check included), a hidden sign up, a lockout, a session cap, a guest's sign in, upgrade, and
// idle deletion, no log line, event, job payload, or problem body carries an email, a domain, an IP address, a CIDR, a
// password, or the breached check's hash prefix, and the new events carry IDs, field names, and reasons only.
public class AuthPolicyLeakTests(PostgresFixture postgres)
{
    private const string PoliciesUrl = AuthPolicyTests.PoliciesUrl;
    private const string Password = "correct horse battery";
    private const string BlockedDomain = "leaky-blocked.example";
    private const string AllowedLooking = "leaky-mail.example";
    private const string ServerCidr = "198.51.100.0/24";
    private const string VisitorIp = "203.0.113.77";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_record_carries_an_email_a_domain_an_address_a_cidr_a_password_or_a_hash_prefix()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true, pwned: true);
        var problems = new StringBuilder();
        var common = "password123";

        using (var methods = await api.AsConsoleAsync(HttpMethod.Patch, "/v1/console/project/auth/methods", new { anonymousEnabled = true }))
            Assert.Equal(HttpStatusCode.OK, methods.Status);
        await SaveAsync(api, new
        {
            blockedEmailDomains = new[] { BlockedDomain },
            trustedServerCidrs = new[] { ServerCidr, "127.0.0.1/32", "::1/128" },
            passwordBreachedCheck = true,
            maxSessionsPerUser = 1,
            signInFailedPerEmailIp = new { limit = 3, windowMinutes = 15 },
        });
        // A refused update names the field and the position, never the bad value.
        using (var bad = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { allowedEmailDomains = new[] { "@" + AllowedLooking } }))
            problems.Append(bad.Body.GetRawText());

        using (var blocked = await api.SignUpAsync($"ada@{BlockedDomain}")) problems.Append(blocked.Body.GetRawText());
        using (var tooCommon = await api.SignUpAsync($"bob@{AllowedLooking}", common)) problems.Append(tooCommon.Body.GetRawText());
        using (var breached = await api.SignUpAsync($"cy@{AllowedLooking}", FakePwnedPasswords.Breached)) problems.Append(breached.Body.GetRawText());
        using (var ada = await api.SignUpAsync($"ada@{AllowedLooking}")) Assert.Equal(HttpStatusCode.Created, ada.Status);

        // A lockout from a named visitor, then the cap ends the older session.
        for (var i = 0; i < 4; i++)
        {
            using var wrong = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/password",
                new { email = $"ada@{AllowedLooking}", password = "wrong horse battery" }, headers: From(VisitorIp));
            problems.Append(wrong.Body.GetRawText());
        }

        using (var signedIn = await api.SignInAsync($"ada@{AllowedLooking}")) Assert.Equal(HttpStatusCode.Created, signedIn.Status);

        // A hidden sign up under the verified email rule, for a known and a new address.
        await SaveAsync(api, new { requireVerifiedEmail = true });
        using (await api.SendAsync(HttpMethod.Post, "/v1/account",
                   new { email = $"ada@{AllowedLooking}", password = Password, verificationRedirectUrl = "https://app.example.com/cb" })) { }
        using (await api.SendAsync(HttpMethod.Post, "/v1/account",
                   new { email = $"dee@{AllowedLooking}", password = Password, verificationRedirectUrl = "https://app.example.com/cb" })) { }
        await SaveAsync(api, new { requireVerifiedEmail = false });

        // A guest signs in and upgrades; another goes idle and retention deletes it.
        using var guest = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/anonymous");
        using (var upgraded = await api.SendAsync(HttpMethod.Post, "/v1/account/anonymous/upgrade",
                   new { email = $"guest@{AllowedLooking}", password = Password }, bearer: AuthApi.AccessToken(guest)))
            Assert.Equal(HttpStatusCode.OK, upgraded.Status);
        using var idle = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/anonymous");
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_users SET created_at = now() - interval '60 days' WHERE id = @id", ("id", Guid.Parse(AuthApi.UserId(idle))));
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET created_at = now() - interval '60 days', last_refreshed_at = now() - interval '60 days' WHERE user_id = @id",
            ("id", Guid.Parse(AuthApi.UserId(idle))));
        await using (var app = Npgsql.NpgsqlDataSource.Create(api.Database.AppUrl))
            Assert.Equal(1, await AuthRetention.DeleteIdleGuestsAsync(new AuthStore(app), Ct));

        var prefix = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(FakePwnedPasswords.Breached)))[..5];
        string[] secrets =
        [
            AllowedLooking, BlockedDomain, ServerCidr, "198.51.100.", VisitorIp, Password, FakePwnedPasswords.Breached, common,
            "wrong horse battery", $"range/{prefix}", $"/{prefix}",
        ];
        var events = await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT coalesce(string_agg(payload::text, ' '), '') FROM orvano.events");
        var jobs = await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT coalesce(string_agg(payload::text, ' '), '') FROM orvano.jobs");
        // The fake range API is served by this same process in Test; its own request lines are the far end of the call,
        // which in production is api.pwnedpasswords.com, so they are left out. Everything the caller logs is scanned.
        var log = string.Join('\n', api.Process.Output.Split('\n').Where(line => !line.Contains("/v1/test/pwned", StringComparison.Ordinal)
            || line.Contains("System.Net.Http", StringComparison.Ordinal)));
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, events, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, jobs, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, problems.ToString(), StringComparison.Ordinal);
        }

        foreach (var code in new[] { "invalid_request", "email_domain_not_allowed", "password_too_common", "password_breached", "invalid_credentials", "rate_limited" })
            Assert.Contains(code, problems.ToString(), StringComparison.Ordinal);

        // The new events carry IDs, field names, and reasons, and each was written.
        Assert.Contains("blockedEmailDomains", events, StringComparison.Ordinal);
        Assert.Contains("trustedServerCidrs", events, StringComparison.Ordinal);
        foreach (var (type, field) in new[]
                 {
                     ("auth.policies.updated", "changed"), ("auth.user.upgraded", "method"), ("auth.user.deleted", "reason"),
                     ("auth.session.ended", "reason"), ("auth.user.created", "method"),
                 })
        {
            Assert.True(await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
                "SELECT count(*) FROM orvano.events WHERE type = @type AND payload ? @field", ("type", type), ("field", field)) > 0, type);
        }

        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.deleted' AND payload->>'reason' = 'anonymous_idle'"));
        Assert.True(await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.ended' AND payload->>'reason' = 'session_limit'") > 0);
    }

    private static async Task SaveAsync(AuthApi api, object change)
    {
        using var saved = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, change);
        Assert.True(saved.Status == HttpStatusCode.OK, $"{saved.Status}: {saved.Code}");
    }

    private static Dictionary<string, string> From(string ip) => new() { ["X-Orvano-Client-IP"] = ip };
}
