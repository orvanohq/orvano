using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using Orvano.Host.Database;
using Orvano.Host.IntegrationTests.Signals;
using Orvano.Infrastructure;
using Orvano.Infrastructure.Jobs;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Signals;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Jobs;

// Spec 0013 build plan steps 16, 17, and 19: the real runner loop on worker hosts (JTS-3): two workers, the signal,
// poll, and connect wakes, shutdown release, and a failing schedule tick. Each test runs on a database of its own.
public sealed class JobWorkerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string RolePassword = "jobs-test-only";

    private readonly List<OrvanoApiFactory> _hosts = [];
    private readonly string _role = $"jobs_{Guid.CreateVersion7():N}";
    private Dictionary<string, string> _settings = null!;
    private TestDatabase _database = null!;
    private JobRows _rows = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "worker", Token);
        _settings["Signals:ReconnectBackoffCap"] = "00:00:01";
        _database = TestDatabase.For(postgres, _settings);
        _rows = _database.Rows();
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
    [Trait("Spec", "0013:AC-19"), Trait("Spec", "0013:CLM-1"), Trait("Spec", "0013:CLM-2"), Trait("Spec", "0013:WAK-2")]
    public async Task Two_workers_run_50_due_jobs_exactly_once_between_them_and_neither_runs_more_than_its_concurrency()
    {
        _settings["Jobs:PollInterval"] = "00:00:01";
        _settings["Jobs:Concurrency"] = "4";
        JobProbes a = new(), b = new();
        OrvanoApiFactory workerA = Host(a, roles: "worker");
        OrvanoApiFactory workerB = Host(b, roles: "worker");
        _ = workerA.Services;
        _ = workerB.Services;
        IJobQueue queue = workerA.Services.GetRequiredService<IJobQueue>();
        await using (var db = ProbeContext.On(workerA.Services))
        {
            await db.InTransactionAsync(
                async token =>
                {
                    for (int i = 0; i < 50; i++)
                    {
                        await queue.EnqueueAsync(db, new ProbeJob(ProbeMode.Slow), null, token);
                    }
                },
                Token);
        }

        await Eventually.TrueAsync(
            async () => await _rows.CountAsync("status = 'succeeded'", Token) == 50, TimeSpan.FromSeconds(30), "not every job ran", Token);

        ProbeRun[] runs = [.. a.Runs, .. b.Runs];
        runs.Length.ShouldBe(50);
        runs.Select(run => run.Id).Distinct().Count().ShouldBe(50);
        runs.ShouldAllBe(run => run.Attempt == 1);
        a.MaxRunning.ShouldBeLessThanOrEqualTo(4);
        b.MaxRunning.ShouldBeLessThanOrEqualTo(4);
        (await _rows.CountAsync("attempt <> 1", Token)).ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-22"), Trait("Spec", "0013:WAK-1"), Trait("Spec", "0013:WAK-2"), Trait("Spec", "0013:WAK-3")]
    public async Task A_job_enqueued_on_an_api_host_starts_on_a_worker_by_signal_by_poll_after_a_drop_and_at_once_on_reconnect()
    {
        _settings["Jobs:PollInterval"] = "00:01:00";
        var clock = new ManualClock();
        var probes = new JobProbes();
        OrvanoApiFactory api = Host(probes, roles: "api", clock);
        OrvanoApiFactory worker = Host(probes, roles: "worker", clock, listenAsRole: true);
        await api.WaitUntilConnectedAsync(Token);
        await worker.WaitUntilConnectedAsync(Token);
        IJobQueue queue = api.Services.GetRequiredService<IJobQueue>();

        // The job signal: within 2 seconds, with the minute long poll nowhere near.
        Guid signalled = (await queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), null, Token))!.Value;
        await Eventually.TrueAsync(() => probes.Started.Contains(signalled), TimeSpan.FromSeconds(2), "the signal did not wake the worker", Token);

        // The worker's listener is gone: the signal is lost, and only the poll finds the job.
        await _database.ExecuteAsync($"ALTER ROLE {_role} NOLOGIN", Token);
        (await _database.TerminateAsync(worker.Services.GetRequiredService<OrvanoInstance>().ApplicationName, Token)).ShouldBe(1);
        await Eventually.TrueAsync(
            () => !worker.Services.GetRequiredService<ISignalConnectionState>().IsConnected, TimeSpan.FromSeconds(5), "the worker stayed connected", Token);
        Guid polled = (await queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), null, Token))!.Value;
        await Task.Delay(1000, Token);
        probes.Started.ShouldNotContain(polled);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Eventually.TrueAsync(() => probes.Started.Contains(polled), TimeSpan.FromSeconds(5), "the poll did not find the job", Token);

        // A future job starts at the first poll after its run_at, not before.
        Guid future = (await queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), new EnqueueOptions { RunAt = clock.GetUtcNow().AddSeconds(30) }, Token))!.Value;
        clock.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(500, Token);
        probes.Started.ShouldNotContain(future);
        clock.Advance(TimeSpan.FromSeconds(30));
        await Eventually.TrueAsync(() => probes.Started.Contains(future), TimeSpan.FromSeconds(5), "the poll after run_at did not run it", Token);

        // A job written while the worker was away starts when it reconnects, with no poll.
        Guid missed = await _rows.InsertQueuedAsync("test.probe", """{"Mode":"ok","Marker":""}""", clock.GetUtcNow(), Token);
        await _database.ExecuteAsync($"ALTER ROLE {_role} LOGIN", Token);
        clock.Advance(TimeSpan.FromSeconds(1)); // the reconnect backoff runs on the clock too; the next poll is 59 s away
        await worker.WaitUntilConnectedAsync(Token);
        await Eventually.TrueAsync(() => probes.Started.Contains(missed), TimeSpan.FromSeconds(5), "the reconnect did not claim", Token);
    }

    [Fact]
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:OUT-6"), Trait("Spec", "0013:OUT-8"), Trait("Spec", "0013:AC-27")]
    public async Task Shutdown_releases_a_cancelled_handlers_job_with_its_attempt_restored_and_records_one_that_finishes()
    {
        _settings["Jobs:PollInterval"] = "00:00:01";
        var probes = new JobProbes();
        var logs = new RecordingLogs();
        OrvanoApiFactory worker = Host(probes, roles: "worker", configure: services => services.AddSingleton<ILoggerProvider>(logs));
        IJobQueue queue = worker.Services.GetRequiredService<IJobQueue>();
        Guid released = (await queue.EnqueueAsync(new ProbeJob(ProbeMode.Block), null, Token))!.Value;
        Guid finished = (await queue.EnqueueAsync(new ProbeJob(ProbeMode.FinishOnCancel), null, Token))!.Value;
        await probes.StartedAsync(released, Token);
        await probes.StartedAsync(finished, Token);
        using var meters = new MeterRecorder(worker.Services.GetRequiredService<IMeterFactory>());

        DateTimeOffset before = TimeProvider.System.GetUtcNow();
        await worker.DisposeAsync();

        JobRows.Row releasedRow = (await _rows.FindAsync(released, Token))!;
        releasedRow.Status.ShouldBe("queued");
        releasedRow.Attempt.ShouldBe(0);
        releasedRow.RunAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        releasedRow.LockedBy.ShouldBeNull();
        (await _rows.FindAsync(finished, Token))!.Status.ShouldBe("succeeded");
        probes.Cancelled.ShouldContain(released);
        probes.Cancelled.ShouldContain(finished);
        logs.Messages(LogEventIds.Infrastructure + 110).ShouldHaveSingleItem().ShouldContain("Released 1 running jobs");
        meters.Sum("orvano.infrastructure.jobs.attempts", "outcome", "released").ShouldBe(1);
        meters.Sum("orvano.infrastructure.jobs.attempts", "outcome", "succeeded").ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-24"), Trait("Spec", "0013:SCH-3"), Trait("Spec", "0013:WAK-2")]
    public async Task A_schedule_tick_that_fails_is_logged_and_the_claim_on_the_same_poll_still_runs()
    {
        _settings["Jobs:PollInterval"] = "00:01:00";
        var clock = new ManualClock();
        var probes = new JobProbes();
        var logs = new RecordingLogs();
        OrvanoApiFactory worker = Host(probes, roles: "worker", clock, configure: services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddJobSchedule("test.minutely", "* * * * *", new ProbeJob(ProbeMode.Ok, "scheduled"));
        });
        _ = worker.Services;
        await Eventually.TrueAsync(
            () => logs.Messages(LogEventIds.Infrastructure + 112).Count == 1, TimeSpan.FromSeconds(5), "the schedules never synced", Token);
        await _database.ExecuteAsync(
            """
            CREATE FUNCTION jobs.test_refuse() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'tick refused'; END $$;
            CREATE TRIGGER test_refuse BEFORE UPDATE ON jobs.schedule FOR EACH ROW EXECUTE FUNCTION jobs.test_refuse();
            """,
            Token);
        Guid direct = await _rows.InsertQueuedAsync("test.probe", """{"Mode":"ok","Marker":""}""", clock.GetUtcNow(), Token);

        clock.Advance(TimeSpan.FromMinutes(1));

        await Eventually.TrueAsync(() => probes.Started.Contains(direct), TimeSpan.FromSeconds(5), "the claim did not run", Token);
        await Eventually.TrueAsync(
            () => logs.Count(LogEventIds.Infrastructure + 115, LogLevel.Error) >= 1, TimeSpan.FromSeconds(5), "the tick failure was not logged", Token);
        (await _rows.CountAsync("dedupe_key LIKE 'test.minutely:%'", Token)).ShouldBe(0); // the failed tick enqueued nothing
    }

    [Fact]
    [Trait("Spec", "0013:AC-26"), Trait("Spec", "0013:JOP-2"), Trait("Spec", "0013:JOP-3")]
    public void Only_a_worker_host_runs_the_runner_and_an_unset_concurrency_follows_the_half_pool_default()
    {
        OrvanoApiFactory api = Host(new JobProbes(), roles: "api");
        OrvanoApiFactory worker = Host(new JobProbes(), roles: "worker");

        api.Services.GetServices<IHostedService>().OfType<JobRunner>().ShouldBeEmpty();
        api.Services.GetServices<ISignalHandler>().ShouldNotContain(handler => handler.Kind == SignalKind.Job);
        worker.Services.GetServices<IHostedService>().OfType<JobRunner>().ShouldHaveSingleItem();
        worker.Services.GetServices<ISignalHandler>().ShouldContain(handler => handler.Kind == SignalKind.Job);
        worker.Services.GetRequiredService<IOptions<JobOptions>>().Value.Concurrency
            .ShouldBe(JobConcurrency.Default(Environment.ProcessorCount, 30));
        worker.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout.ShouldBe(TimeSpan.FromSeconds(25));
    }

    [Theory]
    [Trait("Spec", "0013:AC-26"), Trait("Spec", "0013:JOP-2")]
    [InlineData(32, 30, 15)]
    [InlineData(4, 30, 8)]
    [InlineData(2, 100, 4)]
    [InlineData(64, 100, 16)]
    [InlineData(8, 1, 1)]
    public void Default_concurrency_is_the_smallest_of_twice_the_processors_16_and_half_the_pool_and_at_least_1(int processors, int pool, int expected) =>
        JobConcurrency.Default(processors, pool).ShouldBe(expected);

    private OrvanoApiFactory Host(
        JobProbes probes,
        string roles,
        TimeProvider? clock = null,
        bool listenAsRole = false,
        Action<IServiceCollection>? configure = null)
    {
        string connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString(_settings["Database:Name"]))
        {
            Username = _role,
            Password = RolePassword,
            Pooling = false,
        }.ConnectionString;
        OrvanoApiFactory host = JobHosts.Create(_settings, probes, clock, roles, services =>
        {
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
}
