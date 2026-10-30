using Microsoft.Extensions.Configuration;

namespace Orvano.SharedKernel.Configuration;

/// <summary>
/// Where operators reach the console: <c>ORVANO_PUBLIC_URL</c>, and the origin a state changing console request must
/// come from, <c>ORVANO_CONSOLE_ORIGIN</c> or else the public URL's origin (spec 0006 CON-2). <c>orvano init</c> writes
/// the public URL (spec 0007 INS-3); a server run outside an install may have neither.
/// </summary>
public static class ConsoleAddress
{
    public const string PublicUrlKey = "PUBLIC_URL";

    public const string ConsoleOriginKey = "CONSOLE_ORIGIN";

    /// <summary>The public URL without a trailing slash, or null when it is not set.</summary>
    /// <exception cref="ConfigurationException">It is set but is not an absolute <c>http</c> or <c>https</c> URL.</exception>
    public static string? PublicUrl(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Parse(configuration, PublicUrlKey)?.AbsoluteUri.TrimEnd('/');
    }

    /// <summary>
    /// The console origin (<c>scheme://host[:port]</c>, lower case, default port left out), or null when neither
    /// setting is set, in which case no request passes the CON-2 check.
    /// </summary>
    /// <exception cref="ConfigurationException">A setting is set but is not an absolute <c>http</c> or <c>https</c> URL.</exception>
    public static string? Origin(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Uri? url = Parse(configuration, ConsoleOriginKey) ?? Parse(configuration, PublicUrlKey);
        return url is null ? null : OriginOf(url);
    }

    /// <summary>The origin of <paramref name="url"/> in the form a browser sends in <c>Origin</c>.</summary>
    public static string OriginOf(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    private static Uri? Parse(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new ConfigurationException(
                $"{ConfigurationNames.ToEnvironmentVariable(key)} must be an absolute http or https URL, such as https://orvano.example.com.");
        }

        return url;
    }
}
