using Orvano.Infrastructure.Jobs;

using Shouldly;

using Xunit;

namespace Orvano.Infrastructure.Tests.Jobs;

// Spec 0013 OUT-3: d = min(1 hour, 10 s × 2^(attempt − 1)); the delay lies in [d / 2, d).
public sealed class JobBackoffTests
{
    private const double JustUnderOne = 0.999_999_999;

    [Theory]
    [Trait("Spec", "0013:OUT-3"), Trait("Spec", "0013:AC-21")]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(9, 2560)]
    [InlineData(20, 3600)]
    [InlineData(100, 3600)]
    public void Delay_is_half_the_curve_at_random_0_and_just_under_the_curve_at_random_near_1(int attempt, double curveSeconds)
    {
        JobBackoff.Delay(attempt, 0).ShouldBe(TimeSpan.FromSeconds(curveSeconds / 2));

        TimeSpan high = JobBackoff.Delay(attempt, JustUnderOne);
        high.ShouldBeLessThan(TimeSpan.FromSeconds(curveSeconds));
        high.ShouldBeGreaterThan(TimeSpan.FromSeconds(curveSeconds * 0.999));
    }

    [Fact]
    [Trait("Spec", "0013:OUT-3")]
    public void NextRandom_stays_in_0_to_1()
    {
        for (int i = 0; i < 1000; i++)
        {
            JobBackoff.NextRandom().ShouldBeInRange(0, JustUnderOne);
        }
    }
}
