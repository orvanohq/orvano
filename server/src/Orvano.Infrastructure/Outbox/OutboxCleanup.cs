using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Orvano.Infrastructure.Jobs.Data;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Outbox;

namespace Orvano.Infrastructure.Outbox;

/// <summary>
/// The built in daily outbox cleanup (spec 0013 OCL-1, spec 0007 EVT-3): deletes dispatched events that occurred more
/// than 7 days ago, in every registered outbox. Never an undispatched one.
/// </summary>
internal sealed record OutboxCleanup : IJob
{
    public const string ScheduleName = "outbox.cleanup";

    /// <summary>Daily at 03:37 UTC, 20 minutes after <c>jobs.cleanup</c>.</summary>
    public const string Cron = "37 3 * * *";

    public static string Kind => "outbox.cleanup";
}

internal sealed class OutboxCleanupHandler(IOutboxRegistry registry, JobsDbContext db, TimeProvider time, ILogger<OutboxCleanupHandler> logger)
    : IJobHandler<OutboxCleanup>
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>
    /// Each outbox on its own, so one that fails (a missing table, a lock timeout) does not stop the others; the
    /// failures then fail the attempt, which retries (OUT-2) with the outboxes that worked already clean.
    /// </summary>
    public async Task HandleAsync(OutboxCleanup job, JobContext context, CancellationToken cancellationToken)
    {
        Guid boundary = OutboxIds.Floor(time.GetUtcNow() - Retention);
        var failures = new List<Exception>();
        foreach (OutboxRegistration outbox in registry.Outboxes)
        {
            try
            {
                int deleted = await DeleteAsync(new OutboxSql(outbox.Schema), boundary, cancellationToken);
                logger.OutboxCleanedUp(outbox.Schema, deleted);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) // spec 0013 OCL-1: the other outboxes still run
            {
                failures.Add(ex);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Cleaning up an outbox failed (spec 0013 OCL-1).", failures);
        }
    }

    /// <summary>A chunk per statement, each in its own transaction, until a statement deletes less than a chunk.</summary>
    private async Task<int> DeleteAsync(OutboxSql sql, Guid boundary, CancellationToken cancellationToken)
    {
        int total = 0;
        int deleted;
        do
        {
            // Deviation DAT-6: identifier from a registered outbox schema (spec 0013 DSP-8)
            deleted = await db.InTransactionAsync(token => db.Database.ExecuteSqlAsync(sql.Cleanup(boundary), token), cancellationToken);
            total += deleted;
        }
        while (deleted == OutboxSql.CleanupChunk);

        return total;
    }
}
