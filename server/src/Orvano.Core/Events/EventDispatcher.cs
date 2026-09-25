using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Notifications;

namespace Orvano.Core.Events;

/// <summary>
/// Worker role. Claims undispatched events, enqueues jobs for durable consumers, and marks the events
/// dispatched, all in one transaction. Wakes on NOTIFY and polls as a fallback.
/// A consumer that fails on an event is contained to that consumer on that event: its output is dropped and an
/// <see cref="EventRedispatch"/> job retries it later, while every other consumer and event carries on.
/// </summary>
public sealed class EventDispatcher(
    [FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db,
    WorkRegistry work,
    WakeSignals signals,
    ILogger<EventDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private const string Savepoint = "consumer";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = Timings.PollInterval;
            try
            {
                while (true)
                {
                    var count = await DispatchBatchAsync(stoppingToken);
                    failures = 0;
                    if (count < BatchSize) break;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                delay = RetryDelay(failures);
                logger.LogError(ex, "Event dispatch pass failed ({Failures} in a row); trying again in {Delay}", failures, delay);
            }

            await signals.Events.WaitAsync(delay, stoppingToken);
        }
    }

    /// <summary>After a failed pass: 2 seconds, doubling after each failure in a row, capped at 30 seconds.</summary>
    public static TimeSpan RetryDelay(int failures)
    {
        var delay = Timings.PollInterval * Math.Pow(2, Math.Min(failures - 1, 30));
        return delay < Timings.EventDispatchMaxRetryDelay ? delay : Timings.EventDispatchMaxRetryDelay;
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var events = new List<OutboxEvent>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT id, project_id, type, subject, payload::text, created_at
            FROM orvano.events
            WHERE dispatched_at IS NULL
            ORDER BY id
            LIMIT @limit
            FOR UPDATE SKIP LOCKED
            """, conn, tx))
        {
            select.Parameters.AddWithValue("limit", BatchSize);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) events.Add(Outbox.Map(reader));
        }

        if (events.Count == 0) return 0;

        foreach (var e in events)
        foreach (var consumer in work.ConsumersFor(e))
        {
            if (await RunConsumerAsync(tx, e, consumer, ct) is not { } failure) continue;

            logger.LogError(failure.Error,
                "Event consumer {Consumer} failed on event {EventId} ({EventType}): {Reason}; handing it to an {Kind} job",
                consumer.Name, e.Id, e.Type, failure.Reason, EventRedispatch.Kind);
            EventTelemetry.ConsumerFailures.Add(1,
                new("event.type", e.Type), new("consumer", consumer.Name), new("reason", failure.Reason));
            await JobQueue.EnqueueAsync(tx, EventRedispatch.For(e, consumer.Name), ct);
        }

        await using (var mark = new NpgsqlCommand(
            "UPDATE orvano.events SET dispatched_at = now() WHERE id = ANY(@ids)", conn, tx))
        {
            mark.Parameters.AddWithValue("ids", events.Select(e => e.Id).ToArray());
            await mark.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        logger.LogDebug("Dispatched {Count} event(s)", events.Count);
        return events.Count;
    }

    /// <summary>
    /// Runs one consumer on one event and enqueues its jobs behind a savepoint. Returns null on success, or why
    /// the consumer's output was dropped. Any database error other than one caused by the row itself propagates
    /// and ends the pass, so the same events are claimed again later.
    /// </summary>
    private async Task<ConsumerFailure?> RunConsumerAsync(NpgsqlTransaction tx, OutboxEvent e, NamedConsumer consumer, CancellationToken ct)
    {
        List<NewJob> jobs;
        try
        {
            // Materialize inside the try: a lazy IEnumerable would otherwise throw later, outside it.
            jobs = consumer.Consumer(e).ToList();
        }
        catch (Exception ex)
        {
            return new ConsumerFailure("threw", ex);
        }

        if (work.ValidateJobs(jobs) is { } problem)
            return new ConsumerFailure("invalid_job", new InvalidOperationException(problem));

        try
        {
            await JobQueue.EnqueueManyAsync(tx, jobs, Savepoint, ct);
            return null;
        }
        catch (PostgresException ex) when (IsCausedByTheRow(ex))
        {
            await tx.RollbackAsync(Savepoint, ct);
            return new ConsumerFailure("rejected_by_database", ex);
        }
    }

    /// <summary>
    /// Data errors (class 22) and integrity errors (class 23) are the row's fault. Anything else, including a
    /// class 42 error from a broken deployment, is not the event's fault and should stall loudly.
    /// </summary>
    private static bool IsCausedByTheRow(PostgresException ex) =>
        ex.SqlState.StartsWith("22", StringComparison.Ordinal) || ex.SqlState.StartsWith("23", StringComparison.Ordinal);

    private sealed record ConsumerFailure(string Reason, Exception Error);
}
