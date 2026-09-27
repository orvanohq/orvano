using System.Net;
using Microsoft.Extensions.Configuration;

namespace Orvano.Core.Http;

/// <summary>
/// <c>ORVANO_TRUSTED_PROXIES</c> (spec 0004): the peers whose <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> the api
/// believes. A comma separated list of CIDR ranges or single addresses, <c>private</c> (loopback and the private
/// ranges, the default, right for compose where only Caddy reaches the api), or <c>none</c>.
/// </summary>
public sealed class TrustedProxies
{
    /// <summary>The setting's name.</summary>
    public const string Setting = "ORVANO_TRUSTED_PROXIES";

    /// <summary>The ranges <c>private</c> stands for.</summary>
    public static IReadOnlyList<IPNetwork> PrivateRanges { get; } =
    [
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("fc00::/7"),
    ];

    private TrustedProxies(IReadOnlyList<IPNetwork> networks) => Networks = networks;

    /// <summary>The trusted ranges; empty for <c>none</c>.</summary>
    public IReadOnlyList<IPNetwork> Networks { get; }

    /// <summary>Reads and checks <c>ORVANO_TRUSTED_PROXIES</c>, <c>private</c> when unset.</summary>
    /// <exception cref="OrvanoConfigException">An entry is not a CIDR range or an address.</exception>
    public static TrustedProxies FromConfig(IConfiguration config) =>
        Parse(string.IsNullOrWhiteSpace(config[Setting]) ? "private" : config[Setting]!);

    /// <summary>Parses a setting value.</summary>
    /// <exception cref="OrvanoConfigException">An entry is not a CIDR range or an address.</exception>
    public static TrustedProxies Parse(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Equals("private", StringComparison.OrdinalIgnoreCase)) return new TrustedProxies(PrivateRanges);
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return new TrustedProxies([]);

        var networks = new List<IPNetwork>();
        foreach (var raw in trimmed.Split(','))
        {
            var entry = raw.Trim();
            if (IPNetwork.TryParse(entry, out var network))
            {
                networks.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var address))
            {
                networks.Add(new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
            }
            else
            {
                throw new OrvanoConfigException(
                    $"{Setting} must be 'private', 'none', or a comma separated list of CIDR ranges such as 172.18.0.0/16; '{entry}' is none of these.");
            }
        }

        return new TrustedProxies(networks);
    }
}
