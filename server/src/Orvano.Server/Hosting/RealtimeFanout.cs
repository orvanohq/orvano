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
    private readonly Channel<long> _ids = Channel.CreateBounded<long>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

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
