using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Signals;

/// <summary>A platform context with no tables of its own, for writes made with SQL inside <c>InTransactionAsync</c>.</summary>
internal sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    public static ProbeContext On(IServiceProvider host) =>
        new(new DbContextOptionsBuilder<ProbeContext>().UseOrvanoConventions("probe", host).Options);
}

/// <summary>Records each connection event, in order.</summary>
internal sealed class RecordingObserver : ISignalConnectionObserver
{
    public ConcurrentQueue<string> Events { get; } = new();

    public int Count(string name) => Events.Count(e => e == name);

    public ValueTask OnConnectedAsync(CancellationToken cancellationToken) => Record("connected");

    public ValueTask OnDisconnectedAsync(CancellationToken cancellationToken) => Record("disconnected");

    public ValueTask OnFallbackPollAsync(CancellationToken cancellationToken) => Record("poll");

    private ValueTask Record(string name)
    {
        Events.Enqueue(name);
        return ValueTask.CompletedTask;
    }
}

/// <summary>A heartbeat sender that sends nothing, so the listener's timeout fires (spec 0013 LSN-3).</summary>
internal sealed class SilentHeartbeat : IHeartbeatSender
{
    public Task SendAsync(NpgsqlConnection connection, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// A heartbeat sender whose send never returns and ignores cancellation, like <c>SELECT pg_notify</c> on a connection
/// that went silent without closing (spec 0013 LSN-3).
/// </summary>
internal sealed class HangingHeartbeat : IHeartbeatSender
{
    private int _calls;

    /// <summary>How many sends have started.</summary>
    public int Calls => Volatile.Read(ref _calls);

    public Task SendAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return new TaskCompletionSource().Task;
    }
}

/// <summary>
/// A cache handler whose call never returns and ignores cancellation, like a handler stuck on a lock or a query that
/// never answers (spec 0013 HND-4).
/// </summary>
internal sealed class HangingCacheHandler(ConcurrentQueue<string> calls) : ISignalHandler
{
    public SignalKind Kind => SignalKind.Cache;

    public ValueTask HandleAsync(IReadOnlyList<string> values, CancellationToken cancellationToken)
    {
        calls.Enqueue($"hang:{values[0]}");
        return new ValueTask(new TaskCompletionSource().Task);
    }
}

/// <summary>Records the event id of every log entry the host writes, for checking that a log was written.</summary>
internal sealed class RecordingLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<(EventId Id, LogLevel Level)> _entries = new();

    public int Count(int eventId, LogLevel level) => _entries.Count(e => e.Id.Id == eventId && e.Level == level);

    public ILogger CreateLogger(string categoryName) => new Logger(_entries);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<(EventId Id, LogLevel Level)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((eventId, logLevel));
    }
}

/// <summary>
/// A TCP proxy in front of PostgreSQL that can go silent: on the connections open at that moment it drops every byte
/// in both directions and keeps the sockets open, like a network path that loses packets without a reset. Connections
/// made later pass normally, so a client can reconnect (spec 0013 LSN-3).
/// </summary>
internal sealed class SilentProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _host;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<Link> _links = new();
    private readonly Task _accepting;

    private SilentProxy(string host, int port)
    {
        _host = host;
        _port = port;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static SilentProxy Start(string host, int port) => new(host, port);

    /// <summary>Drops every byte on the connections open now, from now on. They stay open.</summary>
    public void Silence()
    {
        foreach (Link link in _links)
        {
            link.Silent = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (OperationCanceledException)
        {
        }

        foreach (Link link in _links)
        {
            link.Dispose();
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
            var server = new TcpClient();
            await server.ConnectAsync(_host, _port, _stop.Token);
            var link = new Link(client, server);
            _links.Enqueue(link);
            link.Start(_stop.Token);
        }
    }

    private sealed class Link(TcpClient client, TcpClient server) : IDisposable
    {
        private volatile bool _silent;

        public bool Silent
        {
            get => _silent;
            set => _silent = value;
        }

        public void Start(CancellationToken cancellationToken)
        {
            _ = PumpAsync(client.GetStream(), server.GetStream(), cancellationToken);
            _ = PumpAsync(server.GetStream(), client.GetStream(), cancellationToken);
        }

        public void Dispose()
        {
            client.Dispose();
            server.Dispose();
        }

        private async Task PumpAsync(NetworkStream from, NetworkStream to, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[16 * 1024];
            try
            {
                int read;
                while ((read = await from.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (!_silent)
                    {
                        await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // The other side closed, or the proxy stopped.
            }
        }
    }
}

/// <summary>
/// A clock that moves only when the test calls <see cref="Advance"/>, so the listener's deadlines fire at a time the
/// test chooses, not after a wall clock wait (spec 0013 LSN-3). Timers due at once fire on the thread pool.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward and fires every timer that is due by then, once.</summary>
    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_lock)
        {
            _now += by;
            due = [.. _timers.Where(t => t.Due <= _now)];
            foreach (ManualTimer timer in due)
            {
                if (timer.Period == Timeout.InfiniteTimeSpan)
                {
                    _timers.Remove(timer);
                }
                else
                {
                    timer.Due = _now + timer.Period;
                }
            }
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire();
        }
    }

    private void Schedule(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            timer.Due = _now + dueTime;
            timer.Period = period;
            if (dueTime > TimeSpan.Zero)
            {
                _timers.Add(timer);
                return;
            }

            if (period != Timeout.InfiniteTimeSpan)
            {
                timer.Due = _now + period;
                _timers.Add(timer);
            }
        }

        ThreadPool.QueueUserWorkItem(_ => timer.Fire());
    }

    private void Cancel(ManualTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private volatile bool _disposed;

        public DateTimeOffset Due { get; set; }

        public TimeSpan Period { get; set; } = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                return false;
            }

            clock.Schedule(this, dueTime, period);
            return true;
        }

        public void Fire()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            clock.Cancel(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A raw <c>LISTEN orvano_signal</c> connection that collects every payload except heartbeats.</summary>
internal sealed class SignalTap : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly List<string> _payloads = [];

    private SignalTap(NpgsqlConnection connection)
    {
        _connection = connection;
        _connection.Notification += (_, e) =>
        {
            if (!SignalPayload.TryDecode(e.Payload, out string kind, out IReadOnlyList<string> _) || kind != SignalPayload.HeartbeatKind)
            {
                _payloads.Add(e.Payload);
            }
        };
    }

    public static async Task<SignalTap> StartAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (NpgsqlCommand command = new($"LISTEN {SignalPayload.Channel}", connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new SignalTap(connection);
    }

    /// <summary>Reads until no notification arrives for <paramref name="quietMilliseconds"/>, then returns and forgets what it heard.</summary>
    public async Task<List<string>> TakeAsync(CancellationToken cancellationToken, int quietMilliseconds = 300)
    {
        while (await _connection.WaitAsync(TimeSpan.FromMilliseconds(quietMilliseconds), cancellationToken))
        {
        }

        List<string> heard = [.. _payloads];
        _payloads.Clear();
        return heard;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

/// <summary>Raw access to a test database: the platform owner's connection, for NOTIFY and backend control.</summary>
internal sealed class TestDatabase(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    public NpgsqlDataSource DataSource => dataSource;

    public static TestDatabase For(PostgresFixture postgres, IReadOnlyDictionary<string, string> settings) =>
        new(NpgsqlDataSource.Create(postgres.ConnectionString(settings["Database:Name"])));

    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params object[] values)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        foreach (object value in values)
        {
            command.Parameters.AddWithValue(value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task NotifyAsync(string payload, CancellationToken cancellationToken) =>
        ExecuteAsync($"SELECT pg_notify('{SignalPayload.Channel}', $1)", cancellationToken, payload);

    /// <summary>Ends the backends whose application name is <paramref name="applicationName"/>, waiting until they are gone.</summary>
    public async Task<int> TerminateAsync(string applicationName, CancellationToken cancellationToken)
    {
        // In the select list, so it runs only for the rows the WHERE keeps (a WHERE clause has no evaluation order).
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE application_name = $1");
        command.Parameters.AddWithValue(applicationName);
        int terminated = 0;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            terminated += reader.GetBoolean(0) ? 1 : 0;
        }

        return terminated;
    }

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
