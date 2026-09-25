using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Notifications;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Jobs;

// Spec 0002, events and background work: the worker claims jobs with SKIP LOCKED, runs the handler,
// and records success, a retry with backoff, or dead after max_attempts (S-3).
public class JobLoopTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Queue = "test";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly WakeSignals _signals = new();
    private readonly WorkRegistry _work = new();
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.NewDatabaseAsync();
        await _database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Runs_a_queued_job_and_marks_it_succeeded()
    {
        var seen = new TaskCompletionSource<ClaimedJob>();
        _work.HandleJob("greet", Queue, (ctx, _) => { seen.TrySetResult(ctx.Job); return Task.CompletedTask; });
        await using var loop = await StartLoopAsync();

        var id = await EnqueueAsync(new NewJob("greet", """{"name":"Ada"}""", Queue, ProjectId: "abc"));

        var job = await seen.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal((id, "greet", Queue, "abc", """{"name": "Ada"}""", 1), (job.Id, job.Kind, job.Queue, job.ProjectId, job.Payload, job.Attempts));
        var row = await WaitForStatusAsync(id, "succeeded");
        Assert.Equal(1, row.Attempts);
        Assert.True(row.Finished);
        Assert.Null(row.LockedBy);
    }

    [Fact]
    public async Task Requeues_a_failed_job_with_a_backoff_and_records_the_error()
    {
        _work.HandleJob("flaky", Queue, (_, _) => throw new InvalidOperationException("boom"));
        await using var loop = await StartLoopAsync();

        var id = await EnqueueAsync(new NewJob("flaky", Queue: Queue, MaxAttempts: 5));

        var row = await Eventually.ReturnsAsync(() => ReadAsync(id), r => r.Attempts == 1 && r.Status == "queued", Wait, "the first retry");
        Assert.Equal("InvalidOperationException: boom", row.LastError);
        Assert.Null(row.LockedBy);
        Assert.False(row.Finished);
        // Attempt 1 backs off between 1 and 2 seconds (half to all of 2^1, with jitter).
        Assert.InRange(row.RunAtFromNow.TotalSeconds, 0, 2);
    }

    [Fact]
    public async Task Marks_a_job_dead_when_its_last_attempt_fails()
    {
        _work.HandleJob("doomed", Queue, (_, _) => throw new InvalidOperationException("boom"));
        await using var loop = await StartLoopAsync();

        var id = await EnqueueAsync(new NewJob("doomed", Queue: Queue, MaxAttempts: 1));

        var row = await WaitForStatusAsync(id, "dead");
        Assert.Equal(1, row.Attempts);
        Assert.True(row.Finished);
        Assert.Equal("InvalidOperationException: boom", row.LastError);
    }

    [Fact]
    public async Task Fails_a_job_whose_kind_has_no_handler()
    {
        _work.HandleJob("known", Queue, (_, _) => Task.CompletedTask);
        await using var loop = await StartLoopAsync();

        var id = await EnqueueAsync(new NewJob("unknown", Queue: Queue, MaxAttempts: 1));

        var row = await WaitForStatusAsync(id, "dead");
        Assert.Equal("InvalidOperationException: No handler is registered for job kind 'unknown'.", row.LastError);
    }

    [Fact]
    public async Task Leaves_jobs_in_queues_no_handler_serves()
    {
        _work.HandleJob("mine", Queue, (_, _) => Task.CompletedTask);
        await using var loop = await StartLoopAsync();
        var other = await EnqueueAsync(new NewJob("theirs", Queue: "someone-else"));
        var mine = await EnqueueAsync(new NewJob("mine", Queue: Queue));

        await WaitForStatusAsync(mine, "succeeded");

        var row = await ReadAsync(other);
        Assert.Equal(("queued", 0), (row.Status, row.Attempts));
    }

    [Fact]
    public async Task Waits_for_run_at_before_claiming_a_scheduled_job()
    {
        _work.HandleJob("later", Queue, (_, _) => Task.CompletedTask);
        await using var loop = await StartLoopAsync();

        var id = await EnqueueAsync(new NewJob("later", Queue: Queue, RunAt: DateTimeOffset.UtcNow.AddHours(1)));
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Equal("queued", (await ReadAsync(id)).Status);
    }

    [Fact]
    public async Task Hands_a_running_job_back_on_shutdown_without_counting_the_attempt()
    {
        var started = new TaskCompletionSource();
        _work.HandleJob("slow", Queue, async (_, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); });
        var loop = await StartLoopAsync();
        var id = await EnqueueAsync(new NewJob("slow", Queue: Queue));
        await started.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        await loop.StopAsync();

        var row = await ReadAsync(id);
        Assert.Equal(("queued", 0), (row.Status, row.Attempts));
        Assert.Null(row.LockedBy);
    }

    [Fact]
    public async Task Gives_each_job_to_only_one_of_two_workers()
    {
        var runs = 0;
        _work.HandleJob("once", Queue, async (_, ct) => { Interlocked.Increment(ref runs); await Task.Delay(50, ct); });
        await using var first = await StartLoopAsync();
        await using var second = await StartLoopAsync();

        for (var i = 0; i < 20; i++) await EnqueueAsync(new NewJob("once", Queue: Queue));

        await Eventually.ReturnsAsync(
            () => TestDatabase.ScalarAsync<long>(_database.Superuser, "SELECT count(*) FROM orvano.jobs WHERE status = 'succeeded'"),
            n => n == 20, TimeSpan.FromSeconds(20), "all 20 jobs to succeed");
        Assert.Equal(20, runs);
    }

    [Fact]
    public async Task Enqueues_no_job_when_the_callers_transaction_rolls_back()
    {
        await using var conn = await _database.App.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await JobQueue.EnqueueAsync(tx, new NewJob("never"), TestContext.Current.CancellationToken);

        await tx.RollbackAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await TestDatabase.ScalarAsync<long>(_database.Superuser, "SELECT count(*) FROM orvano.jobs"));
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

    private async Task<RunningService> StartLoopAsync()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var loop = new JobLoop(_database.App, _work, _signals, services, NullLogger<JobLoop>.Instance);
        await loop.StartAsync(TestContext.Current.CancellationToken);
        return new RunningService(loop);
    }

    private Task<JobRow> WaitForStatusAsync(long id, string status) =>
        Eventually.ReturnsAsync(() => ReadAsync(id), r => r.Status == status, Wait, $"job {id} to be {status}");

    private async Task<JobRow> ReadAsync(long id)
    {
        await using var cmd = _database.Superuser.CreateCommand("""
            SELECT status, attempts, locked_by, last_error, finished_at IS NOT NULL, run_at - now()
            FROM orvano.jobs WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        return new JobRow(
            reader.GetString(0), reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetBoolean(4), reader.GetFieldValue<TimeSpan>(5));
    }

    private sealed record JobRow(string Status, int Attempts, string? LockedBy, string? LastError, bool Finished, TimeSpan RunAtFromNow);
}
