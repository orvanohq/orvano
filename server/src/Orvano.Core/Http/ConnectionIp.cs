using System.Net;
using Microsoft.AspNetCore.Http;

namespace Orvano.Core.Http;

/// <summary>
/// The caller's address for rate limits and records (spec 0004): the peer address, or the forwarded client address
/// when the peer is a trusted proxy (<see cref="TrustedProxies"/>, applied by the host's forwarded headers step).
/// <c>X-Orvano-Client-IP</c> never counts here.
/// </summary>
public static class ConnectionIp
{
    /// <summary>The connection IP, IPv4 mapped addresses unwrapped, or <see langword="null"/> when unknown (in process tests).</summary>
    public static IPAddress? Of(HttpContext http) =>
        http.Connection.RemoteIpAddress is { IsIPv4MappedToIPv6: true } mapped ? mapped.MapToIPv4() : http.Connection.RemoteIpAddress;

    /// <summary>The connection IP as a rate limit key.</summary>
    public static string Key(HttpContext http) => Of(http)?.ToString() ?? "unknown";
}
