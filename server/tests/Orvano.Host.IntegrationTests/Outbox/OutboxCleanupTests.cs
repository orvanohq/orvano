using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Host.IntegrationTests.Signals;
using Orvano.Infrastructure.Outbox;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Outbox;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Outbox;

// Spec 0013 build plan step 29: the daily outbox.cleanup schedule deletes dispatched events that occurred more than 7
// days ago, 5000 per statement through the primary key, and never an undispatched one. One api host on a ManualClock;
// JobsDriver syncs and fires the schedule.
public sealed class OutboxCleanupTests(PostgresFixture postgres) : IAsyncLifetime
{
    // ManualClock starts at 2026-10-19 12:00 UTC; the first fire after it is the next day's 03:37.
    private static readonly DateTimeOffset FireTime = new(2026, 10, 20, 3, 37, 0, TimeSpan.Zero);

    private readonly ManualClock _clock = new();
    private readonly RecordingLogs _logs = new();
    private Dictionary<string, string> _settings = null!;
    private OrvanoApiFactory _host = null!;
    private TestDatabase _database = null!;
    private OutboxRows _rows = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "outbox_cleanup", Token);
        _database = TestDatabase.For(postgres, _settings);
        _rows = _database.OutboxRows();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:AC-39"), Trait("Spec", "0013:OCL-1"), Trait("Spec", "0013:OCL-2"), Trait("Spec", "0013:OTL-2")]
    public async Task Cleanup_deletes_only_dispatched_events_older_than_7_days_in_chunks_of_5000_through_the_primary_key()
    {
        _host = Host();
        await SampleOutboxDbContext.CreateTablesAsync(_host.Services, Token);
        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBeEmpty(); // the sync sets the next fire
        _clock.Advance(FireTime - _clock.GetUtcNow() + TimeSpan.FromSeconds(30));
        DateTimeOffset now = _clock.GetUtcNow();
        await InsertDispatchedAsync(12_000, now.AddDays(-8));
        await InsertDispatchedAsync(10, now.AddDays(-6));
        Guid waiting = await _rows.InsertAsync(SampleNoteAdded.Type, "{}", now.AddDays(-30), Token);
        await _database.ExecuteAsync("ANALYZE sample.outbox", Token);
        using var deletes = new OutboxDeletes(_settings["Database:Name"]);

        // A transaction holds the waiting row, as an unfinished dispatch would, so the pass before the cleanup skips it.
        IReadOnlyList<JobRun> runs;
        await using (NpgsqlConnection holder = await _database.DataSource.OpenConnectionAsync(Token))
        await using (NpgsqlTransaction transaction = await holder.BeginTransactionAsync(Token))
        {
            await using (var hold = new NpgsqlCommand("SELECT 1 FROM sample.outbox WHERE id = $1 FOR UPDATE", holder, transaction))
            {
                hold.Parameters.AddWithValue(waiting);
                await hold.ExecuteNonQueryAsync(Token);
            }

            runs = await JobsDriver.RunDueAsync(_host.Services, Token);
            await transaction.RollbackAsync(Token);
        }

        runs.Select(run => (run.Kind, run.Outcome)).ShouldBe([("jobs.cleanup", "succeeded"), ("outbox.cleanup", "succeeded")], ignoreOrder: true);
        (await _rows.CountAsync("true", Token)).ShouldBe(11);
        (await _rows.CountAsync("occurred_at = $1", Token, now.AddDays(-6))).ShouldBe(10);
        (await _rows.CountAsync("id = $1 AND dispatched_at IS NULL", Token, waiting)).ShouldBe(1); // never an undispatched row
        deletes.Count.ShouldBe(3); // 5000, 5000, then 2000 ends it
        _logs.Messages(LogEventIds.Infrastructure + 205).ShouldBe(["Outbox cleanup deleted 12000 dispatched events from the sample outbox"]);

        await using NpgsqlCommand explain = _database.DataSource.CreateCommand(
            "EXPLAIN SELECT id FROM sample.outbox WHERE id < $1 AND dispatched_at IS NOT NULL ORDER BY id LIMIT 5000");
        explain.Parameters.AddWithValue(OutboxIds.Floor(now - OutboxCleanupHandler.Retention));
        var plan = new List<string>();
        await using (NpgsqlDataReader reader = await explain.ExecuteReaderAsync(Token))
        {
            while (await reader.ReadAsync(Token))
            {
                plan.Add(reader.GetString(0));
            }
        }

        string.Join('\n', plan).ShouldContain("pk_outbox");
    }

    [Fact]
    [Trait("Spec", "0013:AC-39"), Trait("Spec", "0013:OCL-1")]
    public async Task Cleanup_cleans_the_other_outboxes_when_one_fails_and_then_fails_the_attempt_so_it_retries()
    {
        _host = Host(services => services.AddOutbox("aside", _ => { })); // registered, but no table
        await SampleOutboxDbContext.CreateTablesAsync(_host.Services, Token);
        (await JobsDriver.RunDueAsync(_host.Services, allowDispatchFailures: true, Token)).ShouldBeEmpty(); // aside's dispatch fails too
        _clock.Advance(FireTime - _clock.GetUtcNow() + TimeSpan.FromSeconds(30));
        await InsertDispatchedAsync(3, _clock.GetUtcNow().AddDays(-8));

        IReadOnlyList<JobRun> runs = await JobsDriver.RunDueAsync(_host.Services, allowDispatchFailures: true, Token);

        runs.ShouldContain(run => run.Kind == "outbox.cleanup" && run.Outcome == "failed");
        (await _rows.CountAsync("true", Token)).ShouldBe(0);
        _logs.Messages(LogEventIds.Infrastructure + 205).ShouldBe(["Outbox cleanup deleted 3 dispatched events from the sample outbox"]);
    }

    private OrvanoApiFactory Host(Action<IServiceCollection>? configure = null) =>
        OutboxHosts.Create(_settings, new OutboxProbes(), _clock, roles: "api", services =>
        {
            services.AddSingleton<ILoggerProvider>(_logs);
            configure?.Invoke(services);
        });

    private async Task InsertDispatchedAsync(int count, DateTimeOffset occurredAt)
    {
        Guid[] ids = [.. Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7(occurredAt))];
        await _database.ExecuteAsync(
            "INSERT INTO sample.outbox (id, type, payload, occurred_at, dispatched_at) SELECT id, 'sample.note.added.v1', '{}', $2, $2 FROM unnest($1::uuid[]) AS id",
            Token,
            ids,
            occurredAt);
    }

    /// <summary>Counts the cleanup's DELETE statements on one test database, from Npgsql's command activities.</summary>
    private sealed class OutboxDeletes : IDisposable
    {
        private readonly ActivityListener _listener;
        private int _count;

        public OutboxDeletes(string database)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Npgsql",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    string[] values = [.. activity.TagObjects.Select(tag => tag.Value as string ?? string.Empty)];
                    if (values.Contains(database, StringComparer.Ordinal)
                        && values.Any(value => value.Contains("DELETE FROM \"sample\".outbox", StringComparison.Ordinal)))
                    {
                        Interlocked.Increment(ref _count);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public int Count => Volatile.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
