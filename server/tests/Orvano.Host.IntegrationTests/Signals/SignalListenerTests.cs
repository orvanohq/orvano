using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.Host.Database;
using Orvano.Infrastructure;
using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel.Signals;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Signals;

// Spec 0013 build plan steps 6 and 7: the listener's failure handling and telemetry, on real hosts. Each test runs on
// a database of its own. Host A's listener logs in as a role of the test's own, so the test can stop it reconnecting.
public sealed class SignalListenerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string RolePassword = "signals-test-only";

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
        await a.WaitUntilConnectedAsync(Token);
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
        b.Services.GetRequiredService<ISignalConnectionState>().IsConnected.ShouldBeTrue();
        using (HttpClient client = a.CreateClient())
        {
            using HttpResponseMessage ready = await client.GetAsync("/health/ready", Token);
            ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await _database.ExecuteAsync($"ALTER ROLE {_role} LOGIN", Token);
        await a.WaitUntilConnectedAsync(Token);
        await Eventually.TrueAsync(() => observer.Count("connected") == 2, TimeSpan.FromSeconds(5), "OnConnectedAsync did not run again", Token);

        (await ReadAsync(a, () => 100)).ShouldBe(100); // the entry cached before the drop was cleared on connect
        observer.Count("disconnected").ShouldBe(1);
        observer.Events.First().ShouldBe("connected");
        observer.Events.Skip(1).First().ShouldBe("disconnected");
        meters.Sum("orvano.infrastructure.cache.clears", "reason", "connect").ShouldBe(clearsOnFirstConnect + 1);
        meters.Observe("orvano.infrastructure.signals.connected").ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-9"), Trait("Spec", "0013:LSN-5")]
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

        await _database.ExecuteAsync($"ALTER ROLE {_role} LOGIN", Token);
        await a.WaitUntilConnectedAsync(Token);
    }

    [Fact]
    [Trait("Spec", "0013:AC-8"), Trait("Spec", "0013:AC-11"), Trait("Spec", "0013:LSN-3")]
    public async Task No_heartbeat_for_the_timeout_counts_as_a_disconnect()
    {
        _settings["Signals:HeartbeatInterval"] = "00:00:01";
        _settings["Signals:HeartbeatTimeout"] = "00:00:02";
        OrvanoApiFactory a = Host(listenAsRole: false, services => services.AddSingleton<IHeartbeatSender, SilentHeartbeat>());
        RecordingObserver observer = Observer(a);
        await a.WaitUntilConnectedAsync(Token);

        await Eventually.TrueAsync(() => observer.Count("disconnected") >= 1, TimeSpan.FromSeconds(10), "the heartbeat timeout never fired", Token);
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
        await a.WaitUntilConnectedAsync(Token);

        await Task.Delay(TimeSpan.FromSeconds(4), Token);
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

    private static string ApplicationName(OrvanoApiFactory host) => host.Services.GetRequiredService<SignalInstance>().ApplicationName;

    private static async Task<int> ReadAsync(OrvanoApiFactory host, Func<int> factory) =>
        await host.Services.GetRequiredService<HybridCache>().GetOrCreateAsync(
            "listener:entry", _ => ValueTask.FromResult(factory()), cancellationToken: Token);

    private static RecordingObserver Observer(OrvanoApiFactory host) =>
        host.Services.GetServices<ISignalConnectionObserver>().OfType<RecordingObserver>().Single();

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
                    new NpgsqlConnectionStringBuilder(connection) { ApplicationName = provider.GetRequiredService<SignalInstance>().ApplicationName }.ConnectionString));
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
