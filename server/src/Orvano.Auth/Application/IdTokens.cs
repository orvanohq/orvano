using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// Each provider's discovery document and signing keys (spec 0012, AC-8): one cached set per discovery URL, shared
/// across projects, fetched through the <c>oauth</c> client, kept 12 hours, and fetched again at most once per 5
/// minutes for an unknown <c>kid</c>. Every fetch finishes before the keys are used, so a token signed with a key the
/// provider just published passes on its first try. (IdentityModel's <c>ConfigurationManager</c> refreshes in the
/// background once it holds a configuration, so that token was refused.)
/// </summary>
internal sealed class ProviderKeys(IHttpClientFactory httpFactory, ProviderCatalog catalog, TimeProvider clock, ILogger<ProviderKeys> logger)
{
    private readonly ConcurrentDictionary<Uri, Entry> _entries = new();

    /// <summary>
    /// The provider's keys for <paramref name="kid"/>: the cached set, or a fresh one when the cache is empty, older than
    /// 12 hours, or lacks the <c>kid</c> (at most once per 5 minutes). A failed fetch keeps the cached set. Throws
    /// <see cref="ProviderCallException"/> when there is no set yet and it can't be read.
    /// </summary>
    public async Task<IReadOnlyList<SecurityKey>> GetAsync(Uri discovery, string? kid, CancellationToken ct)
    {
        var entry = _entries.GetOrAdd(discovery, _ => new Entry());
        if (NeedsFetch(entry, kid))
        {
            await entry.Lock.WaitAsync(ct);
            try
            {
                // Another caller may have fetched while this one waited.
                if (NeedsFetch(entry, kid)) await FetchAsync(discovery, entry, kid, ct);
            }
            finally
            {
                entry.Lock.Release();
            }
        }

        // Nothing cached and the last try failed moments ago: fail fast instead of fetching again.
        if (entry.Config is not { } config) throw new ProviderCallException(ProviderFailure.Unavailable, "The provider's discovery document or keys could not be read");
        return [.. config.SigningKeys];
    }

    private bool NeedsFetch(Entry entry, string? kid)
    {
        var now = clock.GetUtcNow();
        if (entry.Config is not { } config) return now - entry.LastAttempt >= AuthTimings.ProviderKeysRetry;
        var expired = now - entry.FetchedAt >= AuthTimings.ProviderKeysCache && now - entry.LastAttempt >= AuthTimings.ProviderKeysRefresh;
        return expired || UnknownKidMayFetch(entry, config, kid, now);
    }

    private static bool UnknownKidMayFetch(Entry entry, OpenIdConnectConfiguration config, string? kid, DateTimeOffset now) =>
        kid is not null && !config.SigningKeys.Any(k => k.KeyId == kid) && now - entry.LastKidFetch >= AuthTimings.ProviderKeysRefresh;

    private async Task FetchAsync(Uri discovery, Entry entry, string? kid, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (entry.Config is { } current && UnknownKidMayFetch(entry, current, kid, now)) entry.LastKidFetch = now;
        entry.LastAttempt = now;
        try
        {
            var retriever = new HttpDocumentRetriever(httpFactory.CreateClient(OAuthHttp.ClientName)) { RequireHttps = !catalog.IsFake };
            entry.Config = await OpenIdConnectConfigurationRetriever.GetAsync(discovery.AbsoluteUri, retriever, ct);
            entry.FetchedAt = now;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var failure = OAuthHttp.IsUnavailable(ex) ? ProviderFailure.Unavailable : ProviderFailure.Error;
            if (entry.Config is null)
                throw new ProviderCallException(failure, "The provider's discovery document or keys could not be read");
            // A failed fetch for an unknown kid must not block the retry for the full window: try again soon.
            if (entry.LastKidFetch == now) entry.LastKidFetch = now - (AuthTimings.ProviderKeysRefresh - AuthTimings.ProviderKeysRetry);
            logger.LogWarning("The keys at {Discovery} could not be fetched again ({Failure}); the cached keys stay in use", discovery, failure);
        }
    }

    /// <summary>One discovery URL's keys; <see cref="Lock"/> lets one fetch run at a time.</summary>
    private sealed class Entry
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public OpenIdConnectConfiguration? Config { get; set; }

        public DateTimeOffset FetchedAt { get; set; }

        /// <summary>When any fetch last started; a failing refresh of expired keys waits 5 minutes between tries, a failing first fetch 30 seconds.</summary>
        public DateTimeOffset LastAttempt { get; set; } = DateTimeOffset.MinValue;

        /// <summary>When a fetch for an unknown <c>kid</c> last started; the first one never waits.</summary>
        public DateTimeOffset LastKidFetch { get; set; } = DateTimeOffset.MinValue;
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
