using Orvano.Auth;

namespace Orvano.Server.Hosting;

/// <summary>
/// <c>ORVANO_TEST_OAUTH_PROVIDER_URL</c> (spec 0012, AC-27): the base URL of the fake sign in provider the
/// <c>Test</c> environment serves under <c>/v1/test/oauth</c>; every provider endpoint, issuer, and the GitHub API
/// move there. Allowed only in <c>Test</c>, and only as an absolute http or https URL; the role refuses to start
/// otherwise.
/// </summary>
/// <param name="Url">The base URL, or null when unset.</param>
/// <param name="Problem">Why the setting can't be used; the role refuses to start when set.</param>
internal sealed record TestOAuthProvider(Uri? Url, string? Problem = null)
{
    public const string Setting = AuthModule.TestProviderSetting;

    /// <summary>Reads the setting. Never throws; a bad value becomes <see cref="Problem"/>.</summary>
    public static TestOAuthProvider Load(IHostEnvironment environment, IConfiguration config)
    {
        var value = config[Setting];
        if (string.IsNullOrEmpty(value)) return new TestOAuthProvider(Url: null);
        if (!environment.IsEnvironment(OrvanoEnvironments.Test))
            return new TestOAuthProvider(null, $"{Setting} is set but the environment is {environment.EnvironmentName}; it is only allowed in {OrvanoEnvironments.Test}");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0
            || url.Query.Length > 0 || url.Fragment.Length > 0)
        {
            return new TestOAuthProvider(null, $"{Setting} must be an absolute http or https URL with no user info, query, or fragment");
        }

        return new TestOAuthProvider(url);
    }
}
