using Microsoft.Extensions.Options;

using Orvano.Infrastructure.Jobs;

namespace Orvano.Host.Database;

/// <summary>
/// The pool rule (spec 0013 JOP-2, JOP-4, spec 0007 JOB-4, PG-4): job concurrency is at most half the platform pool less
/// one, the connection the runner's own loop uses. Then jobs use at most <c>Concurrency</c> + 1 connections in the steady
/// state (half the pool, leaving the other half for requests) and 2 × <c>Concurrency</c> + 1 in the worst case (every
/// handler holding one while every renewal fires), which still leaves a request one connection. It lives here because
/// the Host owns <see cref="DatabaseOptions"/> (CFG-5).
/// </summary>
internal sealed class JobConcurrency(IOptions<DatabaseOptions> database) : IPostConfigureOptions<JobOptions>, IValidateOptions<JobOptions>
{
    private const int DefaultCap = 16;

    /// <summary>The most job handlers a pool of <paramref name="poolSize"/> allows: half of it, less one for the runner's loop.</summary>
    public static int Limit(int poolSize) => (poolSize / 2) - 1;

    /// <summary>The smallest of processor count × 2, 16, and the limit, at least 1; it never fails the rule.</summary>
    public static int Default(int processorCount, int poolSize) => Math.Max(1, Math.Min(Math.Min(processorCount * 2, DefaultCap), Limit(poolSize)));

    public void PostConfigure(string? name, JobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Concurrency ??= Default(Environment.ProcessorCount, database.Value.PoolSize);
    }

    public ValidateOptionsResult Validate(string? name, JobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        int pool = database.Value.PoolSize;
        int limit = Limit(pool);
        return options.Concurrency is { } concurrency && concurrency > limit
            ? ValidateOptionsResult.Fail(
                $"ORVANO_JOBS__CONCURRENCY ({concurrency}) must be at most {limit}: half of ORVANO_DATABASE__POOLSIZE ({pool}) "
                + "less one connection for the job runner (spec 0007 JOB-4).")
            : ValidateOptionsResult.Success;
    }
}
