namespace Orvano.Auth.Domain;

/// <summary>
/// Where one provider lives for one project (spec 0012, AC-4, AC-6, AC-8): its authorize and token endpoints, its
/// discovery document (null for GitHub), the GitHub API base, Apple's revoke endpoint, the scopes, and whether the
/// callback arrives as a form post.
/// </summary>
internal sealed record ProviderEndpoints(
    OAuthProvider Provider,
    Uri Authorize,
    Uri Token,
    Uri? Discovery,
    Uri? Api,
    Uri? Revoke,
    string Scopes,
    bool FormPost);

/// <summary>
/// The fixed endpoints of the four providers (spec 0012, value sourcing). In <c>Test</c>,
/// <c>ORVANO_TEST_OAUTH_PROVIDER_URL</c> moves every endpoint and issuer under one base URL, the fake provider
/// (AC-27); its paths are <c>&lt;base&gt;/&lt;provider&gt;/...</c>, Microsoft's also carrying the tenant.
/// </summary>
internal sealed class ProviderCatalog(Uri? testBase)
{
    /// <summary>The real providers.</summary>
    public static ProviderCatalog Real { get; } = new(null);

    /// <summary>Whether the endpoints are the <c>Test</c> fake's, which may be plain http.</summary>
    public bool IsFake => testBase is not null;

    /// <summary>The endpoints of <paramref name="provider"/>; <paramref name="microsoftTenant"/> is Microsoft's authority.</summary>
    public ProviderEndpoints For(OAuthProvider provider, string? microsoftTenant = null)
    {
        var tenant = MicrosoftTenant.OrDefault(microsoftTenant);
        if (testBase is { } fake)
        {
            var root = Fake(fake, OAuthProviders.Wire(provider));
            return provider switch
            {
                OAuthProvider.Google => new(provider, Join(root, "authorize"), Join(root, "token"), Join(root, ".well-known/openid-configuration"), null, null, Scopes(provider), false),
                OAuthProvider.Apple => new(provider, Join(root, "authorize"), Join(root, "token"), Join(root, ".well-known/openid-configuration"), null, Join(root, "revoke"), Scopes(provider), true),
                OAuthProvider.Microsoft => new(provider, Join(root, $"{tenant}/authorize"), Join(root, $"{tenant}/token"), Join(root, $"{tenant}/v2.0/.well-known/openid-configuration"), null, null, Scopes(provider), false),
                OAuthProvider.GitHub => new(provider, Join(root, "authorize"), Join(root, "token"), null, Join(root, "api/"), null, Scopes(provider), false),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
            };
        }

        return provider switch
        {
            OAuthProvider.Google => new(provider,
                new Uri("https://accounts.google.com/o/oauth2/v2/auth"),
                new Uri("https://oauth2.googleapis.com/token"),
                new Uri("https://accounts.google.com/.well-known/openid-configuration"),
                null, null, Scopes(provider), false),
            OAuthProvider.Apple => new(provider,
                new Uri("https://appleid.apple.com/auth/authorize"),
                new Uri("https://appleid.apple.com/auth/token"),
                new Uri("https://appleid.apple.com/.well-known/openid-configuration"),
                null, new Uri("https://appleid.apple.com/auth/revoke"), Scopes(provider), true),
            OAuthProvider.Microsoft => new(provider,
                new Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize"),
                new Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token"),
                new Uri($"https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration"),
                null, null, Scopes(provider), false),
            OAuthProvider.GitHub => new(provider,
                new Uri("https://github.com/login/oauth/authorize"),
                new Uri("https://github.com/login/oauth/access_token"),
                null, new Uri("https://api.github.com/"), null, Scopes(provider), false),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
    }

    /// <summary>
    /// Whether <paramref name="issuer"/> is the provider's (AC-8): Google's two spellings, Apple's, or Microsoft's
    /// issuer for the token's own <paramref name="tid"/>, which must also suit the configured tenant.
    /// </summary>
    public bool IssuerMatches(OAuthProvider provider, string? microsoftTenant, string? issuer, string? tid)
    {
        if (issuer is null) return false;
        if (provider == OAuthProvider.Microsoft)
        {
            if (tid is null || !Guid.TryParse(tid, out var tenantId) || !MicrosoftTenant.Admits(microsoftTenant, tenantId)) return false;
            var expected = testBase is { } fake
                ? $"{Fake(fake, OAuthProviders.Microsoft)}{tid}/v2.0"
                : $"https://login.microsoftonline.com/{tid}/v2.0";
            return string.Equals(issuer, expected, StringComparison.Ordinal);
        }

        if (testBase is { } testRoot)
            return string.Equals(issuer, Fake(testRoot, OAuthProviders.Wire(provider)).AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal);

        return provider switch
        {
            OAuthProvider.Google => issuer is "https://accounts.google.com" or "accounts.google.com",
            OAuthProvider.Apple => issuer == "https://appleid.apple.com",
            _ => false,
        };
    }

    /// <summary>The <c>aud</c> of Apple's client secret JWT (AC-3): always Apple's own issuer, also against the fake.</summary>
    public const string AppleAudience = "https://appleid.apple.com";

    private static string Scopes(OAuthProvider provider) => provider switch
    {
        OAuthProvider.Google or OAuthProvider.Microsoft => "openid email profile",
        OAuthProvider.Apple => "name email",
        OAuthProvider.GitHub => "read:user user:email",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    private static Uri Fake(Uri testRoot, string provider) => new(new Uri(testRoot.AbsoluteUri.TrimEnd('/') + "/"), provider + "/");

    private static Uri Join(Uri root, string path) => new(root, path);
}

/// <summary>
/// Microsoft's tenant setting (AC-1, AC-8): <c>common</c> (the default), <c>organizations</c>, <c>consumers</c>, or
/// one tenant's GUID; and which token tenants (<c>tid</c>) each admits.
/// </summary>
internal static class MicrosoftTenant
{
    public const string Common = "common";
    public const string Organizations = "organizations";
    public const string Consumers = "consumers";

    /// <summary>The tenant of every personal Microsoft account.</summary>
    public static readonly Guid ConsumersTenantId = Guid.Parse("9188040d-6c67-4c5b-b112-36a304b66dad");

    /// <summary>The stored value, or <c>common</c> when none is set.</summary>
    public static string OrDefault(string? tenant) => tenant ?? Common;

    /// <summary>One of the three names, or a GUID in its <c>D</c> form, stored lowercase; false for anything else.</summary>
    public static bool TryNormalize(string? tenant, out string normalized)
    {
        normalized = "";
        if (tenant is Common or Organizations or Consumers)
        {
            normalized = tenant;
            return true;
        }

        if (!Guid.TryParseExact(tenant, "D", out var id)) return false;
        normalized = id.ToString("D");
        return true;
    }

    /// <summary>Whether a token from tenant <paramref name="tid"/> belongs to the configured tenant.</summary>
    public static bool Admits(string? tenant, Guid tid) => OrDefault(tenant) switch
    {
        Common => true,
        Consumers => tid == ConsumersTenantId,
        Organizations => tid != ConsumersTenantId,
        var guid => Guid.TryParse(guid, out var only) && only == tid,
    };
}
