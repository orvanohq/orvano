using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Infrastructure;
using Orvano.Infrastructure.Caching;
using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Signals;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Signals;

// Spec 0013 build plan steps 6 to 9: the listener's failure handling, telemetry, dispatch limits, and reconnect floor,
// on real hosts. Each test runs on a database of its own. Host A's listener logs in as a role of the test's own, so the
// test can stop it reconnecting.
public sealed class SignalListenerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string RolePassword = "signals-test-only";
    private const int ShortLived = LogEventIds.Infrastructure + 17;

    private readonly List<OrvanoApiFactory> _hosts = [];
    private readonly string _role = $"signals_{Guid.CreateVersion7():N}";
    private Dictionary<string, string> _settings = null!;
    private TestDatabase _database = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "listener", Token);
        _settings["Signals:FallbackPollInterval"] = "00:00:01";
        _settings["Signals:ReconnectBackoffCap"] = "00:00:01";
        _settings["Signals:HandlerTimeout"] = "00:00:01"; // at most the shortest heartbeat timeout a test sets (SOP-2)
        _database = TestDatabase.For(postgres, _settings);
        await _database.ExecuteAsync($"CREATE ROLE {_role} LOGIN PASSWORD '{RolePassword}'", Token);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (OrvanoApiFactory host in _hosts)
        {
            await host.DisposeAsync();
        }

        await _database.ExecuteAsync($"DROP ROLE IF EXISTS {_role}", CancellationToken.None);
        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:AC-8"), Trait("Spec", "0013:AC-9"), Trait("Spec", "0013:AC-13"), Trait("Spec", "0013:LSN-3"), Trait("Spec", "0013:LSN-4")]
    public async Task Terminating_one_hosts_listener_bypasses_its_cache_polls_and_clears_on_reconnect_while_the_other_stays_connected()
    {
        OrvanoApiFactory a = Host(listenAsRole: true);
        OrvanoApiFactory b = Host(listenAsRole: false);
        RecordingObserver observer = Observer(a);
        using var meters = new MeterRecorder(a.Services.GetRequiredService<IMeterFactory>());
        await SettledAsync(a, observer);
        await b.WaitUntilConnectedAsync(Token);
        // The first connect can clear the cache before the recorder exists (the host starts in Observer), and the clear
        // comes before IsConnected (LSN-2), so count from here: the reconnect must add exactly one.
        long clearsOnFirstConnect = meters.Sum("orvano.infrastructure.cache.clears", "reason", "connect");
        meters.Observe("orvano.infrastructure.signals.connected").ShouldBe(1);
        int calls = 0;
        (await ReadAsync(a, () => ++calls)).ShouldBe(1);
        (await ReadAsync(a, () => ++calls)).ShouldBe(1);

        await _database.ExecuteAsync($"ALTER ROLE {_role} NOLOGIN", Token);
        (await _database.TerminateAsync(ApplicationName(a), Token)).ShouldBe(1);

        await Eventually.TrueAsync(() => observer.Count("poll") >= 2, TimeSpan.FromSeconds(10), "no repeated fallback polls", Token);
        a.Services.GetRequiredService<ISignalConnectionState>().IsConnected.ShouldBeFalse();
        meters.Observe("orvano.infrastructure.signals.connected").ShouldBe(0);
        int before = calls;
        await ReadAsync(a, () => ++calls);
        await ReadAsync(a, () => ++calls);
        calls.ShouldBe(before + 2); // bypassed: the factory runs every time
        (await InnerReadAsync(a)).ShouldBe(1); // the bypassed reads neither read nor replaced the entry cached before the drop
        b.Services.GetRequiredService<ISignalConnectionState>().IsConnected.ShouldBeTrue();
        using (HttpClient client = a.CreateClient())
        {
            using HttpResponseMessage ready = await client.GetAsync("/health/ready", Token);
            ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await _database.ExecuteAsync($"ALTER ROLE {_role} LOGIN", Token);
        await a.WaitUntilConnectedAsync(Token);
        await Eventually.TrueAsync(() => observer.Count("connected") == 1, TimeSpan.FromSeconds(5), "OnConnectedAsync did not run again", Token);

        (await ReadAsync(a, () => 100)).ShouldBe(100); // the entry cached before the drop was cleared on connect
        observer.Count("disconnected").ShouldBe(1);
        observer.Events.First().ShouldBe("disconnected");
        meters.Sum("orvano.infrastructure.cache.clears", "reason", "connect").ShouldBe(clearsOnFirstConnect + 1);
        meters.Observe("orvano.infrastructure.signals.connected").ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-9"), Trait("Spec", "0013:LSN-2"), Trait("Spec", "0013:LSN-5")]
    public async Task A_failed_first_connect_announces_one_disconnect_and_polls_from_boot()
    {
        await _database.ExecuteAsync($"ALTER ROLE {_role} NOLOGIN", Token);
        OrvanoApiFactory a = Host(listenAsRole: true);
        RecordingObserver observer = Observer(a); // boots the host

        await Eventually.TrueAsync(() => observer.Count("poll") >= 3, TimeSpan.FromSeconds(10), "no fallback polls from boot", Token);

        observer.Events.First().ShouldBe("disconnected");
        observer.Count("disconnected").ShouldBe(1);
        observer.Count("connected").ShouldBe(0);
        int calls = 0;
        await ReadAsync(a, () => ++calls);
        await ReadAsync(a, () => ++calls);
        calls.ShouldBe(2);
        (await InnerReadAsync(a)).ShouldBe(-1); // the bypassed reads stored nothing

        await _database.ExecuteAsync($"ALTER ROLE {_role} LOGIN", Token);
        await a.WaitUntilConnectedAsync(Token);
        await Eventually.TrueAsync(() => observer.Count("connected") == 1, TimeSpan.FromSeconds(5), "OnConnectedAsync did not run", Token);
        observer.Count("disconnected").ShouldBe(1); // the failed attempts announced nothing new (LSN-2)
    }

    [Fact]
    [Trait("Spec", "0013:AC-9"), Trait("Spec", "0013:LSN-2")]
    public async Task Observers_hear_one_disconnect_before_the_first_connect_and_then_connected_when_the_database_is_reachable()
    {
        OrvanoApiFactory a = Host(listenAsRole: false);
        RecordingObserver observer = Observer(a); // boots the host

        await a.WaitUntilConnectedAsync(Token);
        await Eventually.TrueAsync(() => observer.Count("connected") == 1, TimeSpan.FromSeconds(5), "OnConnectedAsync did not run", Token);

        string[] events = [.. observer.Events];
        events.First().ShouldBe("disconnected"); // queued before the first attempt started
        events.Count(e => e == "disconnected").ShouldBe(1);
        events.Count(e => e == "poll").ShouldBeLessThanOrEqualTo(1); // a poll still queued at the connect is dropped (LSN-2)
        events.Last().ShouldBe("connected");
    }

    [Fact]
    [Trait("Spec", "0013:AC-8"), Trait("Spec", "0013:AC-11"), Trait("Spec", "0013:LSN-3")]
    public async Task No_heartbeat_for_the_timeout_counts_as_a_disconnect()
    {
        _settings["Signals:HeartbeatInterval"] = "00:00:01";
        _settings["Signals:HeartbeatTimeout"] = "00:00:02";
        OrvanoApiFactory a = Host(listenAsRole: false, services => services.AddSingleton<IHeartbeatSender, SilentHeartbeat>());
        RecordingObserver observer = Observer(a);
        await SettledAsync(a, observer);

        await Eventually.TrueAsync(() => observer.Count("disconnected") >= 1, TimeSpan.FromSeconds(10), "the heartbeat timeout never fired", Token);
    }

    [Fact]
    [Trait("Spec", "0013:AC-8"), Trait("Spec", "0013:LSN-3")]
    public async Task A_heartbeat_send_that_never_returns_disconnects_at_the_timeout_and_not_before_when_the_clock_reaches_it()
    {
        // The deadline runs on a clock only the test moves, so the check is exact and never races the wall clock. The
        // first send starts at connect and ignores cancellation, so only the bound on the send can end it.
        _settings["Signals:HeartbeatInterval"] = "00:00:01";
        _settings["Signals:HeartbeatTimeout"] = "00:00:02";
        var clock = new ManualClock();
        var heartbeat = new HangingHeartbeat();
        OrvanoApiFactory a = Host(listenAsRole: false, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IHeartbeatSender>(heartbeat);
        });
        RecordingObserver observer = Observer(a);
        ISignalConnectionState state = a.Services.GetRequiredService<ISignalConnectionState>();
        await SettledAsync(a, observer);
        await Eventually.TrueAsync(() => heartbeat.Calls == 1, TimeSpan.FromSeconds(5), "the first heartbeat send never started", Token);

        // One tick short of the deadline, and longer than the timeout on the wall clock, so a bound taken from the
        // wrong clock would show here too.
        clock.Advance(TimeSpan.FromSeconds(2) - TimeSpan.FromMilliseconds(1));
        await Task.Delay(TimeSpan.FromSeconds(3), Token);
        observer.Count("disconnected").ShouldBe(0);
        state.IsConnected.ShouldBeTrue();

        clock.Advance(TimeSpan.FromMilliseconds(1));

        await Eventually.TrueAsync(() => observer.Count("disconnected") == 1, TimeSpan.FromSeconds(10), "the hung heartbeat send was never timed out", Token);
        await Eventually.TrueAsync(() => observer.Count("connected") == 1, TimeSpan.FromSeconds(10), "the listener did not reconnect", Token);
        observer.Events.First().ShouldBe("disconnected"); // a fallback poll may land before the reconnect
        heartbeat.Calls.ShouldBe(2); // the new connection's send hangs again, and the clock has not moved since
    }

    [Fact]
    [Trait("Spec", "0013:AC-8"), Trait("Spec", "0013:LSN-3")]
    public async Task A_connection_that_goes_silent_disconnects_with_the_real_heartbeat_sender_and_reconnects_when_connections_pass_again()
    {
        // Nothing is faked but the network: the real NotifyHeartbeatSender sends on a socket that never answers, so
        // only the listener's deadline and Npgsql's cancel and close can end it. Wall clock, with a wide margin.
        _settings["Signals:HeartbeatInterval"] = "00:00:01";
        _settings["Signals:HeartbeatTimeout"] = "00:00:02";
        var direct = new NpgsqlConnectionStringBuilder(postgres.ConnectionString(_settings["Database:Name"]));
        await using var proxy = SilentProxy.Start(direct.Host!, direct.Port);
        OrvanoApiFactory a = Host(listenAsRole: false, services => services.AddKeyedSingleton(
            InfrastructureServices.SignalDataSourceKey,
            (provider, _) => NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(direct.ConnectionString)
            {
                Host = IPAddress.Loopback.ToString(),
                Port = proxy.Port,
                Pooling = false,
                ApplicationName = provider.GetRequiredService<OrvanoInstance>().ApplicationName,
            }.ConnectionString)));
        RecordingObserver observer = Observer(a);
        await SettledAsync(a, observer);
        a.Services.GetRequiredService<IHeartbeatSender>().ShouldBeOfType<NotifyHeartbeatSender>();

        proxy.Silence();

        await Eventually.TrueAsync(() => observer.Count("disconnected") == 1, TimeSpan.FromSeconds(15), "the silent connection was never timed out", Token);
        await Eventually.TrueAsync(() => observer.Count("connected") == 1, TimeSpan.FromSeconds(15), "the listener did not reconnect through a new connection", Token);
    }

    [Fact]
    [Trait("Spec", "0013:HND-4"), Trait("Spec", "0013:LSN-6"), Trait("Spec", "0013:AC-10"), Trait("Spec", "0013:SOP-1")]
    public async Task A_handler_that_never_returns_is_given_up_at_the_handler_timeout_and_not_before_and_the_next_signal_runs()
    {
        // Same clock as the hung heartbeat test: the limit fires when the test moves it, never on the wall clock. The
        // handler ignores cancellation, so only the bound on the call can free the dispatch loop. The handler timeout
        // is a third of the heartbeat timeout, so a bound taken from the wrong option would show.
        _settings["Signals:HeartbeatInterval"] = "00:00:01";
        _settings["Signals:HeartbeatTimeout"] = "00:00:03";
        _settings["Signals:HandlerTimeout"] = "00:00:01";
        var clock = new ManualClock();
        var calls = new ConcurrentQueue<string>();
        var logs = new RecordingLogs();
        OrvanoApiFactory a = Host(listenAsRole: false, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(calls);
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddSignalHandler<HangingCacheHandler>();
            services.AddSignalHandler<SessionHandler>();
        });
        await a.WaitUntilConnectedAsync(Token);

        await _database.NotifyAsync("""{"v":1,"k":"cache","d":["stuck"]}""", Token);
        await Eventually.TrueAsync(() => calls.Contains("hang:stuck"), TimeSpan.FromSeconds(5), "the hanging handler never started", Token);
        await _database.NotifyAsync("""{"v":1,"k":"session","d":["s"]}""", Token);

        clock.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(1));
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        calls.ShouldNotContain("session:s"); // one at a time (LSN-6): the session signal waits behind the hung call
        logs.Count(LogEventIds.Infrastructure + 12, LogLevel.Error).ShouldBe(0);

        clock.Advance(TimeSpan.FromMilliseconds(1));

        await Eventually.TrueAsync(() => calls.Contains("session:s"), TimeSpan.FromSeconds(10), "the hung handler still holds the dispatch loop", Token);
        logs.Count(LogEventIds.Infrastructure + 12, LogLevel.Error).ShouldBe(1);
        calls.ShouldBe(["hang:stuck", "session:s"]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-8"), Trait("Spec", "0013:LSN-8"), Trait("Spec", "0013:HND-4")]
    public async Task A_dispatch_loop_lagging_past_the_heartbeat_timeout_disconnects_once_and_items_from_before_the_reconnect_do_not_trip_it_again()
    {
        // Wall clock with real heartbeats, so the connection itself stays healthy. Each handler call hangs and is cut
        // off at 1 s, so of five signals queued at once the fourth has waited past the 3 s heartbeat timeout while the
        // third or fourth still runs. After the reconnect the fourth and fifth are older still, but from the earlier
        // epoch, so they never count; the reconnect's own OnConnectedAsync waits at most 2 s behind them.
        _settings["Signals:HeartbeatInterval"] = "00:00:01";
        _settings["Signals:HeartbeatTimeout"] = "00:00:03";
        _settings["Signals:HandlerTimeout"] = "00:00:01";
        var calls = new ConcurrentQueue<string>();
        var logs = new RecordingLogs();
        OrvanoApiFactory a = Host(listenAsRole: false, services =>
        {
            services.AddSingleton(calls);
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddSignalHandler<HangingCacheHandler>();
        });
        RecordingObserver observer = Observer(a);
        await SettledAsync(a, observer);

        for (int i = 1; i <= 5; i++)
        {
            await _database.NotifyAsync($$"""{"v":1,"k":"cache","d":["lag{{i}}"]}""", Token);
        }

        await Eventually.TrueAsync(() => observer.Count("disconnected") == 1, TimeSpan.FromSeconds(15), "the lagging dispatch loop never counted as a disconnect", Token);
        logs.Count(LogEventIds.Infrastructure + 16, LogLevel.Warning).ShouldBe(1);
        await Eventually.TrueAsync(() => observer.Count("connected") == 1, TimeSpan.FromSeconds(10), "the listener did not reconnect", Token);
        await Eventually.TrueAsync(() => calls.Count == 5, TimeSpan.FromSeconds(10), "not every queued signal reached the handler", Token);

        await Task.Delay(TimeSpan.FromSeconds(4), Token); // past the heartbeat timeout after the old items drained
        logs.Count(LogEventIds.Infrastructure + 16, LogLevel.Warning).ShouldBe(1);
        observer.Count("disconnected").ShouldBe(1);
        a.Services.GetRequiredService<ISignalConnectionState>().IsConnected.ShouldBeTrue();
        calls.ShouldBe(["hang:lag1", "hang:lag2", "hang:lag3", "hang:lag4", "hang:lag5"]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-9"), Trait("Spec", "0013:LSN-5")]
    public async Task A_connection_dropped_before_it_was_stable_backs_off_and_one_dropped_after_reconnects_at_once()
    {
        // The backoff and the stable period run on a clock only the test moves, so a wait the listener takes shows as
        // a reconnect that happens only once the clock moves.
        _settings["Signals:HeartbeatInterval"] = "00:00:01";
        _settings["Signals:HeartbeatTimeout"] = "00:00:10";
        var clock = new ManualClock();
        var calls = new ConcurrentQueue<string>();
        var logs = new RecordingLogs();
        OrvanoApiFactory a = Host(listenAsRole: false, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(calls);
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddSignalHandler<SessionHandler>();
        });
        RecordingObserver observer = Observer(a);
        await SettledAsync(a, observer);

        // Dropped right after LISTEN, with the clock still: failed attempt 1, so the listener waits on the clock.
        (await _database.TerminateAsync(ApplicationName(a), Token)).ShouldBe(1);
        await Eventually.TrueAsync(() => logs.Count(ShortLived, LogLevel.Warning) == 1, TimeSpan.FromSeconds(10), "the short connection was not counted as failed", Token);
        logs.Messages(ShortLived).Single().ShouldContain("failed attempt 1");
        if (!logs.Messages(ShortLived).Single().EndsWith("next attempt in 00:00:00", StringComparison.Ordinal)) // full jitter may draw 0
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5), Token);
            observer.Count("connected").ShouldBe(0);
        }

        clock.Advance(TimeSpan.FromSeconds(1)); // the backoff cap, so the wait is over
        await Eventually.TrueAsync(() => observer.Count("connected") == 1, TimeSpan.FromSeconds(10), "the listener did not reconnect after the backoff", Token);

        // Dropped again before it was stable: the attempt count keeps growing instead of resetting on connect.
        (await _database.TerminateAsync(ApplicationName(a), Token)).ShouldBe(1);
        await Eventually.TrueAsync(() => logs.Count(ShortLived, LogLevel.Warning) == 2, TimeSpan.FromSeconds(10), "the second short connection was not counted as failed", Token);
        logs.Messages(ShortLived)[^1].ShouldContain("failed attempt 2");
        clock.Advance(TimeSpan.FromSeconds(1));
        await Eventually.TrueAsync(() => observer.Count("connected") == 2, TimeSpan.FromSeconds(10), "the listener did not reconnect after the second backoff", Token);

        // Stable once connected for the heartbeat interval: a signal wakes the read loop, whose next pass resets the count.
        clock.Advance(TimeSpan.FromSeconds(1));
        await _database.NotifyAsync("""{"v":1,"k":"session","d":["stable"]}""", Token);
        await Eventually.TrueAsync(() => calls.Contains("session:stable"), TimeSpan.FromSeconds(5), "the signal never arrived", Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200), Token);

        // A drop after that takes the immediate first attempt, with the clock still.
        (await _database.TerminateAsync(ApplicationName(a), Token)).ShouldBe(1);
        await Eventually.TrueAsync(() => observer.Count("connected") == 3, TimeSpan.FromSeconds(10), "the listener did not reconnect at once after a stable connection", Token);
        logs.Count(ShortLived, LogLevel.Warning).ShouldBe(2);
        observer.Count("disconnected").ShouldBe(3);
    }

    [Fact]
    [Trait("Spec", "0013:AC-13"), Trait("Spec", "0013:LSN-7")]
    public async Task Heartbeats_keep_the_listener_connected_and_are_not_counted_as_received()
    {
        _settings["Signals:HeartbeatInterval"] = "00:00:01";
        _settings["Signals:HeartbeatTimeout"] = "00:00:02";
        OrvanoApiFactory a = Host(listenAsRole: false);
        RecordingObserver observer = Observer(a);
        using var meters = new MeterRecorder(a.Services.GetRequiredService<IMeterFactory>());
        await SettledAsync(a, observer);

        await Task.Delay(TimeSpan.FromSeconds(4), Token);
        meters.Sum("orvano.infrastructure.signals.received").ShouldBe(0); // about four heartbeats arrived, none counted
        await _database.NotifyAsync("""{"v":1,"k":"session","d":["u1"]}""", Token);
        await Eventually.TrueAsync(() => meters.Sum("orvano.infrastructure.signals.received") == 1, TimeSpan.FromSeconds(5), "the signal was not counted", Token);

        observer.Count("disconnected").ShouldBe(0);
        meters.Sum("orvano.infrastructure.signals.received", "kind", "session").ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-10"), Trait("Spec", "0013:HND-1"), Trait("Spec", "0013:LSN-6")]
    public async Task A_throwing_handler_is_logged_and_the_others_still_get_each_signal_in_registration_order()
    {
        var calls = new ConcurrentQueue<string>();
        OrvanoApiFactory a = Host(listenAsRole: false, services =>
        {
            services.AddSingleton(calls);
            services.AddSignalHandler<ThrowingCacheHandler>();
            services.AddSignalHandler<FirstCacheHandler>();
            services.AddSignalHandler<SecondCacheHandler>();
            services.AddSignalHandler<SessionHandler>();
        });
        await a.WaitUntilConnectedAsync(Token);

        await _database.NotifyAsync("""{"v":1,"k":"cache","d":["one"]}""", Token);
        await _database.NotifyAsync("""{"v":1,"k":"session","d":["s"]}""", Token);
        await _database.NotifyAsync("""{"v":1,"k":"cache","d":["two"]}""", Token);

        await Eventually.TrueAsync(() => calls.Count == 7, TimeSpan.FromSeconds(5), "not every handler ran", Token);
        calls.ShouldBe(["throw:one", "first:one", "second:one", "session:s", "throw:two", "first:two", "second:two"]);
        a.Services.GetRequiredService<ISignalConnectionState>().IsConnected.ShouldBeTrue();
    }

    private static string ApplicationName(OrvanoApiFactory host) => host.Services.GetRequiredService<OrvanoInstance>().ApplicationName;

    private static async Task<int> ReadAsync(OrvanoApiFactory host, Func<int> factory) =>
        await host.Services.GetRequiredService<HybridCache>().GetOrCreateAsync(
            "listener:entry", _ => ValueTask.FromResult(factory()), cancellationToken: Token);

    // Reads the entry straight from the local cache under the decorator, without storing: -1 when nothing is there.
    private static async Task<int> InnerReadAsync(OrvanoApiFactory host) =>
        await host.Services.GetRequiredKeyedService<HybridCache>(SignalAwareHybridCache.InnerKey).GetOrCreateAsync(
            "listener:entry",
            _ => ValueTask.FromResult(-1),
            new HybridCacheEntryOptions { Flags = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCache },
            cancellationToken: Token);

    private static RecordingObserver Observer(OrvanoApiFactory host) =>
        host.Services.GetServices<ISignalConnectionObserver>().OfType<RecordingObserver>().Single();

    /// <summary>
    /// Waits until the host is connected and its observers heard <c>OnConnectedAsync</c>, then forgets the boot events
    /// (the disconnect announced before the first attempt, LSN-2), so a test counts only what happens after.
    /// </summary>
    private static async Task SettledAsync(OrvanoApiFactory host, RecordingObserver observer)
    {
        await host.WaitUntilConnectedAsync(Token);
        await Eventually.TrueAsync(() => observer.Count("connected") == 1, TimeSpan.FromSeconds(5), "OnConnectedAsync did not run", Token);
        observer.Events.Clear();
    }

    private OrvanoApiFactory Host(bool listenAsRole, Action<IServiceCollection>? configure = null)
    {
        string connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString(_settings["Database:Name"]))
        {
            Username = _role,
            Password = RolePassword,
            Pooling = false,
        }.ConnectionString;
        var host = new OrvanoApiFactory(new Dictionary<string, string>(_settings), services: services =>
        {
            services.AddSignalConnectionObserver<RecordingObserver>();
            if (listenAsRole)
            {
                services.AddKeyedSingleton(InfrastructureServices.SignalDataSourceKey, (provider, _) => NpgsqlDataSource.Create(
                    new NpgsqlConnectionStringBuilder(connection) { ApplicationName = provider.GetRequiredService<OrvanoInstance>().ApplicationName }.ConnectionString));
            }

            configure?.Invoke(services);
        });
        _hosts.Add(host);
        return host;
    }

    private sealed class ThrowingCacheHandler(ConcurrentQueue<string> calls) : ISignalHandler
    {
        public SignalKind Kind => SignalKind.Cache;

        public ValueTask HandleAsync(IReadOnlyList<string> values, CancellationToken cancellationToken)
        {
            calls.Enqueue($"throw:{values[0]}");
            throw new InvalidOperationException("handler failed");
        }
    }

    private sealed class FirstCacheHandler(ConcurrentQueue<string> calls) : ISignalHandler
    {
        public SignalKind Kind => SignalKind.Cache;

        public ValueTask HandleAsync(IReadOnlyList<string> values, CancellationToken cancellationToken)
        {
            calls.Enqueue($"first:{values[0]}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SecondCacheHandler(ConcurrentQueue<string> calls) : ISignalHandler
    {
        public SignalKind Kind => SignalKind.Cache;

        public ValueTask HandleAsync(IReadOnlyList<string> values, CancellationToken cancellationToken)
        {
            calls.Enqueue($"second:{values[0]}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SessionHandler(ConcurrentQueue<string> calls) : ISignalHandler
    {
        public SignalKind Kind => SignalKind.Session;

        public ValueTask HandleAsync(IReadOnlyList<string> values, CancellationToken cancellationToken)
        {
            calls.Enqueue($"session:{values[0]}");
            return ValueTask.CompletedTask;
        }
    }
}
