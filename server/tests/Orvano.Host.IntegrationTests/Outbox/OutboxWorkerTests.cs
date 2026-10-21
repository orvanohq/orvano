using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Host.Database;
using Orvano.Host.IntegrationTests.Jobs;
using Orvano.Host.IntegrationTests.Signals;
using Orvano.Infrastructure;
using Orvano.Infrastructure.Jobs;
using Orvano.Infrastructure.Outbox;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Outbox;
using Orvano.SharedKernel.Signals;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Outbox;

// Spec 0013 build plan steps 27 and 30: the dispatcher loop runs only on a worker, on the runner's wakes, so an event
// published through an api host reaches its subscriber on a worker by signal, and by the poll after the signal is lost.
// Each test runs its hosts on a database of its own.
public sealed class OutboxWorkerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string RolePassword = "outbox-test-only";

    private readonly List<OrvanoApiFactory> _hosts = [];
    private readonly string _role = $"outbox_{Guid.CreateVersion7():N}";
    private Dictionary<string, string> _settings = null!;
    private TestDatabase _database = null!;
    private OutboxRows _rows = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "outbox_worker", Token);
        _settings["Signals:ReconnectBackoffCap"] = "00:00:01";
        _database = TestDatabase.For(postgres, _settings);
        _rows = _database.OutboxRows();
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
    [Trait("Spec", "0013:AC-37"), Trait("Spec", "0013:DSP-1"), Trait("Spec", "0013:WAK-2"), Trait("Spec", "0013:AC-38")]
    public async Task An_event_published_on_an_api_host_reaches_its_subscriber_on_a_worker_by_signal_and_by_two_polls_after_a_drop()
    {
        _settings["Jobs:PollInterval"] = "00:01:00";
        var clock = new ManualClock();
        var probes = new OutboxProbes();
        OrvanoApiFactory api = Host(probes, roles: "api", clock);
        await SampleOutboxDbContext.CreateTablesAsync(api.Services, Token);
        OrvanoApiFactory worker = Host(probes, roles: "worker", clock, listenAsRole: true);
        await api.WaitUntilConnectedAsync(Token);
        await worker.WaitUntilConnectedAsync(Token);

        // The job signal of the publishing commit wakes the dispatcher, and the signal of its commit the runner.
        var project = Guid.CreateVersion7();
        var signalled = Guid.CreateVersion7();
        await OutboxHosts.PublishAsync(api.Services, new SampleNoteAdded(signalled), project, Token);
        await probes.DeliveredAsync(OutboxProbes.Recorder, signalled, TimeSpan.FromSeconds(2), Token);
        await probes.DeliveredAsync(OutboxProbes.Second, signalled, TimeSpan.FromSeconds(2), Token);
        probes.Of(OutboxProbes.Recorder).ShouldHaveSingleItem().Project.ShouldBe(project);

        // The worker's listener is gone: one poll dispatches, and the next one runs the deliveries.
        await _database.ExecuteAsync($"ALTER ROLE {_role} NOLOGIN", Token);
        (await _database.TerminateAsync(worker.Services.GetRequiredService<OrvanoInstance>().ApplicationName, Token)).ShouldBe(1);
        await Eventually.TrueAsync(
            () => !worker.Services.GetRequiredService<ISignalConnectionState>().IsConnected, TimeSpan.FromSeconds(5), "the worker stayed connected", Token);
        var polled = Guid.CreateVersion7();
        Guid polledEvent = await OutboxHosts.PublishAsync(api.Services, new SampleNoteAdded(polled), null, Token);
        await Task.Delay(1000, Token);
        (await _rows.CountAsync("id = $1 AND dispatched_at IS NULL", Token, polledEvent)).ShouldBe(1);

        clock.Advance(TimeSpan.FromMinutes(1));
        await Eventually.TrueAsync(
            async () => await _rows.CountAsync("id = $1 AND dispatched_at IS NOT NULL", Token, polledEvent) == 1,
            TimeSpan.FromSeconds(5), "the first poll did not dispatch", Token);
        clock.Advance(TimeSpan.FromMinutes(1));
        await probes.DeliveredAsync(OutboxProbes.Recorder, polled, TimeSpan.FromSeconds(5), Token);
        await probes.DeliveredAsync(OutboxProbes.Second, polled, TimeSpan.FromSeconds(5), Token);
    }

    [Fact]
    [Trait("Spec", "0013:AC-42"), Trait("Spec", "0013:DSP-1"), Trait("Spec", "0013:JOP-3"), Trait("Spec", "0013:OTL-2"), Trait("Spec", "0013:AC-41")]
    public async Task Only_a_worker_host_runs_the_dispatcher_and_it_is_registered_before_the_runner()
    {
        var logs = new RecordingLogs();
        OrvanoApiFactory api = Host(new OutboxProbes(), roles: "api", extra: services => services.AddSingleton<ILoggerProvider>(logs));
        OrvanoApiFactory worker = Host(new OutboxProbes(), roles: "worker", extra: services => services.AddSingleton<ILoggerProvider>(logs));

        api.Services.GetServices<IHostedService>().OfType<OutboxDispatcher>().ShouldBeEmpty();
        api.Services.GetRequiredService<IOutbox>().ShouldNotBeNull(); // every role can publish
        IHostedService[] hosted = [.. worker.Services.GetServices<IHostedService>()];
        int dispatcher = Array.FindIndex(hosted, service => service is OutboxDispatcher);
        int runner = Array.FindIndex(hosted, service => service is JobRunner);
        dispatcher.ShouldBeGreaterThanOrEqualTo(0);
        runner.ShouldBeGreaterThan(dispatcher); // the host stops the runner first (DSP-1)
        hosted.OfType<OutboxDispatcher>().ShouldHaveSingleItem();
        await Eventually.TrueAsync(
            () => logs.Count(LogEventIds.Infrastructure + 200, LogLevel.Information) == 1, TimeSpan.FromSeconds(5), "the dispatcher did not log its start", Token);
        logs.Messages(LogEventIds.Infrastructure + 200).ShouldBe(["Outbox dispatcher started with 1 outboxes and 3 subscribers"]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-42"), Trait("Spec", "0013:OGR-3")]
    public void Every_outbox_a_host_registers_has_a_migrated_context_that_maps_it_and_the_check_names_one_that_has_none()
    {
        OrvanoApiFactory production = Host(new OutboxProbes(), roles: "api", sampleOutbox: false);
        OrvanoApiFactory sample = Host(new OutboxProbes(), roles: "api");

        UnmappedOutboxes(production.Services).ShouldBeEmpty();
        UnmappedOutboxes(sample.Services).ShouldBe(["sample"]); // the sample context is not in MigrationOrder
    }

    /// <summary>OGR-3: the schemas of registered outboxes that no <c>MigrationOrder</c> context maps.</summary>
    private static IReadOnlyList<string> UnmappedOutboxes(IServiceProvider services)
    {
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        using IServiceScope scope = services.CreateScope();
        foreach (Type type in MigrationOrder.DbContexts)
        {
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(type);
            string? schema = OrvanoConventions.SchemaOf(db.GetService<IDbContextOptions>());
            if (schema is not null && db.Model.FindEntityType(typeof(OutboxEvent)) is not null)
            {
                mapped.Add(schema);
            }
        }

        return [.. services.GetRequiredService<IOutboxRegistry>().Outboxes.Select(outbox => outbox.Schema).Where(schema => !mapped.Contains(schema))];
    }

    private OrvanoApiFactory Host(
        OutboxProbes probes,
        string roles,
        TimeProvider? clock = null,
        bool listenAsRole = false,
        bool sampleOutbox = true,
        Action<IServiceCollection>? extra = null)
    {
        string connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString(_settings["Database:Name"]))
        {
            Username = _role,
            Password = RolePassword,
            Pooling = false,
        }.ConnectionString;
        Action<IServiceCollection> configure = services =>
        {
            if (listenAsRole)
            {
                services.AddKeyedSingleton(InfrastructureServices.SignalDataSourceKey, (provider, _) => NpgsqlDataSource.Create(
                    new NpgsqlConnectionStringBuilder(connection) { ApplicationName = provider.GetRequiredService<OrvanoInstance>().ApplicationName }.ConnectionString));
            }

            extra?.Invoke(services);
        };
        OrvanoApiFactory host = sampleOutbox
            ? OutboxHosts.Create(_settings, probes, clock, roles, configure)
            : JobHosts.Create(_settings, new JobProbes(), clock, roles, configure);
        _hosts.Add(host);
        return host;
    }
}
