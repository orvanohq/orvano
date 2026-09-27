using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Orvano.Server.Install;

/// <summary>What the DNS check needs from the network (spec 0006, AC-6).</summary>
internal interface IInstallNetwork
{
    /// <summary>The domain's A and AAAA records; empty when it has none.</summary>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string domain);

    /// <summary>This server's interface addresses (the installer shares the host network).</summary>
    IReadOnlyList<IPAddress> InterfaceAddresses();

    /// <summary>This server's public addresses as seen from outside; each lookup may fail.</summary>
    Task<IReadOnlyList<IPAddress>> PublicAddressesAsync();
}

internal sealed class InstallNetwork : IInstallNetwork
{
    private static readonly Uri[] Lookups = [new("https://ipv4.icanhazip.com"), new("https://ipv6.icanhazip.com")];

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string domain)
    {
        try
        {
            return await Dns.GetHostAddressesAsync(domain);
        }
        catch (SocketException)
        {
            return [];
        }
    }

    public IReadOnlyList<IPAddress> InterfaceAddresses() =>
    [
        .. NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address),
    ];

    public async Task<IReadOnlyList<IPAddress>> PublicAddressesAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var found = new List<IPAddress>();
        foreach (var lookup in Lookups)
        {
            try
            {
                if (IPAddress.TryParse((await http.GetStringAsync(lookup)).Trim(), out var address)) found.Add(address);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // An IPv6 only (or IPv4 only) host fails one of the two lookups; that is fine.
            }
        }

        return found;
    }
}
