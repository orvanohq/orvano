using System.Net;
using System.Net.Sockets;

namespace Orvano.Messaging.Domain;

/// <summary>
/// The addresses a project's SMTP server may have (spec 0009, AC-3): global unicast only, so a project can never
/// point the server at itself, the private network, or a cloud metadata address.
/// </summary>
internal static class GlobalUnicast
{
    private static readonly IPNetwork[] Refused =
    [
        .. new[]
        {
            "0.0.0.0/8",         // "this network", and the unspecified address
            "10.0.0.0/8",        // private
            "100.64.0.0/10",     // shared (carrier grade NAT)
            "127.0.0.0/8",       // loopback
            "169.254.0.0/16",    // link local, where cloud metadata lives
            "172.16.0.0/12",     // private
            "192.0.2.0/24",      // documentation
            "192.168.0.0/16",    // private
            "198.18.0.0/15",     // benchmarking
            "198.51.100.0/24",   // documentation
            "203.0.113.0/24",    // documentation
            "224.0.0.0/4",       // multicast
            "240.0.0.0/4",       // reserved, and the broadcast address
            "::/96",             // unspecified, loopback, and IPv4 compatible
            "64:ff9b::/96",      // NAT64, which reaches IPv4
            "2001::/32",         // Teredo
            "2001:db8::/32",     // documentation
            "2002::/16",         // 6to4, which embeds an IPv4 address
            "fc00::/7",          // unique local
            "fe80::/10",         // link local
            "ff00::/8",          // multicast
        }.Select(IPNetwork.Parse),
    ];

    /// <summary>True when <paramref name="address"/> is a global unicast address.</summary>
    public static bool Contains(IPAddress address)
    {
        // An IPv4 address mapped into IPv6 is judged as the IPv4 address it reaches.
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)) return false;
        foreach (var network in Refused)
        {
            if (network.Contains(address)) return false;
        }

        return true;
    }

    /// <summary>True when every one of <paramref name="addresses"/> is global unicast: one private address refuses the host.</summary>
    public static bool ContainsAll(IEnumerable<IPAddress> addresses) => addresses.All(Contains);
}
