using System.Globalization;
using System.Net;

namespace Orvano.Messaging.Domain;

/// <summary>
/// The console's invite email (spec 0009, AC-23). Fixed in code: no template, no Liquid. It follows the email
/// accessibility rules of AC-13 and carries a hand written text part.
/// </summary>
internal static class InvitationEmailContent
{
    public const int MaxSubjectLength = 255;

    /// <param name="orgName">The org the invitation joins.</param>
    /// <param name="inviter">The inviter's name, else their email.</param>
    /// <param name="role">The role as a word: <c>owner</c>, <c>developer</c>, or <c>viewer</c>.</param>
    /// <param name="url">The invite link.</param>
    /// <param name="expiresAt">When the link stops working.</param>
    public static EmailContent Build(string orgName, string inviter, string role, string url, DateTimeOffset expiresAt)
    {
        var subject = Subject($"{inviter} invited you to join {orgName} on Orvano");
        var expires = expiresAt.UtcDateTime.ToString("MMM d, yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var article = role.StartsWith('o') ? "an" : "a";

        var text =
            $"""
            Join {orgName} on Orvano

            {inviter} invited you to join {orgName} as {article} {role}.

            Accept invite: {url}

            This link works until {expires}.

            If you weren't expecting this, you can ignore this email.
            """;

        string E(string value) => WebUtility.HtmlEncode(value);
        var html =
            $"""
            <!doctype html>
            <html lang="en" dir="ltr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(subject)}</title>
            </head>
            <body style="margin:0;padding:0;background-color:#f4f4f5;">
            <div lang="en" dir="ltr" style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-size:16px;line-height:1.5;color:#18181b;">
            <div style="display:none;max-height:0;overflow:hidden;">This invite works until {expires}.</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
            <tr>
            <td align="center" style="padding:32px 16px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;background-color:#ffffff;border-radius:8px;">
            <tr>
            <td style="padding:32px;">
            <h1 style="margin:0 0 16px;font-size:24px;line-height:1.3;">Join {E(orgName)} on Orvano</h1>
            <p style="margin:0 0 24px;">{E(inviter)} invited you to join {E(orgName)} as {article} {E(role)}.</p>
            <p style="margin:0 0 24px;"><a href="{E(url)}" style="display:inline-block;padding:12px 24px;font-size:16px;line-height:20px;font-weight:600;color:#ffffff;background-color:#18181b;border-radius:6px;text-decoration:none;">Accept invite</a></p>
            <p style="margin:0 0 16px;">This link works until {expires}.</p>
            <p style="margin:0;color:#52525b;">If you weren't expecting this, you can ignore this email.</p>
            </td>
            </tr>
            </table>
            </td>
            </tr>
            </table>
            </div>
            </body>
            </html>
            """;
        return new EmailContent(subject, html, text);
    }

    /// <summary>One header line: control characters become spaces, and it is cut to <see cref="MaxSubjectLength"/>.</summary>
    internal static string Subject(string value)
    {
        var clean = new string([.. value.Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        return clean.Length <= MaxSubjectLength ? clean : clean[..MaxSubjectLength];
    }
}
