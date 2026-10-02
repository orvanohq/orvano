using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orvano.Contract;
using Orvano.Core;
using Orvano.Messaging;
using Orvano.Messaging.Application;
using Orvano.Messaging.Domain;
using Orvano.Messaging.Smtp;
using SmtpSecurity = Orvano.Messaging.Domain.SmtpSecurity;
using TestEmail = Orvano.Messaging.Domain.TestEmail;

namespace Orvano.Server.Tests.Messaging;

// Spec 0009, slice 1: the business rules as plain types, no database and no network.
public class SmtpSettingsRuleTests
{
    private static readonly SmtpSettingsDraft Good =
        new("smtp.example.com", 587, SmtpSecurity.StartTls, "apikey", "secret", "hello@example.com", "Example", "support@example.com");

    [Fact]
    public void Valid_settings_are_normalized_as_they_are_stored()
    {
        var draft = Good with { Host = "  SMTP.Example.com ", FromEmail = " hello@example.com ", FromName = "  Example  ", ReplyTo = "  " };

        Assert.True(SmtpSettingsRule.TryValidate(draft, null, out var settings, out _));

        Assert.Equal("smtp.example.com", settings.Host);
        Assert.Equal("hello@example.com", settings.FromEmail);
        Assert.Equal("Example", settings.FromName);
        Assert.Null(settings.ReplyTo);
        Assert.Equal(SmtpPasswordSource.Given, settings.PasswordSource);
    }

    // AC-2, row by row. Every error starts with the input's camelCase name and a colon.
    [Theory]
    [InlineData("", "host")]
    [InlineData("   ", "host")]
    [InlineData("smtp.example.com.", "host")]
    [InlineData("smtp..example.com", "host")]
    [InlineData("smtp_1.example.com", "host")]
    [InlineData("münchen.example", "host")]
    [InlineData("[::1]", "host")]
    [InlineData("fe80::1%eth0", "host")]
    [InlineData("smtp.example.com:587", "host")]
    [InlineData("https://smtp.example.com", "host")]
    public void A_host_that_is_neither_a_name_nor_an_address_is_refused(string host, string field) =>
        AssertError(Good with { Host = host }, field);

    [Fact]
    public void A_host_label_longer_than_63_characters_or_a_host_longer_than_253_is_refused()
    {
        AssertError(Good with { Host = new string('a', 64) + ".example.com" }, "host");
        AssertError(Good with { Host = string.Join('.', Enumerable.Repeat(new string('a', 63), 4)) }, "host");
        Assert.True(SmtpSettingsRule.TryValidate(Good with { Host = new string('a', 63) + ".example.com" }, null, out _, out _));
    }

    [Theory]
    [InlineData("smtp.example.com")]
    [InlineData("xn--mnchen-3ya.example")]
    [InlineData("localhost")]
    [InlineData("192.0.2.10")]
    [InlineData("2001:db8::25")]
    [InlineData("::1")]
    public void A_host_name_an_IPv4_literal_and_a_bare_IPv6_literal_pass_the_syntax_rule(string host) =>
        Assert.True(SmtpSettingsRule.TryValidate(Good with { Host = host }, null, out _, out _));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_port_outside_1_to_65535_is_refused(int port) => AssertError(Good with { Port = port }, "port");

    [Fact]
    public void An_unknown_security_value_is_refused() => AssertError(Good with { Security = null }, "security");

    [Theory]
    [InlineData("not an email")]
    [InlineData("a@b@c")]
    [InlineData("Name <a@example.com>")]
    [InlineData("")]
    public void A_from_email_that_fails_the_email_rule_is_refused(string email) => AssertError(Good with { FromEmail = email }, "fromEmail");

    [Fact]
    public void An_address_longer_than_320_characters_is_refused()
    {
        var tooLong = new string('a', 310) + "@example.com";
        AssertError(Good with { FromEmail = tooLong }, "fromEmail");
        AssertError(Good with { ReplyTo = tooLong }, "replyTo");
    }

    [Fact]
    public void A_reply_to_that_fails_the_email_rule_is_refused() => AssertError(Good with { ReplyTo = "nope" }, "replyTo");

    [Theory]
    [InlineData("Line\nbreak")]
    [InlineData("Line\rbreak")]
    public void A_from_name_with_a_line_break_is_refused(string name) => AssertError(Good with { FromName = name }, "fromName");

    [Fact]
    public void A_from_name_longer_than_128_characters_is_refused()
    {
        AssertError(Good with { FromName = new string('n', 129) }, "fromName");
        Assert.True(SmtpSettingsRule.TryValidate(Good with { FromName = new string('n', 128) }, null, out _, out _));
    }

    [Fact]
    public void A_username_is_refused_without_encryption()
    {
        Assert.False(SmtpSettingsRule.TryValidate(Good with { Security = SmtpSecurity.None }, null, out _, out var error));

        Assert.Equal("username: A username needs STARTTLS or TLS.", error);
    }

    [Fact]
    public void No_username_means_no_password()
    {
        Assert.True(SmtpSettingsRule.TryValidate(Good with { Security = SmtpSecurity.None, Username = null, Password = "ignored" }, null, out var settings, out _));

        Assert.Equal(SmtpPasswordSource.None, settings.PasswordSource);
        Assert.Null(settings.Password);
    }

    // AC-1: a stored password is reused only for the same host, port, and username.
    [Fact]
    public void A_null_password_keeps_the_stored_one_for_the_same_server()
    {
        var stored = new StoredSmtpIdentity("smtp.example.com", 587, "apikey", HasPassword: true);

        Assert.True(SmtpSettingsRule.TryValidate(Good with { Password = null, FromName = "Renamed" }, stored, out var settings, out _));

        Assert.Equal(SmtpPasswordSource.Stored, settings.PasswordSource);
        Assert.Null(settings.Password);
    }

    [Theory]
    [InlineData("evil.example.net", 587, "apikey")]
    [InlineData("smtp.example.com", 2525, "apikey")]
    [InlineData("smtp.example.com", 587, "someone-else")]
    public void A_null_password_is_refused_when_the_host_port_or_username_changed(string host, int port, string username)
    {
        var stored = new StoredSmtpIdentity("smtp.example.com", 587, "apikey", HasPassword: true);

        Assert.False(SmtpSettingsRule.TryValidate(Good with { Host = host, Port = port, Username = username, Password = null }, stored, out _, out var error));

        Assert.Equal("password: Enter the password again when you change the host, port, or username.", error);
    }

    [Fact]
    public void A_null_password_is_refused_when_none_is_stored()
    {
        AssertError(Good with { Password = null }, "password");
        AssertError(Good with { Password = "" }, "password");
        Assert.False(SmtpSettingsRule.TryValidate(
            Good with { Password = null }, new StoredSmtpIdentity("smtp.example.com", 587, "apikey", HasPassword: false), out _, out _));
    }

    private static void AssertError(SmtpSettingsDraft draft, string field)
    {
        Assert.False(SmtpSettingsRule.TryValidate(draft, null, out _, out var error));
        Assert.StartsWith(field + ": ", error, StringComparison.Ordinal);
    }
}

public class GlobalUnicastTests
{
    // AC-3's list, one address from each range.
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("255.255.255.255")]
    [InlineData("240.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::10.0.0.1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:a00:1::1")]
    [InlineData("2001::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("::ffff:169.254.169.254")]
    public void An_address_outside_global_unicast_is_refused(string address) =>
        Assert.False(GlobalUnicast.Contains(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.1")]
    [InlineData("223.255.255.255")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001::1")]
    [InlineData("::ffff:8.8.8.8")]
    public void A_global_unicast_address_is_allowed(string address) =>
        Assert.True(GlobalUnicast.Contains(IPAddress.Parse(address)));

    [Fact]
    public void One_private_address_among_public_ones_refuses_the_host() =>
        Assert.False(GlobalUnicast.ContainsAll([IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.5")]));

    [Theory]
    [InlineData("127.1")]
    [InlineData("2130706433")]
    [InlineData("0x7f.0.0.1")]
    public void An_oddly_spelled_address_is_still_read_as_the_address_it_reaches(string host)
    {
        Assert.True(SmtpHost.TryGetLiteral(host, out var address));
        Assert.False(GlobalUnicast.Contains(address));
    }
}

public class SmtpOutcomesTests
{
    // The SMTP outcomes table, row by row: the code, what the worker does, and what a test answers.
    [Theory]
    [InlineData(nameof(SmtpFailureKind.Unreachable), null, ErrorCode.SmtpUnreachable, true, 502)]
    [InlineData(nameof(SmtpFailureKind.Timeout), null, ErrorCode.SmtpTimeout, true, 504)]
    [InlineData(nameof(SmtpFailureKind.TlsFailed), null, ErrorCode.SmtpTlsFailed, false, 502)]
    [InlineData(nameof(SmtpFailureKind.AuthFailed), 535, ErrorCode.SmtpAuthFailed, false, 502)]
    [InlineData(nameof(SmtpFailureKind.AuthFailed), 454, ErrorCode.SmtpAuthFailed, true, 502)]
    [InlineData(nameof(SmtpFailureKind.AuthFailed), null, ErrorCode.SmtpAuthFailed, false, 502)]
    [InlineData(nameof(SmtpFailureKind.Rejected), 451, ErrorCode.SmtpRejected, true, 502)]
    [InlineData(nameof(SmtpFailureKind.Rejected), 550, ErrorCode.SmtpRejected, false, 502)]
    [InlineData(nameof(SmtpFailureKind.HostNotAllowed), null, ErrorCode.SmtpHostNotAllowed, false, 400)]
    public void Each_failure_maps_to_its_code_retry_rule_and_test_status(string kindName, int? reply, string code, bool retries, int status)
    {
        var kind = Enum.Parse<SmtpFailureKind>(kindName);
        var failure = new SmtpFailure(kind, reply);

        Assert.Equal(code, SmtpOutcomes.Code(kind));
        Assert.Equal(retries, SmtpOutcomes.Retries(failure));
        Assert.Equal(status, SmtpOutcomes.TestStatus(kind));
    }

    [Fact]
    public void A_rejected_email_shows_the_reply_code_and_text_cut_to_200_characters()
    {
        Assert.Equal("550 5.1.1 No such user", SmtpOutcomes.Detail(new SmtpFailure(SmtpFailureKind.Rejected, 550, "5.1.1 No such user")));

        var detail = SmtpOutcomes.Detail(new SmtpFailure(SmtpFailureKind.Rejected, 550, "line one\r\nline two " + new string('x', 400)));
        Assert.Equal(200, detail.Length);
        Assert.DoesNotContain('\n', detail);
        Assert.DoesNotContain('\r', detail);
    }

    [Fact]
    public void Every_other_failure_shows_a_fixed_sentence_never_the_servers_words()
    {
        foreach (var kind in Enum.GetValues<SmtpFailureKind>().Where(k => k != SmtpFailureKind.Rejected))
            Assert.DoesNotContain("leaked", SmtpOutcomes.Detail(new SmtpFailure(kind, 535, "leaked by the server")));
    }
}

public class SmtpSenderTests
{
    [Fact]
    public void A_message_is_multipart_with_the_sender_the_reply_address_and_the_automatic_header()
    {
        using var message = SmtpSender.Build(new OutgoingEmail(
            "hello@example.com", "Example", "support@example.com", "grace@example.com", "0199@orvano.example.com",
            new EmailContent("Subject", "<p>Hi</p>", "Hi")));

        Assert.Equal("\"Example\" <hello@example.com>".Replace("\"", ""), message.From.ToString().Replace("\"", ""));
        Assert.Equal("support@example.com", message.ReplyTo.Mailboxes.Single().Address);
        Assert.Equal("grace@example.com", message.To.Mailboxes.Single().Address);
        Assert.Equal("0199@orvano.example.com", message.MessageId);
        Assert.Equal("auto-generated", message.Headers["Auto-Submitted"]);
        Assert.Equal("Hi", message.TextBody);
        Assert.Equal("<p>Hi</p>", message.HtmlBody);
        Assert.StartsWith("multipart/alternative", message.Body!.ContentType.MimeType, StringComparison.Ordinal);
    }

    [Fact]
    public void The_test_email_names_the_project_and_encodes_it_in_the_HTML_part()
    {
        var email = TestEmail.ForProject("Tom & <Jerry>");

        Assert.Equal("Test email from Orvano", email.Subject);
        Assert.Contains("the project Tom & <Jerry>", email.Text, StringComparison.Ordinal);
        Assert.Contains("the project Tom &amp; &lt;Jerry&gt;", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<Jerry>", email.Html, StringComparison.Ordinal);
    }
}

public class SmtpConnectorTests
{
    private sealed class FixedResolver(params string[] addresses) : IHostResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(addresses.Select(IPAddress.Parse).ToArray());
    }

    [Fact]
    public async Task A_name_with_any_private_address_is_refused_before_anything_is_dialed()
    {
        var connector = new SmtpConnector(new FixedResolver("8.8.8.8", "169.254.169.254"));

        await Assert.ThrowsAsync<SmtpHostNotAllowedException>(() =>
            connector.ConnectAsync("rebind.example.com", 25, publicOnly: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_private_literal_is_refused_for_a_project_and_dialed_for_the_install()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connector = new SmtpConnector(new FixedResolver());

        await Assert.ThrowsAsync<SmtpHostNotAllowedException>(() =>
            connector.ConnectAsync("127.0.0.1", port, publicOnly: true, TestContext.Current.CancellationToken));
        using var socket = await connector.ConnectAsync("127.0.0.1", port, publicOnly: false, TestContext.Current.CancellationToken);
        Assert.True(socket.Connected);
    }
}

public class MessagingSettingsTests
{
    // AC-28: unset or empty means the default, since Compose passes ${VAR:-} as an empty string.
    [Theory]
    [InlineData(null, null, false, 200)]
    [InlineData("", "", false, 200)]
    [InlineData("true", "1", true, 1)]
    [InlineData("false", "1000000", false, 1_000_000)]
    public void The_settings_are_read_with_their_defaults(string? allow, string? limit, bool expectedAllow, int expectedLimit)
    {
        var settings = Configure(allow, limit).GetRequiredService<MessagingSettings>();

        Assert.Equal(new MessagingSettings(expectedAllow, expectedLimit), settings);
    }

    [Theory]
    [InlineData("yes", null, "ORVANO_SMTP_ALLOW_PRIVATE_HOSTS must be true or false, got 'yes'.")]
    [InlineData("True", null, "ORVANO_SMTP_ALLOW_PRIVATE_HOSTS must be true or false, got 'True'.")]
    [InlineData(null, "0", "ORVANO_EMAIL_INSTALL_HOURLY_LIMIT must be a whole number from 1 to 1000000, got '0'.")]
    [InlineData(null, "1000001", "ORVANO_EMAIL_INSTALL_HOURLY_LIMIT must be a whole number from 1 to 1000000, got '1000001'.")]
    [InlineData(null, "many", "ORVANO_EMAIL_INSTALL_HOURLY_LIMIT must be a whole number from 1 to 1000000, got 'many'.")]
    public void A_bad_value_refuses_to_start(string? allow, string? limit, string message)
    {
        var error = Assert.Throws<OrvanoConfigException>(() => Configure(allow, limit));

        Assert.Equal(message, error.Message);
    }

    private static ServiceProvider Configure(string? allow, string? limit)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MessagingSettings.AllowPrivateHostsSetting] = allow,
                [MessagingSettings.InstallHourlyLimitSetting] = limit,
            })
            .Build();
        var services = new ServiceCollection();
        new MessagingModule().ConfigureServices(services, config);
        return services.BuildServiceProvider();
    }
}
