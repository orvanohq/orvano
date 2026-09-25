using Microsoft.Extensions.Logging.Abstractions;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Jobs;
using Orvano.Core.Notifications;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Notifications;

// The worker and realtime roles wake on NOTIFY through one dedicated LISTEN connection.
public class PgNotificationListenerTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task Delivers_the_event_id_from_an_outbox_write_after_commit()
    {
        var received = new TaskCompletionSource<string>();
        var connected = new TaskCompletionSource();
        await using var listener = await StartAsync(new() { [Outbox.Channel] = p => received.TrySetResult(p) }, () => connected.TrySetResult());
        await connected.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        await using var conn = await _database.App.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var id = await Outbox.WriteAsync(tx, new EventDraft("user.created", "{}"), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.False(received.Task.IsCompleted); // nothing before commit
        await tx.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(id.ToString(), await received.Task.WaitAsync(Wait, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delivers_the_queue_name_from_an_enqueued_job()
    {
        var received = new TaskCompletionSource<string>();
        var connected = new TaskCompletionSource();
        await using var listener = await StartAsync(new() { [JobQueue.Channel] = p => received.TrySetResult(p) }, () => connected.TrySetResult());
        await connected.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        await using var conn = await _database.App.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await JobQueue.EnqueueAsync(tx, new NewJob("k", Queue: "email"), TestContext.Current.CancellationToken);
        await tx.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Equal("email", await received.Task.WaitAsync(Wait, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sends_each_channel_only_to_its_own_handler()
    {
        var events = new List<string>();
        var jobs = new TaskCompletionSource<string>();
        var connected = new TaskCompletionSource();
        await using var listener = await StartAsync(new()
        {
            [Outbox.Channel] = p => { lock (events) events.Add(p); },
            [JobQueue.Channel] = p => jobs.TrySetResult(p),
        }, () => connected.TrySetResult());
        await connected.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        await TestDatabase.ExecuteAsync(_database.App, "SELECT pg_notify('orvano_jobs', 'default')");

        Assert.Equal("default", await jobs.Task.WaitAsync(Wait, TestContext.Current.CancellationToken));
        lock (events) Assert.Empty(events);
    }

    [Fact]
    public async Task Reconnects_and_signals_a_resync_after_the_connection_drops()
    {
        var connects = 0;
        var reconnected = new TaskCompletionSource();
        await using var listener = await StartAsync(new() { [Outbox.Channel] = _ => { } }, () =>
        {
            if (Interlocked.Increment(ref connects) == 2) reconnected.TrySetResult();
        });
        await Eventually.TrueAsync(() => Task.FromResult(Volatile.Read(ref connects) == 1), Wait, "the first connect");

        await TestDatabase.ExecuteAsync(_database.Superuser,
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = 'listener-test' AND datname = current_database()");

        await reconnected.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
    }

    private async Task<RunningService> StartAsync(Dictionary<string, Action<string>> handlers, Action onConnected)
    {
        var dedicated = _database.Track(OrvanoDb.CreateDedicated(_database.AppUrl, "listener-test"));
        var listener = new PgNotificationListener(dedicated, handlers, onConnected, NullLogger<PgNotificationListener>.Instance);
        await listener.StartAsync(TestContext.Current.CancellationToken);
        return new RunningService(listener);
    }
}
