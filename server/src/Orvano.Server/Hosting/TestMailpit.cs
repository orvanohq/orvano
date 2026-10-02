namespace Orvano.Server.Hosting;

/// <summary>
/// <c>ORVANO_TEST_MAILPIT_URL</c> (spec 0010): the API base URL of the Mailpit that catches the <c>Test</c>
/// environment's emails, which <c>test.getLatestEmail</c> reads. Allowed only in <c>Test</c>, and only as an absolute
/// http or https URL; the role refuses to start otherwise.
/// </summary>
/// <param name="Url">The base URL, or null when unset.</param>
/// <param name="Problem">Why the setting can't be used; the role refuses to start when set.</param>
internal sealed record TestMailpit(Uri? Url, string? Problem = null)
{
    public const string Setting = "ORVANO_TEST_MAILPIT_URL";

    /// <summary>Reads the setting. Never throws; a bad value becomes <see cref="Problem"/>.</summary>
    public static TestMailpit Load(IHostEnvironment environment, IConfiguration config)
    {
        var value = config[Setting];
        if (string.IsNullOrEmpty(value)) return new TestMailpit(Url: null);
        if (!environment.IsEnvironment(OrvanoEnvironments.Test))
            return new TestMailpit(null, $"{Setting} is set but the environment is {environment.EnvironmentName}; it is only allowed in {OrvanoEnvironments.Test}");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0)
            return new TestMailpit(null, $"{Setting} must be an absolute http or https URL with no user info");
        return new TestMailpit(url);
    }
}
