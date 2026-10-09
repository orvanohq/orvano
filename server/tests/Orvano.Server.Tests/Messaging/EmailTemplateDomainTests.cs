using System.Text.RegularExpressions;
using Orvano.Messaging.Contracts;
using Orvano.Messaging.Domain;

namespace Orvano.Server.Tests.Messaging;

// Spec 0009, slice 3: the template rules as plain types, no database and no network.
public class EmailTemplateDomainTests
{
    private const string Hostile = "<script>alert('x')</script> & \"q\"";

    private static readonly EmailTemplateInfo Recovery = EmailTemplateCatalog.Get(AuthEmailKind.Recovery);

    private static readonly TemplateValues Sample = EmailTemplateCatalog.Sample(AuthEmailKind.Recovery, "Acme Shop", "grace@example.com", "Grace");

    public static TheoryData<AuthEmailKind> Kinds => [AuthEmailKind.Verification, AuthEmailKind.Recovery, AuthEmailKind.MagicLink, AuthEmailKind.EmailCode];

    public static TheoryData<SecurityAlertKind, string> Alerts => new()
    {
        { SecurityAlertKind.MfaEnabled, "Two step verification is on" },
        { SecurityAlertKind.MfaDisabled, "Two step verification is off" },
        { SecurityAlertKind.PasskeyAdded, "A passkey was added" },
        { SecurityAlertKind.PasskeyRemoved, "A passkey was removed" },
        { SecurityAlertKind.RecoveryCodesCreated, "New recovery codes were made" },
        { SecurityAlertKind.RecoveryCodeUsed, "A recovery code was used" },
    };

    // Spec 0013 AC-31: the security alert's default, worded per alert through one Liquid case, with AC-13's rules
    // apart from the button.
    [Theory]
    [MemberData(nameof(Alerts))]
    public async Task The_security_alert_default_names_each_change_and_when_with_no_button(SecurityAlertKind alert, string heading)
    {
        var info = EmailTemplateCatalog.Get(AuthEmailKind.SecurityAlert);
        var source = DefaultTemplates.Get(AuthEmailKind.SecurityAlert);
        Assert.True(EmailTemplates.TryNormalize(source.Subject, source.Html, source.Text, out var stored, out var error), error);
        Assert.Equal(source, stored);
        var values = AuthEmailRule.CheckAlert("p1", "grace@example.com", "Acme Shop", alert, new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.FromHours(2)));

        var (email, problem) = await EmailTemplates.CheckAsync(info, source, values);

        Assert.True(problem is null, problem?.Detail);
        var html = email!.Html;
        Assert.Equal("Security alert for Acme Shop", email.Subject);
        Assert.StartsWith("<!doctype html>\n<html lang=\"en\" dir=\"ltr\">", html, StringComparison.Ordinal);
        Assert.Contains($"<title>{email.Subject}</title>", html, StringComparison.Ordinal);
        Assert.Matches($"""<div style="display:none[^"]*">{heading} on 2026-06-01 10:30 UTC\.</div>""", html);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Contains($">{heading}</h1>", html, StringComparison.Ordinal);
        Assert.Contains("When: 2026-06-01 10:30 UTC", html, StringComparison.Ordinal);
        Assert.Contains("Acme Shop account", html + email.Text, StringComparison.Ordinal);
        Assert.Equal(Regex.Matches(html, "<table ").Count, Regex.Matches(html, """<table role="presentation" """).Count);
        Assert.DoesNotMatch(@"font-size:(\d|1[0-5])px", html);
        Assert.DoesNotContain("href", html, StringComparison.Ordinal);
        Assert.StartsWith($"{heading}\n\n", email.Text, StringComparison.Ordinal);
        Assert.Contains("When: 2026-06-01 10:30 UTC", email.Text, StringComparison.Ordinal);
        Assert.Contains("If it wasn't, sign in to Acme Shop now", email.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", html + email.Text + email.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("{%", html + email.Text + email.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain('<', email.Text);
    }

    [Fact]
    public void A_security_alert_goes_only_through_its_own_method_and_needs_one_address()
    {
        var asAuthEmail = new AuthEmail("p1", "Acme", AuthEmailKind.SecurityAlert, "grace@example.com", null, null, null, 10);

        Assert.Throws<ArgumentException>(() => AuthEmailRule.Check(asAuthEmail));
        Assert.Throws<ArgumentException>(() => AuthEmailRule.CheckAlert("p1", "not an email", "Acme", SecurityAlertKind.MfaEnabled, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => AuthEmailRule.CheckAlert("p1", "grace@example.com\r\nBcc: x@y.z", "Acme", SecurityAlertKind.MfaEnabled, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => AuthEmailRule.CheckAlert("", "grace@example.com", "Acme", SecurityAlertKind.MfaEnabled, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => AuthEmailRule.CheckAlert("p1", "grace@example.com", "Acme", (SecurityAlertKind)99, DateTimeOffset.UtcNow));
    }

    // AC-13, rule by rule, on what a recipient actually gets.
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_default_template_passes_its_own_rules_and_the_accessibility_rules(AuthEmailKind kind)
    {
        var info = EmailTemplateCatalog.Get(kind);
        var source = DefaultTemplates.Get(kind);
        Assert.True(EmailTemplates.TryNormalize(source.Subject, source.Html, source.Text, out var stored, out var error), error);
        Assert.Equal(source, stored);

        var (email, problem) = await EmailTemplates.CheckAsync(info, source, EmailTemplateCatalog.Sample(kind, "Acme Shop", "grace@example.com", "Grace"));
        Assert.True(problem is null, problem?.Detail);
        var html = email!.Html;

        Assert.EndsWith("for Acme Shop", email.Subject, StringComparison.Ordinal);
        Assert.StartsWith("<!doctype html>\n<html lang=\"en\" dir=\"ltr\">", html, StringComparison.Ordinal);
        Assert.Matches("""<body[^>]*>\s*<div lang="en" dir="ltr" """, html);
        Assert.Contains($"<title>{email.Subject}</title>", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Equal(Regex.Matches(html, "<table ").Count, Regex.Matches(html, """<table role="presentation" """).Count);
        Assert.Contains("font-size:16px", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"font-size:(\d|1[0-5])px", html);
        Assert.Contains("Hi Grace,", html, StringComparison.Ordinal);
        Assert.Contains("If you didn't ask for this, you can ignore this email.", html, StringComparison.Ordinal);
        Assert.Contains("If you didn't ask for this, you can ignore this email.", email.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", html + email.Text + email.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("{%", html + email.Text + email.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain('<', email.Text);

        if (kind == AuthEmailKind.EmailCode)
        {
            Assert.Matches("""<div style="display:none[^"]*">This code works for 10 minutes\.</div>""", html);
            Assert.Matches("""<p style="[^"]*monospace;font-size:28px;[^"]*">428613</p>""", html);
            Assert.Contains("\n428613\n", email.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("href", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.Matches("""<div style="display:none[^"]*">This link works for 60 minutes\.</div>""", html);
            // 12 px of padding above and below a 20 px line: 44 px tall.
            Assert.Matches("""<a href="https://example.com/auth/confirm\?token=sample" style="[^"]*padding:12px 24px;font-size:16px;line-height:20px;""", html);
            Assert.Contains(": https://example.com/auth/confirm?token=sample\n", email.Text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(15, "15 minutes")]
    [InlineData(119, "119 minutes")]
    [InlineData(180, "3 hours")]
    [InlineData(1440, "24 hours")]
    [InlineData(10080, "7 days")]
    public async Task A_default_template_says_the_expiry_in_words_and_greets_a_nameless_user(int minutes, string words)
    {
        var values = new TemplateValues("Acme", "grace@example.com", "", "https://acme.test/reset?token=abc", null, minutes);
        Assert.True(EmailTemplates.TryCompile(Recovery, DefaultTemplates.Get(AuthEmailKind.Recovery), out var compiled, out _));

        var email = await EmailTemplates.RenderAsync(compiled, values);

        Assert.Contains($"This link works for {words} and can be used once.", email.Text, StringComparison.Ordinal);
        Assert.Contains($"This link works for {words} and can be used once.", email.Html, StringComparison.Ordinal);
        Assert.StartsWith("Hi,\n", email.Text, StringComparison.Ordinal);
        Assert.Contains("<p style=\"margin:0 0 16px;\">Hi,</p>", email.Html, StringComparison.Ordinal);
    }

    // AC-11: every output in the HTML part is encoded, and only there.
    [Fact]
    public async Task Values_are_encoded_in_the_HTML_part_only_and_a_blank_text_part_is_derived()
    {
        var values = Sample with { ProjectName = Hostile, UserName = Hostile };
        var source = new TemplateSource("Hi {{ user.name }} from {{ project.name }}", "<p>Hi {{ user.name }}, <a href=\"{{ action_url }}\">reset</a></p>", null);

        var (email, problem) = await EmailTemplates.CheckAsync(Recovery, source, values);

        Assert.Null(problem);
        Assert.Equal($"Hi {Hostile} from {Hostile}", email!.Subject);
        Assert.DoesNotContain("<script", email.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", email.Html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com/auth/confirm?token=sample\"", email.Html, StringComparison.Ordinal);
        Assert.Equal($"Hi {Hostile}, reset (https://example.com/auth/confirm?token=sample)", email.Text.Trim());

        var (written, _) = await EmailTemplates.CheckAsync(Recovery, source with { Text = "Plain {{ user.name }}" }, values);
        Assert.Equal($"Plain {Hostile}", written!.Text);
    }

    // AC-11: no filter, and no way of combining them, lets a value through unencoded.
    [Fact]
    public async Task No_filter_or_tag_can_skip_the_encoding()
    {
        var values = Sample with { UserName = Hostile };
        var shapes = new List<string>
        {
            "{% assign a = user.name | split: '&' %}{{ a }}",
            "{% assign a = user.name | split: '&' %}{% cycle a, a %}",
            "{% cycle user.name, user.name %}",
            "{% capture c %}x{% endcapture %}{{ c | append: user.name }}",
            "{% capture c %}x{% endcapture %}{{ c | replace: 'x', user.name }}",
            "{% capture c %}x{% endcapture %}{% assign d = c | prepend: user.name %}{{ d }}",
            "{% capture c %}{{ user.name }}{% endcapture %}{{ c }}{{ c | upcase }}",
            "{% assign a = user.name | split: '&' %}{% for x in a %}{{ x }}{{ forloop.index }}{% endfor %}{{ a | join: user.name }}{{ a | first }}{{ a[0] }}",
            "{{ user.name | escape }}{{ user.name | escape_once }}{{ user.name | url_encode | url_decode }}{{ user.name | base64_encode | base64_decode }}",
            "{% echo user.name %}{% liquid echo user.name %}",
            "{% tablerow x in (1..2) %}{{ user.name }}{{ tablerowloop.index }}{% endtablerow %}",
        };
        shapes.AddRange(LiquidEngine.AllowedFilters.Select(f => "{{ user.name | " + f + " }}{{ user.name | " + f + ": user.name }}{{ user.name | " + f + ": user.name, user.name }}"));

        foreach (var shape in shapes)
        {
            Assert.True(EmailTemplates.TryCompile(Recovery, new TemplateSource("s", shape, null), out var compiled, out var problem), $"{shape}: {problem?.Detail}");
            string html;
            try
            {
                html = (await EmailTemplates.RenderAsync(compiled, values)).Html;
            }
            catch (EmailRenderException)
            {
                // A filter that can't take these arguments sends nothing at all.
                continue;
            }

            Assert.False(html.Contains("<script", StringComparison.OrdinalIgnoreCase) || html.Contains("tpircs<", StringComparison.OrdinalIgnoreCase), $"{shape} rendered {html}");
        }
    }

    // AC-10: what a template may not use, each with its part and line.
    [Theory]
    [InlineData("<p>\n\n\n{{ action_ur }}</p>", "html: line 4: unknown variable action_ur")]
    [InlineData("<p>{{ user.emial }}</p>", "html: line 1: unknown variable user.emial")]
    [InlineData("<p>{{ user }}</p>", "html: line 1: unknown variable user")]
    [InlineData("<p>{{ user.name.size }}</p>", "html: line 1: unknown variable user.name.size")]
    [InlineData("<p>{{ user['name'] }}</p>", "html: line 1: unknown variable user[...]")]
    [InlineData("<p>{{ code }}</p>", "html: line 1: unknown variable code")]
    [InlineData("<p>{{ forloop.index }}</p>", "html: line 1: unknown variable forloop.index")]
    [InlineData("{% for i in (1..2) %}{% endfor %}\n{{ i }}", "html: line 2: unknown variable i")]
    [InlineData("{{ later }}{% assign later = 1 %}", "html: line 1: unknown variable later")]
    [InlineData("{% if user.nam == 'x' %}{% endif %}", "html: line 1: unknown variable user.nam")]
    [InlineData("{{ user.name | append: projct.name }}", "html: line 1: unknown variable projct.name")]
    [InlineData("<p>\n{{ user.name | raw }}</p>", "html: line 2: unknown filter raw")]
    [InlineData("<p>{{ user.name | shout }}</p>", "html: line 1: unknown filter shout")]
    [InlineData("<p>{{ user.name | json }}</p>", "html: line 1: unknown filter json")]
    [InlineData("<p>{{ '{0,1000000000}' | format_string: 1 }}</p>", "html: line 1: unknown filter format_string")]
    [InlineData("a\n{% include 'other' %}", "html: line 2: include is not allowed")]
    [InlineData("a\n\n{%- render 'other' %}", "html: line 3: render is not allowed")]
    [InlineData("a\n{{ user.name | }}", "html: line 2: An identifier was expected after the '|' sign")]
    [InlineData("{% macro f(a) %}x{% endmacro %}", "html: line 1: Unknown tag 'macro'")]
    [InlineData("{% for i in (1..100001) %}x{% endfor %}", "html: rendering takes more than 100,000 steps")]
    [InlineData("{% for i in (1..400) %}{% for j in (1..400) %}{% endfor %}{% endfor %}", "html: rendering takes more than 100,000 steps")]
    [InlineData("{% assign s = 'ab' %}{% for i in (1..30) %}{% assign s = s | append: s %}{% endfor %}", "html: a value or the output grows past 1 MB")]
    [InlineData("{% assign s = 'ab' %}{% for i in (1..30) %}{% capture s %}{{ s }}{{ s }}{% endcapture %}{% endfor %}", "html: a value or the output grows past 1 MB")]
    [InlineData("{% assign s = 'aaaaaaaaaa' %}{% for i in (1..8) %}{% assign s = s | replace: 'a', s %}{% endfor %}", "html: a value or the output grows past 1 MB")]
    [InlineData("{% assign s = 'aaaaaaaaaa' | split: '' %}{% for i in (1..30) %}{% assign s = s | concat: s %}{% endfor %}", "html: a value or the output grows past 1 MB")]
    [InlineData("{% assign a = 'a,a' | split: ',' %}{% for i in (1..9) %}{% assign a = a | concat: a %}{% endfor %}{% assign sep = 'ab' %}{% for i in (1..10) %}{% assign sep = sep | append: sep %}{% endfor %}{{ a | join: sep }}", "html: a value or the output grows past 1 MB")]
    [InlineData("<i>i</i> {{ 'i' }}\n{% for i in (1..2) %}{{ i }}\n{{ i | plus: 1 }}{% endfor %}\n\n{{ user.name | upcase }}{{ i | upcase }}", "html: line 5: unknown variable i")]
    [InlineData("{{ user.name | upcase }}\n{{ user.name | upcase | shout | upcase }}\n{{ user.name | shout }}", "html: line 2: unknown filter shout")]
    [InlineData("{% for i in (1..20000) %}{{ 'abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz' }}{% endfor %}", "html: a value or the output grows past 1 MB")]
    [InlineData("{% assign s = 'ab' %}{% for i in (1..18) %}{% assign s = s | append: s %}{% endfor %}{% for i in (1..40) %}{% assign t = s | upcase %}{% endfor %}", "html: the filters do too much work")]
    [InlineData("{{ 'not base64!' | base64_decode }}", "html: it can't be rendered with these values")]
    public async Task A_template_that_breaks_a_rule_is_refused_with_its_part_and_line(string html, string detail)
    {
        var (email, problem) = await EmailTemplates.CheckAsync(Recovery, new TemplateSource("Reset", html, null), Sample);

        Assert.Null(email);
        Assert.Equal(detail, problem?.Detail);
    }

    [Fact]
    public async Task The_subject_and_the_text_part_are_checked_like_the_HTML()
    {
        var (_, subject) = await EmailTemplates.CheckAsync(Recovery, new TemplateSource("Hi {{ nope }}", "<p>x</p>", null), Sample);
        var (_, text) = await EmailTemplates.CheckAsync(Recovery, new TemplateSource("Hi", "<p>x</p>", "a\n{{ user.name | raw }}"), Sample);
        // The code belongs to the email code template alone, and the link to the other three.
        var (_, link) = await EmailTemplates.CheckAsync(
            EmailTemplateCatalog.Get(AuthEmailKind.EmailCode), new TemplateSource("Hi", "<p>{{ code }} {{ action_url }}</p>", null),
            EmailTemplateCatalog.Sample(AuthEmailKind.EmailCode, "Acme", "grace@example.com", null));

        Assert.Equal("subject: line 1: unknown variable nope", subject?.Detail);
        Assert.Equal("text: line 2: unknown filter raw", text?.Detail);
        Assert.Equal("html: line 1: unknown variable action_url", link?.Detail);
    }

    // AC-10: names the template creates itself are allowed, from where they are created.
    [Fact]
    public async Task Names_made_by_assign_capture_and_loops_are_allowed()
    {
        const string html =
            """
            {% assign greeting = "Hi" %}{% capture who %}{{ user.name | default: "there" }}{% endcapture -%}
            {{ greeting }} {{ who }}{% for part in (1..2) %} {{ part }}/{{ forloop.length }}{% endfor %}
            {%- tablerow cell in (1..2) %}{{ cell }}{{ tablerowloop.col }}{% endtablerow %}
            {%- if expires_in_minutes > 30 %} {{ project.name }}{% endif %}
            """;

        var (email, problem) = await EmailTemplates.CheckAsync(Recovery, new TemplateSource("Reset", html, null), Sample);

        Assert.True(problem is null, problem?.Detail);
        Assert.StartsWith("Hi Grace 1/2 2/2", email!.Html, StringComparison.Ordinal);
        Assert.EndsWith(" Acme Shop", email.Html, StringComparison.Ordinal);
    }

    // AC-10: the sizes, which answer 400.
    [Fact]
    public void The_sizes_of_the_parts_are_checked_before_anything_is_parsed()
    {
        Assert.True(EmailTemplates.TryNormalize("  Reset  ", "<p>x</p>", "  \n ", out var stored, out _));
        Assert.Equal(new TemplateSource("Reset", "<p>x</p>", null), stored);
        Assert.True(EmailTemplates.TryNormalize(new string('s', 255), new string('h', 102_400), new string('t', 102_400), out _, out _));
        Assert.True(EmailTemplates.TryNormalize("Reset", "", null, out _, out _));

        Assert.Equal("subject: Enter a subject.", Error(" ", "<p>x</p>", null));
        Assert.Equal("subject: Enter a subject.", Error(null, "<p>x</p>", null));
        Assert.Equal("subject: Use at most 255 characters.", Error(new string('s', 256), "<p>x</p>", null));
        Assert.Equal("html: Use at most 100 KB.", Error("Reset", new string('h', 102_401), null));
        // Bytes, as the table counts them: 51,201 two byte characters are too many.
        Assert.Equal("html: Use at most 100 KB.", Error("Reset", new string('é', 51_201), null));
        Assert.Equal("text: Use at most 100 KB.", Error("Reset", "<p>x</p>", new string('t', 102_401)));

        static string Error(string? subject, string? html, string? text)
        {
            Assert.False(EmailTemplates.TryNormalize(subject, html, text, out _, out var error));
            return error;
        }
    }

    // AC-11: a rendered subject is one header line of at most 255 characters.
    [Fact]
    public async Task A_rendered_subject_loses_its_control_characters_and_is_cut()
    {
        var values = Sample with { UserName = "Grace\r\nBcc: evil@example.com", ProjectName = new string('p', 300) };
        Assert.True(EmailTemplates.TryCompile(Recovery, new TemplateSource("{{ user.name }} {{ project.name }}", "<p>x</p>", null), out var compiled, out _));

        var email = await EmailTemplates.RenderAsync(compiled, values);

        Assert.Equal(255, email.Subject.Length);
        Assert.StartsWith("Grace  Bcc: evil@example.com ppp", email.Subject, StringComparison.Ordinal);
    }

    // AC-14, amended by spec 0010 AC-31: what a caller may hand the queue.
    [Fact]
    public void An_auth_email_is_checked_before_it_is_queued()
    {
        var link = new AuthEmail("shop", "Shop", AuthEmailKind.Verification, "grace@example.com", null, "https://shop.test/verify?token=abc", null, 60);
        var code = new AuthEmail("shop", "Shop", AuthEmailKind.EmailCode, "grace@example.com", "Grace", null, "Ab12", 10);

        Assert.Equal(new TemplateValues("Shop", "grace@example.com", "", "https://shop.test/verify?token=abc", null, 60), AuthEmailRule.Check(link));
        Assert.Equal(new TemplateValues("Shop", "grace@example.com", "Grace", null, "Ab12", 10), AuthEmailRule.Check(code));
        Assert.Equal("http://localhost:3000/verify", AuthEmailRule.Check(link with { ActionUrl = "http://localhost:3000/verify" }).ActionUrl);
        // Auth checks links against the project's platforms (spec 0010 AC-6), so an app's own scheme passes here.
        Assert.Equal("com.acme.app://auth?x=1", AuthEmailRule.Check(link with { ActionUrl = "com.acme.app://auth?x=1" }).ActionUrl);
        Assert.Equal("http://shop.test/verify", AuthEmailRule.Check(link with { ActionUrl = "http://shop.test/verify" }).ActionUrl);
        AuthEmailRule.Check(link with { ExpiresInMinutes = 1 });
        AuthEmailRule.Check(code with { ExpiresInMinutes = 10_080, Code = "ABCDEF123456" });

        AuthEmail[] broken =
        [
            link with { ProjectId = " " },
            link with { ProjectName = null! },
            link with { Kind = (AuthEmailKind)9 },
            link with { To = "grace" },
            link with { To = " grace@example.com" },
            link with { To = "grace@example.com\nBcc: evil@example.com" },
            link with { To = new string('a', 312) + "@example.com" },
            link with { ActionUrl = null },
            link with { ActionUrl = "/verify?token=abc" },
            link with { ActionUrl = "javascript:alert(1)" },
            link with { ActionUrl = "JavaScript:alert(1)" },
            link with { ActionUrl = "data:text/html,hi" },
            link with { ActionUrl = "vbscript:x" },
            link with { ActionUrl = "file:///etc/passwd" },
            link with { ActionUrl = "blob:https://shop.test/1" },
            link with { ActionUrl = "about:blank" },
            link with { ActionUrl = "https://grace:pw@shop.test/verify" },
            link with { Code = "428613" },
            link with { ExpiresInMinutes = 0 },
            link with { ExpiresInMinutes = 10_081 },
            code with { Code = null },
            code with { Code = "123" },
            code with { Code = "1234567890123" },
            code with { Code = "12 34" },
            code with { Code = "１２３４" },
            code with { ActionUrl = "https://shop.test/verify" },
        ];
        foreach (var email in broken)
        {
            var thrown = Assert.ThrowsAny<ArgumentException>(() => AuthEmailRule.Check(email));
            // The message names the member and its rule, never the value.
            Assert.DoesNotContain("grace", thrown.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("token=abc", thrown.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_catalog_names_the_five_templates_and_their_variables()
    {
        Assert.Equal(["verification", "recovery", "magic_link", "email_code", "security_alert"], EmailTemplateCatalog.All.Select(t => t.Wire));
        Assert.Equal(["Email verification", "Password reset", "Magic link", "Email code", "Security alert"], EmailTemplateCatalog.All.Select(t => t.Name));
        Assert.Equal(["project.name", "alert", "occurred_at"], EmailTemplateCatalog.Get(AuthEmailKind.SecurityAlert).Variables.Select(v => v.Name));
        Assert.Equal(
            ["project.name", "user.email", "user.name", "action_url", "expires_in_minutes"],
            EmailTemplateCatalog.Get(AuthEmailKind.MagicLink).Variables.Select(v => v.Name));
        Assert.Equal(
            ["project.name", "user.email", "user.name", "code", "expires_in_minutes"],
            EmailTemplateCatalog.Get(AuthEmailKind.EmailCode).Variables.Select(v => v.Name));
        Assert.Null(EmailTemplateCatalog.Find("console_invitation"));

        var link = EmailTemplateCatalog.Sample(AuthEmailKind.Verification, "Acme", "grace@example.com", null);
        var code = EmailTemplateCatalog.Sample(AuthEmailKind.EmailCode, "Acme", "grace@example.com", "Grace");
        Assert.Equal(("https://example.com/auth/confirm?token=sample", "", "60", ""), (link.Text("action_url"), link.Text("code"), link.Text("expires_in_minutes"), link.Text("user.name")));
        Assert.Equal(("", "428613", "10", "Grace"), (code.Text("action_url"), code.Text("code"), code.Text("expires_in_minutes"), code.Text("user.name")));
    }
}
