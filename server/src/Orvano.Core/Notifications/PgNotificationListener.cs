using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Orvano.Core.Notifications;

/// <summary>
/// Holds the role's one dedicated LISTEN connection. On a drop it reconnects with backoff, listens
/// again, and calls <paramref name="onConnected"/> so consumers resync what the gap missed.
/// Handlers run on the connection's read loop and must not block.
/// </summary>
public sealed class PgNotificationListener(
    NpgsqlDataSource dedicatedDb,
    IReadOnlyDictionary<string, Action<string>> handlers,
    Action onConnected,
    ILogger<PgNotificationListener> logger) : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = await dedicatedDb.OpenConnectionAsync(stoppingToken);
                conn.Notification += (_, e) =>
                {
                    if (handlers.TryGetValue(e.Channel, out var handle)) handle(e.Payload);
                };

                foreach (var channel in handlers.Keys)
                {
                    await using var listen = new NpgsqlCommand($"LISTEN \"{channel}\"", conn);
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                logger.LogInformation("Listening on {Channels}", string.Join(", ", handlers.Keys));
                backoff = TimeSpan.FromSeconds(1);
                onConnected();

                while (true) await conn.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LISTEN connection lost; reconnecting in {Delay}", backoff);
            }

            try { await Task.Delay(backoff, stoppingToken); }
            catch (OperationCanceledException) { return; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }
}
