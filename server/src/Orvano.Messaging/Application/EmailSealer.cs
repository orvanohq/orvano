using System.Text.Json;
using Orvano.Core.Secrets;

namespace Orvano.Messaging.Application;

/// <summary>What a queued email row seals: everything needed to send it. Personal data and a working link; never log it.</summary>
/// <param name="To">The full recipient address.</param>
/// <param name="Subject">The rendered subject.</param>
/// <param name="Html">The rendered HTML part.</param>
/// <param name="Text">The text part.</param>
/// <param name="MessageId">The <c>Message-ID</c>, without angle brackets, the same on every attempt.</param>
internal sealed record SealedEmail(string To, string Subject, string Html, string Text, string MessageId);

/// <summary>
/// Seals a queued email's content to its row (spec 0009, data model): JSON <c>{to, subject, html, text, messageId}</c>
/// in a <see cref="SecretBox"/> blob bound to <c>messaging_emails:&lt;id&gt;:content_ciphertext</c>, so a database dump
/// holds no recipient, link, or code, and a blob can't be moved to another row.
/// </summary>
internal static class EmailSealer
{
    public static string Binding(Guid emailId) => SecretBox.AssociatedData("messaging_emails", emailId.ToString(), "content_ciphertext");

    public static byte[] Seal(SecretBox secrets, Guid emailId, SealedEmail email)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("to", email.To);
            w.WriteString("subject", email.Subject);
            w.WriteString("html", email.Html);
            w.WriteString("text", email.Text);
            w.WriteString("messageId", email.MessageId);
            w.WriteEndObject();
        }

        return secrets.Encrypt(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), Binding(emailId));
    }

    /// <exception cref="SecretBoxException">The blob can't be opened, or does not hold an email.</exception>
    public static SealedEmail Open(SecretBox secrets, Guid emailId, ReadOnlySpan<byte> blob)
    {
        var json = secrets.Decrypt(blob, Binding(emailId));
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string Read(string name) => root.GetProperty(name).GetString() ?? throw new SecretBoxException("The sealed email is incomplete.");
            return new SealedEmail(Read("to"), Read("subject"), Read("html"), Read("text"), Read("messageId"));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new SecretBoxException("The sealed email is not in a known format.");
        }
    }
}
