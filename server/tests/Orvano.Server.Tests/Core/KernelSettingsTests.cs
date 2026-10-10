using System.Diagnostics.Metrics;
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

    // Spec 0014 AC-39: each refusal adds 1 to orvano.auth.limit_refused, tagged with the policy name and nothing else,
    // so the email or address a limit counted never reaches a metric.
    [Fact]
    public void A_refusal_counts_on_the_metric_by_policy_name_only()
    {
        using var limits = new RateLimits();
        var policy = new RateLimitPolicy($"test.metric.{Guid.NewGuid():N}", 1, TimeSpan.FromMinutes(15));
        var seen = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "orvano.auth.limit_refused") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var copy = tags.ToArray();
            if (copy.Any(t => Equals(t.Value, policy.Name)))
                lock (seen) seen.AddRange(Enumerable.Repeat(copy, (int)value));
        });
        listener.Start();

        Assert.True(limits.Acquire(policy, "ada@example.com\n203.0.113.7").Allowed);
        Assert.False(limits.Acquire(policy, "ada@example.com\n203.0.113.7").Allowed);
        Assert.False(limits.Check(policy, "ada@example.com\n203.0.113.7").Allowed);

        Assert.Equal(2, seen.Count);
        Assert.All(seen, tags => Assert.Equal([new KeyValuePair<string, object?>("policy", policy.Name)], tags));
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
        Assert.Equal((300, TimeSpan.FromMinutes(15)), (RateLimitPolicies.SignInPerIp.PermitLimit, RateLimitPolicies.SignInPerIp.Window));
        Assert.Equal((60, TimeSpan.FromHours(1)), (RateLimitPolicies.SignUpPerIp.PermitLimit, RateLimitPolicies.SignUpPerIp.Window));
        Assert.Equal((10, TimeSpan.FromMinutes(15)), (RateLimitPolicies.PasswordCheckPerUser.PermitLimit, RateLimitPolicies.PasswordCheckPerUser.Window));
        Assert.Equal((60, TimeSpan.FromMinutes(15)), (RateLimitPolicies.RefreshPerSession.PermitLimit, RateLimitPolicies.RefreshPerSession.Window));
        Assert.Equal((60, TimeSpan.FromMinutes(15)), (RateLimitPolicies.FailedRefreshPerIp.PermitLimit, RateLimitPolicies.FailedRefreshPerIp.Window));
        Assert.Equal((60, TimeSpan.FromMinutes(1)), (RateLimitPolicies.ConsoleSetupPerIp.PermitLimit, RateLimitPolicies.ConsoleSetupPerIp.Window));
    }

    // Spec 0013 AC-32: the MFA and passkey limits of its Rate limits table, by name, limit, and window.
    [Theory]
    [InlineData("auth.mfa_failed.user_ip", 10)]
    [InlineData("auth.mfa_ticket_failed.ip", 60)]
    [InlineData("auth.passkey.ip", 300)]
    [InlineData("auth.passkey_challenge.user", 30)]
    [InlineData("auth.passkey_failed.ip", 60)]
    [InlineData("auth.mfa_enroll.user", 10)]
    public void Mfa_and_passkey_policies_match_spec_0013(string name, int limit)
    {
        RateLimitPolicy[] policies =
        [
            RateLimitPolicies.FailedMfaPerUserIp, RateLimitPolicies.FailedMfaTicketPerIp, RateLimitPolicies.PasskeyPerIp,
            RateLimitPolicies.PasskeyChallengePerUser, RateLimitPolicies.FailedPasskeyPerIp, RateLimitPolicies.MfaEnrollPerUser,
        ];

        var policy = Assert.Single(policies, p => p.Name == name);

        Assert.Equal((limit, TimeSpan.FromMinutes(15)), (policy.PermitLimit, policy.Window));
    }

    // Spec 0014's Rate limits table: the new and rekeyed policies, by name, default limit, and window.
    [Theory]
    [InlineData("auth.sign_in.ip", 300, 15)]
    [InlineData("auth.sign_in_failed.email_ip", 10, 15)]
    [InlineData("auth.sign_in_failed.ip", 100, 15)]
    [InlineData("auth.sign_up.ip", 60, 60)]
    [InlineData("auth.anonymous.ip", 30, 60)]
    [InlineData("auth.anonymous.project", 1000, 60)]
    [InlineData("auth.email_send.ip", 300, 60)]
    [InlineData("auth.email_send.recipient", 5, 60)]
    [InlineData("auth.email_send.recipient_total", 20, 60)]
    [InlineData("auth.email_code_failed.recipient_ip", 5, 15)]
    [InlineData("auth.email_code_failed.recipient", 30, 60)]
    [InlineData("auth.mfa_totp_failed.user", 60, 60)]
    [InlineData("auth.api_key_failed.ip", 60, 15)]
    [InlineData("console.invite_email.recipient", 5, 60)]
    public void Policies_match_spec_0014(string name, int limit, int windowMinutes)
    {
        var policy = Assert.Single(
            typeof(RateLimitPolicies).GetProperties().Select(p => (RateLimitPolicy)p.GetValue(null)!), p => p.Name == name);
        Assert.Equal((limit, TimeSpan.FromMinutes(windowMinutes)), (policy.PermitLimit, policy.Window));
    }

    [Fact]
    public void The_replaced_spec_0013_and_0010_policies_are_gone()
    {
        var names = typeof(RateLimitPolicies).GetProperties().Select(p => ((RateLimitPolicy)p.GetValue(null)!).Name).ToList();
        Assert.DoesNotContain("auth.sign_in.email", names);
        Assert.DoesNotContain("auth.mfa_failed.user", names);
        Assert.DoesNotContain("auth.email_code.recipient", names);
    }

    [Fact]
    public void A_reservation_counts_while_in_flight_and_only_a_failure_stays()
    {
        using var limits = new RateLimits();
        var policy = new RateLimitPolicy("test.reserve", 2, TimeSpan.FromMinutes(15));

        using (var first = limits.Reserve((policy, "a")))
        using (var second = limits.Reserve((policy, "a")))
        {
            Assert.True(first.Allowed);
            Assert.True(second.Allowed);
            // Two in flight fill the limit, so a parallel third is refused.
            using var third = limits.Reserve((policy, "a"));
            Assert.False(third.Allowed);
            Assert.InRange(third.Decision.RetryAfterSeconds, 1, 15 * 60);
            first.Fail();
        }

        // The released one freed its slot; the failure stays.
        Assert.True(limits.Check(policy, "a").Allowed);
        using (var again = limits.Reserve((policy, "a"))) again.Fail();
        Assert.False(limits.Check(policy, "a").Allowed);
    }

    [Fact]
    public void A_refused_reservation_holds_nothing_and_fail_can_name_one_policy()
    {
        using var limits = new RateLimits();
        var wide = new RateLimitPolicy("test.wide", 10, TimeSpan.FromMinutes(15));
        var narrow = new RateLimitPolicy("test.narrow", 1, TimeSpan.FromMinutes(15));
        using (var hold = limits.Reserve((narrow, "k"))) hold.Fail();

        using (var refused = limits.Reserve((wide, "k"), (narrow, "k"))) Assert.False(refused.Allowed);
        using (var both = limits.Reserve((wide, "k"))) both.Fail(wide);

        // The refused reservation released its slot under "wide": one failure only, so nine are left.
        for (var i = 0; i < 9; i++) Assert.True(limits.Acquire(wide, "k").Allowed);
        Assert.False(limits.Acquire(wide, "k").Allowed);
    }

    [Fact]
    public void A_window_ends_and_a_changed_limit_starts_fresh_counters()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-10-09T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        using var limits = new RateLimits(clock, 100);
        var policy = new RateLimitPolicy("test.window", 1, TimeSpan.FromMinutes(15));
        Assert.True(limits.Acquire(policy, "a").Allowed);
        Assert.False(limits.Acquire(policy, "a").Allowed);
        Assert.True(limits.Acquire(policy with { PermitLimit = 2 }, "a").Allowed);

        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.True(limits.Acquire(policy, "a").Allowed);
    }

    [Fact]
    public void A_policy_keeps_at_most_its_key_cap_dropping_the_oldest_windows()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-10-09T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        using var limits = new RateLimits(clock, 50);
        var policy = new RateLimitPolicy("test.cap", 1, TimeSpan.FromHours(1));
        Assert.True(limits.Acquire(policy, "oldest").Allowed);
        for (var i = 0; i < 200; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            limits.Acquire(policy, $"key-{i}");
        }

        Assert.InRange(limits.KeyCount(policy), 1, 50);
        // The oldest key was dropped, so it starts again; the newest is still counted.
        Assert.True(limits.Acquire(policy, "oldest").Allowed);
        Assert.False(limits.Acquire(policy, "key-199").Allowed);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan by) => current += by;
    }
}
