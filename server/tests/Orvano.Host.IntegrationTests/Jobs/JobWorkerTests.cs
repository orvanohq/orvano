using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using Orvano.Host.Database;
using Orvano.Host.IntegrationTests.Outbox;
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
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:OUT-6"), Trait("Spec", "0013:WAK-4")]
    public async Task A_claim_stalled_at_shutdown_is_cut_off_at_its_bound_and_the_running_handler_is_still_released()
    {
        _settings["Jobs:PollInterval"] = "00:01:00";
        var clock = new ManualClock();
        var probes = new JobProbes();
        var logs = new RecordingLogs();
        OrvanoApiFactory worker = Host(probes, roles: "worker", clock, configure: services => services.AddSingleton<ILoggerProvider>(logs));
        Guid running = (await worker.Services.GetRequiredService<IJobQueue>().EnqueueAsync(new ProbeJob(ProbeMode.Block), null, Token))!.Value;
        await probes.StartedAsync(running, Token);

        // The test's table lock stalls the next claim, as a stalled database would; SKIP LOCKED skips row locks only.
        await using NpgsqlConnection locker = await _database.DataSource.OpenConnectionAsync(Token);
        await using NpgsqlTransaction tableLock = await locker.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("LOCK TABLE jobs.job IN ACCESS EXCLUSIVE MODE", locker, tableLock))
        {
            await command.ExecuteNonQueryAsync(Token);
        }

        // A job signal wakes the loop into the claim alone; a poll would run the dead marking first (WAK-2).
        await worker.WaitUntilConnectedAsync(Token);
        await _database.NotifyAsync("""{"v":1,"k":"job","d":[]}""", Token);
        await Eventually.TrueAsync(async () => await StalledClaimsAsync() == 1, TimeSpan.FromSeconds(5), "no claim stalled", Token);
        Task stopping = worker.DisposeAsync().AsTask();

        clock.Advance(JobExecutor.ClaimBound);

        await Eventually.TrueAsync(async () => await StalledClaimsAsync() == 0, TimeSpan.FromSeconds(5), "the claim was not cut off", Token);
        await tableLock.RollbackAsync(Token); // the release write waits on the lock too
        await stopping.WaitAsync(TimeSpan.FromSeconds(15), Token);
        JobRows.Row row = (await _rows.FindAsync(running, Token))!;
        row.Status.ShouldBe("queued");
        row.Attempt.ShouldBe(0);
        probes.Cancelled.ShouldContain(running);
        logs.Count(LogEventIds.Infrastructure + 101, LogLevel.Error).ShouldBe(1);

        async Task<long> StalledClaimsAsync()
        {
            await using NpgsqlCommand count = _database.DataSource.CreateCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%UPDATE jobs.job AS j%'");
            return (long)(await count.ExecuteScalarAsync(Token))!;
        }
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
    public void Only_a_worker_host_runs_the_runner_and_an_unset_concurrency_follows_the_pool_default()
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
    [InlineData(32, 30, 13)]
    [InlineData(4, 30, 8)]
    [InlineData(2, 100, 4)]
    [InlineData(64, 100, 16)]
    [InlineData(8, 6, 1)]
    [InlineData(8, 9, 2)] // half of an odd pool rounds down
    public void Default_concurrency_is_the_smallest_of_twice_the_processors_16_and_the_pool_limit_and_at_least_1(int processors, int pool, int expected) =>
        JobConcurrency.Default(processors, pool).ShouldBe(expected);

    [Theory]
    [Trait("Spec", "0013:AC-26"), Trait("Spec", "0013:JOP-2"), Trait("Spec", "0013:AC-40"), Trait("Spec", "0013:OPL-1")]
    [InlineData(30, 13, true)]
    [InlineData(30, 14, false)]
    [InlineData(31, 13, true)]
    [InlineData(31, 14, false)] // half of an odd pool rounds down
    [InlineData(6, 1, true)]
    [InlineData(6, 2, false)]
    public void The_pool_rule_allows_concurrency_up_to_half_the_pool_less_two_and_names_both_variables_past_it(int pool, int concurrency, bool allowed)
    {
        var rule = new JobConcurrency(Options.Create(new DatabaseOptions { PoolSize = pool }));

        ValidateOptionsResult result = rule.Validate(null, new JobOptions { Concurrency = concurrency });

        result.Succeeded.ShouldBe(allowed);
        if (!allowed)
        {
            int limit = (pool / 2) - 2;
            result.FailureMessage.ShouldBe(
                $"ORVANO_JOBS__CONCURRENCY ({concurrency}) must be at most {limit}: half of ORVANO_DATABASE__POOLSIZE ({pool}) "
                + "less two connections for the job runner and the outbox dispatcher (spec 0007 JOB-4).");
        }
    }

    [Fact]
    [Trait("Spec", "0013:AC-26"), Trait("Spec", "0013:JOP-2"), Trait("Spec", "0013:AC-40")]
    public void The_default_concurrency_passes_the_pool_rule_for_every_pool_size_the_options_allow()
    {
        int[] processorCounts = [1, 2, 8, 64];
        string[] refused = [.. Enumerable.Range(6, 995)
            .SelectMany(pool => processorCounts.Select(processors => (pool, processors)))
            .Where(pair => !new JobConcurrency(Options.Create(new DatabaseOptions { PoolSize = pair.pool }))
                .Validate(null, new JobOptions { Concurrency = JobConcurrency.Default(pair.processors, pair.pool) }).Succeeded)
            .Select(pair => $"pool {pair.pool}, {pair.processors} processors")];

        refused.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:AC-26"), Trait("Spec", "0013:JOP-2")]
    public void The_pool_rule_keeps_an_explicit_concurrency_and_fills_an_unset_one_with_a_default_that_passes()
    {
        var rule = new JobConcurrency(Options.Create(new DatabaseOptions { PoolSize = 6 }));
        var explicitOptions = new JobOptions { Concurrency = 3 };
        var unset = new JobOptions();

        rule.PostConfigure(null, explicitOptions);
        rule.PostConfigure(null, unset);

        explicitOptions.Concurrency.ShouldBe(3);
        unset.Concurrency.ShouldBe(1); // the smallest pool's limit
        rule.Validate(null, new JobOptions()).Succeeded.ShouldBeTrue(); // unset is never refused
    }

    [Fact]
    [Trait("Spec", "0013:AC-20"), Trait("Spec", "0013:CLM-1"), Trait("Spec", "0013:WAK-2")]
    public async Task An_expired_last_attempt_is_not_marked_dead_by_a_signal_wake_but_is_by_the_next_poll_tick()
    {
        _settings["Jobs:PollInterval"] = "00:01:00";
        var clock = new ManualClock();
        var probes = new JobProbes();
        OrvanoApiFactory worker = Host(probes, roles: "worker", clock);
        await worker.WaitUntilConnectedAsync(Token);
        var expired = Guid.CreateVersion7();
        await _database.ExecuteAsync(
            """
            INSERT INTO jobs.job (id, kind, payload, status, attempt, max_attempts, run_at, locked_until, locked_by, created_at)
            VALUES ($1, 'test.probe', '{"Mode":"ok","Marker":""}', 'running', 1, 1, $2, $2, 'a-gone-worker', $2)
            """,
            Token,
            expired,
            clock.GetUtcNow().AddSeconds(-1));

        // A signal wake claims the due job, and nothing else.
        Guid signalled = (await worker.Services.GetRequiredService<IJobQueue>().EnqueueAsync(new ProbeJob(ProbeMode.Ok), null, Token))!.Value;
        await Eventually.TrueAsync(
            async () => (await _rows.FindAsync(signalled, Token))!.Status == "succeeded", TimeSpan.FromSeconds(5), "the signal did not wake the worker", Token);
        (await _rows.FindAsync(expired, Token))!.Status.ShouldBe("running");

        clock.Advance(TimeSpan.FromMinutes(1));

        await Eventually.TrueAsync(
            async () => (await _rows.FindAsync(expired, Token))!.Status == "dead", TimeSpan.FromSeconds(5), "the poll tick did not mark it dead", Token);
        JobRows.Row row = (await _rows.FindAsync(expired, Token))!;
        row.LastError.ShouldBe("lease expired on the last attempt");
        row.FinishedAt.ShouldBe(clock.GetUtcNow());
        probes.Started.ShouldNotContain(expired);
    }

    [Fact]
    [Trait("Spec", "0013:CLM-1"), Trait("Spec", "0013:WAK-2")]
    public async Task A_wake_after_a_freed_slot_runs_only_the_claim_transaction()
    {
        _settings["Jobs:PollInterval"] = "00:01:00";
        _settings["Jobs:Concurrency"] = "1";
        var clock = new ManualClock();
        var probes = new JobProbes();
        OrvanoApiFactory worker = Host(probes, roles: "worker", clock);
        await worker.WaitUntilConnectedAsync(Token);
        Guid blocking = (await worker.Services.GetRequiredService<IJobQueue>().EnqueueAsync(new ProbeJob(ProbeMode.Block), null, Token))!.Value;
        await probes.StartedAsync(blocking, Token); // the claim asked for 1 and got 1, so a freed slot wakes the loop
        using var statements = new JobStatements(_settings["Database:Name"]);

        probes.Release.SetResult();

        await Eventually.TrueAsync(() => statements.Claims == 1, TimeSpan.FromSeconds(5), "the freed slot did not wake a claim", Token);
        statements.DeadMarkings.ShouldBe(0);
        (await _rows.FindAsync(blocking, Token))!.Status.ShouldBe("succeeded");
    }

    [Fact]
    [Trait("Spec", "0013:AC-26"), Trait("Spec", "0013:JOP-2"), Trait("Spec", "0013:JOP-4"), Trait("Spec", "0013:AC-40"), Trait("Spec", "0013:OPL-1")]
    public async Task Handlers_holding_a_connection_each_while_every_renewal_fires_and_a_dispatch_batch_runs_leave_a_request_a_pool_connection()
    {
        // Pool 10, so the limit is 3: 3 held, 3 renewals, the dispatcher's batch, and the request use 8, and none waits out
        // the pool timeout.
        _settings["Database:PoolSize"] = "10";
        _settings["Jobs:Concurrency"] = "3";
        _settings["Jobs:PollInterval"] = "00:01:00";
        var clock = new ManualClock();
        var probes = new JobProbes();
        var dispatch = new DispatchInterceptor { Hold = true, IgnoreCancel = true };
        OrvanoApiFactory worker = Host(probes, roles: "worker", clock, configure: services =>
        {
            services.AddSampleSubscribers(new OutboxProbes());
            DispatchInterceptor.AddTo(services, dispatch);
        });
        try
        {
            await SampleOutboxDbContext.CreateTablesAsync(worker.Services, Token);
            IJobQueue queue = worker.Services.GetRequiredService<IJobQueue>();
            var ids = new Guid[3];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = (await queue.EnqueueAsync(new LeaseJob(ProbeMode.HoldConnection), null, Token))!.Value;
            }

            await Eventually.TrueAsync(() => probes.Holding.Count == 3, TimeSpan.FromSeconds(10), "the handlers did not all hold a connection", Token);
            await OutboxHosts.PublishAsync(worker.Services, new SampleNoteAdded(Guid.CreateVersion7()), null, Token);
            await dispatch.Holding.Task.WaitAsync(TimeSpan.FromSeconds(10), Token); // a batch holds its connection, stalled
            DateTimeOffset renewedUntil = clock.GetUtcNow().AddSeconds(80);
            using HttpClient client = worker.CreateClient();

            clock.Advance(TimeSpan.FromSeconds(20)); // every handler's renewal is due at once (lease 1 minute ÷ 3)
            using HttpResponseMessage ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token)
                .WaitAsync(TimeSpan.FromSeconds(5), Token);

            ready.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
            await Eventually.TrueAsync(
                async () => await _rows.CountAsync("status = 'running' AND locked_until = $1", Token, renewedUntil) == 3,
                TimeSpan.FromSeconds(5), "not every renewal completed", Token);
        }
        finally
        {
            dispatch.Release.TrySetResult();
            probes.Release.TrySetResult();
        }

        await Eventually.TrueAsync(
            async () => await _rows.CountAsync("status = 'succeeded' AND kind = 'test.lease'", Token) == 3, TimeSpan.FromSeconds(10), "the jobs did not succeed", Token);
    }

    [Fact]
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:OUT-6"), Trait("Spec", "0013:CLM-6")]
    public async Task A_handler_still_running_past_the_shutdown_timeout_is_left_running_and_its_lease_recovers_it_with_attempt_2()
    {
        _settings["Jobs:PollInterval"] = "00:00:01";
        var probes = new JobProbes();
        OrvanoApiFactory worker = Host(probes, roles: "worker", configure: services =>
            services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(1)));
        Guid id = (await worker.Services.GetRequiredService<IJobQueue>().EnqueueAsync(new ProbeJob(ProbeMode.BlockIgnoringCancel), null, Token))!.Value;
        await probes.StartedAsync(id, Token);
        string workerId = worker.Services.GetRequiredService<OrvanoInstance>().WorkerId;

        try
        {
            await worker.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15), Token); // the shutdown does not wait on the handler

            JobRows.Row row = (await _rows.FindAsync(id, Token))!;
            row.Status.ShouldBe("running");
            row.Attempt.ShouldBe(1);
            row.LockedBy.ShouldBe(workerId);

            var clock = new ManualClock();
            TimeSpan untilExpired = row.LockedUntil!.Value - clock.GetUtcNow() + TimeSpan.FromSeconds(1);
            clock.Advance(untilExpired > TimeSpan.Zero ? untilExpired : TimeSpan.Zero);
            OrvanoApiFactory other = Host(new JobProbes(), roles: "api", clock);

            ClaimedJob claimed = (await other.Services.GetRequiredService<JobExecutor>().ClaimAsync(1, Token)).ShouldHaveSingleItem();
            claimed.Id.ShouldBe(id);
            claimed.Attempt.ShouldBe(2);
            (await _rows.FindAsync(id, Token))!.LockedBy.ShouldBe(other.Services.GetRequiredService<OrvanoInstance>().WorkerId);
        }
        finally
        {
            probes.Release.TrySetResult(); // the first host's handler returns; its fenced write finds the row moved on
        }
    }

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
