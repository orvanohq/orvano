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
/// </summary>
public sealed class EventDispatcher(
    [FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db,
    WorkRegistry work,
    WakeSignals signals,
    ILogger<EventDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) == BatchSize) { }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Event dispatch failed; retrying on the next wake up");
            }

            await signals.Events.WaitAsync(Timings.PollInterval, stoppingToken);
        }
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
        foreach (var job in consumer(e))
        {
            await JobQueue.EnqueueAsync(tx, job, ct);
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
}
