using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Infrastructure.Jobs.Data;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;

namespace Orvano.Infrastructure.Jobs;

/// <summary>
/// Keeps <c>jobs.schedule</c> in step with the schedules in code and fires the due ones (spec 0013 SCH-2 to SCH-5). Each
/// occurrence fires once across workers: the row lock picks one worker, and the dedupe key makes a second fire of the
/// same occurrence a no op (JOB-5).
/// </summary>
internal sealed class JobScheduler(
    JobRegistry registry,
    JobQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    InfrastructureTelemetry telemetry,
    ILogger<JobScheduler> logger)
{
    /// <summary>The most schedules one tick fires (SCH-4).</summary>
    private const int TickLimit = 100;

    private const string UpsertSql = """
        INSERT INTO jobs.schedule (id, name, cron, enabled, next_run_at, created_at, updated_at)
        VALUES (@id, @name, @cron, true, @next, @now, @now)
        ON CONFLICT (name) DO UPDATE SET cron = EXCLUDED.cron, next_run_at = EXCLUDED.next_run_at, updated_at = EXCLUDED.updated_at
        WHERE jobs.schedule.cron IS DISTINCT FROM EXCLUDED.cron
        RETURNING (xmax = 0) AS inserted
        """;

    private const string DueSql = """
        SELECT id, name, next_run_at FROM jobs.schedule
        WHERE enabled AND next_run_at <= @now
        ORDER BY next_run_at LIMIT @limit FOR UPDATE SKIP LOCKED
        """;

    /// <summary>
    /// SCH-2: one transaction that upserts every registered schedule in name order (so concurrent syncs take row locks in
    /// the same order) and deletes rows whose name is no longer registered. A changed cron recomputes <c>next_run_at</c>;
    /// <c>enabled</c> and an unchanged schedule's state are kept.
    /// </summary>
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        JobsDbContext db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        (int added, int updated, int removed) = await db.InTransactionAsync(async token =>
        {
            DateTimeOffset now = time.GetUtcNow();
            int added = 0, updated = 0;
            foreach (ScheduledJob schedule in registry.Schedules)
            {
                // JobRegistry only keeps a schedule with a next occurrence.
                DateTimeOffset next = JobRegistry.NextOccurrence(schedule.Cron, now)!.Value;
                await using NpgsqlCommand upsert = JobSql.Command(db, UpsertSql);
                upsert.Parameters.Add(new NpgsqlParameter<Guid>("id", Guid.CreateVersion7(now)));
                upsert.Parameters.Add(new NpgsqlParameter<string>("name", schedule.Name));
                upsert.Parameters.Add(new NpgsqlParameter<string>("cron", schedule.CronText));
                upsert.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("next", next));
                upsert.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
                object? inserted = await upsert.ExecuteScalarAsync(token);
                if (inserted is true)
                {
                    added++;
                }
                else if (inserted is false)
                {
                    updated++;
                }
            }

            await using NpgsqlCommand delete = JobSql.Command(db, "DELETE FROM jobs.schedule WHERE name <> ALL(@names)");
            delete.Parameters.Add(new NpgsqlParameter<string[]>("names", [.. registry.Schedules.Select(schedule => schedule.Name)]));
            int removed = await delete.ExecuteNonQueryAsync(token);
            return (added, updated, removed);
        }, cancellationToken);

        logger.JobSchedulesSynced(added, updated, removed);
    }

    /// <summary>
    /// SCH-4, SCH-5: in one transaction, locks the due schedules, enqueues each one's job for its stored
    /// <c>next_run_at</c>, and moves it to the next occurrence after now, so missed occurrences collapse into one fire.
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        JobsDbContext db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        await db.InTransactionAsync(async token =>
        {
            DateTimeOffset now = time.GetUtcNow();
            var due = new List<(Guid Id, string Name, DateTimeOffset FireTime)>();
            await using (NpgsqlCommand select = JobSql.Command(db, DueSql))
            {
                select.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
                select.Parameters.Add(new NpgsqlParameter<int>("limit", TickLimit));
                await using NpgsqlDataReader reader = await select.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    due.Add((reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));
                }
            }

            foreach ((Guid id, string name, DateTimeOffset fireTime) in due)
            {
                // A row whose name is not registered waits for the next sync to delete it.
                if (registry.FindSchedule(name) is not { } schedule)
                {
                    continue;
                }

                string dedupeKey = name + ":" + fireTime.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                Guid? job = await queue.InsertAsync(
                    db, schedule.Registration, schedule.Payload, new EnqueueOptions { DedupeKey = dedupeKey, RunAt = fireTime }, JobQueue.CurrentTraceParent(), token);
                if (job is not null)
                {
                    TransactionSignals.AddOnCommitted(db, _ =>
                    {
                        telemetry.ScheduleFired(name);
                        logger.JobScheduleFired(name, fireTime);
                        return ValueTask.CompletedTask;
                    });
                }

                DateTimeOffset? next = JobRegistry.NextOccurrence(schedule.Cron, now);
                if (next is null)
                {
                    // JobRegistry rejects a cron with no next occurrence, so only an expression that runs out gets here.
                    logger.JobScheduleNeverFires(name);
                }

                await db.Database.ExecuteSqlAsync(
                    $"""
                    UPDATE jobs.schedule SET next_run_at = COALESCE({next}, next_run_at), enabled = {next is not null},
                        last_fired_at = {now}, updated_at = {now}
                    WHERE id = {id}
                    """,
                    token);
            }
        }, cancellationToken);
    }
}
