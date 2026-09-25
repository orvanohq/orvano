using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Modules;
using Orvano.Core.Scheduling;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Scheduling;

// Spec 0002 S-3: the leader's internal schedules, the job lease reaper and event pruning.
public class CoreWorkTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.NewDatabaseAsync();
        await _database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Requeues_a_running_job_whose_lease_expired()
    {
        var id = await InsertRunningJobAsync(attempts: 1, maxAttempts: 3, leaseEnds: "now() - interval '1 minute'");

        await RunScheduleAsync("jobs.reap");

        var (status, lastError, lockedBy) = await ReadJobAsync(id);
        Assert.Equal(("queued", "lease expired", null), (status, lastError, lockedBy));
    }

    [Fact]
    public async Task Marks_an_expired_job_dead_when_it_has_no_attempts_left()
    {
        var id = await InsertRunningJobAsync(attempts: 3, maxAttempts: 3, leaseEnds: "now() - interval '1 minute'");

        await RunScheduleAsync("jobs.reap");

        Assert.Equal("dead", (await ReadJobAsync(id)).Status);
        Assert.True(await TestDatabase.ScalarAsync<bool>(_database.Superuser,
            "SELECT finished_at IS NOT NULL FROM orvano.jobs WHERE id = @id", ("id", id)));
    }

    [Fact]
    public async Task Leaves_a_running_job_with_a_live_lease_alone()
    {
        var id = await InsertRunningJobAsync(attempts: 1, maxAttempts: 3, leaseEnds: "now() + interval '1 minute'");

        await RunScheduleAsync("jobs.reap");

        Assert.Equal(("running", null, "worker-a"), await ReadJobAsync(id));
    }

    [Fact]
    public async Task Deletes_dispatched_events_older_than_the_retention_period()
    {
        await InsertEventAsync("old", dispatchedAgo: "2 days");

        await RunScheduleAsync("events.prune", retentionDays: 1);

        Assert.Empty(await EventTypesAsync());
    }

    [Fact]
    public async Task Keeps_dispatched_events_inside_the_retention_period()
    {
        await InsertEventAsync("recent", dispatchedAgo: "2 hours");

        await RunScheduleAsync("events.prune", retentionDays: 1);

        Assert.Equal(["recent"], await EventTypesAsync());
    }

    [Fact]
    public async Task Never_prunes_an_event_that_was_not_dispatched_however_old()
    {
        await TestDatabase.ExecuteAsync(_database.Superuser,
            "INSERT INTO orvano.events (type, payload, created_at) VALUES ('stuck', '{}', now() - interval '30 days')");

        await RunScheduleAsync("events.prune", retentionDays: 1);

        Assert.Equal(["stuck"], await EventTypesAsync());
    }

    [Fact]
    public async Task Uses_the_configured_retention_rather_than_a_fixed_one()
    {
        await InsertEventAsync("five-days", dispatchedAgo: "5 days");

        await RunScheduleAsync("events.prune", retentionDays: 7);

        Assert.Equal(["five-days"], await EventTypesAsync());
    }

    private async Task RunScheduleAsync(string name, int retentionDays = 7)
    {
        var work = new WorkRegistry();
        CoreWork.Register(work, retentionDays);
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddKeyedSingleton<NpgsqlDataSource>(OrvanoDb.App, (_, _) => NpgsqlDataSource.Create(_database.AppUrl))
            .BuildServiceProvider();

        await work.Schedules.Single(s => s.Name == name).Task(services, TestContext.Current.CancellationToken);
    }

    private Task<long> InsertRunningJobAsync(int attempts, int maxAttempts, string leaseEnds) =>
        TestDatabase.ScalarAsync<long>(_database.App, $$"""
            INSERT INTO orvano.jobs (queue, kind, payload, status, attempts, max_attempts, lease_until, locked_by)
            VALUES ('q', 'k', '{}', 'running', @attempts, @max, {{leaseEnds}}, 'worker-a')
            RETURNING id
            """, ("attempts", attempts), ("max", maxAttempts));

    private Task InsertEventAsync(string type, string dispatchedAgo) =>
        TestDatabase.ExecuteAsync(_database.App,
            "INSERT INTO orvano.events (type, payload, created_at, dispatched_at) VALUES (@t, '{}', now() - @ago::interval, now() - @ago::interval)",
            ("t", type), ("ago", dispatchedAgo));

    private async Task<string[]> EventTypesAsync() =>
        await TestDatabase.ScalarAsync<string[]?>(_database.Superuser, "SELECT array_agg(type ORDER BY type) FROM orvano.events") ?? [];

    private async Task<(string Status, string? LastError, string? LockedBy)> ReadJobAsync(long id)
    {
        await using var cmd = _database.Superuser.CreateCommand("SELECT status, last_error, locked_by FROM orvano.jobs WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }
}
