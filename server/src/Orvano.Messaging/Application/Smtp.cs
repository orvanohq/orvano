using System.Net;
using Orvano.Messaging.Domain;

namespace Orvano.Messaging.Application;

/// <summary>The SMTP server one send goes through. Never log any of it.</summary>
/// <param name="Host">The host name or IP literal.</param>
/// <param name="Port">The port.</param>
/// <param name="Security">How the connection is secured.</param>
/// <param name="Username">The username, or null to skip sign in.</param>
/// <param name="Password">The password, when there is a username.</param>
/// <param name="PublicOnly">True for a project's own server: only global unicast addresses are dialed (AC-3).</param>
internal sealed record SmtpServer(string Host, int Port, SmtpSecurity Security, string? Username, string? Password, bool PublicOnly);

/// <summary>One email with its envelope.</summary>
/// <param name="FromEmail">The From address.</param>
/// <param name="FromName">The name beside it, if any.</param>
/// <param name="ReplyTo">The Reply-To address, if any.</param>
/// <param name="To">The recipient.</param>
/// <param name="MessageId">The <c>Message-ID</c>, without angle brackets.</param>
/// <param name="Content">The subject and both bodies.</param>
internal sealed record OutgoingEmail(string FromEmail, string? FromName, string? ReplyTo, string To, string MessageId, EmailContent Content);

/// <summary>Sends one email over SMTP. Implemented with MailKit.</summary>
internal interface ISmtpSender
{
    /// <summary>
    /// Connects, signs in when there is a username, and sends <paramref name="email"/> as one multipart message, all
    /// within <paramref name="budget"/>. Certificates are always validated.
    /// </summary>
    /// <returns><see langword="null"/> when the server accepted the email; otherwise what went wrong.</returns>
    Task<SmtpFailure?> SendAsync(SmtpServer server, OutgoingEmail email, TimeSpan budget, CancellationToken ct);
}

/// <summary>Looks up a host name's addresses, in the order DNS returned them.</summary>
internal interface IHostResolver
{
    /// <exception cref="System.Net.Sockets.SocketException">The name does not resolve.</exception>
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}
