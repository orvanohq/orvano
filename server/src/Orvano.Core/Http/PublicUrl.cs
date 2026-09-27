using Microsoft.Extensions.Configuration;

namespace Orvano.Core.Http;

/// <summary>
/// <c>ORVANO_PUBLIC_URL</c> (spec 0002): the install's public base URL, an absolute <c>http</c> or <c>https</c> URL
/// with no path, query, or fragment. It forms the token issuer (spec 0004) and the origin the console CSRF rule
/// accepts.
/// </summary>
public sealed class PublicUrl
{
    /// <summary>The setting's name.</summary>
    public const string Setting = "ORVANO_PUBLIC_URL";

    private PublicUrl(string origin) => Origin = origin;

    /// <summary>The URL's origin, <c>scheme://host[:port]</c> with no trailing slash, for example <c>https://orvano.example.com</c>.</summary>
    public string Origin { get; }

    /// <summary>Reads and checks <c>ORVANO_PUBLIC_URL</c>.</summary>
    /// <exception cref="OrvanoConfigException">The setting is unset or not an origin.</exception>
    public static PublicUrl FromConfig(IConfiguration config) => Parse(OrvanoConfig.Required(config, Setting));

    /// <summary>Parses a setting value.</summary>
    /// <exception cref="OrvanoConfigException">The value is not an absolute http or https URL without path, query, or fragment.</exception>
    public static PublicUrl Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length > 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || value.TrimEnd().EndsWith('?')
            || value.TrimEnd().EndsWith('#'))
        {
            throw new OrvanoConfigException(
                $"{Setting} must be an absolute http or https URL with no path, query, or fragment, for example https://orvano.example.com, got '{value}'.");
        }

        return new PublicUrl(uri.GetLeftPart(UriPartial.Authority));
    }

    /// <inheritdoc />
    public override string ToString() => Origin;
}
