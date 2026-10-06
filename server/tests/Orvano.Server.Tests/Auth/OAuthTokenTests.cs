using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0012's token rules with no database: Apple's client secret JWT and its cache (AC-3), and the ID token checks
// shared by the callback and native sign in, over a stub provider that serves discovery and keys (AC-8).
public class OAuthTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FakeBase = new("http://provider.test/oauth");
    private const string GoogleIssuer = "http://provider.test/oauth/google";
    private const string Nonce = "nonce-0123456789abcdef";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // AC-3: Apple's client secret.

    [Fact]
    public void A_new_secret_drops_the_ones_for_older_settings_and_the_ones_past_the_reuse_window()
    {
        var clock = new ManualClock(Now);
        var secrets = new AppleSecrets(clock);

        secrets.For(Apple(updatedAt: Now.AddDays(-2)), "com.acme.app", () => AuthApi.ApplePrivateKey);
        secrets.For(Apple(updatedAt: Now.AddDays(-1)), "com.acme.app", () => AuthApi.ApplePrivateKey);
        Assert.Equal(1, secrets.CachedCount);

        clock.Advance(TimeSpan.FromMinutes(51));
        secrets.For(Apple(updatedAt: Now.AddDays(-1)), "com.acme.web", () => AuthApi.ApplePrivateKey);
        Assert.Equal(1, secrets.CachedCount);
    }

    [Fact]
    public async Task Apples_client_secret_is_an_es256_jwt_for_the_client_id_that_lasts_one_hour()
    {
        var secrets = new AppleSecrets(new ManualClock(Now));

        var jwt = secrets.For(Apple(), "com.acme.app", () => AuthApi.ApplePrivateKey);

        var token = new JsonWebToken(jwt);
        Assert.Equal("ES256", token.Alg);
        Assert.Equal("KEY1234567", token.Kid);
        Assert.Equal("TEAM123456", token.Issuer);
        Assert.Equal("com.acme.app", token.Subject);
        Assert.Equal(["https://appleid.apple.com"], token.Audiences);
        Assert.Equal(Now.ToUnixTimeSeconds(), token.GetPayloadValue<long>("iat"));
        Assert.Equal((Now + TimeSpan.FromHours(1)).ToUnixTimeSeconds(), token.GetPayloadValue<long>("exp"));

        using var key = ECDsa.Create();
        key.ImportFromPem(AuthApi.ApplePrivateKey);
        var verified = await new JsonWebTokenHandler().ValidateTokenAsync(jwt, new TokenValidationParameters
        {
            IssuerSigningKey = new ECDsaSecurityKey(ECDsa.Create(key.ExportParameters(includePrivateParameters: false))),
            ValidIssuer = "TEAM123456",
            ValidAudience = "https://appleid.apple.com",
            ValidateLifetime = false,
        });
        Assert.True(verified.IsValid, verified.Exception?.Message);
    }

    [Fact]
    public void Apples_client_secret_is_reused_for_50_minutes_and_opens_the_key_only_to_make_one()
    {
        var clock = new ManualClock(Now);
        var secrets = new AppleSecrets(clock);
        var opened = 0;
        string Open()
        {
            opened++;
            return AuthApi.ApplePrivateKey;
        }

        var first = secrets.For(Apple(), "com.acme.app", Open);
        clock.Advance(TimeSpan.FromMinutes(49));
        var cached = secrets.For(Apple(), "com.acme.app", Open);
        clock.Advance(TimeSpan.FromMinutes(1));
        var renewed = secrets.For(Apple(), "com.acme.app", Open);

        Assert.Equal(first, cached);
        Assert.NotEqual(first, renewed);
        Assert.Equal(2, opened);
    }

    [Fact]
    public void Apples_client_secret_is_cached_per_client_id_and_settings_version_and_evicted_per_project()
    {
        var secrets = new AppleSecrets(new ManualClock(Now));
        var opened = 0;
        string Open()
        {
            opened++;
            return AuthApi.ApplePrivateKey;
        }

        var bundle = secrets.For(Apple(), "com.acme.app", Open);
        var services = secrets.For(Apple(), "com.acme.app.web", Open);
        Assert.Equal("com.acme.app.web", new JsonWebToken(services).Subject);
        Assert.Equal(2, opened);

        // Another instance replaced the key: the row's updated_at moved, so the cached secret is not used.
        var replaced = secrets.For(Apple(updatedAt: Now.AddSeconds(1)), "com.acme.app", Open);
        Assert.NotEqual(bundle, replaced);
        Assert.Equal(3, opened);

        // Another project's eviction leaves this one cached; its own eviction does not.
        secrets.Evict("someotherproject");
        Assert.Equal(replaced, secrets.For(Apple(updatedAt: Now.AddSeconds(1)), "com.acme.app", Open));
        Assert.Equal(3, opened);
        secrets.Evict(AuthApi.Project);
        secrets.For(Apple(updatedAt: Now.AddSeconds(1)), "com.acme.app", Open);
        Assert.Equal(4, opened);
    }

    [Fact]
    public void Apples_client_secret_is_refused_without_a_key_and_never_opens_one()
    {
        var secrets = new AppleSecrets(new ManualClock(Now));
        var noKey = Apple() with { Config = Apple().Config with { ApplePrivateKeySet = false } };
        var noTeam = Apple() with { Config = Apple().Config with { AppleTeamId = null } };

        Assert.Throws<InvalidOperationException>(() => secrets.For(noKey, "com.acme.app", () => throw new InvalidOperationException("opened")));
        Assert.Throws<InvalidOperationException>(() => secrets.For(noTeam, "com.acme.app", () => throw new InvalidOperationException("opened")));
        Assert.Throws<InvalidOperationException>(() => secrets.For(Apple() with { UpdatedAt = null }, "com.acme.app", () => throw new InvalidOperationException("opened")));
    }

    // AC-8: ID token checks.

    [Fact]
    public async Task A_provider_signed_token_passes_and_names_the_audience_it_matched()
    {
        using var provider = new StubProvider();
        var token = provider.Mint(aud: "ios-client");

        var checkedToken = await provider.CheckAsync(token, audiences: ["web-client", "ios-client"]);

        Assert.NotNull(checkedToken);
        Assert.Equal("ios-client", checkedToken.Audience);
        Assert.Equal(Now.AddMinutes(10), checkedToken.ExpiresAt);
        Assert.Equal("user-1", checkedToken.Payload.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task Only_rs256_with_a_known_kid_is_accepted()
    {
        using var provider = new StubProvider();
        using var stranger = RSA.Create(2048);

        Assert.Null(await provider.CheckAsync(provider.Mint(algorithm: SecurityAlgorithms.RsaSha384)));
        Assert.Null(await provider.CheckAsync(provider.Mint(signingKey: new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "k1" }, algorithm: SecurityAlgorithms.HmacSha256)));
        Assert.Null(await provider.CheckAsync(Unsigned(new { alg = "none", kid = "k1" }, Claims())));
        Assert.Null(await provider.CheckAsync(provider.Mint(signingKey: new RsaSecurityKey(provider.Key))));
        // Right kid, wrong key: the signature fails.
        Assert.Null(await provider.CheckAsync(provider.Mint(signingKey: new RsaSecurityKey(stranger) { KeyId = "k1" })));
        Assert.Null(await provider.CheckAsync("not-a-jwt"));
    }

    [Theory]
    [InlineData(-29, 0, true)]
    [InlineData(-31, 0, false)]
    [InlineData(600, 29, true)]
    [InlineData(600, 31, false)]
    public async Task Exp_and_iat_get_30_seconds_of_leeway(int expiresInSeconds, int issuedInSeconds, bool accepted)
    {
        using var provider = new StubProvider();
        var token = provider.Mint(issuedAt: Now.AddSeconds(issuedInSeconds), expires: Now.AddSeconds(expiresInSeconds));

        var checkedToken = await provider.CheckAsync(token);

        Assert.Equal(accepted, checkedToken is not null);
    }

    [Fact]
    public async Task The_issuer_audience_subject_and_times_must_all_be_present_and_right()
    {
        using var provider = new StubProvider();

        Assert.Null(await provider.CheckAsync(provider.Mint(issuer: "https://appleid.apple.com")));
        Assert.Null(await provider.CheckAsync(provider.Mint(aud: "someone-elses-client")));
        Assert.Null(await provider.CheckAsync(provider.Mint(), audiences: []));
        Assert.Null(await provider.CheckAsync(provider.Mint(sub: null)));
        Assert.Null(await provider.CheckAsync(provider.Mint(sub: "")));
        Assert.Null(await provider.CheckAsync(provider.Mint(timesUnset: true, issuedAt: Now)));
        Assert.Null(await provider.CheckAsync(provider.Mint(timesUnset: true, expires: Now.AddMinutes(10))));
    }

    [Fact]
    public async Task The_nonce_must_hash_to_the_expected_value_unless_the_caller_waives_it()
    {
        using var provider = new StubProvider();

        Assert.Null(await provider.CheckAsync(provider.Mint(nonce: "another-nonce-0123456789")));
        Assert.Null(await provider.CheckAsync(provider.Mint(nonce: null)));
        // Apple's second token, from the native code exchange, is matched by sub instead.
        Assert.NotNull(await provider.CheckAsync(provider.Mint(nonce: null), requireNonce: false));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Keys_that_cant_be_read_throw_with_the_failure_kind(HttpStatusCode status, bool unavailable)
    {
        using var provider = new StubProvider { DiscoveryStatus = status };

        var thrown = await Assert.ThrowsAsync<ProviderCallException>(() => provider.CheckAsync(provider.Mint()));

        Assert.Equal(unavailable ? ProviderFailure.Unavailable : ProviderFailure.Error, thrown.Failure);
    }

    [Fact]
    public async Task A_token_signed_with_a_newly_published_key_is_accepted_after_one_refetch()
    {
        using var provider = new StubProvider();
        Assert.NotNull(await provider.CheckAsync(provider.Mint()));
        using var rotated = RSA.Create(2048);
        provider.Publish(rotated, "k2");

        var checkedToken = await provider.CheckAsync(provider.Mint(signingKey: new RsaSecurityKey(rotated) { KeyId = "k2" }));

        Assert.NotNull(checkedToken);
        Assert.Equal(2, provider.JwksFetches);
    }

    [Fact]
    public async Task An_unknown_kid_fetches_the_keys_again_at_most_once_per_5_minutes()
    {
        using var provider = new StubProvider();
        using var stranger = RSA.Create(2048);
        var unknown = provider.Mint(signingKey: new RsaSecurityKey(stranger) { KeyId = "k9" });
        Assert.NotNull(await provider.CheckAsync(provider.Mint()));

        Assert.Null(await provider.CheckAsync(unknown));
        Assert.Null(await provider.CheckAsync(unknown));
        provider.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Null(await provider.CheckAsync(unknown));
        Assert.Equal(2, provider.JwksFetches);

        provider.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await provider.CheckAsync(unknown));
        Assert.Equal(3, provider.JwksFetches);
    }

    [Fact]
    public async Task A_failed_first_fetch_fails_fast_for_30_seconds_instead_of_fetching_for_every_caller()
    {
        using var provider = new StubProvider { DiscoveryStatus = HttpStatusCode.ServiceUnavailable };

        await Assert.ThrowsAsync<ProviderCallException>(() => provider.CheckAsync(provider.Mint()));
        var afterFirst = provider.DiscoveryFetches;
        var second = await Assert.ThrowsAsync<ProviderCallException>(() => provider.CheckAsync(provider.Mint()));
        Assert.Equal(ProviderFailure.Unavailable, second.Failure);
        Assert.Equal(afterFirst, provider.DiscoveryFetches);

        provider.DiscoveryStatus = HttpStatusCode.OK;
        provider.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.NotNull(await provider.CheckAsync(provider.Mint()));
        Assert.True(provider.DiscoveryFetches > afterFirst);
    }

    [Fact]
    public async Task A_failed_refresh_for_an_unknown_kid_is_tried_again_after_30_seconds()
    {
        using var provider = new StubProvider();
        Assert.NotNull(await provider.CheckAsync(provider.Mint()));
        using var rotated = RSA.Create(2048);
        provider.Publish(rotated, "k2");
        var rotatedToken = provider.Mint(signingKey: new RsaSecurityKey(rotated) { KeyId = "k2" });

        provider.DiscoveryStatus = HttpStatusCode.ServiceUnavailable;
        Assert.Null(await provider.CheckAsync(rotatedToken));
        provider.DiscoveryStatus = HttpStatusCode.OK;
        Assert.Null(await provider.CheckAsync(rotatedToken));

        provider.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.NotNull(await provider.CheckAsync(rotatedToken));
    }

    [Fact]
    public async Task Known_keys_are_kept_12_hours_then_fetched_again()
    {
        using var provider = new StubProvider();
        Assert.NotNull(await provider.CheckAsync(provider.Mint()));

        // The token's times don't matter here, only when the keys are fetched.
        provider.Clock.Advance(TimeSpan.FromHours(12) - TimeSpan.FromSeconds(1));
        await provider.CheckAsync(provider.Mint());
        Assert.Equal(1, provider.JwksFetches);
        provider.Clock.Advance(TimeSpan.FromSeconds(1));
        await provider.CheckAsync(provider.Mint());

        Assert.Equal(2, provider.JwksFetches);
    }

    [Fact]
    public async Task A_failed_fetch_after_the_first_keeps_the_cached_keys()
    {
        using var provider = new StubProvider();
        Assert.NotNull(await provider.CheckAsync(provider.Mint()));
        provider.Clock.Advance(TimeSpan.FromHours(13));
        provider.DiscoveryStatus = HttpStatusCode.ServiceUnavailable;

        var checkedToken = await provider.CheckAsync(provider.Mint(issuedAt: provider.Clock.Now, expires: provider.Clock.Now.AddMinutes(10)));

        Assert.NotNull(checkedToken);
        Assert.Equal(1, provider.JwksFetches);
    }

    private static StoredProvider Apple(DateTimeOffset? updatedAt = null) => new(
        AuthApi.Project,
        ProviderConfig.Empty(OAuthProvider.Apple) with
        {
            Enabled = true,
            ClientId = "com.acme.app.web",
            ClientIdsExtra = ["com.acme.app"],
            AppleTeamId = "TEAM123456",
            AppleKeyId = "KEY1234567",
            ApplePrivateKeySet = true,
        },
        null,
        [1, 2, 3],
        updatedAt ?? Now.AddDays(-1));

    private static Dictionary<string, object> Claims() => new()
    {
        ["iss"] = GoogleIssuer,
        ["aud"] = "web-client",
        ["sub"] = "user-1",
        ["nonce"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Nonce))),
        ["iat"] = Now.ToUnixTimeSeconds(),
        ["exp"] = Now.AddMinutes(10).ToUnixTimeSeconds(),
    };

    private static string Unsigned(object header, object payload) =>
        $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload))}.";

    /// <summary>
    /// A Google stand in under <see cref="FakeBase"/>: its discovery document and JWKS come from an in memory handler
    /// that counts the key fetches, and it mints tokens with its own RSA key (<c>k1</c>).
    /// </summary>
    private sealed class StubProvider : IDisposable
    {
        private readonly ConcurrentDictionary<string, RSA> _published = new();
        private readonly ProviderCatalog _catalog = new(FakeBase);
        private readonly IdTokens _idTokens;
        private int _jwksFetches;
        private int _discoveryFetches;

        public StubProvider()
        {
            _published["k1"] = Key;
            var http = new StubHttpFactory(new Handler(this));
            _idTokens = new IdTokens(new ProviderKeys(http, _catalog, Clock, NullLogger<ProviderKeys>.Instance), _catalog, Clock);
        }

        public RSA Key { get; } = RSA.Create(2048);

        /// <summary>The clock of both the key cache and the token checks; tokens are minted around <see cref="Now"/>.</summary>
        public ManualClock Clock { get; } = new(Now);

        public HttpStatusCode DiscoveryStatus { get; set; } = HttpStatusCode.OK;

        public int JwksFetches => _jwksFetches;

        public int DiscoveryFetches => _discoveryFetches;

        public void Publish(RSA key, string kid) => _published[kid] = key;

        public string Mint(
            string? aud = "web-client", string? sub = "user-1", string? issuer = GoogleIssuer, string? nonce = Nonce,
            DateTimeOffset? issuedAt = null, DateTimeOffset? expires = null, SecurityKey? signingKey = null,
            string algorithm = SecurityAlgorithms.RsaSha256, bool timesUnset = false)
        {
            var claims = new Dictionary<string, object>();
            if (sub is not null) claims["sub"] = sub;
            if (nonce is not null) claims["nonce"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nonce)));
            // timesUnset leaves out whichever of iat and exp is not given.
            if (issuedAt != null || !timesUnset) claims["iat"] = (issuedAt ?? Now).ToUnixTimeSeconds();
            if (expires != null || !timesUnset) claims["exp"] = (expires ?? Now.AddMinutes(10)).ToUnixTimeSeconds();
            return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = aud,
                Claims = claims,
                SigningCredentials = new SigningCredentials(signingKey ?? new RsaSecurityKey(Key) { KeyId = "k1" }, algorithm),
            });
        }

        public Task<CheckedIdToken?> CheckAsync(string token, IReadOnlyCollection<string>? audiences = null, bool requireNonce = true) =>
            _idTokens.CheckAsync(OAuthProvider.Google, null, token, audiences ?? ["web-client"], FlowSecret.Hash(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Nonce)))), Ct, requireNonce);

        public void Dispose() => Key.Dispose();

        private string Jwks() => JsonSerializer.Serialize(new
        {
            keys = _published.Select(entry =>
            {
                var p = entry.Value.ExportParameters(includePrivateParameters: false);
                return new { kty = "RSA", use = "sig", alg = "RS256", kid = entry.Key, n = Base64Url.EncodeToString(p.Modulus), e = Base64Url.EncodeToString(p.Exponent) };
            }),
        });

        private sealed class Handler(StubProvider provider) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref provider._discoveryFetches);
                    if (provider.DiscoveryStatus != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(provider.DiscoveryStatus));
                    return Json(new { issuer = GoogleIssuer, jwks_uri = $"{GoogleIssuer}/jwks", authorization_endpoint = $"{GoogleIssuer}/authorize", token_endpoint = $"{GoogleIssuer}/token" });
                }

                if (path.EndsWith("/jwks", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref provider._jwksFetches);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(provider.Jwks(), Encoding.UTF8, "application/json") });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private static Task<HttpResponseMessage> Json(object body) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class StubHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
