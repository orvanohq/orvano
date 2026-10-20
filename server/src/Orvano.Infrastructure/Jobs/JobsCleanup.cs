using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Orvano.Infrastructure.Jobs.Data;
using Orvano.SharedKernel.Jobs;

namespace Orvano.Infrastructure.Jobs;

/// <summary>
/// The built in daily cleanup (spec 0013 SCH-7, spec 0007 JOB-3): deletes finished jobs past their retention, which also
/// frees their dedupe keys.
/// </summary>
internal sealed record JobsCleanup : IJob
{
    public const string ScheduleName = "jobs.cleanup";

    /// <summary>Daily at 03:17 UTC.</summary>
    public const string Cron = "17 3 * * *";

    public static string Kind => "jobs.cleanup";
}

internal sealed class JobsCleanupHandler(JobsDbContext db, TimeProvider time, ILogger<JobsCleanupHandler> logger) : IJobHandler<JobsCleanup>
{
    /// <summary>Rows per statement, so one cleanup never holds a long lock.</summary>
    public const int Chunk = 5000;

    public static readonly TimeSpan SucceededRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan DeadRetention = TimeSpan.FromDays(30);

    public async Task HandleAsync(JobsCleanup job, JobContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        int succeeded = await DeleteAsync("succeeded", now - SucceededRetention, cancellationToken);
        int dead = await DeleteAsync("dead", now - DeadRetention, cancellationToken);
        logger.JobsCleanedUp(succeeded, dead);
    }

    /// <summary>Deletes <paramref name="status"/> jobs finished before <paramref name="before"/>, a chunk per statement.</summary>
    private async Task<int> DeleteAsync(string status, DateTimeOffset before, CancellationToken cancellationToken)
    {
        int total = 0;
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlAsync(
                $"""
                DELETE FROM jobs.job WHERE id IN (
                    SELECT id FROM jobs.job WHERE status = {status} AND finished_at < {before} LIMIT {Chunk})
                """,
                cancellationToken);
            total += deleted;
        }
        while (deleted == Chunk);

        return total;
    }
}

/// <summary>Infrastructure's own job payloads (spec 0002 JSN-1).</summary>
[JsonSerializable(typeof(JobsCleanup))]
internal sealed partial class InfrastructureJsonContext : JsonSerializerContext;
