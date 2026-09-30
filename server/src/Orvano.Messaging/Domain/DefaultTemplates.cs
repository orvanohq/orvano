using Orvano.Messaging.Contracts;

namespace Orvano.Messaging.Domain;

/// <summary>
/// The four auth email templates as they ship (spec 0009, AC-13), used until a project edits its own. Each follows
/// the email accessibility rules: a language and direction on <c>&lt;html&gt;</c> and on the body's first child, a
/// title, one heading, layout tables marked as presentation, one column, 16 px text, a 44 px tall button, a
/// preheader that says when the link or code expires, and a hand written text part.
/// </summary>
internal static class DefaultTemplates
{
    private const string Ignore = "If you didn't ask for this, you can ignore this email.";

    /// <summary>The expiry as words: minutes up to two hours, then whole hours, then whole days.</summary>
    private const string Expires =
        """
        {%- capture expires -%}
        {%- if expires_in_minutes == 1 -%}1 minute
        {%- elsif expires_in_minutes < 120 -%}{{ expires_in_minutes }} minutes
        {%- elsif expires_in_minutes < 2880 -%}{{ expires_in_minutes | divided_by: 60 }} hours
        {%- else -%}{{ expires_in_minutes | divided_by: 1440 }} days
        {%- endif -%}
        {%- endcapture -%}
        """;

    private const string Greeting = """{% if user.name != "" %}Hi {{ user.name }},{% else %}Hi,{% endif %}""";

    private static readonly TemplateSource Verification = Link(
        "Verify your email for {{ project.name }}",
        "Verify your email",
        "Confirm that {{ user.email }} is your email address for {{ project.name }}.",
        "Verify email");

    private static readonly TemplateSource Recovery = Link(
        "Reset your password for {{ project.name }}",
        "Reset your password",
        "We got a request to reset the password of your {{ project.name }} account.",
        "Reset password");

    private static readonly TemplateSource MagicLink = Link(
        "Your sign in link for {{ project.name }}",
        "Sign in to {{ project.name }}",
        "Use this link to sign in to {{ project.name }} as {{ user.email }}.",
        "Sign in");

    private static readonly TemplateSource EmailCode = new(
        "Your sign in code for {{ project.name }}",
        Html(
            "Your sign in code for {{ project.name }}",
            "This code works for {{ expires }}.",
            "Your sign in code",
            "Enter this code to sign in to {{ project.name }} as {{ user.email }}.",
            """<p style="margin:0 0 24px;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,'Liberation Mono',monospace;font-size:28px;line-height:1.3;font-weight:600;letter-spacing:4px;">{{ code }}</p>""",
            "This code works for {{ expires }}."),
        Expires + Greeting +
        $$$"""


        Enter this code to sign in to {{ project.name }} as {{ user.email }}:

        {{ code }}

        This code works for {{ expires }}.

        {{{Ignore}}}

        """);

    /// <summary>The default template of <paramref name="kind"/>.</summary>
    public static TemplateSource Get(AuthEmailKind kind) => kind switch
    {
        AuthEmailKind.Verification => Verification,
        AuthEmailKind.Recovery => Recovery,
        AuthEmailKind.MagicLink => MagicLink,
        AuthEmailKind.EmailCode => EmailCode,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static TemplateSource Link(string subject, string heading, string intro, string button) => new(
        subject,
        Html(
            subject,
            "This link works for {{ expires }}.",
            heading,
            intro,
            $$$"""<p style="margin:0 0 24px;"><a href="{{ action_url }}" style="display:inline-block;padding:12px 24px;font-size:16px;line-height:20px;font-weight:600;color:#ffffff;background-color:#18181b;border-radius:6px;text-decoration:none;">{{{button}}}</a></p>""",
            "This link works for {{ expires }} and can be used once."),
        Expires + Greeting +
        $$$"""


        {{{intro}}}

        {{{button}}}: {{ action_url }}

        This link works for {{ expires }} and can be used once.

        {{{Ignore}}}

        """);

    private static string Html(string title, string preheader, string heading, string intro, string action, string expiry) =>
        Expires +
        $$$"""
        <!doctype html>
        <html lang="en" dir="ltr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{{title}}}</title>
        </head>
        <body style="margin:0;padding:0;background-color:#f4f4f5;">
        <div lang="en" dir="ltr" style="font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-size:16px;line-height:1.5;color:#18181b;">
        <div style="display:none;max-height:0;overflow:hidden;">{{{preheader}}}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
        <tr>
        <td align="center" style="padding:32px 16px;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;background-color:#ffffff;border-radius:8px;">
        <tr>
        <td style="padding:32px;">
        <h1 style="margin:0 0 16px;font-size:24px;line-height:1.3;">{{{heading}}}</h1>
        <p style="margin:0 0 16px;">{{{Greeting}}}</p>
        <p style="margin:0 0 24px;">{{{intro}}}</p>
        {{{action}}}
        <p style="margin:0 0 16px;">{{{expiry}}}</p>
        <p style="margin:0;color:#52525b;">{{{Ignore}}}</p>
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
}
