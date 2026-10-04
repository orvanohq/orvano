using System.Net;
using System.Security.Cryptography;
using System.Text;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0012 build task 7 over HTTP against the real binary: after redirect sign in with every provider, a refused
// code, native sign in, a link, and an Apple user deleted, no log line, event, job payload, or problem body carries a
// state, nonce, verifier, handoff code, ID token, client secret, private key, email, or redirect URL, and the four
// tables hold no plain secret. AC-18, AC-19.
public class OAuthLeakTests(PostgresFixture postgres)
{
    [Fact]
    public async Task No_record_carries_a_flow_secret_a_token_or_personal_data()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var problems = new StringBuilder();
        var seen = new List<string>();

        foreach (var provider in new[] { "google", "apple", "github", "microsoft" })
        {
            var (start, verifier) = await OAuthDriver.StartAsync(api, provider);
            using (start)
            {
                var url = start.Body.GetProperty("url").GetString()!;
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(url).Query);
                seen.Add(query["state"].ToString());
                if (query.TryGetValue("nonce", out var nonce)) seen.Add(nonce.ToString());
                seen.Add(verifier);
                var back = await OAuthDriver.FollowAsync(api, url, new { sub = $"9{provider.Length}42", email = $"leak-{provider}@x.com", emailVerified = true, xmsEdov = true, name = "Leak Name" });
                var code = OAuthDriver.Param(back, "orvano_code")!;
                seen.Add(code);
                using var refused = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code, codeVerifier = OAuthDriver.Pkce().Verifier });
                problems.Append(refused.Body.GetRawText());
                using var signedIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/oauth", new { code, codeVerifier = verifier });
                Assert.Equal(HttpStatusCode.Created, signedIn.Status);
            }
        }

        const string nativeNonce = "leak-nonce-0123456789";
        var hashed = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nativeNonce)));
        using var minted = await api.SendAsync(HttpMethod.Post, "/v1/test/oauth/id-tokens",
            new { provider = "apple", aud = AuthApi.AppleBundleId, sub = "leak-apple", email = "leak-native@x.com", emailVerified = true, nonce = hashed });
        var idToken = minted.Body.GetProperty("idToken").GetString()!;
        var appleCode = minted.Body.GetProperty("authorizationCode").GetString()!;
        seen.AddRange([idToken, appleCode]);
        using var native = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token",
            new { provider = "apple", idToken, nonce = nativeNonce, authorizationCode = appleCode });
        Assert.Equal(HttpStatusCode.Created, native.Status);
        using var replay = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token",
            new { provider = "apple", idToken, nonce = nativeNonce, authorizationCode = appleCode });
        problems.Append(replay.Body.GetRawText());
        using (await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{AuthApi.UserId(native)}")) { }

        string[] secrets =
        [
            .. seen, "orv_oc_", "eyJ", "leak-", "@x.com", "Leak Name", "app.example.com", "google-secret-0001", "github-secret-0001",
            "microsoft-secret-0001", "PRIVATE KEY", "fake-refresh",
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

        Assert.Contains("invalid_oauth_code", problems.ToString(), StringComparison.Ordinal);
        Assert.Contains("invalid_id_token", problems.ToString(), StringComparison.Ordinal);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.jobs WHERE kind = 'auth.apple.revoke'"));

        // The tables hold hashes and sealed values only.
        foreach (var (table, column) in new[]
                 {
                     ("auth_oauth_providers", "client_secret_ciphertext"), ("auth_oauth_providers", "apple_private_key_ciphertext"),
                     ("auth_identities", "provider_refresh_ciphertext"), ("auth_oauth_flows", "result_ciphertext"),
                     ("auth_oauth_flows", "provider_verifier_ciphertext"),
                 })
        {
            foreach (var plain in new[] { "secret", "PRIVATE", "fake-refresh", "leak-", "@x.com" })
            {
                Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
                    $"SELECT count(*) FROM orvano.{table} WHERE position('{plain}'::bytea in coalesce({column}, ''::bytea)) > 0"));
            }
        }

        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.auth_id_token_uses WHERE octet_length(token_hash) <> 32"));
        Assert.Equal(4 * 4 - 1 + 2, seen.Count); // state, verifier, code per provider, a nonce for all but GitHub, and the native token and code
    }
}
