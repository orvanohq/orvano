using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Host.IntegrationTests.Signals;
using Orvano.Infrastructure.Jobs;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Jobs;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Jobs;

// Spec 0013 build plan step 19: schedules sync from code, fire once per occurrence across workers, collapse missed
// fires, and the built in cleanup. Api hosts (no runner) on a shared ManualClock, driven by JobsDriver and the scheduler.
public sealed class JobScheduleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero); // ManualClock's start

    private readonly JobProbes _probes = new();
    private readonly ManualClock _clock = new();
    private readonly RecordingLogs _logs = new();
    private readonly List<OrvanoApiFactory> _hosts = [];
    private Dictionary<string, string> _settings = null!;
    private TestDatabase _database = null!;
    private JobRows _rows = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "schedule", Token);
        _database = TestDatabase.For(postgres, _settings);
        _rows = _database.Rows();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (OrvanoApiFactory host in _hosts)
        {
            await host.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:AC-24"), Trait("Spec", "0013:SCH-4"), Trait("Spec", "0013:JOB-5"), Trait("Spec", "0013:AC-27")]
    public async Task Two_workers_ticking_at_the_same_instant_fire_a_due_schedule_once_with_its_dedupe_key()
    {
        OrvanoApiFactory a = Host(services => services.AddJobSchedule("test.minutely", "* * * * *", new ProbeJob(ProbeMode.Ok)));
        OrvanoApiFactory b = Host(services => services.AddJobSchedule("test.minutely", "* * * * *", new ProbeJob(ProbeMode.Ok)));
        using var metersA = new MeterRecorder(a.Services.GetRequiredService<IMeterFactory>());
        using var metersB = new MeterRecorder(b.Services.GetRequiredService<IMeterFactory>());
        await Task.WhenAll(Scheduler(a).SyncAsync(Token), Scheduler(b).SyncAsync(Token));
        _clock.Advance(TimeSpan.FromMinutes(1));

        await Task.WhenAll(Scheduler(a).TickAsync(Token), Scheduler(b).TickAsync(Token));

        JobRows.Row job = (await _rows.WhereAsync("kind = 'test.probe'", Token)).ShouldHaveSingleItem();
        job.DedupeKey.ShouldBe("test.minutely:20261019T120100Z");
        job.RunAt.ShouldBe(Start.AddMinutes(1));
        (metersA.Sum("orvano.infrastructure.jobs.schedules.fired", "schedule", "test.minutely")
            + metersB.Sum("orvano.infrastructure.jobs.schedules.fired", "schedule", "test.minutely")).ShouldBe(1);
        (await ScheduleAsync("test.minutely")).NextRunAt.ShouldBe(Start.AddMinutes(2));
    }

    [Fact]
    [Trait("Spec", "0013:AC-24"), Trait("Spec", "0013:SCH-5"), Trait("Spec", "0013:AC-29"), Trait("Spec", "0013:JTS-2")]
    public async Task A_schedule_missed_for_3_days_fires_once_through_JobsDriver_on_an_api_host_then_moves_past_now()
    {
        OrvanoApiFactory host = Host(services => services.AddJobSchedule("test.hourly", "0 * * * *", new ProbeJob(ProbeMode.Ok)));
        (await JobsDriver.RunDueAsync(host.Services, Token)).ShouldBeEmpty(); // syncs first: next_run_at 13:00
        (await ScheduleAsync("test.hourly")).NextRunAt.ShouldBe(Start.AddHours(1));

        _clock.Advance(TimeSpan.FromDays(3));
        IReadOnlyList<JobRun> runs = await JobsDriver.RunDueAsync(host.Services, Token);

        runs.Where(run => run.Kind == "test.probe").ShouldHaveSingleItem().Outcome.ShouldBe("succeeded");
        (await _rows.WhereAsync("kind = 'test.probe'", Token)).ShouldHaveSingleItem().DedupeKey.ShouldBe("test.hourly:20261019T130000Z");
        Schedule schedule = await ScheduleAsync("test.hourly");
        schedule.NextRunAt.ShouldBe(Start.AddDays(3).AddHours(1));
        schedule.LastFiredAt.ShouldBe(Start.AddDays(3));
        runs.Where(run => run.Kind == "jobs.cleanup").ShouldHaveSingleItem(); // also behind, also once
    }

    [Fact]
    [Trait("Spec", "0013:AC-24"), Trait("Spec", "0013:SCH-2"), Trait("Spec", "0013:AC-27")]
    public async Task Sync_moves_next_run_at_when_the_cron_changes_keeps_it_when_not_and_deletes_a_schedule_no_longer_registered()
    {
        OrvanoApiFactory first = Host(services => services.AddJobSchedule("test.sched", "0 * * * *", new ProbeJob(ProbeMode.Ok)));
        await Scheduler(first).SyncAsync(Token);
        (await ScheduleAsync("test.sched")).NextRunAt.ShouldBe(Start.AddHours(1));
        _logs.Messages(LogEventIds.Infrastructure + 112)[^1].ShouldBe("Job schedules synced: 2 added, 0 updated, 0 removed");

        _clock.Advance(TimeSpan.FromMinutes(10));
        await Scheduler(first).SyncAsync(Token); // same cron: state kept
        (await ScheduleAsync("test.sched")).NextRunAt.ShouldBe(Start.AddHours(1));
        _logs.Messages(LogEventIds.Infrastructure + 112)[^1].ShouldBe("Job schedules synced: 0 added, 0 updated, 0 removed");

        OrvanoApiFactory changed = Host(services => services.AddJobSchedule("test.sched", "30 * * * *", new ProbeJob(ProbeMode.Ok)));
        await Scheduler(changed).SyncAsync(Token);
        Schedule moved = await ScheduleAsync("test.sched");
        moved.Cron.ShouldBe("30 * * * *");
        moved.NextRunAt.ShouldBe(Start.AddMinutes(30));
        _logs.Messages(LogEventIds.Infrastructure + 112)[^1].ShouldBe("Job schedules synced: 0 added, 1 updated, 0 removed");

        OrvanoApiFactory removed = Host();
        await Scheduler(removed).SyncAsync(Token);
        (await ScheduleNamesAsync()).ShouldBe(["jobs.cleanup"]);
        _logs.Messages(LogEventIds.Infrastructure + 112)[^1].ShouldBe("Job schedules synced: 0 added, 0 updated, 1 removed");
    }

    [Fact]
    [Trait("Spec", "0013:AC-24"), Trait("Spec", "0013:SCH-5")]
    public async Task A_disabled_schedule_never_fires_and_keeps_its_state()
    {
        OrvanoApiFactory host = Host(services => services.AddJobSchedule("test.hourly", "0 * * * *", new ProbeJob(ProbeMode.Ok)));
        await Scheduler(host).SyncAsync(Token);
        await _database.ExecuteAsync("UPDATE jobs.schedule SET enabled = false WHERE name = 'test.hourly'", Token);

        _clock.Advance(TimeSpan.FromHours(5));
        await JobsDriver.RunDueAsync(host.Services, Token);

        (await _rows.CountAsync("kind = 'test.probe'", Token)).ShouldBe(0);
        Schedule schedule = await ScheduleAsync("test.hourly");
        schedule.NextRunAt.ShouldBe(Start.AddHours(1));
        schedule.Enabled.ShouldBeFalse(); // the sync in RunDueAsync kept it
    }

    [Fact]
    [Trait("Spec", "0013:AC-25"), Trait("Spec", "0013:SCH-7"), Trait("Spec", "0013:JOB-3")]
    public async Task Cleanup_deletes_only_old_finished_jobs_in_chunks_of_5000_and_frees_their_dedupe_keys()
    {
        OrvanoApiFactory host = Host();
        (await JobsDriver.RunDueAsync(host.Services, Token)).ShouldBeEmpty(); // syncs: next_run_at 03:17 the next day
        DateTimeOffset now = Start.AddHours(15).AddMinutes(17); // 03:17 UTC the next day, the cleanup's first fire
        await Finished("succeeded", now.AddDays(-8), 12_000);
        await Finished("succeeded", now.AddDays(-6), 10);
        await Finished("dead", now.AddDays(-31), 3);
        await Finished("dead", now.AddDays(-29), 2);
        await _database.ExecuteAsync(
            "UPDATE jobs.job SET dedupe_key = 'reuse:1' WHERE id = (SELECT id FROM jobs.job WHERE finished_at = $1 LIMIT 1)", Token, now.AddDays(-8));
        await _database.ExecuteAsync(
            """
            CREATE TABLE public.test_deletes (n int);
            CREATE FUNCTION jobs.test_count_delete() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN INSERT INTO public.test_deletes VALUES (1); RETURN NULL; END $$;
            CREATE TRIGGER test_count_delete AFTER DELETE ON jobs.job FOR EACH STATEMENT EXECUTE FUNCTION jobs.test_count_delete();
            """,
            Token);

        _clock.Advance(now - Start);
        IReadOnlyList<JobRun> runs = await JobsDriver.RunDueAsync(host.Services, Token);

        JobRun cleanup = runs.ShouldHaveSingleItem();
        cleanup.Kind.ShouldBe("jobs.cleanup");
        cleanup.Outcome.ShouldBe("succeeded");
        (await _rows.CountAsync("status = 'succeeded' AND kind = 'test.probe'", Token)).ShouldBe(10);
        (await _rows.CountAsync("status = 'dead'", Token)).ShouldBe(2);
        await using (NpgsqlCommand statements = _database.DataSource.CreateCommand("SELECT count(*) FROM public.test_deletes"))
        {
            // 12,000 succeeded in 5000, 5000, 2000 (three statements), and 3 dead in one.
            ((long)(await statements.ExecuteScalarAsync(Token))!).ShouldBe(4);
        }

        _logs.Messages(LogEventIds.Infrastructure + 116).ShouldHaveSingleItem().ShouldBe("Job cleanup deleted 12000 succeeded and 3 dead jobs");
        (await host.Services.GetRequiredService<IJobQueue>().EnqueueAsync(new ProbeJob(ProbeMode.Ok), new EnqueueOptions { DedupeKey = "reuse:1" }, Token))
            .ShouldNotBeNull();
    }

    private Task Finished(string status, DateTimeOffset finishedAt, int count) =>
        _database.ExecuteAsync(
            """
            INSERT INTO jobs.job (id, kind, payload, status, attempt, max_attempts, run_at, created_at, finished_at)
            SELECT gen_random_uuid(), 'test.probe', '{}', $1, 1, 10, $2, $2, $2 FROM generate_series(1, $3)
            """,
            Token,
            status,
            finishedAt,
            count);

    private static JobScheduler Scheduler(OrvanoApiFactory host) => host.Services.GetRequiredService<JobScheduler>();

    private sealed record Schedule(string Cron, bool Enabled, DateTimeOffset NextRunAt, DateTimeOffset? LastFiredAt);

    private async Task<Schedule> ScheduleAsync(string name)
    {
        await using NpgsqlCommand command = _database.DataSource.CreateCommand(
            "SELECT cron, enabled, next_run_at, last_fired_at FROM jobs.schedule WHERE name = $1");
        command.Parameters.AddWithValue(name);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Token);
        (await reader.ReadAsync(Token)).ShouldBeTrue($"no schedule {name}");
        return new Schedule(
            reader.GetString(0),
            reader.GetBoolean(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    private async Task<List<string>> ScheduleNamesAsync()
    {
        await using NpgsqlCommand command = _database.DataSource.CreateCommand("SELECT name FROM jobs.schedule ORDER BY name");
        var names = new List<string>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private OrvanoApiFactory Host(Action<IServiceCollection>? configure = null)
    {
        OrvanoApiFactory host = JobHosts.Create(_settings, _probes, _clock, roles: "api", services =>
        {
            services.AddSingleton<ILoggerProvider>(_logs);
            configure?.Invoke(services);
        });
        _hosts.Add(host);
        _ = host.Services;
        return host;
    }
}
