using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Events;

namespace Orvano.Server.Hosting;

/// <summary>
/// Realtime role. Receives event IDs from the LISTEN connection, fetches each event, and will push it
/// to matching sockets once the realtime protocol exists (scope row 22). Delivery is at most once.
/// </summary>
public sealed class RealtimeFanout(
    [FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db,
    ILogger<RealtimeFanout> logger) : BackgroundService
{
    public const string MeterName = "Orvano.Realtime";

    /// <summary>Event IDs waiting to be fetched. Past this, the oldest are dropped (at most once delivery).</summary>
    public const int Capacity = 10_000;

    private static readonly Counter<long> Dropped = new Meter(MeterName).CreateCounter<long>(
        "orvano.realtime.events_dropped",
        unit: "{event}",
        description: "Event IDs dropped because the realtime role fell behind; affected clients resync.");

    private readonly Channel<long> _ids = Channel.CreateBounded<long>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
        _ => Dropped.Add(1));

    /// <summary>Called on the LISTEN read loop; never blocks.</summary>
    public void OnNotify(string payload)
    {
        if (long.TryParse(payload, out var id)) _ids.Writer.TryWrite(id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in _ids.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var e = await Outbox.ReadAsync(db, id, stoppingToken);
                if (e is not null) logger.LogDebug("Event {EventId} {Type}: no subscribers yet", e.Id, e.Type);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not fetch event {EventId}; clients resync on their own", id);
            }
        }
    }
}
