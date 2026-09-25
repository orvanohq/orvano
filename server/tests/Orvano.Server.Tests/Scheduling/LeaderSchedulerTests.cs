using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Modules;
using Orvano.Core.Scheduling;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Scheduling;

// Spec 0002 value sourcing, leader lock: only the worker holding advisory lock (ORVA, 2) runs the
// internal schedules, and another worker takes over when that connection goes away.
public class LeaderSchedulerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> _ticks = new();
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.NewDatabaseAsync();
        await _database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Runs_each_schedule_as_soon_as_it_becomes_leader()
    {
        await using var worker = await StartSchedulerAsync("a", TimeSpan.FromHours(1));

        await Eventually.TrueAsync(() => Task.FromResult(_ticks.Contains("a")), TimeSpan.FromSeconds(10), "the first tick");
    }

    [Fact]
    public async Task Holds_the_ORVA_2_advisory_lock_on_one_connection_while_leading()
    {
        await using var worker = await StartSchedulerAsync("a", TimeSpan.FromHours(1));
        await Eventually.TrueAsync(() => Task.FromResult(_ticks.Contains("a")), TimeSpan.FromSeconds(10), "leadership");

        var holders = await TestDatabase.ScalarAsync<string[]>(_database.Superuser, """
            SELECT array_agg(a.application_name::text) FROM pg_locks l JOIN pg_stat_activity a USING (pid)
            WHERE l.locktype = 'advisory' AND l.granted AND l.classid = 1330796097 AND l.objid = 2
              AND l.database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """);

        Assert.Equal(["scheduler-a"], holders);
    }

    [Fact]
    public async Task Lets_only_one_of_two_workers_run_the_schedules()
    {
        await using var first = await StartSchedulerAsync("a", TimeSpan.FromMilliseconds(100));
        await Eventually.TrueAsync(() => Task.FromResult(_ticks.Contains("a")), TimeSpan.FromSeconds(10), "a to lead");

        await using var second = await StartSchedulerAsync("b", TimeSpan.FromMilliseconds(100));
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("b", _ticks);
    }

    // The standby retries every 10 seconds (Timings.LeaderRetry), so this test takes about that long.
    [Fact]
    public async Task Hands_leadership_to_the_other_worker_when_the_leader_stops()
    {
        var first = await StartSchedulerAsync("a", TimeSpan.FromMilliseconds(100));
        await Eventually.TrueAsync(() => Task.FromResult(_ticks.Contains("a")), TimeSpan.FromSeconds(10), "a to lead");
        await using var second = await StartSchedulerAsync("b", TimeSpan.FromMilliseconds(100));
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await first.StopAsync();

        await Eventually.TrueAsync(() => Task.FromResult(_ticks.Contains("b")), TimeSpan.FromSeconds(25), "b to take over");
    }

    [Fact]
    public async Task Stops_leading_and_takes_the_lock_back_after_its_connection_is_killed()
    {
        await using var worker = await StartSchedulerAsync("a", TimeSpan.FromHours(1));
        await Eventually.TrueAsync(() => Task.FromResult(_ticks.Count == 1), TimeSpan.FromSeconds(10), "leadership");

        await TestDatabase.ExecuteAsync(_database.Superuser,
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = 'scheduler-a' AND datname = current_database()");

        // Leading again runs the hourly schedule again right away.
        await Eventually.TrueAsync(() => Task.FromResult(_ticks.Count == 2), TimeSpan.FromSeconds(30), "leadership to come back");
    }

    private async Task<RunningService> StartSchedulerAsync(string name, TimeSpan interval)
    {
        var work = new WorkRegistry();
        work.AddInternalSchedule("tick", interval, (_, _) =>
        {
            _ticks.Enqueue(name);
            return Task.CompletedTask;
        });

        var dedicated = _database.Track(OrvanoDb.CreateDedicated(_database.AppUrl, $"scheduler-{name}"));
        var services = new ServiceCollection().BuildServiceProvider();
        var scheduler = new LeaderScheduler(dedicated, work, services, NullLogger<LeaderScheduler>.Instance);
        await scheduler.StartAsync(TestContext.Current.CancellationToken);
        return new RunningService(scheduler);
    }
}
