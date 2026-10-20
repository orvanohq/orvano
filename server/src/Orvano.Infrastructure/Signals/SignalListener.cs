using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Channels;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using Orvano.Infrastructure.Caching;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Signals;

/// <summary>
/// One <c>LISTEN orvano_signal</c> connection per instance (spec 0013 LSN-1 to LSN-7, spec 0007 SIG-1, SIG-2). It
/// reads notifications and heartbeats on its read loop, hands signals and connection events to one dispatch loop, and
/// keeps the cache bypassed whenever it might have missed a signal. Only <c>serve</c> runs it.
/// </summary>
internal sealed class SignalListener : BackgroundService, ISignalConnectionState
{
    /// <summary>A dispatch queue longer than this logs a Warning, at most once per <see cref="QueueWarningEvery"/>.</summary>
    public const int QueueWarningLength = 1000;

    private const string ListenSql = "LISTEN " + SignalPayload.Channel;

    private static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan QueueWarningEvery = TimeSpan.FromMinutes(1);

    private readonly NpgsqlDataSource _dataSource;
    private readonly SignalOptions _options;
    private readonly TimeProvider _time;
    private readonly ISignalHandler[] _handlers;
    private readonly ISignalConnectionObserver[] _observers;
    private readonly HybridCache _cache;
    private readonly IHeartbeatSender _heartbeat;
    private readonly InfrastructureTelemetry _telemetry;
    private readonly ILogger<SignalListener> _logger;
    // One loop reads it, but it is not marked single reader: only the general unbounded channel can count its items.
    private readonly Channel<DispatchItem> _queue = Channel.CreateUnbounded<DispatchItem>();
    private readonly Lock _queueWarningLock = new();

    private volatile bool _connected;

    // Bumped on every connect and disconnect, so a fallback poll queued for an earlier disconnect is dropped (LSN-2).
    private long _epoch;
    private int _pollQueued;
    private ITimer? _pollTimer;
    private DateTimeOffset _lastQueueWarning = DateTimeOffset.MinValue;

    // Read loop only.
    private DateTimeOffset _lastHeartbeat;
    private DateTimeOffset _lastSent;

    public SignalListener(
        [FromKeyedServices(InfrastructureServices.SignalDataSourceKey)] NpgsqlDataSource dataSource,
        IOptions<SignalOptions> options,
        TimeProvider time,
        IEnumerable<ISignalHandler> handlers,
        IEnumerable<ISignalConnectionObserver> observers,
        [FromKeyedServices(SignalAwareHybridCache.InnerKey)] HybridCache cache,
        IHeartbeatSender heartbeat,
        InfrastructureTelemetry telemetry,
        ILogger<SignalListener> logger)
    {
        _dataSource = dataSource;
        _options = options.Value;
        _time = time;
        _handlers = [.. handlers];
        _observers = [.. observers];
        _cache = cache;
        _heartbeat = heartbeat;
        _telemetry = telemetry;
        _logger = logger;
        telemetry.Meter.CreateObservableGauge(
            "orvano.infrastructure.signals.connected", () => _connected ? 1 : 0, "{connection}", "1 while the signal listener is connected.");
    }

    private enum ObserverCall
    {
        Connected,
        Disconnected,
        FallbackPoll,
    }

    public bool IsConnected => _connected;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task dispatch = DispatchAsync(stoppingToken);
        try
        {
            await ListenAsync(stoppingToken);
        }
        finally
        {
            // LSN-6: stop reading, count as disconnected, and drop whatever is still queued.
            _connected = false;
            StopPolling();
            _queue.Writer.TryComplete();
            await dispatch;
        }
    }

    public override void Dispose()
    {
        _pollTimer?.Dispose();
        base.Dispose();
    }

    /// <summary>A random delay between 0 and min(cap, 1 s × 2^(n − 1)) after failed attempt n (LSN-5, full jitter).</summary>
    internal static TimeSpan Backoff(int failedAttempts, TimeSpan cap)
    {
        double exponential = BackoffBase.TotalMilliseconds * Math.Pow(2, Math.Min(failedAttempts - 1, 30));
        long ceiling = (long)Math.Min(cap.TotalMilliseconds, exponential);
        return TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(0, (int)Math.Min(ceiling, int.MaxValue - 1) + 1));
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        int failures = 0;
        bool disconnectAnnounced = false; // the instance starts disconnected, so a failed first connect announces it
        while (!stoppingToken.IsCancellationRequested)
        {
            bool wasConnected = false;
            Exception? error = null;
            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(stoppingToken);
                connection.Notification += OnNotification;
                await ConnectAsync(connection, stoppingToken);
                wasConnected = true;
                failures = 0;
                disconnectAnnounced = false;
                await ReadAsync(connection, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) // spec 0013 LSN-3: any failure of the connection counts as a disconnect, never a crash
            {
                error = ex;
            }

            _connected = false;
            if (wasConnected)
            {
                // The first attempt after a disconnect is immediate (LSN-5).
                AnnounceDisconnect(error is null ? "no heartbeat" : "connection lost", error);
                disconnectAnnounced = true;
                continue;
            }

            failures++;
            TimeSpan delay = Backoff(failures, _options.ReconnectBackoffCap);
            _logger.SignalsConnectFailed(failures, delay, error!);
            if (!disconnectAnnounced)
            {
                AnnounceDisconnect("could not connect", null);
                disconnectAnnounced = true;
            }

            try
            {
                await Task.Delay(delay, _time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>LSN-2: listen, clear the cache, then count as connected and tell the observers.</summary>
    private async Task ConnectAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        using Activity? activity = InfrastructureTelemetry.ActivitySource.StartActivity("signals connect");
        await using (var listen = new NpgsqlCommand(ListenSql, connection))
        {
            await listen.ExecuteNonQueryAsync(cancellationToken);
        }

        _lastHeartbeat = _time.GetUtcNow();
        _lastSent = DateTimeOffset.MinValue;

        // Anything sent while this instance was not listening may have been lost, so nothing cached before now is
        // trusted (CAC-6).
        await _cache.RemoveByTagAsync(SignalPayload.Everything, cancellationToken);
        _telemetry.CacheCleared(InfrastructureTelemetry.ClearOnConnect);

        _connected = true;
        StopPolling();
        _logger.SignalsConnected(SignalPayload.Channel);
        Enqueue(new ObserverItem(ObserverCall.Connected, Interlocked.Read(ref _epoch)));
    }

    /// <summary>
    /// LSN-2, LSN-3: waits for notifications, each wait bounded by the next heartbeat send and the timeout deadline.
    /// Returns when no heartbeat arrived for <see cref="SignalOptions.HeartbeatTimeout"/>; throws on any I/O failure.
    /// </summary>
    private async Task ReadAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                DateTimeOffset now = _time.GetUtcNow();
                DateTimeOffset deadline = _lastHeartbeat + _options.HeartbeatTimeout;
                if (now >= deadline)
                {
                    _connected = false; // the bypass starts before any queued work runs
                    return;
                }

                if (now - _lastSent >= _options.HeartbeatInterval)
                {
                    if (!await SendHeartbeatAsync(connection, deadline - now, cancellationToken))
                    {
                        _connected = false; // a send still waiting at the deadline means a silent connection (LSN-3)
                        return;
                    }

                    _lastSent = now;
                }

                DateTimeOffset nextSend = _lastSent + _options.HeartbeatInterval;
                TimeSpan wait = (nextSend < deadline ? nextSend : deadline) - now;
                await connection.WaitAsync(wait < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : wait, cancellationToken);
            }
        }
        catch (Exception) when (MarkDisconnected())
        {
            // Never reached: the filter only flips the state before the connection is disposed.
        }
    }

    /// <summary>
    /// Sends one heartbeat, bounded by <paramref name="left"/> (the time to the timeout deadline). Returns false when the
    /// send is still waiting then, as on a connection that went silent without closing. The send is cancelled and left
    /// behind, so a sender that ignores the cancel cannot hold the read loop.
    /// </summary>
    private async Task<bool> SendHeartbeatAsync(NpgsqlConnection connection, TimeSpan left, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(left, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await _heartbeat.SendAsync(connection, linked.Token).WaitAsync(linked.Token);
            return true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private bool MarkDisconnected()
    {
        _connected = false;
        return false;
    }

    /// <summary>Runs on the read loop, inside <see cref="NpgsqlConnection.WaitAsync(TimeSpan, CancellationToken)"/>.</summary>
    private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        if (!SignalPayload.TryDecode(e.Payload, out string wireKind, out IReadOnlyList<string> values))
        {
            _logger.SignalMalformed();
            return;
        }

        if (wireKind == SignalPayload.HeartbeatKind)
        {
            _lastHeartbeat = _time.GetUtcNow(); // a heartbeat from any replica counts
            return;
        }

        if (!SignalPayload.TryParseKind(wireKind, out SignalKind kind))
        {
            _logger.SignalKindUnknown(wireKind);
            return;
        }

        // SND-7: a job carries no values, and a cache or session signal always carries some.
        if ((kind == SignalKind.Job) != (values.Count == 0))
        {
            _logger.SignalIgnored(kind, values.Count);
            return;
        }

        _telemetry.SignalReceived(kind);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.SignalReceived(kind, string.Join(',', values));
        }

        Enqueue(new SignalItem(kind, values));
    }

    /// <summary>LSN-3, LSN-4: tell the observers, then poll right away and every interval until connected.</summary>
    private void AnnounceDisconnect(string reason, Exception? error)
    {
        _logger.SignalsDisconnected(reason, error);
        long epoch = Interlocked.Increment(ref _epoch);
        Enqueue(new ObserverItem(ObserverCall.Disconnected, epoch));
        _pollTimer?.Dispose();
        _pollTimer = _time.CreateTimer(_ => QueuePoll(epoch), null, TimeSpan.Zero, _options.FallbackPollInterval);
    }

    private void StopPolling()
    {
        Interlocked.Increment(ref _epoch);
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    // At most one poll waits in the queue; a tick that finds one still queued is skipped.
    private void QueuePoll(long epoch)
    {
        if (_connected || epoch != Interlocked.Read(ref _epoch) || Interlocked.CompareExchange(ref _pollQueued, 1, 0) != 0)
        {
            return;
        }

        Enqueue(new ObserverItem(ObserverCall.FallbackPoll, epoch));
    }

    private void Enqueue(DispatchItem item)
    {
        if (!_queue.Writer.TryWrite(item))
        {
            return; // shutting down
        }

        int count = _queue.Reader.Count;
        if (count <= QueueWarningLength)
        {
            return;
        }

        lock (_queueWarningLock)
        {
            DateTimeOffset now = _time.GetUtcNow();
            if (now - _lastQueueWarning < QueueWarningEvery)
            {
                return;
            }

            _lastQueueWarning = now;
        }

        _logger.SignalQueueLong(count);
    }

    /// <summary>LSN-6: one loop, so handlers and observers run one at a time, in arrival and registration order.</summary>
    private async Task DispatchAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            await foreach (DispatchItem item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                switch (item)
                {
                    case SignalItem signal:
                        await HandleAsync(signal, stoppingToken);
                        break;
                    case ObserverItem { Call: ObserverCall.FallbackPoll } poll:
                        Interlocked.Exchange(ref _pollQueued, 0);
                        if (!_connected && poll.Epoch == Interlocked.Read(ref _epoch))
                        {
                            await NotifyAsync(poll.Call, stoppingToken);
                        }

                        break;
                    case ObserverItem observer:
                        await NotifyAsync(observer.Call, stoppingToken);
                        break;
                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleAsync(SignalItem signal, CancellationToken stoppingToken)
    {
        foreach (ISignalHandler handler in _handlers)
        {
            if (handler.Kind != signal.Kind)
            {
                continue;
            }

            try
            {
                await handler.HandleAsync(signal.Values, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) // spec 0013 LSN-6: a failing handler is logged and never stops the others or the listener
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.SignalHandlerFailed(handler.GetType().Name, signal.Kind, ex);
                }
            }
        }
    }

    private async Task NotifyAsync(ObserverCall call, CancellationToken stoppingToken)
    {
        foreach (ISignalConnectionObserver observer in _observers)
        {
            try
            {
                ValueTask task = call switch
                {
                    ObserverCall.Connected => observer.OnConnectedAsync(stoppingToken),
                    ObserverCall.Disconnected => observer.OnDisconnectedAsync(stoppingToken),
                    _ => observer.OnFallbackPollAsync(stoppingToken),
                };
                await task;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) // spec 0013 LSN-6: a failing observer is logged and never stops the others or the listener
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.SignalObserverFailed(observer.GetType().Name, call.ToString(), ex);
                }
            }
        }
    }

    private abstract record DispatchItem;

    private sealed record SignalItem(SignalKind Kind, IReadOnlyList<string> Values) : DispatchItem;

    private sealed record ObserverItem(ObserverCall Call, long Epoch) : DispatchItem;
}
