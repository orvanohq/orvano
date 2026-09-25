using Microsoft.Extensions.Logging.Abstractions;
using Orvano.Core.Events;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Notifications;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Events;

// Spec 0002, events and background work: the outbox is written in the caller's transaction and the
// worker's dispatcher turns each event into durable jobs (S-3).
public class EventDispatcherTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.NewDatabaseAsync();
        await _database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Writes_an_event_that_commits_with_the_callers_transaction()
    {
        var id = await WriteEventAsync(new EventDraft("user.created", """{"email":"a@b.c"}""", ProjectId: "abc", Subject: "user/1"));

        var e = await Outbox.ReadAsync(_database.App, id, TestContext.Current.CancellationToken);

        Assert.NotNull(e);
        Assert.Equal("user.created", e.Type);
        Assert.Equal("abc", e.ProjectId);
        Assert.Equal("user/1", e.Subject);
        Assert.Equal("""{"email": "a@b.c"}""", e.Payload);
    }

    [Fact]
    public async Task Writes_no_event_when_the_callers_transaction_rolls_back()
    {
        await using var conn = await _database.App.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var id = await Outbox.WriteAsync(tx, new EventDraft("user.created", "{}"), TestContext.Current.CancellationToken);

        await tx.RollbackAsync(TestContext.Current.CancellationToken);

        Assert.Null(await Outbox.ReadAsync(_database.App, id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Returns_null_for_an_event_that_does_not_exist()
    {
        Assert.Null(await Outbox.ReadAsync(_database.App, 424242, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Enqueues_the_jobs_a_consumer_asks_for_and_marks_the_event_dispatched()
    {
        var work = new WorkRegistry();
        work.OnEvent("user.created", e => [new NewJob("welcome.send", $$"""{"event":{{e.Id}}}""", Queue: "email", ProjectId: e.ProjectId)]);
        await using var dispatcher = await StartDispatcherAsync(work);

        var id = await WriteEventAsync(new EventDraft("user.created", "{}", ProjectId: "abc"));
        await WaitUntilDispatchedAsync(id);

        await using var cmd = _database.Superuser.CreateCommand("SELECT kind, queue, project_id, payload::text, status FROM orvano.jobs");
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("welcome.send", "email", "abc", $$"""{"event": {{id}}}""", "queued"),
            (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Enqueues_one_job_per_consumer_when_several_listen_to_the_same_type()
    {
        var work = new WorkRegistry();
        work.OnEvent("user.created", _ => [new NewJob("a")]);
        work.OnEvent("user.created", _ => [new NewJob("b"), new NewJob("c")]);
        await using var dispatcher = await StartDispatcherAsync(work);

        var id = await WriteEventAsync(new EventDraft("user.created", "{}"));
        await WaitUntilDispatchedAsync(id);

        var kinds = await TestDatabase.ScalarAsync<string[]>(_database.Superuser, "SELECT array_agg(kind ORDER BY kind) FROM orvano.jobs");
        Assert.Equal(["a", "b", "c"], kinds);
    }

    [Fact]
    public async Task Marks_an_event_nobody_consumes_as_dispatched_without_any_job()
    {
        await using var dispatcher = await StartDispatcherAsync(new WorkRegistry());

        var id = await WriteEventAsync(new EventDraft("nobody.cares", "{}"));
        await WaitUntilDispatchedAsync(id);

        Assert.Equal(0, await TestDatabase.ScalarAsync<long>(_database.Superuser, "SELECT count(*) FROM orvano.jobs"));
    }

    // With no NOTIFY at all the dispatcher must still find the event through its 2 second poll.
    [Fact]
    public async Task Finds_an_event_through_the_poll_fallback_when_nothing_wakes_it()
    {
        await using var dispatcher = await StartDispatcherAsync(new WorkRegistry(), wake: false);
        await Task.Delay(300, TestContext.Current.CancellationToken); // let the first pass find nothing

        var id = await TestDatabase.ScalarAsync<long>(_database.App,
            "INSERT INTO orvano.events (type, payload) VALUES ('raw.insert', '{}') RETURNING id");

        await WaitUntilDispatchedAsync(id);
    }

    [Fact]
    public async Task Dispatches_a_backlog_larger_than_one_batch()
    {
        await TestDatabase.ExecuteAsync(_database.App,
            "INSERT INTO orvano.events (type, payload) SELECT 'bulk', '{}' FROM generate_series(1, 250)");

        await using var dispatcher = await StartDispatcherAsync(new WorkRegistry());

        await Eventually.ReturnsAsync(
            () => TestDatabase.ScalarAsync<long>(_database.Superuser, "SELECT count(*) FROM orvano.events WHERE dispatched_at IS NULL"),
            left => left == 0, Wait, "the backlog to drain");
    }

    private async Task<long> WriteEventAsync(EventDraft draft)
    {
        await using var conn = await _database.App.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var id = await Outbox.WriteAsync(tx, draft, TestContext.Current.CancellationToken);
        await tx.CommitAsync(TestContext.Current.CancellationToken);
        _signals.Events.Set();
        return id;
    }

    private Task WaitUntilDispatchedAsync(long id) => Eventually.TrueAsync(
        () => TestDatabase.ScalarAsync<bool>(_database.Superuser, "SELECT dispatched_at IS NOT NULL FROM orvano.events WHERE id = @id", ("id", id)),
        Wait, $"event {id} to be dispatched");

    private readonly WakeSignals _signals = new();

    private async Task<RunningService> StartDispatcherAsync(WorkRegistry work, bool wake = true)
    {
        var dispatcher = new EventDispatcher(_database.App, work, wake ? _signals : new WakeSignals(), NullLogger<EventDispatcher>.Instance);
        await dispatcher.StartAsync(TestContext.Current.CancellationToken);
        return new RunningService(dispatcher);
    }
}
