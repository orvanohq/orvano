using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Orvano.Infrastructure.Jobs.Data;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Outbox;

namespace Orvano.Infrastructure.Outbox;

/// <summary>
/// The built in daily outbox cleanup (spec 0013 OCL-1 to OCL-3, spec 0007 EVT-3): deletes dispatched events that
/// occurred more than 7 days ago, in every registered outbox, and waiting events of types this process does not register
/// that occurred more than 30 days ago. Never a waiting event of a type it registers.
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
    /// How long a waiting event of a type this process does not register is kept for a version that does (OCL-3): the
    /// dead job retention (SCH-7), long past any rolling upgrade and long enough to roll forward after a rollback.
    /// </summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromDays(30);

    /// <summary>
    /// Each outbox on its own, so one that fails (a missing table, a lock timeout) does not stop the others; the
    /// failures then fail the attempt, which retries (OUT-2) with the outboxes that worked already clean.
    /// </summary>
    public async Task HandleAsync(OutboxCleanup job, JobContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        Guid boundary = OutboxIds.Floor(now - Retention);
        Guid expiry = OutboxIds.Floor(now - Expiry);
        var failures = new List<Exception>();
        foreach (OutboxRegistration outbox in registry.Outboxes)
        {
            try
            {
                var sql = OutboxSql.For(outbox);
                int deleted = await DeleteAsync(sql.Cleanup(boundary), cancellationToken);
                logger.OutboxCleanedUp(outbox.Schema, deleted);

                // OCL-3: events no version picked up. The types are row text, so only the count is logged.
                int expired = await DeleteAsync(sql.Expire(expiry), cancellationToken);
                if (expired > 0)
                {
                    logger.OutboxEventsExpired(outbox.Schema, expired);
                }
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
    private async Task<int> DeleteAsync(FormattableString statement, CancellationToken cancellationToken)
    {
        int total = 0;
        int deleted;
        do
        {
            // Deviation DAT-6: identifier from a registered outbox schema (spec 0013 DSP-8)
            deleted = await db.InTransactionAsync(token => db.Database.ExecuteSqlAsync(statement, token), cancellationToken);
            total += deleted;
        }
        while (deleted == OutboxSql.CleanupChunk);

        return total;
    }
}
