using System.Net;
using System.Text.RegularExpressions;

namespace Orvano.Server.Install;

/// <summary>
/// The domain Orvano answers on and the public URL it gives (spec 0006, AC-5).
/// </summary>
internal static partial class DomainRule
{
    public const string Localhost = "localhost";

    /// <summary>
    /// True for exactly <c>localhost</c>, or a lowercase hostname with at least one dot, labels of
    /// 1 to 63 of <c>a-z0-9-</c>, at most 253 characters, and not an IP address.
    /// </summary>
    public static bool IsValid(string domain)
    {
        if (domain == Localhost) return true;
        if (domain.Length > 253 || !domain.Contains('.')) return false;
        if (IPAddress.TryParse(domain, out _)) return false;
        return domain.Split('.').All(label => Label().IsMatch(label)) && !AllNumeric().IsMatch(domain);
    }

    /// <summary><c>https://&lt;domain&gt;</c>, or <c>http://localhost</c>.</summary>
    public static string PublicUrl(string domain) => domain == Localhost ? "http://localhost" : $"https://{domain}";

    /// <summary>The host of an existing <c>ORVANO_PUBLIC_URL</c>, the default on a rerun.</summary>
    public static string? HostOf(string? publicUrl) =>
        Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : null;

    [GeneratedRegex("^[a-z0-9-]{1,63}$")]
    private static partial Regex Label();

    // Dotted numbers that IPAddress does not parse (for example 1.2.3.4.5) are not hostnames either.
    [GeneratedRegex(@"^[0-9.]+$")]
    private static partial Regex AllNumeric();
}
