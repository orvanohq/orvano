using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Orvano.Messaging.Application;
using Orvano.Messaging.Domain;

namespace Orvano.Messaging.Smtp;

/// <summary>
/// Sends one email through MailKit on a socket <see cref="SmtpConnector"/> opened: one client per send, no pooling
/// (spec 0009, stack additions). TLS still checks the certificate against the host name, and there is no way to skip
/// that. Every MailKit failure becomes an <see cref="SmtpFailure"/> here, so no exception text travels further.
/// </summary>
internal sealed class SmtpSender(SmtpConnector connector) : ISmtpSender
{
    internal enum Stage
    {
        Connect,
        Authenticate,
        Send,
    }

    public async Task<SmtpFailure?> SendAsync(SmtpServer server, OutgoingEmail email, TimeSpan budget, CancellationToken ct)
    {
        using var message = Build(email);
        using var limited = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limited.CancelAfter(budget);
        var token = limited.Token;
        var stage = Stage.Connect;

        using var client = new SmtpClient();
        try
        {
            var socket = await connector.ConnectAsync(server.Host, server.Port, server.PublicOnly, token);
            try
            {
                await client.ConnectAsync(socket, server.Host, server.Port, Options(server.Security), token);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            if (server.Username is not null)
            {
                stage = Stage.Authenticate;
                await client.AuthenticateAsync(server.Username, server.Password ?? "", token);
            }

            stage = Stage.Send;
            await client.SendAsync(message, token);
        }
        catch (Exception e) when (!ct.IsCancellationRequested && Classify(e, stage) is { } failure)
        {
            return failure;
        }

        try
        {
            await client.DisconnectAsync(quit: true, token);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The server already accepted the email; a failed goodbye changes nothing.
        }

        return null;
    }

    internal static MimeMessage Build(OutgoingEmail email)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(email.FromName ?? "", email.FromEmail));
        if (email.ReplyTo is not null) message.ReplyTo.Add(new MailboxAddress("", email.ReplyTo));
        message.To.Add(new MailboxAddress("", email.To));
        message.Subject = email.Content.Subject;
        message.MessageId = email.MessageId;
        // Tells auto responders (out of office replies) not to answer (RFC 3834).
        message.Headers.Add("Auto-Submitted", "auto-generated");
        message.Body = new BodyBuilder { TextBody = email.Content.Text, HtmlBody = email.Content.Html }.ToMessageBody();
        return message;
    }

    private static SecureSocketOptions Options(SmtpSecurity security) => security switch
    {
        // StartTls, not StartTlsWhenAvailable: a server that doesn't offer it fails the send.
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.Tls => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => throw new ArgumentOutOfRangeException(nameof(security), security, null),
    };

    /// <summary>The rows of the SMTP outcomes table (spec 0009), or null for an exception that is not an SMTP failure.</summary>
    internal static SmtpFailure? Classify(Exception e, Stage stage) => e switch
    {
        SmtpHostNotAllowedException => new SmtpFailure(SmtpFailureKind.HostNotAllowed),
        OperationCanceledException => new SmtpFailure(SmtpFailureKind.Timeout),
        SslHandshakeException or System.Security.Authentication.AuthenticationException => new SmtpFailure(SmtpFailureKind.TlsFailed),
        // MailKit throws this for a server without STARTTLS, and for one that offers no way to sign in.
        NotSupportedException => new SmtpFailure(stage == Stage.Authenticate ? SmtpFailureKind.AuthFailed : SmtpFailureKind.TlsFailed),
        MailKit.Security.AuthenticationException auth => new SmtpFailure(SmtpFailureKind.AuthFailed, ReplyCodeOf(auth)),
        SmtpCommandException command => stage == Stage.Authenticate
            ? new SmtpFailure(SmtpFailureKind.AuthFailed, (int)command.StatusCode)
            : new SmtpFailure(SmtpFailureKind.Rejected, (int)command.StatusCode, command.Message),
        SmtpProtocolException or SocketException or IOException or MailKit.ServiceNotConnectedException => new SmtpFailure(SmtpFailureKind.Unreachable),
        _ => null,
    };

    // MailKit reports a refused AUTH as "535: 5.7.8 ...", with the command's reply inside when it has one.
    private static int? ReplyCodeOf(MailKit.Security.AuthenticationException auth)
    {
        if (auth.InnerException is SmtpCommandException command) return (int)command.StatusCode;
        var message = auth.Message.AsSpan();
        return message.Length >= 3 && int.TryParse(message[..3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var code)
            ? code
            : null;
    }
}
