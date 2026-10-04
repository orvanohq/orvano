namespace Orvano.Auth.Domain;

/// <summary>A sign in provider (spec 0012, AC-16): the <c>provider</c> column of every OAuth table.</summary>
internal enum OAuthProvider
{
    Google,
    Apple,
    GitHub,
    Microsoft,
}

/// <summary>The wire names of <see cref="OAuthProvider"/> and what each one supports.</summary>
internal static class OAuthProviders
{
    public const string Google = "google";
    public const string Apple = "apple";
    public const string GitHub = "github";
    public const string Microsoft = "microsoft";

    /// <summary>Every provider, in the order the console lists them.</summary>
    public static IReadOnlyList<OAuthProvider> All { get; } = [OAuthProvider.Google, OAuthProvider.Apple, OAuthProvider.GitHub, OAuthProvider.Microsoft];

    /// <summary>The column value, the path segment, and the contract enum value.</summary>
    public static string Wire(OAuthProvider provider) => provider switch
    {
        OAuthProvider.Google => Google,
        OAuthProvider.Apple => Apple,
        OAuthProvider.GitHub => GitHub,
        OAuthProvider.Microsoft => Microsoft,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>Reads a wire name; false for anything else, case included.</summary>
    public static bool TryParse(string? value, out OAuthProvider provider)
    {
        switch (value)
        {
            case Google:
                provider = OAuthProvider.Google;
                return true;
            case Apple:
                provider = OAuthProvider.Apple;
                return true;
            case GitHub:
                provider = OAuthProvider.GitHub;
                return true;
            case Microsoft:
                provider = OAuthProvider.Microsoft;
                return true;
            default:
                provider = default;
                return false;
        }
    }

    /// <summary>Whether the provider signs an OpenID Connect ID token, so a flow carries a nonce (all but GitHub).</summary>
    public static bool IssuesIdToken(OAuthProvider provider) => provider != OAuthProvider.GitHub;

    /// <summary>Whether a native app can sign in with the provider's ID token (AC-9): Google and Apple.</summary>
    public static bool SupportsNative(OAuthProvider provider) => provider is OAuthProvider.Google or OAuthProvider.Apple;
}
