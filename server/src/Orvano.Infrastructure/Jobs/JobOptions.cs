using System.ComponentModel.DataAnnotations;

namespace Orvano.Infrastructure.Jobs;

/// <summary>How a worker runs jobs (spec 0013 JOP-1, spec 0007 JOB-4).</summary>
public sealed class JobOptions : IValidatableObject
{
    public const string Section = "Jobs";

    public const int MaxConcurrency = 256;

    private static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often a worker polls for due jobs and fires schedules, 1 second to 5 minutes. Jobs due now also wake workers
    /// at once through the <c>job</c> signal; delayed jobs, retries, and schedules wait for the poll.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The most jobs one worker process runs at once, 1 to 256 and at most half of <c>ORVANO_DATABASE__POOLSIZE</c> less
    /// one (the runner's own connection). Unset, it is the smallest of processor count × 2, 16, and that limit (at least 1).
    /// </summary>
    public int? Concurrency { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PollInterval < MinPollInterval || PollInterval > MaxPollInterval)
        {
            yield return new ValidationResult("must be between 1 second (00:00:01) and 5 minutes (00:05:00)", [nameof(PollInterval)]);
        }

        if (Concurrency is < 1 or > MaxConcurrency)
        {
            yield return new ValidationResult($"must be between 1 and {MaxConcurrency}", [nameof(Concurrency)]);
        }
    }
}
