using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orvano.Core;
using Orvano.Messaging;
using Orvano.Messaging.Domain;

namespace Orvano.Server.Tests.Messaging;

// Spec 0011, AC-4: how ORVANO_INSTALL_SMTP_URL and ORVANO_INSTALL_SMTP_FROM are read.
public class InstallSmtpSeedRuleTests
{
    [Theory]
    [InlineData("smtp://mailpit:1025", "mailpit", 1025, "none", null, null)]
    [InlineData("smtp://Mail.Example.com", "mail.example.com", 25, "none", null, null)]
    [InlineData("smtp+starttls://user:pass@smtp.example.com", "smtp.example.com", 587, "starttls", "user", "pass")]
    [InlineData("smtps://us%40er:p%3Ass%2F@smtp.example.com:2465/", "smtp.example.com", 2465, "tls", "us@er", "p:ss/")]
    [InlineData("smtp://[::1]:1025", "::1", 1025, "none", null, null)]
    public void Reads_host_port_security_and_the_decoded_user_info(string url, string host, int port, string security, string? user, string? password)
    {
        var seed = InstallSmtpSeed.Parse(url, " orvano@local.test ", out var error);

        Assert.Null(error);
        Assert.Equal(new InstallSmtpSeed(host, port, SmtpSettingsRule.ParseSecurity(security), user, password, "orvano@local.test"), seed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void An_unset_or_empty_url_means_no_seed_whatever_the_from(string? url)
    {
        Assert.Null(InstallSmtpSeed.Parse(url, null, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("http://secret-host:25", "must be an smtp://, smtp+starttls://, or smtps:// URL")]
    [InlineData("secret-host:25", "must be an smtp://, smtp+starttls://, or smtps:// URL")]
    [InlineData("smtp://secret-host:25/path", "must be an smtp://, smtp+starttls://, or smtps:// URL")]
    [InlineData("smtp://secret-host:25?x=1", "must be an smtp://, smtp+starttls://, or smtps:// URL")]
    [InlineData("smtp://:25", "must be an smtp://, smtp+starttls://, or smtps:// URL")]
    [InlineData("smtp://secret-host:0", "has a port outside 1 to 65535")]
    [InlineData("smtp://secret-host:65536", "must be an smtp://, smtp+starttls://, or smtps:// URL")]
    [InlineData("smtp://user:secret-pass@secret-host", "can't carry a user or password with smtp://")]
    [InlineData("smtps://user@secret-host", "needs both a user and a password, or neither")]
    [InlineData("smtps://:secret-pass@secret-host", "needs both a user and a password, or neither")]
    [InlineData("smtps://secret_host", "is not usable: host:")]
    public void Refuses_a_bad_url_naming_the_setting_but_never_the_value(string url, string message)
    {
        Assert.Null(InstallSmtpSeed.Parse(url, "orvano@local.test", out var error));

        Assert.StartsWith("ORVANO_INSTALL_SMTP_URL ", error, StringComparison.Ordinal);
        Assert.Contains(message, error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "ORVANO_INSTALL_SMTP_FROM must be set to an email address when ORVANO_INSTALL_SMTP_URL is set.")]
    [InlineData("", "ORVANO_INSTALL_SMTP_FROM must be set to an email address when ORVANO_INSTALL_SMTP_URL is set.")]
    [InlineData("not-an-email", "ORVANO_INSTALL_SMTP_FROM is not an email address of at most 320 characters.")]
    public void Refuses_a_missing_or_bad_from(string? from, string message)
    {
        Assert.Null(InstallSmtpSeed.Parse("smtp://mailpit:1025", from, out var error));
        Assert.Equal(message, error);
    }

    [Fact]
    public void Its_text_form_never_shows_the_password()
    {
        var seed = InstallSmtpSeed.Parse("smtps://user:hunter2@smtp.example.com", "a@b.test", out _);

        Assert.DoesNotContain("hunter2", seed!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_role_refuses_to_start_on_a_bad_seed()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [InstallSmtpSeed.UrlSetting] = "smtp://mailpit:1025",
                [InstallSmtpSeed.FromSetting] = "",
            })
            .Build();

        var error = Assert.Throws<OrvanoConfigException>(() => new MessagingModule().ConfigureServices(new ServiceCollection(), config));

        Assert.Equal("ORVANO_INSTALL_SMTP_FROM must be set to an email address when ORVANO_INSTALL_SMTP_URL is set.", error.Message);
    }
}
