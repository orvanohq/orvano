using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Notifications;
using Orvano.Core.Scheduling;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Events;

// Spec 0002, poison events: the events.redispatch job reruns one consumer on a copy of one event, goes dead at
// once when the consumer is gone, and heals on its own once the consumer is fixed (S-8).
public class EventRedispatchTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly JobHandler NoOp = (_, _) => Task.CompletedTask;
    // The payload is in jsonb's own text form (keys by length, then bytes), as every event read from orvano.events is.
    private static readonly OutboxEvent Sample = new(42, "abc", "user.created", "user/1", """{"tags": [1, {"x": null}], "email": "a@b.c"}""", new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));

    private readonly WakeSignals _signals = new();
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.NewDatabaseAsync();
        await _database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public void Builds_the_specced_job_for_a_failed_consumer()
    {
        var job = EventRedispatch.For(Sample, "webhooks.deliver");

        Assert.Equal(("events.redispatch", JobQueues.Internal, "abc", 25), (job.Kind, job.Queue, job.ProjectId, job.MaxAttempts));
        var root = JsonDocument.Parse(job.PayloadJson).RootElement;
        Assert.Equal(["consumer", "event"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("webhooks.deliver", root.GetProperty("consumer").GetString());
        var e = root.GetProperty("event");
        Assert.Equal(["id", "projectId", "type", "subject", "payload", "createdAt"], e.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Object, e.GetProperty("payload").ValueKind);
    }

    [Fact]
    public void Reads_back_the_same_event_it_copied()
    {
        var payload = EventRedispatch.Read(EventRedispatch.For(Sample, "webhooks.deliver").PayloadJson);

        Assert.Equal("webhooks.deliver", payload.Consumer);
        Assert.Equal(Sample, payload.Event.ToOutboxEvent());
    }

    [Fact]
    public void Copies_a_platform_event_with_no_project_or_subject()
    {
        var platform = Sample with { ProjectId = null, Subject = null };

        var job = EventRedispatch.For(platform, "webhooks.deliver");

        Assert.Null(job.ProjectId);
        Assert.Equal(platform, EventRedispatch.Read(job.PayloadJson).Event.ToOutboxEvent());
    }

    // jsonb nests far deeper than System.Text.Json's default limit of 64. Building the job must never throw,
    // because the dispatcher builds it outside any consumer's try.
    [Fact]
    public void Copies_an_event_payload_nested_deeper_than_the_default_json_limit()
    {
        var deep = new string('[', 500) + new string(']', 500);

        var job = EventRedispatch.For(Sample with { Payload = deep }, "webhooks.deliver");

        Assert.Equal(deep, EventRedispatch.Read(job.PayloadJson).Event.Payload.GetRawText());
    }

    [Fact]
    public async Task Marks_the_job_dead_after_one_attempt_when_the_consumer_is_no_longer_registered()
    {
        var work = Registry();
        await using var loop = await StartLoopAsync(work);

        var id = await EnqueueAsync(EventRedispatch.For(Sample, "webhooks.gone"));

        var row = await WaitForStatusAsync(id, "dead");
        Assert.Equal(1, row.Attempts);
        Assert.Equal("PermanentJobFailureException: Consumer 'webhooks.gone' for event type 'user.created' is no longer registered", row.LastError);
    }

    [Fact]
    public async Task Enqueues_the_consumers_jobs_when_it_now_succeeds()
    {
        var work = Registry();
        OutboxEvent? seen = null;
        work.OnEvent("user.created", "webhooks.deliver", e => { seen = e; return [new NewJob("deliver", $$"""{"event":{{e.Id}}}""", "hooks", e.ProjectId)]; });
        await using var loop = await StartLoopAsync(work);

        var id = await EnqueueAsync(EventRedispatch.For(Sample, "webhooks.deliver"));

        await WaitForStatusAsync(id, "succeeded");
        Assert.Equal(Sample, seen);
        Assert.Equal(("hooks", "abc", """{"event": 42}"""), await ReadJobAsync("deliver"));
    }

    [Fact]
    public async Task Retries_with_backoff_when_the_consumer_still_throws()
    {
        var work = Registry();
        work.OnEvent("user.created", "webhooks.deliver", _ => throw new InvalidOperationException("still broken"));
        await using var loop = await StartLoopAsync(work);

        var id = await EnqueueAsync(EventRedispatch.For(Sample, "webhooks.deliver"));

        var row = await Eventually.ReturnsAsync(() => ReadAsync(id), r => r is { Attempts: 1, Status: "queued" }, Wait, "the first retry");
        Assert.Equal("InvalidOperationException: still broken", row.LastError);
    }

    [Fact]
    public async Task Retries_when_the_consumer_returns_an_invalid_job()
    {
        var work = Registry();
        work.OnEvent("user.created", "webhooks.deliver", _ => [new NewJob("deliver", Queue: "hooks"), new NewJob("nobody.handles")]);
        await using var loop = await StartLoopAsync(work);

        var id = await EnqueueAsync(EventRedispatch.For(Sample, "webhooks.deliver"));

        var row = await Eventually.ReturnsAsync(() => ReadAsync(id), r => r is { Attempts: 1, Status: "queued" }, Wait, "the first retry");
        Assert.Equal("InvalidOperationException: Job kind 'nobody.handles' has no registered handler.", row.LastError);
        Assert.Equal(1, await TestDatabase.ScalarAsync<long>(_database.Superuser, "SELECT count(*) FROM orvano.jobs"));
    }

    // The whole path: the dispatcher hands a failing consumer to a redispatch job, the consumer is fixed (a new
    // deploy), and the next attempt enqueues its jobs with nobody touching the database.
    [Fact]
    public async Task Heals_on_its_own_once_the_failing_consumer_is_fixed()
    {
        var broken = true;
        var work = Registry();
        work.OnEvent("user.created", "webhooks.deliver", e => broken
            ? throw new InvalidOperationException("bug")
            : [new NewJob("deliver", Queue: "hooks", ProjectId: e.ProjectId)]);
        await using (var dispatcher = await StartDispatcherAsync(work))
        {
            await WriteEventAsync(new EventDraft("user.created", "{}", ProjectId: "abc"));
            await Eventually.ReturnsAsync(
                () => TestDatabase.ScalarAsync<long>(_database.Superuser, "SELECT count(*) FROM orvano.jobs WHERE kind = 'events.redispatch'"),
                n => n == 1, Wait, "the redispatch job");
        }

        broken = false;
        await using var loop = await StartLoopAsync(work);
        _signals.Jobs.Set();

        await Eventually.ReturnsAsync(
            () => TestDatabase.ScalarAsync<string>(_database.Superuser, "SELECT status FROM orvano.jobs WHERE kind = 'events.redispatch'"),
            s => s == "succeeded", Wait, "the redispatch job to succeed");
        Assert.Equal(("hooks", "abc", "{}"), await ReadJobAsync("deliver"));
    }

    private static WorkRegistry Registry()
    {
        var work = new WorkRegistry();
        CoreWork.Register(work, eventRetentionDays: 7);
        work.HandleJob("deliver", "hooks", NoOp);
        return work;
    }

    private async Task<RunningService> StartLoopAsync(WorkRegistry work)
    {
        var services = new ServiceCollection()
            .AddSingleton(work)
            .AddKeyedSingleton(OrvanoDb.App, _database.App)
            .BuildServiceProvider();
        // Only the internal queue, so the loop runs the redispatch job and leaves the jobs it enqueues alone.
        var loop = new JobLoop(_database.App, new InternalOnly(work).Registry, _signals, services, NullLogger<JobLoop>.Instance);
        await loop.StartAsync(TestContext.Current.CancellationToken);
        return new RunningService(loop);
    }

    private async Task<RunningService> StartDispatcherAsync(WorkRegistry work)
    {
        var dispatcher = new EventDispatcher(_database.App, work, _signals, NullLogger<EventDispatcher>.Instance);
        await dispatcher.StartAsync(TestContext.Current.CancellationToken);
        return new RunningService(dispatcher);
    }

    private async Task WriteEventAsync(EventDraft draft)
    {
        await using var conn = await _database.App.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await Outbox.WriteAsync(tx, draft, TestContext.Current.CancellationToken);
        await tx.CommitAsync(TestContext.Current.CancellationToken);
        _signals.Events.Set();
    }

    private async Task<long> EnqueueAsync(NewJob job)
    {
        await using var conn = await _database.App.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var id = await JobQueue.EnqueueAsync(tx, job, TestContext.Current.CancellationToken);
        await tx.CommitAsync(TestContext.Current.CancellationToken);
        _signals.Jobs.Set();
        return id;
    }

    private async Task<(string Queue, string? ProjectId, string Payload)> ReadJobAsync(string kind)
    {
        await using var cmd = _database.Superuser.CreateCommand("SELECT queue, project_id, payload::text FROM orvano.jobs WHERE kind = @kind");
        cmd.Parameters.AddWithValue("kind", kind);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), $"no '{kind}' job");
        var row = (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken), $"more than one '{kind}' job");
        return row;
    }

    private Task<JobRow> WaitForStatusAsync(long id, string status) =>
        Eventually.ReturnsAsync(() => ReadAsync(id), r => r.Status == status, Wait, $"job {id} to be {status}");

    private async Task<JobRow> ReadAsync(long id)
    {
        await using var cmd = _database.Superuser.CreateCommand("SELECT status, attempts, last_error FROM orvano.jobs WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        return new JobRow(reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private sealed record JobRow(string Status, int Attempts, string? LastError);

    /// <summary>A registry the job loop reads that serves only the internal queue, sharing the real one's handler.</summary>
    private sealed class InternalOnly
    {
        public InternalOnly(WorkRegistry work)
        {
            Registry.HandleJob(EventRedispatch.Kind, JobQueues.Internal, work.HandlerFor(EventRedispatch.Kind)!);
        }

        public WorkRegistry Registry { get; } = new();
    }
}
