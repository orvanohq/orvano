using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Orvano.Core.Http;

/// <summary>
/// The address an auth limit counts by (spec 0014, AC-16): the connection IP after the trusted proxies, except that a
/// request from one of the project's trusted app servers names the visitor in <c>X-Orvano-Client-IP</c>. IPv4 mapped
/// addresses count as IPv4, and an IPv6 address counts by its <c>/64</c>, so one host's block is one address.
/// </summary>
public static partial class LimitIp
{
    /// <summary>
    /// The limit address: <c>X-Orvano-Client-IP</c> when the connection IP is inside <paramref name="trustedServers"/>
    /// and the header is exactly one address, else the connection IP; null when unknown (in process tests).
    /// </summary>
    public static IPAddress? Of(HttpContext http, IReadOnlyList<IPNetwork> trustedServers)
    {
        var connection = ConnectionIp.Of(http);
        if (connection is null || trustedServers.Count == 0) return connection;
        if (!trustedServers.Any(server => server.Contains(connection))) return connection;
        return http.Request.Headers[OrvanoHeaders.ClientIp] is [{ } header] && TryParseOne(header, out var visitor) ? visitor : connection;
    }

    /// <summary>The address as a key: IPv4 as is, IPv6 as its <c>/64</c> prefix, <c>unknown</c> when there is none.</summary>
    public static string Key(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    /// <summary>
    /// Exactly one IPv4 or IPv6 address, as AC-16 reads <c>X-Orvano-Client-IP</c>: no list, no port, no zone ID, no
    /// brackets, and IPv4 only in its four part dotted form. IPv4 mapped IPv6 becomes IPv4.
    /// </summary>
    public static bool TryParseOne(string? value, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrEmpty(value) || value.Length > 45) return false;
        var text = value.Trim();
        if (text.Contains(':', StringComparison.Ordinal))
        {
            if (!Ipv6Shape().IsMatch(text) || !IPAddress.TryParse(text, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6) return false;
            address = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
            return true;
        }

        if (!Ipv4Shape().IsMatch(text) || !IPAddress.TryParse(text, out var v4)) return false;
        address = v4;
        return true;
    }

    [GeneratedRegex(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4Shape();

    // Hex groups and colons, and an IPv4 tail; no brackets, zone ID, or port.
    [GeneratedRegex(@"^[0-9A-Fa-f:]+(\.\d{1,3}){0,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6Shape();
}
