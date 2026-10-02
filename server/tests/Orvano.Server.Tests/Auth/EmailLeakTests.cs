using System.Text;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 build task 8 over HTTP against the real binary: after every flow runs, no event, job payload, log line,
// or problem body carries a token, code, email address, or redirect URL, and the database holds no usable secret.
// AC-1, AC-30.
public class EmailLeakTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth/callback";

    [Fact]
    public async Task No_record_carries_a_token_a_code_an_email_or_a_redirect_url()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var problems = new StringBuilder();
        async Task<Reply> SendAsync(HttpMethod method, string url, object body, string? bearer = null)
        {
            var reply = await api.SendAsync(method, url, body, bearer: bearer);
            if ((int)reply.Status >= 400) problems.Append(reply.Body.GetRawText());
            return reply;
        }

        using var signUp = await SendAsync(HttpMethod.Post, "/v1/account", new { email = "leak-ada@x.com", password = "correct horse battery", verificationRedirectUrl = Redirect });
        var verification = (await api.LatestEmailAsync("leak-ada@x.com"))!.Token!;
        using (await SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = verification })) { }
        using (await SendAsync(HttpMethod.Post, "/v1/account/verification/confirm", new { token = verification })) { }

        var reset = await RecoveryTests.RequestTokenAsync(api, "leak-ada@x.com");
        using (await SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token = reset, password = "short" })) { }
        using var recovered = await SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token = reset, password = "another horse battery" });

        var link = await PasswordlessTests.MagicLinkAsync(api, "leak-neo@x.com");
        using (await SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = link })) { }
        using (await SendAsync(HttpMethod.Post, "/v1/account/sessions/magic-link", new { token = link })) { }

        var code = await PasswordlessTests.EmailCodeAsync(api, "leak-cal@x.com");
        using (await SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "leak-cal@x.com", code = code == "000000" ? "000001" : "000000" })) { }
        using (await SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email = "leak-cal@x.com", code })) { }

        using (await SendAsync(HttpMethod.Put, "/v1/account/email", new { email = "leak-ada-new@x.com", redirectUrl = Redirect, password = "another horse battery" }, AuthApi.AccessToken(recovered))) { }
        var change = (await api.LatestEmailAsync("leak-ada-new@x.com"))!.Token!;
        using (await SendAsync(HttpMethod.Post, "/v1/account/email/confirm", new { token = change })) { }
        using (await SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "leak-ada-new@x.com", redirectUrl = "https://evil.example/steal" })) { }

        string[] secrets = [verification, reset, link, code, change, "orv_el_", "leak-", "@x.com", "app.example.com", "evil.example"];
        var events = await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT coalesce(string_agg(payload::text, ' '), '') FROM orvano.events");
        var jobs = await TestDatabase.ScalarAsync<string>(api.Database.Superuser, "SELECT coalesce(string_agg(payload::text, ' '), '') FROM orvano.jobs");
        var log = api.Process.Output;
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, events, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, jobs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, problems.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // The table keeps 32 byte hashes only: no column holds a token or a code in a usable form.
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_email_tokens WHERE octet_length(secret_hash) <> 32 OR encode(secret_hash, 'escape') LIKE '%orv_el_%'"));
        Assert.Contains("invalid_email_token", problems.ToString(), StringComparison.Ordinal); // the refusals were captured
    }
}
