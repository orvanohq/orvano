using System.Net;
using Orvano.Server.Install;

namespace Orvano.Server.Tests.Install;

// Spec 0006: the plain rules the installer decides with (AC-5, AC-7, AC-11, AC-13, Install layout).
public class InstallRulesTests
{
    [Theory]
    [InlineData(null, "0.1.0", nameof(InstallMode.Fresh))]
    [InlineData("", "0.1.0", nameof(InstallMode.Fresh))]
    [InlineData("0.1.0", "0.1.0", nameof(InstallMode.Repair))]
    [InlineData("0.1.0", "0.1.1", nameof(InstallMode.Upgrade))]
    [InlineData("0.9.3", "0.10.0", nameof(InstallMode.Upgrade))]
    [InlineData("1.9.9", "2.0.0", nameof(InstallMode.Upgrade))]
    public void Version_rule_picks_fresh_repair_or_upgrade(string? installed, string target, string expected)
    {
        var (mode, refusal) = VersionRule.Decide(installed, target);

        Assert.Null(refusal);
        Assert.Equal(expected, mode.ToString());
    }

    [Theory]
    [InlineData("0.10.0", "0.9.3")]
    [InlineData("1.0.0", "0.99.99")]
    public void Version_rule_refuses_a_downgrade(string installed, string target)
    {
        var (_, refusal) = VersionRule.Decide(installed, target);

        Assert.Equal($"Orvano {installed} is installed and this is {target}. Downgrades are not supported; restore a backup instead.", refusal);
    }

    [Theory]
    [InlineData("dev", "0.1.0")]
    [InlineData("0.1", "0.1.0")]
    [InlineData(null, "latest")]
    [InlineData(null, "v0.1.0")]
    [InlineData(null, "01.0.0")]
    public void Version_rule_refuses_anything_but_X_Y_Z(string? installed, string target)
    {
        var (_, refusal) = VersionRule.Decide(installed, target);

        Assert.NotNull(refusal);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("orvano.example.com")]
    [InlineData("a.b")]
    [InlineData("my-host.example.co.uk")]
    [InlineData("xn--bcher-kva.example")]
    public void Domain_rule_accepts_hostnames_and_localhost(string domain) => Assert.True(DomainRule.IsValid(domain));

    [Theory]
    [InlineData("")]
    [InlineData("example")]
    [InlineData("Orvano.Example.com")]
    [InlineData("https://orvano.example.com")]
    [InlineData("orvano.example.com:8443")]
    [InlineData("orvano.example.com/path")]
    [InlineData("192.168.1.10")]
    [InlineData("1.2.3.4.5")]
    [InlineData("::1")]
    [InlineData("under_score.example.com")]
    [InlineData("orvano..example.com")]
    [InlineData("localhost.")]
    [InlineData("127.0.0.1")]
    public void Domain_rule_refuses_everything_else(string domain) => Assert.False(DomainRule.IsValid(domain));

    [Fact]
    public void Domain_rule_refuses_long_labels_and_names()
    {
        Assert.True(DomainRule.IsValid(new string('a', 63) + ".com"));
        Assert.False(DomainRule.IsValid(new string('a', 64) + ".com"));
        var name = string.Join('.', Enumerable.Repeat(new string('a', 62), 4)) + ".com"; // 4 * 62 + 3 dots + 4 = 255 characters
        Assert.False(DomainRule.IsValid(name));
    }

    [Fact]
    public void Domain_rule_gives_https_for_a_domain_and_plain_http_for_localhost()
    {
        Assert.Equal("https://orvano.example.com", DomainRule.PublicUrl("orvano.example.com"));
        Assert.Equal("http://localhost", DomainRule.PublicUrl("localhost"));
        Assert.Equal("orvano.example.com", DomainRule.HostOf("https://orvano.example.com"));
        Assert.Null(DomainRule.HostOf(""));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("ops@example.com", true)]
    [InlineData("o'brien@example.com", true)]
    [InlineData("ops", false)]
    [InlineData("ops @example.com", false)]
    [InlineData("ops@@example.com", false)]
    public void Email_rule_allows_empty_or_one_at_sign(string email, bool valid) => Assert.Equal(valid, EmailRule.IsValid(email));

    [Fact]
    public void Pg_tuning_at_4096_MiB_equals_spec_0002()
    {
        Assert.Equal(new PgTuning("512MB", "1536MB", "8MB", "128MB", "1280M"), PgTuning.For(4096));
    }

    [Fact]
    public void Pg_tuning_clamps_small_and_large_servers()
    {
        Assert.Equal(new PgTuning("228MB", "684MB", "8MB", "64MB", "570M"), PgTuning.For(1824));
        Assert.Equal(new PgTuning("8192MB", "49152MB", "16MB", "1024MB", "40960M"), PgTuning.For(131072));
        Assert.Equal("8MB", PgTuning.For(8191).WorkMem);
        Assert.Equal("16MB", PgTuning.For(8192).WorkMem);
        Assert.Equal("128MB", PgTuning.For(512).SharedBuffers);
    }

    [Fact]
    public void Pg_tuning_reads_MemTotal_in_whole_MiB()
    {
        Assert.Equal(4095, PgTuning.ParseMemTotalMib("MemTotal:        4194303 kB\nMemFree: 1 kB\n"));
        Assert.Equal(15990, PgTuning.ParseMemTotalMib("MemTotal:       16374656 kB\n"));
        Assert.Null(PgTuning.ParseMemTotalMib("MemFree: 1 kB\n"));
    }

    [Fact]
    public void Secrets_have_the_install_layout_formats()
    {
        Assert.Matches("^[0-9a-f]{48}$", InstallSecrets.NewPassword());
        var key = InstallSecrets.NewMasterKey(new DateTimeOffset(2026, 9, 26, 23, 30, 0, TimeSpan.FromHours(-5)));
        Assert.StartsWith("k20260927:", key);
        Assert.Equal(32, Convert.FromBase64String(key["k20260927:".Length..]).Length);
        var token = InstallSecrets.NewSetupToken();
        Assert.True(InstallSecrets.IsSetupToken(token), token);
        Assert.NotEqual(token, InstallSecrets.NewSetupToken());
        Assert.NotEqual(InstallSecrets.NewPassword(), InstallSecrets.NewPassword());
    }

    [Theory]
    [InlineData("ost_", false)]
    [InlineData("ost_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("ost_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", false)]
    [InlineData("ost_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("xst_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("ost_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+A", false)]
    public void Setup_token_shape_is_ost_and_43_base64url_characters(string token, bool valid) =>
        Assert.Equal(valid, InstallSecrets.IsSetupToken(token));

    [Fact]
    public void Dns_check_matches_records_to_server_addresses()
    {
        var records = new[] { IPAddress.Parse("203.0.113.9"), IPAddress.Parse("2001:db8::1") };

        Assert.Equal([IPAddress.Parse("2001:db8::1")], DnsCheck.Matches(records, [IPAddress.Parse("2001:db8::1"), IPAddress.Loopback]));
        Assert.Equal([IPAddress.Parse("203.0.113.9")], DnsCheck.Matches(records, [IPAddress.Parse("::ffff:203.0.113.9")]));
        Assert.Empty(DnsCheck.Matches(records, [IPAddress.Loopback]));
        Assert.Null(DnsCheck.Problem("x.example.com", records, [records[0]]));
        Assert.Equal("x.example.com has no A or AAAA record yet, so HTTPS will fail until its DNS points to this server.", DnsCheck.Problem("x.example.com", [], []));
    }
}
