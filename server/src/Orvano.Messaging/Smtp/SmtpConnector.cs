using System.Net;
using System.Net.Sockets;
using Orvano.Messaging.Application;
using Orvano.Messaging.Domain;

namespace Orvano.Messaging.Smtp;

/// <summary>The system resolver.</summary>
internal sealed class DnsHostResolver : IHostResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);
}

/// <summary>A project's SMTP host is, or resolves to, an address outside global unicast (spec 0009, AC-3).</summary>
internal sealed class SmtpHostNotAllowedException() : Exception("The SMTP host resolves to an address that is not allowed.");

/// <summary>
/// Opens the TCP connection to an SMTP server itself, so the address that was checked is the address that is dialed
/// (spec 0009, AC-3): it resolves the name, refuses the host if any address is outside global unicast, then dials
/// the addresses in the order DNS returned them. A DNS answer that changes after the check can't reach an internal
/// service, because nothing resolves the name a second time.
/// </summary>
internal sealed class SmtpConnector(IHostResolver resolver)
{
    /// <summary>The longest one address is given to accept the connection.</summary>
    public static readonly TimeSpan DialTimeout = TimeSpan.FromSeconds(10);

    /// <param name="host">The host name or IP literal.</param>
    /// <param name="port">The port.</param>
    /// <param name="publicOnly">True to refuse a host with any address outside global unicast.</param>
    /// <param name="ct">The operation's whole budget.</param>
    /// <exception cref="SmtpHostNotAllowedException"><paramref name="publicOnly"/> is set and an address is not global unicast.</exception>
    /// <exception cref="SocketException">The name does not resolve, or no address accepted the connection.</exception>
    public async Task<Socket> ConnectAsync(string host, int port, bool publicOnly, CancellationToken ct)
    {
        var addresses = SmtpHost.TryGetLiteral(host, out var literal) ? [literal] : await resolver.ResolveAsync(host, ct);
        if (publicOnly && !GlobalUnicast.ContainsAll(addresses)) throw new SmtpHostNotAllowedException();

        SocketException? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var dial = CancellationTokenSource.CreateLinkedTokenSource(ct);
            dial.CancelAfter(DialTimeout);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), dial.Token);
                return socket;
            }
            catch (SocketException e)
            {
                socket.Dispose();
                last = e;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // This address took too long; the next one still gets its turn within the budget.
                socket.Dispose();
                last = new SocketException((int)SocketError.TimedOut);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }
}
