using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Orvano.Messaging.Domain;

/// <summary>
/// The syntax of an SMTP host (spec 0009, AC-2): a host name (labels of letters, digits, and hyphens, 1 to 63
/// characters each, separated by dots, no trailing dot; international names as punycode), or an IPv4 or IPv6
/// literal (IPv6 without brackets). At most 253 characters.
/// </summary>
internal static partial class SmtpHost
{
    public const int MaxLength = 253;

    // An IPv4 literal is four labels of digits, so this covers it too.
    [GeneratedRegex(@"^[A-Za-z0-9-]{1,63}(\.[A-Za-z0-9-]{1,63})*$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();

    /// <summary>The host trimmed and lower cased, as stored and compared, when its syntax is valid.</summary>
    public static bool TryNormalize(string? host, out string normalized)
    {
        normalized = host?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length is 0 or > MaxLength) return false;
        return Name().IsMatch(normalized) || IsIpv6Literal(normalized);
    }

    /// <summary>
    /// The address a host names directly, when it is a literal. Anything the resolver would read as an address
    /// counts (<c>127.1</c> is loopback), so the private address rule can't be dodged with an odd spelling.
    /// </summary>
    public static bool TryGetLiteral(string host, out IPAddress address) => IPAddress.TryParse(host, out address!);

    // No brackets and no zone: '[::1]' and 'fe80::1%eth0' both parse as addresses in .NET.
    private static bool IsIpv6Literal(string host) =>
        host.Contains(':') && !host.Contains('[') && !host.Contains('%')
        && IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6;
}
