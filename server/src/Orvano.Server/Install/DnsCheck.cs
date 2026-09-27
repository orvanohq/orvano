using System.Net;

namespace Orvano.Server.Install;

/// <summary>
/// Whether a domain's A and AAAA records point at this server (spec 0006, AC-6). A mismatch is a
/// warning, not a refusal: DNS may simply not have caught up yet.
/// </summary>
internal static class DnsCheck
{
    /// <summary>The addresses among <paramref name="records"/> that belong to this server.</summary>
    public static IReadOnlyList<IPAddress> Matches(IEnumerable<IPAddress> records, IEnumerable<IPAddress> server)
    {
        var mine = server.Select(Normalize).ToHashSet();
        return [.. records.Select(Normalize).Where(mine.Contains).Distinct()];
    }

    /// <summary>The explanation shown before asking whether to continue, or null when DNS points here.</summary>
    public static string? Problem(string domain, IReadOnlyList<IPAddress> records, IReadOnlyList<IPAddress> matches)
    {
        if (records.Count == 0)
        {
            return $"{domain} has no A or AAAA record yet, so HTTPS will fail until its DNS points to this server.";
        }

        if (matches.Count == 0)
        {
            return $"{domain} points to {string.Join(", ", records)}, which is not this server, so HTTPS will fail until its DNS points here.";
        }

        return null;
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
