using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Orvano.Server.Hosting;
using Orvano.Server.Modules;

namespace Orvano.Server.Tests.Hosting;

// Spec 0010: test.getLatestEmail reads Mailpit's text part, and ORVANO_TEST_MAILPIT_URL is allowed only in Test.
public class TestEmailsTests
{
    [Fact]
    public void Reads_the_first_link_with_a_token_or_else_the_first_six_digits()
    {
        var link = TestEmails.Read("Reset your password", """
            Hi,

            Visit https://docs.example.com/help for help.
            Reset your password: http://localhost:3000/cb?next=1&orvano_type=recovery&orvano_token=orv_el_abc

            It works for 1 hour (until 123456 or so).
            """);
        Assert.Equal("Reset your password", link.Subject);
        Assert.Equal("recovery", link.Type);
        Assert.Equal("orv_el_abc", link.Token);
        Assert.Equal("http://localhost:3000/cb?next=1&orvano_type=recovery&orvano_token=orv_el_abc", link.Url);
        Assert.Null(link.Code);

        var code = TestEmails.Read("Your code", "Enter this code to sign in to Shop as ada1234567@x.com:\n\n042137\n\nThis code works for 10 minutes.");
        Assert.Equal("042137", code.Code);
        Assert.Null(code.Token);
        Assert.Null(code.Url);
    }

    [Theory]
    [InlineData("Test", "http://mailpit:8025", true)]
    [InlineData("Test", "", true)]
    [InlineData("Production", "http://mailpit:8025", false)]
    [InlineData("Development", "http://mailpit:8025", false)]
    [InlineData("Test", "mailpit:8025", false)]
    [InlineData("Test", "ftp://mailpit", false)]
    [InlineData("Test", "/relative", false)]
    public void The_mailpit_setting_is_allowed_only_in_test_as_an_http_url(string environment, string value, bool usable)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TestMailpit.Setting] = value }).Build();
        var mailpit = TestMailpit.Load(new HostingEnvironment { EnvironmentName = environment }, config);

        Assert.Equal(usable, mailpit.Problem is null);
        if (!usable) Assert.DoesNotContain("mailpit:8025", mailpit.Problem!, StringComparison.Ordinal);
    }
}
