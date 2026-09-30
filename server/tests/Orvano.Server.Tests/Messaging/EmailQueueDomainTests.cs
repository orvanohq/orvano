using Microsoft.Extensions.Configuration;
using Orvano.Core.Secrets;
using Orvano.Messaging.Application;
using Orvano.Messaging.Domain;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Messaging;

// Spec 0009, slice 2: the queue's business rules as plain types, no database and no network.
public class RecipientMaskTests
{
    // AC-22: the first character of the local part, then ***, then @ and the whole domain.
    [Theory]
    [InlineData("grace@example.com", "g***@example.com")]
    [InlineData("g@example.com", "g***@example.com")]
    [InlineData("Ada.Lovelace+tag@mail.example.co.uk", "A***@mail.example.co.uk")]
    [InlineData("😀smile@example.com", "😀***@example.com")]
    public void Keeps_the_first_character_and_the_domain(string email, string masked) =>
        Assert.Equal(masked, RecipientMask.Mask(email));
}

public class EmailDeliveryTests
{
    // AC-16: 30 seconds, then 1, 2, 4, and 8 minutes.
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    public void A_retry_waits_on_the_spec_schedule(int attempt, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), EmailDelivery.RetryDelay(attempt));

    // AC-17: more than 30 minutes after it was queued, an email is never sent.
    [Fact]
    public void An_email_is_stale_only_after_30_minutes()
    {
        var created = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

        Assert.False(EmailDelivery.IsStale(created, created.AddMinutes(30)));
        Assert.True(EmailDelivery.IsStale(created, created.AddMinutes(30).AddSeconds(1)));
    }
}

public class HtmlToTextTests
{
    [Fact]
    public void Blocks_and_breaks_become_lines_and_a_heading_gets_a_blank_line_after()
    {
        var text = HtmlToText.Convert("<h1>Reset your password</h1><p>Hello   <b>Grace</b>,</p><div>Line one<br>Line two</div>");

        Assert.Equal("Reset your password\n\nHello Grace,\nLine one\nLine two", text);
    }

    [Fact]
    public void A_link_becomes_text_and_url_and_a_bare_url_is_not_repeated()
    {
        Assert.Equal("Open Reset (https://example.com/r?t=1&u=2) now",
            HtmlToText.Convert("<p>Open <a href=\"https://example.com/r?t=1&amp;u=2\">Reset</a> now</p>"));
        Assert.Equal("https://example.com/r", HtmlToText.Convert("<a href=\"https://example.com/r\">https://example.com/r</a>"));
        Assert.Equal("Top", HtmlToText.Convert("<a href=\"#top\">Top</a>"));
    }

    [Fact]
    public void List_items_get_a_dash_and_script_style_and_head_are_dropped()
    {
        var text = HtmlToText.Convert(
            """
            <html><head><title>Hidden</title><style>p { color: red; }</style></head>
            <body><script>alert("no")</script><ul><li>One</li><li>Two</li></ul></body></html>
            """);

        Assert.Equal("- One\n- Two", text);
    }

    [Fact]
    public void Entities_are_decoded_and_runs_of_blank_lines_collapse_to_one()
    {
        Assert.Equal("Tom &amp; Jerry <3".Replace("&amp;", "&", StringComparison.Ordinal),
            HtmlToText.Convert("<p>Tom &amp; Jerry &lt;3</p>"));
        Assert.Equal("One\n\nTwo", HtmlToText.Convert("<p>One</p><br><br><br><p>Two</p>"));
    }

    [Fact]
    public void A_layout_table_reads_top_to_bottom()
    {
        var text = HtmlToText.Convert(
            "<table role=\"presentation\"><tr><td><p>First</p></td></tr><tr><td><a href=\"https://x.test/a\">Go</a></td></tr></table>");

        Assert.Equal("First\nGo (https://x.test/a)", text);
    }
}

public class InvitationEmailContentTests
{
    private static readonly DateTimeOffset Expires = new(2026, 10, 7, 9, 5, 0, TimeSpan.FromHours(2));

    // AC-23: the subject, the org, the inviter, the role, the button to the link, and the expiry in UTC.
    [Fact]
    public void Says_who_invited_you_to_what_as_what_and_until_when()
    {
        var email = InvitationEmailContent.Build("Acme", "Grace Hopper", "developer", "https://orvano.test/invite#tok", Expires);

        Assert.Equal("Grace Hopper invited you to join Acme on Orvano", email.Subject);
        Assert.Contains("Grace Hopper invited you to join Acme as a developer.", email.Text, StringComparison.Ordinal);
        Assert.Contains("Accept invite: https://orvano.test/invite#tok", email.Text, StringComparison.Ordinal);
        Assert.Contains("Oct 7, 2026, 07:05 UTC", email.Text, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://orvano.test/invite#tok\"", email.Html, StringComparison.Ordinal);
        Assert.Contains(">Accept invite</a>", email.Html, StringComparison.Ordinal);
        Assert.Contains("Oct 7, 2026, 07:05 UTC", email.Html, StringComparison.Ordinal);
        Assert.Contains("as an owner", InvitationEmailContent.Build("Acme", "g@x.com", "owner", "https://o.test/i#t", Expires).Text, StringComparison.Ordinal);
    }

    // AC-13's rules, which AC-23 asks the invite to follow.
    [Fact]
    public void Follows_the_email_accessibility_rules()
    {
        var html = InvitationEmailContent.Build("Acme", "Grace", "viewer", "https://orvano.test/invite#tok", Expires).Html;

        Assert.Contains("<html lang=\"en\" dir=\"ltr\">", html, StringComparison.Ordinal);
        Assert.Contains("<div lang=\"en\" dir=\"ltr\"", html, StringComparison.Ordinal);
        Assert.Contains("<title>", html, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<h1"));
        Assert.DoesNotContain("<table width", html, StringComparison.Ordinal);
        Assert.Equal(
            System.Text.RegularExpressions.Regex.Matches(html, "<table").Count,
            System.Text.RegularExpressions.Regex.Matches(html, "<table role=\"presentation\"").Count);
        Assert.Contains("font-size:16px", html, StringComparison.Ordinal);
        // The button: 12 + 20 + 12 = 44 px tall.
        Assert.Contains("padding:12px 24px;font-size:16px;line-height:20px", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_are_encoded_in_html_and_cannot_break_the_subject_line()
    {
        var email = InvitationEmailContent.Build("<b>Acme</b>", "Eve\r\nBcc: x@y.z", "viewer", "https://orvano.test/invite#tok", Expires);

        Assert.DoesNotContain("<b>Acme</b>", email.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Acme&lt;/b&gt;", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', email.Subject);
        Assert.DoesNotContain('\n', email.Subject);
        Assert.True(InvitationEmailContent.Subject(new string('a', 400)).Length == 255);
    }
}

public class EmailSealerTests
{
    private static readonly SecretBox Box = new(MasterKeys.Parse(OrvanoProcess.MasterKeys));
    private static readonly SealedEmail Email = new("grace@example.com", "Subject", "<p>Hi</p>", "Hi", "id@localhost");

    [Fact]
    public void A_sealed_email_opens_only_for_its_own_row()
    {
        var id = Guid.CreateVersion7();
        var blob = EmailSealer.Seal(Box, id, Email);

        Assert.Equal(Email, EmailSealer.Open(Box, id, blob));
        Assert.DoesNotContain("grace@example.com", System.Text.Encoding.Latin1.GetString(blob), StringComparison.Ordinal);
        Assert.Throws<SecretBoxException>(() => EmailSealer.Open(Box, Guid.CreateVersion7(), blob));
    }

    [Fact]
    public void A_blob_that_is_not_an_email_is_unreadable()
    {
        var id = Guid.CreateVersion7();

        Assert.Throws<SecretBoxException>(() => EmailSealer.Open(Box, id, Box.Encrypt("{\"to\":\"x\"}"u8, EmailSealer.Binding(id))));
        Assert.Throws<SecretBoxException>(() => EmailSealer.Open(Box, id, Box.Encrypt("not json"u8, EmailSealer.Binding(id))));
        Assert.Throws<SecretBoxException>(() => EmailSealer.Open(Box, id, []));
    }
}

// Spec 0009, AC-29: the shared scenarios' fixtures seed the install's SMTP settings.
public class InstallSmtpFixtureTests
{
    [Fact]
    public void The_scenario_fixtures_point_the_install_SMTP_at_mailpit()
    {
        var fixtures = Load(Path.Combine(RepoPaths.Root, "tests", "scenarios", "fixtures.yaml"));

        Assert.Null(fixtures.Problem);
        Assert.Equal(
            new Orvano.Messaging.Fixtures.FixtureInstallSmtp("mailpit", 1025, SmtpSecurity.None, "orvano@scenarios.test"),
            fixtures.InstallSmtp);
    }

    [Theory]
    [InlineData("installSmtp: { host: 'bad host', port: 1025, security: none, fromEmail: a@b.test }", "installSmtp host:")]
    [InlineData("installSmtp: { host: mailpit, port: 1025, security: ssl, fromEmail: a@b.test }", "installSmtp security:")]
    [InlineData("installSmtp: { host: mailpit, port: 1025, security: none }", "installSmtp fromEmail:")]
    public void A_bad_install_SMTP_fixture_is_refused(string entry, string problem)
    {
        var path = Path.Combine(Path.GetTempPath(), $"orvano-fixtures-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, $"consoleUsers: [{{ email: owner@x.com, password: owner horse battery }}]\n{entry}\n");

        Assert.Contains(problem, Load(path).Problem, StringComparison.Ordinal);
    }

    private static Orvano.Server.Hosting.TestFixtures Load(string path) => Orvano.Server.Hosting.TestFixtures.Load(
        new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Test" },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ORVANO_TEST_FIXTURES"] = path }).Build());
}
