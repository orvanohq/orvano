using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orvano.Core.Events;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Notifications;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Events;

// Spec 0002, events and background work: the outbox is written in the caller's transaction and the
// worker's dispatcher turns each event into durable jobs (S-3). Poison events: one consumer failing on one
// event is contained to that consumer on that event and handed to an events.redispatch job (S-8).
public class EventDispatcherTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Secret = "payload-secret-7f3a";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly JobHandler NoOp = (_, _) => Task.CompletedTask;

    private readonly WakeSignals _signals = new();
    private readonly ListLogger<EventDispatcher> _log = new();
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
        var work = Registry();
        work.OnEvent("user.created", "email.welcome", e => [new NewJob("welcome.send", $$"""{"event":{{e.Id}}}""", Queue: "email", ProjectId: e.ProjectId)]);
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
        var work = Registry();
        work.OnEvent("user.created", "test.first", _ => [new NewJob("a")]);
        work.OnEvent("user.created", "test.second", _ => [new NewJob("b"), new NewJob("c")]);
        await using var dispatcher = await StartDispatcherAsync(work);

        var id = await WriteEventAsync(new EventDraft("user.created", "{}"));
        await WaitUntilDispatchedAsync(id);

        Assert.Equal(["a", "b", "c"], await KindsAsync());
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

    [Fact]
    public async Task Keeps_dispatching_past_an_event_that_makes_a_consumer_throw()
    {
        var work = Registry();
        work.OnEvent("user.created", "email.welcome", e => e.Subject == "poison"
            ? throw new InvalidOperationException("cannot handle this one")
            : [new NewJob("welcome.send", Queue: "email")]);
        await using var dispatcher = await StartDispatcherAsync(work);

        var poison = await WriteEventAsync(new EventDraft("user.created", $$"""{"secret":"{{Secret}}","n":[1,{"x":null}]}""", ProjectId: "abc", Subject: "poison"));
        var normal = await WriteEventAsync(new EventDraft("user.created", "{}", Subject: "fine"));
        await WaitUntilDispatchedAsync(poison);
        await WaitUntilDispatchedAsync(normal);

        Assert.Equal([EventRedispatch.Kind, "welcome.send"], await KindsAsync());
        var redispatch = await ReadRedispatchJobAsync();
        Assert.Equal(("internal", "abc", 25, "queued"), (redispatch.Queue, redispatch.ProjectId, redispatch.MaxAttempts, redispatch.Status));

        var payload = JsonDocument.Parse(redispatch.Payload).RootElement;
        Assert.Equal("email.welcome", payload.GetProperty("consumer").GetString());
        var copy = payload.GetProperty("event");
        Assert.Equal(poison, copy.GetProperty("id").GetInt64());
        Assert.Equal("abc", copy.GetProperty("projectId").GetString());
        Assert.Equal("user.created", copy.GetProperty("type").GetString());
        Assert.Equal("poison", copy.GetProperty("subject").GetString());
        Assert.Equal(JsonValueKind.Object, copy.GetProperty("payload").ValueKind); // embedded as JSON, not a string
        Assert.Equal(Secret, copy.GetProperty("payload").GetProperty("secret").GetString());
        Assert.True(copy.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Hands_only_the_failing_consumer_to_a_redispatch_job()
    {
        var work = Registry();
        work.OnEvent("user.created", "test.healthy", _ => [new NewJob("a")]);
        work.OnEvent("user.created", "test.broken", _ => throw new InvalidOperationException("bug"));
        work.OnEvent("user.created", "test.after", _ => [new NewJob("b")]);
        await using var dispatcher = await StartDispatcherAsync(work);

        var id = await WriteEventAsync(new EventDraft("user.created", "{}"));
        await WaitUntilDispatchedAsync(id);

        Assert.Equal(["a", "b", EventRedispatch.Kind], await KindsAsync());
        Assert.Equal("test.broken", JsonDocument.Parse((await ReadRedispatchJobAsync()).Payload).RootElement.GetProperty("consumer").GetString());
    }

    public static TheoryData<string, NewJob> InvalidJobs => new()
    {
        { "unknown kind", new NewJob("nobody.handles") },
        { "unregistered queue", new NewJob("a", Queue: "nobody-claims") },
        { "payload not JSON", new NewJob("a", "{not json") },
        { "zero attempts", new NewJob("a", MaxAttempts: 0) },
    };

    [Theory]
    [MemberData(nameof(InvalidJobs))]
    public async Task Treats_an_invalid_job_as_a_consumer_failure_and_drops_its_whole_output(string _, NewJob invalid)
    {
        var work = Registry();
        work.OnEvent("user.created", "test.sloppy", e => e.Subject == "bad" ? [new NewJob("b"), invalid] : [new NewJob("c")]);
        await using var dispatcher = await StartDispatcherAsync(work);

        var bad = await WriteEventAsync(new EventDraft("user.created", "{}", Subject: "bad"));
        var behind = await WriteEventAsync(new EventDraft("user.created", "{}", Subject: "good"));
        await WaitUntilDispatchedAsync(bad);
        await WaitUntilDispatchedAsync(behind);

        // "b" was valid, but a failing consumer's output is dropped as a whole.
        Assert.Equal(["c", EventRedispatch.Kind], await KindsAsync());
        Assert.Equal("invalid_job", Assert.Single(FailureLogs()).Values["Reason"]);
    }

    // \u0000 is valid JSON, but jsonb rejects it with 22P05. The savepoint undoes only this consumer's inserts.
    [Fact]
    public async Task Rolls_back_only_the_consumer_whose_jobs_postgres_rejects()
    {
        var work = Registry();
        work.OnEvent("user.created", "test.before", _ => [new NewJob("a")]);
        work.OnEvent("user.created", "test.nul", e => e.Subject == "bad"
            ? [new NewJob("b"), new NewJob("b", $$"""{"s":"\u0000","secret":"{{Secret}}"}""")]
            : [new NewJob("b")]);
        work.OnEvent("user.created", "test.after", _ => [new NewJob("c")]);
        await using var dispatcher = await StartDispatcherAsync(work);

        var bad = await WriteEventAsync(new EventDraft("user.created", "{}", Subject: "bad"));
        var behind = await WriteEventAsync(new EventDraft("user.created", "{}", Subject: "good"));
        await WaitUntilDispatchedAsync(bad);
        await WaitUntilDispatchedAsync(behind);

        Assert.Equal(["a", "a", "b", "c", "c", EventRedispatch.Kind], await KindsAsync());
        var failure = Assert.Single(FailureLogs());
        Assert.Equal("rejected_by_database", failure.Values["Reason"]);
        Assert.Equal("22P05", Assert.IsType<Npgsql.PostgresException>(failure.Exception).SqlState);
        Assert.DoesNotContain(_log.Entries, e => e.FullText.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enqueues_nothing_from_a_lazy_consumer_that_throws_partway_through()
    {
        var work = Registry();
        work.OnEvent("user.created", "test.lazy", Lazy);
        await using var dispatcher = await StartDispatcherAsync(work);

        var id = await WriteEventAsync(new EventDraft("user.created", "{}"));
        await WaitUntilDispatchedAsync(id);

        Assert.Equal([EventRedispatch.Kind], await KindsAsync());
        Assert.Equal("threw", Assert.Single(FailureLogs()).Values["Reason"]);

        static IEnumerable<NewJob> Lazy(OutboxEvent e)
        {
            yield return new NewJob("a");
            throw new InvalidOperationException("second job failed");
        }
    }

    [Fact]
    public async Task Treats_a_consumer_that_returns_null_as_one_that_threw()
    {
        var work = Registry();
        work.OnEvent("user.created", "test.null", _ => null!);
        await using var dispatcher = await StartDispatcherAsync(work);

        var id = await WriteEventAsync(new EventDraft("user.created", "{}"));
        await WaitUntilDispatchedAsync(id);

        Assert.Equal([EventRedispatch.Kind], await KindsAsync());
    }

    [Fact]
    public async Task Logs_a_failure_with_the_event_and_consumer_but_never_the_payload()
    {
        var work = Registry();
        work.OnEvent("user.created", "test.broken", _ => throw new InvalidOperationException("bug"));
        await using var dispatcher = await StartDispatcherAsync(work);

        var id = await WriteEventAsync(new EventDraft("user.created", $$"""{"secret":"{{Secret}}"}"""));
        await WaitUntilDispatchedAsync(id);

        var entry = Assert.Single(FailureLogs());
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal((id, "user.created", "test.broken", "threw"),
            ((long)entry.Values["EventId"]!, (string)entry.Values["EventType"]!, (string)entry.Values["Consumer"]!, (string)entry.Values["Reason"]!));
        Assert.Equal("bug", entry.Exception?.Message);
        Assert.DoesNotContain(_log.Entries, e => e.FullText.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Counts_each_failure_by_event_type_consumer_and_reason()
    {
        var failures = new List<(long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == EventTelemetry.MeterName && instrument.Name == "orvano.events.consumer_failures")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var map = new Dictionary<string, object?>();
            foreach (var tag in tags) map[tag.Key] = tag.Value;
            if (map.GetValueOrDefault("consumer") is "test.counted") lock (failures) failures.Add((value, map));
        });
        listener.Start();

        var work = Registry();
        work.OnEvent("user.created", "test.counted", e => e.Subject == "throw" ? throw new InvalidOperationException() : [new NewJob("nobody.handles")]);
        await using var dispatcher = await StartDispatcherAsync(work);

        await WaitUntilDispatchedAsync(await WriteEventAsync(new EventDraft("user.created", "{}", Subject: "throw")));
        await WaitUntilDispatchedAsync(await WriteEventAsync(new EventDraft("user.created", "{}", Subject: "invalid")));

        lock (failures)
        {
            Assert.Equal([1L, 1L], failures.Select(f => f.Value));
            Assert.Equal(["threw", "invalid_job"], failures.Select(f => f.Tags["reason"]));
            Assert.All(failures, f => Assert.Equal("user.created", f.Tags["event.type"]));
        }
    }

    // A 42 error (here: permission denied) is a broken deployment, not a bad event: the pass stalls loudly,
    // nothing is marked, nothing is redispatched, and it recovers once the deployment is fixed.
    [Fact]
    public async Task Ends_the_pass_without_marking_anything_on_an_error_that_is_not_the_events_fault()
    {
        var work = Registry();
        work.OnEvent("user.created", "test.first", _ => [new NewJob("a")]);
        await TestDatabase.ExecuteAsync(_database.Superuser, "REVOKE INSERT ON orvano.jobs FROM orvano_app");
        await using var dispatcher = await StartDispatcherAsync(work);

        var id = await WriteEventAsync(new EventDraft("user.created", "{}"));
        await Eventually.TrueAsync(() => Task.FromResult(_log.Entries.Any(e => e.Message.StartsWith("Event dispatch pass failed", StringComparison.Ordinal))),
            Wait, "a failed pass to be logged");

        Assert.False(await TestDatabase.ScalarAsync<bool>(_database.Superuser, "SELECT dispatched_at IS NOT NULL FROM orvano.events WHERE id = @id", ("id", id)));
        Assert.Equal(0, await TestDatabase.ScalarAsync<long>(_database.Superuser, "SELECT count(*) FROM orvano.jobs"));
        Assert.Empty(FailureLogs());

        await TestDatabase.ExecuteAsync(_database.Superuser, "GRANT INSERT ON orvano.jobs TO orvano_app");
        _signals.Events.Set();
        await WaitUntilDispatchedAsync(id);
        Assert.Equal(["a"], await KindsAsync());
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]
    [InlineData(6, 30)]
    [InlineData(1000, 30)]
    public void Waits_longer_after_each_failed_pass_up_to_30_seconds(int failures, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), EventDispatcher.RetryDelay(failures));
    }

    private static WorkRegistry Registry()
    {
        var work = new WorkRegistry();
        work.HandleJob("welcome.send", "email", NoOp);
        foreach (var kind in new[] { "a", "b", "c" }) work.HandleJob(kind, JobQueues.Default, NoOp);
        return work;
    }

    private IEnumerable<ListLogger<EventDispatcher>.Entry> FailureLogs() =>
        _log.Entries.Where(e => e.Message.StartsWith("Event consumer ", StringComparison.Ordinal));

    private Task<string[]> KindsAsync() =>
        TestDatabase.ScalarAsync<string[]>(_database.Superuser, "SELECT coalesce(array_agg(kind ORDER BY kind), '{}') FROM orvano.jobs");

    private async Task<RedispatchRow> ReadRedispatchJobAsync()
    {
        await using var cmd = _database.Superuser.CreateCommand(
            "SELECT queue, project_id, max_attempts, status, payload::text FROM orvano.jobs WHERE kind = 'events.redispatch'");
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        var row = new RedispatchRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return row;
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

    private async Task<RunningService> StartDispatcherAsync(WorkRegistry work, bool wake = true)
    {
        var dispatcher = new EventDispatcher(_database.App, work, wake ? _signals : new WakeSignals(), _log);
        await dispatcher.StartAsync(TestContext.Current.CancellationToken);
        return new RunningService(dispatcher);
    }

    private sealed record RedispatchRow(string Queue, string? ProjectId, int MaxAttempts, string Status, string Payload);
}
