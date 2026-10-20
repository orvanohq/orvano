using Orvano.Infrastructure.Signals;

using Shouldly;

using Xunit;

namespace Orvano.Infrastructure.Tests.Signals;

// Spec 0013 LSN-5, AC-9: the reconnect delay is full jitter between 0 and min(cap, 1 s × 2^(n − 1)).
public sealed class SignalBackoffTests
{
    private const int Draws = 2000;

    [Theory]
    [Trait("Spec", "0013:LSN-5"), Trait("Spec", "0013:AC-9")]
    [InlineData(1, 30, 1000)]
    [InlineData(2, 30, 2000)]
    [InlineData(5, 30, 16000)]
    [InlineData(6, 30, 30000)]
    [InlineData(40, 30, 30000)]
    [InlineData(3, 2, 2000)]
    [InlineData(1, 2, 1000)]
    public void Backoff_stays_between_zero_and_the_smaller_of_the_cap_and_the_doubling_when_drawn_many_times(
        int failedAttempts, int capSeconds, int ceilingMilliseconds)
    {
        var ceiling = TimeSpan.FromMilliseconds(ceilingMilliseconds);

        TimeSpan[] delays = [.. Enumerable.Range(0, Draws).Select(_ => SignalListener.Backoff(failedAttempts, TimeSpan.FromSeconds(capSeconds)))];

        delays.ShouldAllBe(delay => delay >= TimeSpan.Zero && delay <= ceiling);
        // Full jitter spreads over the whole range: all draws landing in the lower half has a chance of 2^-2000.
        delays.Max().ShouldBeGreaterThan(ceiling / 2);
    }

    [Fact]
    [Trait("Spec", "0013:LSN-5")]
    public void Backoff_stays_under_the_cap_and_does_not_throw_when_the_attempts_and_the_cap_are_huge()
    {
        var cap = TimeSpan.FromDays(365);

        TimeSpan[] delays = [.. Enumerable.Range(0, Draws).Select(_ => SignalListener.Backoff(int.MaxValue, cap))];

        delays.ShouldAllBe(delay => delay >= TimeSpan.Zero && delay <= cap);
    }
}
