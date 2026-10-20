using System.Security.Cryptography;

namespace Orvano.Infrastructure.Jobs;

/// <summary>
/// The retry delay after a failed attempt (spec 0013 OUT-3, spec 0007 JOB-3): d = min(1 hour, 10 s × 2^(attempt − 1)),
/// and the delay is d / 2 plus a random share of d / 2 ("equal jitter"), so a retry never comes sooner than half the
/// curve. The base, cap, and curve are constants, not options.
/// </summary>
internal static class JobBackoff
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Cap = TimeSpan.FromHours(1);

    /// <summary>The delay after failed attempt <paramref name="attempt"/> (1 or more), for <paramref name="random"/> in [0, 1).</summary>
    public static TimeSpan Delay(int attempt, double random)
    {
        // In double, with the exponent clamped, so attempt 100 cannot overflow.
        double exponent = Math.Clamp(attempt - 1, 0, 30);
        double d = Math.Min(Cap.TotalSeconds, Base.TotalSeconds * Math.Pow(2, exponent));
        return TimeSpan.FromSeconds((d / 2) + (Math.Clamp(random, 0, 1) * d / 2));
    }

    /// <summary>A value in [0, 1) for <see cref="Delay"/>.</summary>
    public static double NextRandom() => RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
}
