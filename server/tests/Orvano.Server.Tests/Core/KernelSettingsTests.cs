using System.Net;
using Orvano.Core;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;

namespace Orvano.Server.Tests.Core;

// Spec 0004, configuration required (ORVANO_PUBLIC_URL, ORVANO_TRUSTED_PROXIES) and rate limits (AC-30).
public class KernelSettingsTests
{
    [Theory]
    [InlineData("https://orvano.example.com", "https://orvano.example.com")]
    [InlineData("https://orvano.example.com/", "https://orvano.example.com")]
    [InlineData("HTTP://LocalHost:8080", "http://localhost:8080")]
    [InlineData("https://orvano.example.com:443", "https://orvano.example.com")]
    public void Public_url_keeps_the_origin(string value, string origin) =>
        Assert.Equal(origin, PublicUrl.Parse(value).Origin);

    [Theory]
    [InlineData("orvano.example.com")]
    [InlineData("ftp://orvano.example.com")]
    [InlineData("https://orvano.example.com/console")]
    [InlineData("https://orvano.example.com?x=1")]
    [InlineData("https://orvano.example.com#top")]
    [InlineData("https://user:pass@orvano.example.com")]
    public void Public_url_refuses_anything_but_an_origin(string value)
    {
        var error = Assert.Throws<OrvanoConfigException>(() => PublicUrl.Parse(value));

        Assert.StartsWith("ORVANO_PUBLIC_URL must be an absolute http or https URL", error.Message);
    }

    [Fact]
    public void A_dev_master_key_of_42_random_letters_and_digits_and_an_A_is_32_bytes()
    {
        // The AppHost generates exactly this shape (dev/Orvano.AppHost), read as base64url: the 43rd
        // character carries 2 unused bits, which must be zero, as they are in `A`.
        for (var i = 0; i < 200; i++)
        {
            var key = System.Security.Cryptography.RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 42);

            Assert.Equal("kdev", Orvano.Core.Secrets.MasterKeys.Parse($"kdev:{key}A").ActiveId);
        }
    }

    [Fact]
    public void Trusted_proxies_default_to_the_private_ranges()
    {
        var proxies = TrustedProxies.Parse("private");

        Assert.Contains(proxies.Networks, n => n.Contains(IPAddress.Parse("172.18.0.5")));
        Assert.Contains(proxies.Networks, n => n.Contains(IPAddress.Loopback));
        Assert.DoesNotContain(proxies.Networks, n => n.Contains(IPAddress.Parse("203.0.113.9")));
    }

    [Fact]
    public void Trusted_proxies_read_none_ranges_and_single_addresses()
    {
        Assert.Empty(TrustedProxies.Parse("none").Networks);

        var list = TrustedProxies.Parse("10.1.0.0/16, 203.0.113.9, 2001:db8::/32");

        Assert.Equal(3, list.Networks.Count);
        Assert.Contains(list.Networks, n => n.Contains(IPAddress.Parse("203.0.113.9")));
        Assert.DoesNotContain(list.Networks, n => n.Contains(IPAddress.Parse("203.0.113.10")));
    }

    [Theory]
    [InlineData("everyone")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/8,")]
    public void Trusted_proxies_refuse_a_bad_entry(string value)
    {
        var error = Assert.Throws<OrvanoConfigException>(() => TrustedProxies.Parse(value));

        Assert.StartsWith("ORVANO_TRUSTED_PROXIES must be", error.Message);
    }

    [Fact]
    public void A_limit_refuses_past_its_permits_per_key_with_a_retry_after()
    {
        using var limits = new RateLimits();
        var policy = new RateLimitPolicy("test.three", 3, TimeSpan.FromMinutes(15));

        for (var i = 0; i < 3; i++) Assert.True(limits.Acquire(policy, "a").Allowed);
        var refused = limits.Acquire(policy, "a");

        Assert.False(refused.Allowed);
        Assert.InRange(refused.RetryAfterSeconds, 1, 15 * 60);
        Assert.True(limits.Acquire(policy, "b").Allowed);
    }

    [Fact]
    public void A_check_takes_no_permit()
    {
        using var limits = new RateLimits();
        var policy = new RateLimitPolicy("test.one", 1, TimeSpan.FromMinutes(15));

        Assert.True(limits.Check(policy, "a").Allowed);
        Assert.True(limits.Check(policy, "a").Allowed);
        Assert.True(limits.Acquire(policy, "a").Allowed);
        Assert.False(limits.Check(policy, "a").Allowed);
    }

    [Fact]
    public void Built_in_policies_match_the_spec()
    {
        Assert.Equal((10, TimeSpan.FromMinutes(15)), (RateLimitPolicies.SignInPerEmail.PermitLimit, RateLimitPolicies.SignInPerEmail.Window));
        Assert.Equal((300, TimeSpan.FromMinutes(15)), (RateLimitPolicies.SignInPerIp.PermitLimit, RateLimitPolicies.SignInPerIp.Window));
        Assert.Equal((60, TimeSpan.FromHours(1)), (RateLimitPolicies.SignUpPerIp.PermitLimit, RateLimitPolicies.SignUpPerIp.Window));
        Assert.Equal((10, TimeSpan.FromMinutes(15)), (RateLimitPolicies.PasswordCheckPerUser.PermitLimit, RateLimitPolicies.PasswordCheckPerUser.Window));
        Assert.Equal((60, TimeSpan.FromMinutes(15)), (RateLimitPolicies.RefreshPerSession.PermitLimit, RateLimitPolicies.RefreshPerSession.Window));
        Assert.Equal((60, TimeSpan.FromMinutes(15)), (RateLimitPolicies.FailedRefreshPerIp.PermitLimit, RateLimitPolicies.FailedRefreshPerIp.Window));
        Assert.Equal((60, TimeSpan.FromMinutes(1)), (RateLimitPolicies.ConsoleSetupPerIp.PermitLimit, RateLimitPolicies.ConsoleSetupPerIp.Window));
    }
}
