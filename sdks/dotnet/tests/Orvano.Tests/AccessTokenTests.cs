using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Orvano.Tests;

// Spec 0004 AC-19: VerifyAccessTokenAsync checks a user's token locally against the project's JWKS, caching the keys
// and fetching again (with no-cache, at most once per 30 seconds) for an unknown kid; online also asks Orvano.
public class AccessTokenTests
{
    private const string Endpoint = "https://orvano.example.com";
    private const string Project = "shop";
    private const string Issuer = Endpoint + "/v1/projects/" + Project;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Verifies_a_token_of_the_project_and_keeps_the_keys()
    {
        var key = NewKey("k1");
        var server = new FakeServer().Then(() => Jwks(key));
        using var client = server.Client(o => o.Project = Project);
        var expires = DateTime.UtcNow.AddMinutes(15);

        var first = await client.VerifyAccessTokenAsync(Sign(key, expires: expires), cancellationToken: Ct);
        var second = await client.VerifyAccessTokenAsync(Sign(key), cancellationToken: Ct);

        Assert.Equal("user-1", first.UserId);
        Assert.Equal("session-1", first.SessionId);
        Assert.Equal(new DateTimeOffset(expires, TimeSpan.Zero).ToUnixTimeSeconds(), first.ExpiresAt.ToUnixTimeSeconds());
        Assert.Equal("user-1", second.UserId);
        Assert.False(first.EmailVerified); // spec 0010 AC-14: a token without the claim reads as unverified
        Assert.True((await client.VerifyAccessTokenAsync(Sign(key, emailVerified: true), cancellationToken: Ct)).EmailVerified);
        Assert.False((await client.VerifyAccessTokenAsync(Sign(key, emailVerified: false), cancellationToken: Ct)).EmailVerified);
        var fetch = Assert.Single(server.Requests);
        Assert.Equal("/v1/projects/shop/.well-known/jwks.json", fetch.RequestUri!.AbsolutePath);
        Assert.Null(fetch.Headers.CacheControl);
    }

    [Fact]
    public async Task An_expired_token_is_token_expired_after_the_30_second_leeway()
    {
        var key = NewKey("k1");
        var server = new FakeServer().Then(() => Jwks(key));
        using var client = server.Client(o => o.Project = Project);

        var withinLeeway = await client.VerifyAccessTokenAsync(Sign(key, expires: DateTime.UtcNow.AddSeconds(-20)), cancellationToken: Ct);
        var expired = await Assert.ThrowsAsync<OrvanoException>(() =>
            client.VerifyAccessTokenAsync(Sign(key, expires: DateTime.UtcNow.AddSeconds(-40)), cancellationToken: Ct));

        Assert.Equal("user-1", withinLeeway.UserId);
        Assert.Equal((401, "token_expired"), (expired.Status, expired.Code));
    }

    [Fact]
    public async Task Refuses_another_project_issuer_algorithm_or_a_forgery_as_invalid_token()
    {
        var key = NewKey("k1");
        var server = new FakeServer().Then(() => Jwks(key));
        using var client = server.Client(o => o.Project = Project);
        var valid = Sign(key);
        var parts = valid.Split('.');
        var none = Encode("""{"alg":"none","typ":"JWT","kid":"k1"}""") + "." + parts[1] + ".";
        var hs256 = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Project,
            Subject = new ClaimsIdentity([new Claim("sub", "user-1"), new Claim("sid", "session-1")]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(new byte[32]) { KeyId = "k1" }, SecurityAlgorithms.HmacSha256),
        });

        foreach (var forged in new[]
        {
            Sign(key, audience: "blog"),
            Sign(key, issuer: Endpoint + "/v1/projects/blog"),
            Sign(key, issuer: "https://elsewhere.example.com/v1/projects/shop"),
            Sign(NewKey("k1")), // same kid, other key
            Sign(key, sid: null),
            none,
            hs256,
            parts[0] + "." + parts[1] + "." + parts[2][..^4] + "AAAA",
            "not a token",
        })
        {
            var error = await Assert.ThrowsAsync<OrvanoException>(() => client.VerifyAccessTokenAsync(forged, cancellationToken: Ct));
            Assert.Equal((401, "invalid_token"), (error.Status, error.Code));
        }
    }

    [Fact]
    public async Task An_unknown_kid_fetches_the_keys_again_with_no_cache_at_most_once_per_30_seconds()
    {
        var old = NewKey("old");
        var rotated = NewKey("new");
        var server = new FakeServer().Then(() => Jwks(old)).Then(() => Jwks(old, rotated));
        using var client = server.Client(o => o.Project = Project);

        await client.VerifyAccessTokenAsync(Sign(old), cancellationToken: Ct);
        var afterRotation = await client.VerifyAccessTokenAsync(Sign(rotated), cancellationToken: Ct);
        var unknown = await Assert.ThrowsAsync<OrvanoException>(() => client.VerifyAccessTokenAsync(Sign(NewKey("other")), cancellationToken: Ct));

        Assert.Equal("user-1", afterRotation.UserId);
        Assert.Equal("invalid_token", unknown.Code);
        Assert.Equal(2, server.Requests.Count); // the third kid is inside the 30 seconds
        Assert.True(server.Requests[1].Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task Online_asks_Orvano_as_the_user_without_the_API_key()
    {
        var key = NewKey("k1");
        var server = new FakeServer()
            .Then(() => Jwks(key))
            .Then(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(UserJson) })
            .Then(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"type":"about:blank","title":"Unauthorized","status":401,"code":"invalid_token","detail":"ended"}""", Encoding.UTF8, "application/problem+json"),
            });
        using var client = server.Client(o =>
        {
            o.Project = Project;
            o.ApiKey = "orv_sk_secret";
        });
        var token = Sign(key);

        var verified = await client.VerifyAccessTokenAsync(token, online: true, cancellationToken: Ct);
        var ended = await Assert.ThrowsAsync<OrvanoException>(() => client.VerifyAccessTokenAsync(token, online: true, cancellationToken: Ct));

        Assert.Equal("session-1", verified.SessionId);
        var check = server.Requests[1];
        Assert.Equal("/v1/account", check.RequestUri!.AbsolutePath);
        Assert.Equal(("Bearer", token), (check.Headers.Authorization?.Scheme, check.Headers.Authorization?.Parameter));
        Assert.Equal(Project, FakeServer.Header(check, "X-Orvano-Project"));
        Assert.Null(FakeServer.Header(check, "X-Orvano-Key"));
        Assert.Equal("invalid_token", ended.Code);
    }

    [Fact]
    public async Task Needs_a_project()
    {
        using var client = new FakeServer().Client();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VerifyAccessTokenAsync("x", cancellationToken: Ct));
    }

    private const string UserJson = """
        {"id":"user-1","email":null,"emailVerified":false,"name":null,"status":"active","metadata":{},"createdAt":"2026-01-01T00:00:00Z","lastSignInAt":null,"providers":[],"hasPassword":false}
        """;

    private static ECDsaSecurityKey NewKey(string kid) => new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = kid };

    // Spec 0013 AC-39: aal and amr from the token, missing ones read as 1 and empty; RequireMfa refuses a one factor
    // session with mfa_required before any online check.
    [Fact]
    public async Task Reads_aal_and_amr_and_RequireMfa_refuses_a_one_factor_session()
    {
        var key = NewKey("k1");
        var server = new FakeServer().Then(() => Jwks(key));
        using var client = server.Client(o => o.Project = Project);
        var strong = Sign(key, extra: new Dictionary<string, object> { ["aal"] = 2, ["amr"] = new[] { "mfa", "otp", "pwd" } });
        var weak = Sign(key, extra: new Dictionary<string, object> { ["aal"] = 1, ["amr"] = new[] { "pwd" } });
        var old = Sign(key);
        var junk = Sign(key, extra: new Dictionary<string, object> { ["aal"] = "two", ["amr"] = "pwd" });

        var verified = await client.VerifyAccessTokenAsync(strong, new VerifyAccessTokenOptions { RequireMfa = true }, Ct);

        Assert.Equal(2, verified.Aal);
        Assert.Equal(["mfa", "otp", "pwd"], verified.Amr);
        Assert.Equal(["pwd"], (await client.VerifyAccessTokenAsync(weak, cancellationToken: Ct)).Amr);
        var before = await client.VerifyAccessTokenAsync(old, cancellationToken: Ct);
        Assert.Equal((1, 0), (before.Aal, before.Amr.Count));
        var odd = await client.VerifyAccessTokenAsync(junk, cancellationToken: Ct);
        Assert.Equal((1, 0), (odd.Aal, odd.Amr.Count));
        foreach (var token in new[] { weak, old })
        {
            var refused = await Assert.ThrowsAsync<OrvanoException>(() =>
                client.VerifyAccessTokenAsync(token, new VerifyAccessTokenOptions { RequireMfa = true, Online = true }, Ct));
            Assert.Equal((403, "mfa_required"), (refused.Status, refused.Code));
        }

        Assert.Single(server.Requests);
    }

    // Spec 0014 AC-36: the is_anonymous claim, false when missing or not a boolean.
    [Fact]
    public async Task Reads_is_anonymous()
    {
        var key = NewKey("k1");
        var server = new FakeServer().Then(() => Jwks(key));
        using var client = server.Client(o => o.Project = Project);

        Assert.True((await client.VerifyAccessTokenAsync(Sign(key, extra: new Dictionary<string, object> { ["is_anonymous"] = true }), cancellationToken: Ct)).IsAnonymous);
        Assert.False((await client.VerifyAccessTokenAsync(Sign(key), cancellationToken: Ct)).IsAnonymous);
        Assert.False((await client.VerifyAccessTokenAsync(Sign(key, extra: new Dictionary<string, object> { ["is_anonymous"] = "yes" }), cancellationToken: Ct)).IsAnonymous);
    }

    private static string Sign(
        ECDsaSecurityKey key, string audience = Project, string issuer = Issuer, string? sid = "session-1", DateTime? expires = null, bool? emailVerified = null,
        IDictionary<string, object>? extra = null)
    {
        var claims = new List<Claim> { new("sub", "user-1") };
        if (sid is not null) claims.Add(new Claim("sid", sid));
        var exp = expires ?? DateTime.UtcNow.AddMinutes(15);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = emailVerified is null && extra is null
                ? null
                : new Dictionary<string, object>(extra ?? new Dictionary<string, object>()) { ["email_verified"] = emailVerified ?? false },
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = exp.AddMinutes(-15),
            NotBefore = exp.AddMinutes(-15),
            Expires = exp,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256),
        });
    }

    private static HttpResponseMessage Jwks(params ECDsaSecurityKey[] keys)
    {
        var jwks = string.Join(",", keys.Select(k =>
        {
            var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(k);
            return $$"""{"kty":"EC","crv":"P-256","x":"{{jwk.X}}","y":"{{jwk.Y}}","kid":"{{k.KeyId}}","alg":"ES256","use":"sig"}""";
        }));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"keys":[{{jwks}}]}""") };
    }

    private static string Encode(string json) => Base64UrlEncoder.Encode(json);
}
