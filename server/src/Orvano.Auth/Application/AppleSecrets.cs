using System.Collections.Concurrent;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orvano.Auth.Domain;
using Orvano.Core.Http;

namespace Orvano.Auth.Application;

/// <summary>
/// Apple's client secret, made by Orvano (spec 0012, AC-3): an ES256 JWT with header <c>kid</c> = the key ID and claims
/// <c>iss</c> = team ID, <c>sub</c> = the client ID it is used for, <c>aud</c> = Apple, <c>iat</c> = now, and
/// <c>exp</c> = now plus 1 hour, signed with the decrypted <c>.p8</c> key. Cached in memory for 50 minutes per project,
/// client ID, key ID, and the settings row's <c>updated_at</c>, so another instance never signs with a replaced key;
/// this instance's settings changes evict the project at once.
/// </summary>
internal sealed class AppleSecrets(TimeProvider clock)
{
    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    private readonly ConcurrentDictionary<(string ProjectId, string ClientId, string KeyId, DateTimeOffset UpdatedAt), (string Jwt, DateTimeOffset MadeAt)> _cache = new();

    /// <summary>
    /// The secret for <paramref name="clientId"/>; <paramref name="openKey"/> decrypts the private key only on a cache
    /// miss. Throws when the settings lack the Apple key.
    /// </summary>
    public string For(StoredProvider apple, string clientId, Func<string> openKey)
    {
        var config = apple.Config;
        if (config.AppleTeamId is not { } team || config.AppleKeyId is not { } keyId || !config.ApplePrivateKeySet || apple.UpdatedAt is not { } updatedAt)
            throw new InvalidOperationException("The project's Apple settings have no key.");

        var key = (apple.ProjectId, clientId, keyId, updatedAt);
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && now - cached.MadeAt < AuthTimings.AppleClientSecretCache) return cached.Jwt;

        using var ecdsa = AppleKey.Load(openKey());
        // No cached signature provider: it would hold this key after it is disposed.
        var signingKey = new ECDsaSecurityKey(ecdsa) { KeyId = keyId, CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };
        var jwt = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = team,
            Audience = ProviderCatalog.AppleAudience,
            IssuedAt = now.UtcDateTime,
            Expires = (now + AuthTimings.AppleClientSecret).UtcDateTime,
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = clientId },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256),
        });
        // Drop what no longer serves: this key's older settings, and anything past the reuse window.
        foreach (var old in _cache.Where(e => now - e.Value.MadeAt >= AuthTimings.AppleClientSecretCache
                     || (e.Key.ProjectId == apple.ProjectId && e.Key.ClientId == clientId && e.Key.UpdatedAt < updatedAt)).Select(e => e.Key))
            _cache.TryRemove(old, out _);
        _cache[key] = (jwt, now);
        return jwt;
    }

    /// <summary>How many secrets are cached; for tests.</summary>
    internal int CachedCount => _cache.Count;

    /// <summary>Forgets every secret made for the project, after its Apple settings changed or went.</summary>
    public void Evict(string projectId)
    {
        foreach (var key in _cache.Keys.Where(k => k.ProjectId == projectId)) _cache.TryRemove(key, out _);
    }
}

/// <summary>The provider callback URL (spec 0012, AC-5): <c>&lt;ORVANO_PUBLIC_URL&gt;/v1/projects/{projectId}/oauth/{provider}/callback</c>.</summary>
internal sealed class OAuthCallbacks(PublicUrl publicUrl)
{
    /// <summary>The route the callback endpoints map, under <c>/v1</c>.</summary>
    public const string Route = "/projects/{projectId}/oauth/{provider}/callback";

    public string UrlFor(string projectId, OAuthProvider provider) =>
        $"{publicUrl.Origin}/v1/projects/{Uri.EscapeDataString(projectId)}/oauth/{OAuthProviders.Wire(provider)}/callback";
}
