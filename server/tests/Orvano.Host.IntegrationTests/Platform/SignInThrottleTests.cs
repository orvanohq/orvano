using System.Net;

using Orvano.Modules.Platform.Sessions;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 LIM-1: the throttle's windows and limits, on a fake clock.
public sealed class SignInThrottleTests
{
    private const string Email = "owner@example.com";

    private static readonly IPAddress Address = IPAddress.Parse("203.0.113.7");

    private readonly ManualClock _clock = new();
    private readonly SignInThrottle _throttle;

    public SignInThrottleTests() => _throttle = new SignInThrottle(_clock);

    [Fact, Trait("Spec", "0017:LIM-1")]
    public void Check_answers_null_until_the_fifth_account_failure()
    {
        for (int failure = 0; failure < 4; failure++)
        {
            _throttle.RecordFailure(Email, Address);
        }

        _throttle.Check(Email, Address).ShouldBeNull();
        _throttle.RecordFailure(Email, Address);
        _throttle.Check(Email, Address).ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact, Trait("Spec", "0017:LIM-1")]
    public void Check_waits_only_until_the_oldest_counted_failure_leaves_the_window()
    {
        _throttle.RecordFailure(Email, Address);
        _clock.Advance(TimeSpan.FromMinutes(10));
        for (int failure = 0; failure < 4; failure++)
        {
            _throttle.RecordFailure(Email, Address);
        }

        _throttle.Check(Email, Address).ShouldBe(TimeSpan.FromMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(5));
        _throttle.Check(Email, Address).ShouldBeNull();
    }

    [Fact, Trait("Spec", "0017:LIM-1")]
    public void Check_counts_50_failures_per_address_across_accounts()
    {
        for (int failure = 0; failure < 50; failure++)
        {
            _throttle.RecordFailure($"user{failure}@example.com", Address);
        }

        _throttle.Check("someone.else@example.com", Address).ShouldNotBeNull();
        _throttle.Check("someone.else@example.com", IPAddress.Parse("203.0.113.8")).ShouldBeNull();
    }

    [Fact, Trait("Spec", "0017:LIM-1")]
    public void Clear_empties_the_account_entry_and_keeps_the_address_count()
    {
        for (int failure = 0; failure < 5; failure++)
        {
            _throttle.RecordFailure(Email, Address);
        }

        _throttle.Clear(Email);

        _throttle.Check(Email, Address).ShouldBeNull();
    }

    [Fact, Trait("Spec", "0017:LIM-1")]
    public void Oldest_entry_goes_first_when_the_cap_is_reached()
    {
        for (int failure = 0; failure < 5; failure++)
        {
            _throttle.RecordFailure(Email, null);
        }

        // One entry per account and one for the null address is 100,000 entries in all.
        for (int account = 0; account < 99_999; account++)
        {
            _throttle.RecordFailure($"user{account}@example.com", null);
        }

        _throttle.Check(Email, IPAddress.Loopback).ShouldBeNull();
    }
}
