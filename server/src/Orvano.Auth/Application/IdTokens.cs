using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// Each provider's discovery document and signing keys (spec 0012, AC-8): one <see cref="ConfigurationManager{T}"/>
/// per discovery URL, shared across projects, fetched through the <c>oauth</c> client, cached 12 hours, and fetched
/// again at most once per 5 minutes for an unknown <c>kid</c>.
/// </summary>
internal sealed class ProviderKeys(IHttpClientFactory httpFactory, ProviderCatalog catalog)
{
    private readonly ConcurrentDictionary<Uri, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new();

    /// <summary>
    /// The provider's keys for <paramref name="kid"/>: the cached set, or a fresh one when the cache lacks the
    /// <c>kid</c> and the last fetch is old enough. Throws <see cref="ProviderCallException"/> when they can't be read.
    /// </summary>
    public async Task<IReadOnlyList<SecurityKey>> GetAsync(Uri discovery, string? kid, CancellationToken ct)
    {
        var manager = _managers.GetOrAdd(discovery, Create);
        var config = await ReadAsync(manager, ct);
        if (kid is not null && !config.SigningKeys.Any(k => k.KeyId == kid))
        {
            manager.RequestRefresh();
            config = await ReadAsync(manager, ct);
        }

        return [.. config.SigningKeys];
    }

    private ConfigurationManager<OpenIdConnectConfiguration> Create(Uri discovery) =>
        new(discovery.AbsoluteUri, new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(httpFactory.CreateClient(OAuthHttp.ClientName)) { RequireHttps = !catalog.IsFake })
        {
            AutomaticRefreshInterval = AuthTimings.ProviderKeysCache,
            RefreshInterval = AuthTimings.ProviderKeysRefresh,
        };

    private static async Task<OpenIdConnectConfiguration> ReadAsync(ConfigurationManager<OpenIdConnectConfiguration> manager, CancellationToken ct)
    {
        try
        {
            return await manager.GetConfigurationAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            throw new ProviderCallException(
                OAuthHttp.IsUnavailable(ex) ? ProviderFailure.Unavailable : ProviderFailure.Error, "The provider's discovery document or keys could not be read");
        }
    }
}

/// <summary>The checked payload of a provider ID token, and when it expires.</summary>
internal sealed record CheckedIdToken(JsonElement Payload, DateTimeOffset ExpiresAt, string Audience);

/// <summary>
/// Checks provider ID tokens by AC-8's rules, for the callback and for native sign in: a compact JWS with <c>alg</c>
/// pinned to RS256 and a <c>kid</c> in the provider's keys, the provider's issuer, an allowed audience, <c>exp</c> not
/// passed and <c>iat</c> not in the future (30 seconds of leeway each), a <c>sub</c>, and the expected nonce.
/// </summary>
internal sealed class IdTokens(ProviderKeys keys, ProviderCatalog catalog, TimeProvider clock)
{
    private static readonly JsonWebTokenHandler Handler = new() { MapInboundClaims = false };

    /// <summary>
    /// The checked token, or null when it fails any rule. The SHA-256 of the token's <c>nonce</c> claim must equal
    /// <paramref name="expectedNonceHash"/>, compared in fixed time; only Apple's second token, from the native code
    /// exchange, is checked without it (<paramref name="requireNonce"/> false), since it is matched to the first by <c>sub</c>. Throws <see cref="ProviderCallException"/> only when the
    /// provider's keys can't be read.
    /// </summary>
    public async Task<CheckedIdToken?> CheckAsync(
        OAuthProvider provider, string? microsoftTenant, string token, IReadOnlyCollection<string> audiences, byte[] expectedNonceHash, CancellationToken ct,
        bool requireNonce = true)
    {
        JsonWebToken jwt;
        try
        {
            jwt = Handler.ReadJsonWebToken(token);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (jwt.Alg != SecurityAlgorithms.RsaSha256 || string.IsNullOrEmpty(jwt.Kid) || audiences.Count == 0) return null;

        var endpoints = catalog.For(provider, microsoftTenant);
        var signingKeys = await keys.GetAsync(endpoints.Discovery!, jwt.Kid, ct);
        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = signingKeys.Where(k => k.KeyId == jwt.Kid),
            TryAllIssuerSigningKeys = false,
            RequireSignedTokens = true,
            ValidAudiences = audiences,
            ValidateAudience = true,
            // The issuer and the times are checked below: Microsoft's issuer depends on the token's tid, and the times
            // use the injected clock.
            ValidateIssuer = false,
            ValidateLifetime = false,
            RequireExpirationTime = true,
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken valid) return null;

        using var doc = JsonDocument.Parse(Base64UrlEncoder.Decode(valid.EncodedPayload));
        var payload = doc.RootElement.Clone();
        var now = clock.GetUtcNow();
        if (!TryTime(payload, "exp", out var exp) || now > exp + AuthTimings.ClockLeeway) return null;
        if (!TryTime(payload, "iat", out var iat) || iat > now + AuthTimings.ClockLeeway) return null;
        if (string.IsNullOrEmpty(ProviderClaims.Text(payload, "sub"))) return null;
        if (!catalog.IssuerMatches(provider, microsoftTenant, ProviderClaims.Text(payload, "iss"), ProviderClaims.Text(payload, "tid"))) return null;
        if (requireNonce && (ProviderClaims.Text(payload, "nonce") is not { } nonce
            || !CryptographicOperations.FixedTimeEquals(FlowSecret.Hash(nonce), expectedNonceHash))) return null;

        var audience = audiences.First(a => valid.Audiences.Contains(a, StringComparer.Ordinal));
        return new CheckedIdToken(payload, exp, audience);
    }

    private static bool TryTime(JsonElement payload, string claim, out DateTimeOffset time)
    {
        time = default;
        if (!payload.TryGetProperty(claim, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var seconds)) return false;
        if (seconds is < 0 or > 253402300799) return false;
        time = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }
}
