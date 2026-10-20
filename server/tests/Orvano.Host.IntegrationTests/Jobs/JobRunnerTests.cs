using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Host.IntegrationTests.Signals;
using Orvano.Infrastructure.Jobs;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Jobs;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Jobs;

// Spec 0013 build plan steps 16 to 18: claims, outcomes, leases, timeouts, the project scope, and project cancel, on an
// api host (no runner) whose ManualClock the test moves, with JobsDriver running due jobs inline (JTS-2, JTS-3).
public sealed class JobRunnerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero); // ManualClock's start

    private readonly JobProbes _probes = new();
    private readonly ManualClock _clock = new();
    private readonly RecordingLogs _logs = new();
    private readonly List<OrvanoApiFactory> _hosts = [];
    private OrvanoApiFactory _host = null!;
    private TestDatabase _database = null!;
    private JobRows _rows = null!;
    private Dictionary<string, string> _settings = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IJobQueue Queue => _host.Services.GetRequiredService<IJobQueue>();

    private JobExecutor Executor => _host.Services.GetRequiredService<JobExecutor>();

    public async ValueTask InitializeAsync()
    {
        _settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "runner", Token);
        _settings["Logging:LogLevel:Default"] = "Debug";
        _database = TestDatabase.For(postgres, _settings);
        _rows = _database.Rows();
        _host = Host();
        _ = _host.Services; // starts it
    }

    public async ValueTask DisposeAsync()
    {
        _probes.Release.TrySetResult();
        foreach (OrvanoApiFactory host in _hosts)
        {
            await host.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:AC-23"), Trait("Spec", "0013:OUT-1"), Trait("Spec", "0013:CLM-7")]
    public async Task Jobs_run_as_the_system_caller_with_their_project_and_succeed_with_finished_at_and_no_lease()
    {
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());
        var project = Guid.CreateVersion7();
        Guid withProject = (await Queue.EnqueueAsync(new ProjectJob(ProbeMode.Ok), new EnqueueOptions { ProjectId = project }, Token))!.Value;
        Guid without = (await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), null, Token))!.Value;

        IReadOnlyList<JobRun> runs = await JobsDriver.RunDueAsync(_host.Services, Token);

        // Both are due at the same instant, and v7 ids made in one millisecond are not ordered.
        runs.ShouldBe([new JobRun("test.project", withProject, "succeeded"), new JobRun("test.probe", without, "succeeded")], ignoreOrder: true);
        ProbeRun projectRun = _probes.Runs.Single(run => run.Id == withProject);
        projectRun.Caller.ShouldBe(CallerKind.System);
        projectRun.Project.ShouldBe(project);
        projectRun.Attempt.ShouldBe(1);
        ProbeRun plainRun = _probes.Runs.Single(run => run.Id == without);
        plainRun.Caller.ShouldBe(CallerKind.System);
        plainRun.Project.ShouldBeNull();
        plainRun.ProjectError.ShouldNotBeNull();
        JobRows.Row row = (await _rows.FindAsync(withProject, Token))!;
        row.Status.ShouldBe("succeeded");
        row.FinishedAt.ShouldBe(Start);
        row.LockedBy.ShouldBeNull();
        row.LockedUntil.ShouldBeNull();
        meters.Sum("orvano.infrastructure.jobs.attempts", "outcome", "succeeded").ShouldBe(2);
        meters.Values("orvano.infrastructure.jobs.duration", "kind", "test.project").ShouldHaveSingleItem();
        meters.Values("orvano.infrastructure.jobs.lag", "kind", "test.project").ShouldHaveSingleItem().ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-29"), Trait("Spec", "0013:JTS-1"), Trait("Spec", "0013:JTS-2")]
    public async Task A_delayed_job_runs_only_once_the_clock_passes_its_run_at()
    {
        Guid id = (await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), new EnqueueOptions { RunAt = Start.AddMinutes(10) }, Token))!.Value;

        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBeEmpty();
        _clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBeEmpty();
        _clock.Advance(TimeSpan.FromSeconds(1));

        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun("test.probe", id, "succeeded")]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:OUT-2"), Trait("Spec", "0013:OUT-3"), Trait("Spec", "0013:OUT-5"), Trait("Spec", "0013:AC-27")]
    public async Task A_throwing_handler_puts_the_job_back_with_backoff_and_its_error_until_the_last_attempt_kills_it()
    {
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());
        Guid id = (await Queue.EnqueueAsync(new ShortJob(ProbeMode.Throw), null, Token))!.Value;

        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun("test.short", id, "failed")]);

        JobRows.Row failed = (await _rows.FindAsync(id, Token))!;
        failed.Status.ShouldBe("queued");
        failed.Attempt.ShouldBe(1);
        failed.RunAt.ShouldBeInRange(Start.AddSeconds(5), Start.AddSeconds(10));
        failed.LastError.ShouldBe("InvalidOperationException: boom");
        failed.LockedBy.ShouldBeNull();

        _clock.Advance(TimeSpan.FromSeconds(10));
        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun("test.short", id, "dead")]);

        JobRows.Row dead = (await _rows.FindAsync(id, Token))!;
        dead.Status.ShouldBe("dead");
        dead.Attempt.ShouldBe(2);
        dead.FinishedAt.ShouldBe(Start.AddSeconds(10));
        dead.LastError.ShouldBe("InvalidOperationException: boom");
        meters.Sum("orvano.infrastructure.jobs.attempts", "outcome", "failed").ShouldBe(1);
        meters.Sum("orvano.infrastructure.jobs.attempts", "outcome", "dead").ShouldBe(1);
        _logs.Count(LogEventIds.Infrastructure + 104, LogLevel.Warning).ShouldBe(1);
        _logs.Count(LogEventIds.Infrastructure + 105, LogLevel.Error).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:OUT-4"), Trait("Spec", "0013:JQ-4")]
    public async Task A_permanent_failure_kills_the_job_at_once()
    {
        Guid id = (await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Permanent), null, Token))!.Value;

        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun("test.probe", id, "dead")]);

        JobRows.Row row = (await _rows.FindAsync(id, Token))!;
        row.Status.ShouldBe("dead");
        row.Attempt.ShouldBe(1);
        row.LastError.ShouldBe("PermanentJobFailureException: retrying will not help");
    }

    [Fact]
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:OUT-4"), Trait("Spec", "0013:JQ-5")]
    public async Task A_row_of_a_project_kind_with_no_project_dies_at_once_without_running()
    {
        Guid id = await _rows.InsertQueuedAsync("test.project", """{"Mode":"ok","Marker":""}""", Start, Token);

        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun("test.project", id, "dead")]);

        (await _rows.FindAsync(id, Token))!.LastError.ShouldBe("project id missing");
        _probes.Runs.ShouldBeEmpty();
    }

    [Theory]
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:OUT-2")]
    [InlineData("test.gone", """{}""", "no handler registered for kind")]
    [InlineData("test.probe", """{"Mode":5}""", "payload could not be read: ")]
    public async Task An_unregistered_kind_or_unreadable_payload_is_a_failed_attempt_with_backoff(string kind, string payload, string lastError)
    {
        Guid id = await _rows.InsertQueuedAsync(kind, payload, Start, Token);

        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun(kind, id, "failed")]);

        JobRows.Row row = (await _rows.FindAsync(id, Token))!;
        row.Status.ShouldBe("queued");
        row.Attempt.ShouldBe(1);
        row.RunAt.ShouldBeInRange(Start.AddSeconds(5), Start.AddSeconds(10));
        row.LastError.ShouldStartWith(lastError);
        _probes.Runs.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:OUT-8")]
    public async Task A_handler_that_throws_its_own_cancellation_is_a_failed_attempt()
    {
        Guid id = (await Queue.EnqueueAsync(new ProbeJob(ProbeMode.OwnCancel), null, Token))!.Value;

        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun("test.probe", id, "failed")]);

        (await _rows.FindAsync(id, Token))!.LastError.ShouldBe("OperationCanceledException: the handler's own timeout");
    }

    [Fact]
    [Trait("Spec", "0013:AC-21"), Trait("Spec", "0013:AC-19"), Trait("Spec", "0013:OUT-2"), Trait("Spec", "0013:CLM-2")]
    public async Task A_timed_out_handler_gets_its_outcome_at_once_but_keeps_its_slot_and_is_not_claimed_again_until_it_returns()
    {
        Guid id = (await Queue.EnqueueAsync(new ShortJob(ProbeMode.BlockIgnoringCancel), null, Token))!.Value;
        Task<IReadOnlyList<JobRun>> running = JobsDriver.RunDueAsync(_host.Services, Token);
        await _probes.StartedAsync(id, Token);

        _clock.Advance(TimeSpan.FromSeconds(1)); // the kind's one second timeout

        (await running.WaitAsync(TimeSpan.FromSeconds(10), Token)).ShouldBe([new JobRun("test.short", id, "failed")]);
        JobRows.Row row = (await _rows.FindAsync(id, Token))!;
        row.Status.ShouldBe("queued");
        row.LastError.ShouldBe("timed out after 00:00:01");
        Executor.RunningCount.ShouldBe(1); // the handler has not returned
        using (var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>()))
        {
            meters.Observe("orvano.infrastructure.jobs.running").ShouldBe(1);
        }

        _clock.Advance(TimeSpan.FromSeconds(10)); // due again
        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBeEmpty(); // still running here (@mine)

        _probes.Release.SetResult();
        await Eventually.TrueAsync(() => Executor.RunningCount == 0, TimeSpan.FromSeconds(5), "the slot was not freed", Token);
        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun("test.short", id, "succeeded")]);
        _logs.Count(LogEventIds.Infrastructure + 107, LogLevel.Warning).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-20"), Trait("Spec", "0013:CLM-5")]
    public async Task A_running_handler_renews_its_lease_every_third_of_it_and_succeeds()
    {
        Guid id = (await Queue.EnqueueAsync(new LeaseJob(ProbeMode.Block), null, Token))!.Value;
        Task<IReadOnlyList<JobRun>> running = JobsDriver.RunDueAsync(_host.Services, Token);
        await _probes.StartedAsync(id, Token);
        (await _rows.FindAsync(id, Token))!.LockedUntil.ShouldBe(Start.AddMinutes(1));

        _clock.Advance(TimeSpan.FromSeconds(20));

        await Eventually.TrueAsync(
            async () => (await _rows.FindAsync(id, Token))!.LockedUntil == Start.AddSeconds(80),
            TimeSpan.FromSeconds(5), "the lease was not renewed", Token);
        _probes.Release.SetResult();
        (await running.WaitAsync(TimeSpan.FromSeconds(10), Token)).ShouldBe([new JobRun("test.lease", id, "succeeded")]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-20"), Trait("Spec", "0013:CLM-5")]
    public async Task A_renewal_still_in_flight_makes_the_next_tick_skip_and_a_lease_never_renewed_is_lost_when_it_runs_out()
    {
        Guid id = (await Queue.EnqueueAsync(new LeaseJob(ProbeMode.Block), null, Token))!.Value;
        Task<IReadOnlyList<JobRun>> running = JobsDriver.RunDueAsync(_host.Services, Token);
        await _probes.StartedAsync(id, Token);
        await using RowLock rowLock = await RowLock.TakeAsync(_database.DataSource, id, Token);

        _clock.Advance(TimeSpan.FromSeconds(20)); // the renewal waits on the test's row lock
        await Eventually.TrueAsync(async () => await rowLock.WaitingRenewalsAsync(Token) == 1, TimeSpan.FromSeconds(5), "no renewal waited", Token);

        // The next tick finds that renewal in flight and skips; the renewal's own lease ÷ 3 bound then gives it up.
        _clock.Advance(TimeSpan.FromSeconds(20));
        await Eventually.TrueAsync(
            () => _logs.Count(LogEventIds.Infrastructure + 119, LogLevel.Warning) == 1, TimeSpan.FromSeconds(5), "the renewal was not given up", Token);
        await Task.Delay(300, Token);
        (await rowLock.WaitingRenewalsAsync(Token)).ShouldBe(0);
        await rowLock.ReleaseAsync(Token);

        _clock.Advance(TimeSpan.FromSeconds(20)); // past the only lease this worker set

        (await running.WaitAsync(TimeSpan.FromSeconds(10), Token)).ShouldBe([new JobRun("test.lease", id, "lease_lost")]);
        await Eventually.TrueAsync(() => _probes.Cancelled.Contains(id), TimeSpan.FromSeconds(5), "the handler was not cancelled", Token);
        (await _rows.FindAsync(id, Token))!.Status.ShouldBe("running"); // no outcome written; the lease recovers it
    }

    [Fact]
    [Trait("Spec", "0013:AC-20"), Trait("Spec", "0013:CLM-4"), Trait("Spec", "0013:CLM-5"), Trait("Spec", "0013:AC-27")]
    public async Task A_row_another_worker_took_cancels_the_handler_at_the_next_renewal_and_writes_no_outcome()
    {
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());
        Guid id = (await Queue.EnqueueAsync(new LeaseJob(ProbeMode.Block), null, Token))!.Value;
        Task<IReadOnlyList<JobRun>> running = JobsDriver.RunDueAsync(_host.Services, Token);
        await _probes.StartedAsync(id, Token);

        await _database.ExecuteAsync("UPDATE jobs.job SET locked_by = 'thief:00000000' WHERE id = $1", Token, id);
        _clock.Advance(TimeSpan.FromSeconds(20));

        (await running.WaitAsync(TimeSpan.FromSeconds(10), Token)).ShouldBe([new JobRun("test.lease", id, "lease_lost")]);
        await Eventually.TrueAsync(() => _probes.Cancelled.Contains(id), TimeSpan.FromSeconds(5), "the handler was not cancelled", Token);
        JobRows.Row row = (await _rows.FindAsync(id, Token))!;
        row.Status.ShouldBe("running");
        row.LockedBy.ShouldBe("thief:00000000");
        meters.Sum("orvano.infrastructure.jobs.attempts", "outcome", "lease_lost").ShouldBe(1);
        _logs.Count(LogEventIds.Infrastructure + 106, LogLevel.Warning).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-20"), Trait("Spec", "0013:CLM-5"), Trait("Spec", "0013:AC-27")]
    public async Task A_handler_that_returns_while_a_renewal_is_in_flight_succeeds_and_counts_no_lost_lease()
    {
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());
        Guid id = (await Queue.EnqueueAsync(new LeaseJob(ProbeMode.Block), null, Token))!.Value;
        Task<IReadOnlyList<JobRun>> running = JobsDriver.RunDueAsync(_host.Services, Token);
        await _probes.StartedAsync(id, Token);
        await using RowLock rowLock = await RowLock.TakeAsync(_database.DataSource, id, Token);
        _clock.Advance(TimeSpan.FromSeconds(20));
        await Eventually.TrueAsync(async () => await rowLock.WaitingRenewalsAsync(Token) == 1, TimeSpan.FromSeconds(5), "no renewal waited", Token);

        _probes.Release.SetResult(); // the handler returns; the outcome waits for the renewal
        await Task.Delay(200, Token);
        running.IsCompleted.ShouldBeFalse();
        await rowLock.ReleaseAsync(Token);

        (await running.WaitAsync(TimeSpan.FromSeconds(10), Token)).ShouldBe([new JobRun("test.lease", id, "succeeded")]);
        meters.Sum("orvano.infrastructure.jobs.attempts", "outcome", "lease_lost").ShouldBe(0);
        meters.Sum("orvano.infrastructure.jobs.attempts", "outcome", "succeeded").ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-20"), Trait("Spec", "0013:CLM-6"), Trait("Spec", "0013:AC-19")]
    public async Task A_job_whose_worker_died_is_claimed_by_another_host_after_its_lease_with_attempt_2()
    {
        Guid id = (await Queue.EnqueueAsync(new LeaseJob(ProbeMode.Ok), null, Token))!.Value;
        (await Executor.ClaimAsync(1, Token)).ShouldHaveSingleItem().Id.ShouldBe(id); // claimed, and never run
        await _host.DisposeAsync();
        OrvanoApiFactory other = Host();

        _clock.Advance(TimeSpan.FromSeconds(59));
        (await JobsDriver.RunDueAsync(other.Services, Token)).ShouldBeEmpty(); // the lease still holds
        _clock.Advance(TimeSpan.FromSeconds(2));

        (await JobsDriver.RunDueAsync(other.Services, Token)).ShouldBe([new JobRun("test.lease", id, "succeeded")]);
        _probes.Runs.ShouldHaveSingleItem().Attempt.ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "0013:AC-20"), Trait("Spec", "0013:CLM-1"), Trait("Spec", "0013:CLM-6"), Trait("Spec", "0013:AC-27")]
    public async Task A_job_whose_worker_died_on_its_last_attempt_is_dead_after_its_lease_without_running_again()
    {
        Guid id = (await Queue.EnqueueAsync(new LeaseJob(ProbeMode.Ok), null, Token))!.Value;
        await _database.ExecuteAsync("UPDATE jobs.job SET max_attempts = 1 WHERE id = $1", Token, id);
        (await Executor.ClaimAsync(1, Token)).ShouldHaveSingleItem();
        await _host.DisposeAsync();
        OrvanoApiFactory other = Host();
        using var meters = new MeterRecorder(other.Services.GetRequiredService<IMeterFactory>());

        _clock.Advance(TimeSpan.FromSeconds(61));

        (await JobsDriver.RunDueAsync(other.Services, Token)).ShouldBeEmpty();
        JobRows.Row row = (await _rows.FindAsync(id, Token))!;
        row.Status.ShouldBe("dead");
        row.LastError.ShouldBe("lease expired on the last attempt");
        row.LockedBy.ShouldBeNull();
        _probes.Runs.ShouldBeEmpty();
        meters.Sum("orvano.infrastructure.jobs.attempts", "outcome", "dead").ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-30"), Trait("Spec", "0013:ENQ-7")]
    public async Task Cancelling_a_projects_jobs_kills_its_queued_and_running_ones_but_the_given_one_and_other_projects()
    {
        Guid project = Guid.CreateVersion7(), other = Guid.CreateVersion7();
        Guid running = (await Queue.EnqueueAsync(new ProjectJob(ProbeMode.Block), new EnqueueOptions { ProjectId = project }, Token))!.Value;
        Task<IReadOnlyList<JobRun>> driver = JobsDriver.RunDueAsync(_host.Services, Token);
        await _probes.StartedAsync(running, Token);
        Guid queued = (await Queue.EnqueueAsync(new ProjectJob(ProbeMode.Ok), new EnqueueOptions { ProjectId = project }, Token))!.Value;
        Guid kept = (await Queue.EnqueueAsync(new ProjectJob(ProbeMode.Ok), new EnqueueOptions { ProjectId = project }, Token))!.Value;
        Guid otherProject = (await Queue.EnqueueAsync(new ProjectJob(ProbeMode.Ok), new EnqueueOptions { ProjectId = other }, Token))!.Value;

        (await Queue.CancelProjectJobsAsync(project, kept, Token)).ShouldBe(2);

        foreach (Guid id in new[] { running, queued })
        {
            JobRows.Row row = (await _rows.FindAsync(id, Token))!;
            row.Status.ShouldBe("dead");
            row.LastError.ShouldBe("cancelled: project deleted");
            row.FinishedAt.ShouldBe(Start);
        }

        (await _rows.FindAsync(kept, Token))!.Status.ShouldBe("queued");
        (await _rows.FindAsync(otherProject, Token))!.Status.ShouldBe("queued");
        _probes.Cancelled.ShouldBeEmpty();

        _clock.Advance(TimeSpan.FromSeconds(100)); // the 5 minute lease's renewal finds the row gone

        IReadOnlyList<JobRun> runs = await driver.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await Eventually.TrueAsync(() => _probes.Cancelled.Contains(running), TimeSpan.FromSeconds(5), "the handler was not cancelled", Token);
        runs.ShouldBe(
            [
                new JobRun("test.project", running, "lease_lost"),
                new JobRun("test.project", kept, "succeeded"),
                new JobRun("test.project", otherProject, "succeeded"),
            ],
            ignoreOrder: true);
    }

    [Fact]
    [Trait("Spec", "0013:AC-19"), Trait("Spec", "0013:CLM-1"), Trait("Spec", "0013:JDB-2")]
    public async Task An_idle_claim_over_10000_future_jobs_uses_the_due_and_lease_index_range_scans()
    {
        await _database.ExecuteAsync(
            """
            INSERT INTO jobs.job (id, kind, payload, status, attempt, max_attempts, run_at, created_at)
            SELECT gen_random_uuid(), 'test.probe', '{}', 'queued', 0, 10, $1 + make_interval(secs => n), $1
            FROM generate_series(1, 10000) AS n
            """,
            Token,
            Start.AddHours(1));
        await _database.ExecuteAsync("ANALYZE jobs.job", Token);

        await using NpgsqlCommand explain = _database.DataSource.CreateCommand("EXPLAIN " + JobExecutor.ClaimSql);
        explain.Parameters.Add(new NpgsqlParameter<string>("worker", "test:00000000"));
        explain.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", Start));
        explain.Parameters.Add(new NpgsqlParameter<string[]>("kinds", ["test.probe"]));
        explain.Parameters.Add(new NpgsqlParameter<TimeSpan[]>("leases", [TimeSpan.FromMinutes(5)]));
        explain.Parameters.Add(new NpgsqlParameter<Guid[]>("mine", []));
        explain.Parameters.Add(new NpgsqlParameter<int>("free", 4));
        var plan = new List<string>();
        await using (NpgsqlDataReader reader = await explain.ExecuteReaderAsync(Token))
        {
            while (await reader.ReadAsync(Token))
            {
                plan.Add(reader.GetString(0));
            }
        }

        string text = string.Join('\n', plan);
        text.ShouldContain("ix_job_due");
        text.ShouldContain("ix_job_lease");
        text.ShouldNotContain("Seq Scan on job");
        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:AC-27"), Trait("Spec", "0013:JTL-2"), Trait("Spec", "0013:JTL-3")]
    public async Task An_attempt_runs_in_a_consumer_activity_linked_to_the_enqueuing_trace_and_logs_no_payload_above_Debug()
    {
        const string marker = "payload-marker-7f3a";
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Orvano.Infrastructure",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (activities)
                {
                    activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var enqueuing = new Activity("test enqueue");
        enqueuing.SetIdFormat(ActivityIdFormat.W3C);
        enqueuing.Start();
        Guid id = (await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Throw, marker), null, Token))!.Value;
        enqueuing.Stop();
        (await _rows.FindAsync(id, Token))!.TraceParent.ShouldBe(enqueuing.Id);

        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun("test.probe", id, "failed")]);

        Activity job;
        lock (activities)
        {
            job = activities.Single(activity => Equals(activity.GetTagItem("orvano.job.id"), id));
        }

        job.DisplayName.ShouldBe("job test.probe");
        job.Kind.ShouldBe(ActivityKind.Consumer);
        job.Links.ShouldHaveSingleItem().Context.TraceId.ShouldBe(enqueuing.TraceId);
        job.GetTagItem("orvano.job.kind").ShouldBe("test.probe");
        job.GetTagItem("orvano.job.attempt").ShouldBe(1);
        job.Status.ShouldBe(ActivityStatusCode.Error);
        _logs.TextAtOrAbove(LogLevel.Information).ShouldNotContain(text => text.Contains(marker, StringComparison.Ordinal));
        _logs.TextAtOrAbove(LogLevel.Debug).ShouldContain(text => text.Contains(marker, StringComparison.Ordinal)); // the capture sees Debug
    }

    private OrvanoApiFactory Host()
    {
        OrvanoApiFactory host = JobHosts.Create(_settings, _probes, _clock, roles: "api", services =>
            services.AddSingleton<ILoggerProvider>(_logs));
        _hosts.Add(host);
        return host;
    }

    /// <summary>A test transaction holding a job row's lock, so the runner's renewal of it waits.</summary>
    private sealed class RowLock : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly NpgsqlConnection _connection;
        private NpgsqlTransaction? _transaction;

        private RowLock(NpgsqlDataSource dataSource, NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _dataSource = dataSource;
            _connection = connection;
            _transaction = transaction;
        }

        public static async Task<RowLock> TakeAsync(NpgsqlDataSource dataSource, Guid id, CancellationToken cancellationToken)
        {
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
            NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using NpgsqlCommand command = new("SELECT 1 FROM jobs.job WHERE id = $1 FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue(id);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new RowLock(dataSource, connection, transaction);
        }

        /// <summary>Renewals now blocked on this lock.</summary>
        public async Task<long> WaitingRenewalsAsync(CancellationToken cancellationToken)
        {
            await using NpgsqlCommand command = _dataSource.CreateCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%SET locked_until%'");
            return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        public async Task ReleaseAsync(CancellationToken cancellationToken)
        {
            if (_transaction is not null)
            {
                await _transaction.RollbackAsync(cancellationToken);
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync(CancellationToken.None);
            await _connection.DisposeAsync();
        }
    }
}
