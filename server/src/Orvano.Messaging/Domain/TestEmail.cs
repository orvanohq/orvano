using System.Net;

namespace Orvano.Messaging.Domain;

/// <summary>An email ready to send: the rendered parts, with no SMTP details.</summary>
internal sealed record EmailContent(string Subject, string Html, string Text);

/// <summary>The fixed email a test send delivers (spec 0009, AC-6). It names what sent it and nothing else.</summary>
internal static class TestEmail
{
    public const string Subject = "Test email from Orvano";

    /// <summary>What the install's own test names itself, since it belongs to no project.</summary>
    public const string InstallName = "this Orvano server";

    /// <summary>The test email of the project called <paramref name="projectName"/>.</summary>
    public static EmailContent ForProject(string projectName) => Build($"the project {projectName}");

    private static EmailContent Build(string sender)
    {
        var text =
            $"""
            It works.

            This is a test email from {sender}, sent through the SMTP settings you just tested. Emails from Orvano will arrive like this one.

            You got this because you pressed "Send test email" in the Orvano console.
            """;
        var html =
            $"""
            <!doctype html>
            <html lang="en" dir="ltr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{Subject}</title>
            </head>
            <body style="margin:0;padding:0;background-color:#f4f4f5;">
            <div lang="en" dir="ltr" style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-size:16px;line-height:1.5;color:#18181b;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
            <tr>
            <td align="center" style="padding:32px 16px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;background-color:#ffffff;border-radius:8px;">
            <tr>
            <td style="padding:32px;">
            <h1 style="margin:0 0 16px;font-size:24px;line-height:1.3;">It works.</h1>
            <p style="margin:0 0 16px;">This is a test email from {WebUtility.HtmlEncode(sender)}, sent through the SMTP settings you just tested. Emails from Orvano will arrive like this one.</p>
            <p style="margin:0;color:#52525b;">You got this because you pressed "Send test email" in the Orvano console.</p>
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
        return new EmailContent(Subject, html, text);
    }
}
