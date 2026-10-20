using Microsoft.Extensions.Options;

using Orvano.Infrastructure.Jobs;

namespace Orvano.Host.Database;

/// <summary>
/// The half pool rule (spec 0013 JOP-2, spec 0007 JOB-4, PG-4): job handlers may use at most half the platform pool, so
/// the other half stays free for requests. It lives here because the Host owns <see cref="DatabaseOptions"/> (CFG-5).
/// Half is the typical case, not a hard ceiling: each handler's renewal or outcome write takes one more connection
/// briefly, so with every handler holding one at once the jobs can use up to twice <c>Concurrency</c> plus one (JOP-4).
/// </summary>
internal sealed class JobConcurrency(IOptions<DatabaseOptions> database) : IPostConfigureOptions<JobOptions>, IValidateOptions<JobOptions>
{
    private const int DefaultCap = 16;

    /// <summary>The smallest of processor count × 2, 16, and half the pool, at least 1; it never fails the rule.</summary>
    public static int Default(int processorCount, int poolSize) => Math.Max(1, Math.Min(Math.Min(processorCount * 2, DefaultCap), poolSize / 2));

    public void PostConfigure(string? name, JobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Concurrency ??= Default(Environment.ProcessorCount, database.Value.PoolSize);
    }

    public ValidateOptionsResult Validate(string? name, JobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        int pool = database.Value.PoolSize;
        return options.Concurrency is { } concurrency && concurrency > pool / 2
            ? ValidateOptionsResult.Fail(
                $"ORVANO_JOBS__CONCURRENCY ({concurrency}) must be at most half of ORVANO_DATABASE__POOLSIZE ({pool}) (spec 0007 JOB-4).")
            : ValidateOptionsResult.Success;
    }
}
