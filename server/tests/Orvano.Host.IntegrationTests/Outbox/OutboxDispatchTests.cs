using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Orvano.Host.IntegrationTests.Jobs;
using Orvano.Host.IntegrationTests.Signals;
using Orvano.Infrastructure.Outbox;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Outbox;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Outbox;

// Spec 0013 build plan steps 27 and 28: a dispatch pass moves committed events into one job per subscriber in batches,
// marks every selected row, rolls a failed batch back whole, and halves that outbox's next batch. One api host (no
// dispatcher loop) with the sample outbox and a second one, "aside", whose only type has no subscriber; the tests drive
// each pass with JobsDriver.DispatchAsync on a ManualClock.
public sealed class OutboxDispatchTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Aside = "aside";

    private readonly ManualClock _clock = new();
    private readonly RecordingLogs _logs = new();
    private readonly DispatchInterceptor _interceptor = new();
    private readonly List<OrvanoApiFactory> _hosts = [];
    private Dictionary<string, string> _settings = null!;
    private OrvanoApiFactory _host = null!;
    private TestDatabase _database = null!;
    private OutboxRows _rows = null!;
    private JobRows _jobs = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private OutboxDispatch Dispatch => _host.Services.GetRequiredService<OutboxDispatch>();

    public async ValueTask InitializeAsync()
    {
        _settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "dispatch", Token);
        _settings["Logging:LogLevel:Default"] = "Debug";
        _database = TestDatabase.For(postgres, _settings);
        _rows = _database.OutboxRows();
        _jobs = _database.Rows();
        _host = Host();
        await SampleOutboxDbContext.CreateTablesAsync(_host.Services, Token);
        await SampleOutboxDbContext.CreateTablesAsync(_host.Services, Aside, Token);
    }

    public async ValueTask DisposeAsync()
    {
        _interceptor.Release.TrySetResult();
        foreach (OrvanoApiFactory host in _hosts)
        {
            await host.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:AC-36"), Trait("Spec", "0013:DSP-2"), Trait("Spec", "0013:DSP-3"), Trait("Spec", "0013:DLV-3"), Trait("Spec", "0013:AC-41")]
    public async Task A_pass_dispatches_250_events_in_batches_of_100_into_one_job_per_subscriber_and_marks_each_with_the_clock()
    {
        var project = Guid.CreateVersion7();
        Guid[] ids = await OutboxHosts.PublishManyAsync(_host.Services, 250, _ => new SampleNoteAdded(Guid.CreateVersion7()), project, Token);
        _clock.Advance(TimeSpan.FromSeconds(3));
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());
        using var activities = new ActivityCapture();
        using var root = new Activity("test dispatch");
        root.SetIdFormat(ActivityIdFormat.W3C);
        root.Start();

        int marked = await JobsDriver.DispatchAsync(_host.Services, Token);
        root.Stop();

        marked.ShouldBe(250);
        _interceptor.Sizes.ShouldBe([100, 100, 100]); // the third batch takes 50 of 100, so the rounds end
        (await _rows.CountAsync("dispatched_at = $1", Token, _clock.GetUtcNow())).ShouldBe(250);
        IReadOnlyList<JobRows.Row> jobs = await _jobs.WhereAsync("true", Token);
        jobs.Count.ShouldBe(500);
        jobs.ShouldAllBe(job => job.Status == "queued" && job.ProjectId == project && job.RunAt == _clock.GetUtcNow() && job.TraceParent == null);
        jobs.Select(job => $"{job.Kind} {job.DedupeKey}").ShouldBe(
            ids.SelectMany(id => new[] { $"{OutboxProbes.Recorder} {id:D}:{OutboxProbes.Recorder}", $"{OutboxProbes.Second} {id:D}:{OutboxProbes.Second}" }),
            ignoreOrder: true);
        jobs.Single(job => job.Kind == OutboxProbes.Recorder && job.DedupeKey!.StartsWith(ids[0].ToString("D"), StringComparison.Ordinal))
            .MaxAttempts.ShouldBe(2); // the subscriber's own kind options (DLV-1)
        meters.Sum("orvano.infrastructure.outbox.dispatched", "type", SampleNoteAdded.Type).ShouldBe(250);
        meters.Values("orvano.infrastructure.outbox.lag", "outbox", "sample").ShouldBe(Enumerable.Repeat(3.0, 250));
        meters.Sum("orvano.infrastructure.jobs.enqueued", "kind", OutboxProbes.Second).ShouldBe(250);
        Activity[] dispatches = activities.Named("outbox dispatch sample", root.TraceId);
        dispatches.Select(activity => activity.GetTagItem("orvano.outbox.events")).ShouldBe([100, 100, 50]);
        dispatches.ShouldAllBe(activity => Equals(activity.GetTagItem("orvano.outbox.schema"), "sample") && activity.Kind == ActivityKind.Internal);
        activities.Named("outbox dispatch aside", root.TraceId).ShouldBeEmpty(); // a batch that selects nothing runs no activity
        (await JobsDriver.DispatchAsync(_host.Services, Token)).ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-36"), Trait("Spec", "0013:DSP-4"), Trait("Spec", "0013:DLV-4"), Trait("Spec", "0013:AC-41")]
    public async Task Rows_of_an_unknown_type_and_without_subscribers_are_marked_and_a_second_dispatch_inserts_no_job()
    {
        Guid note = await OutboxHosts.PublishAsync(_host.Services, new SampleNoteAdded(Guid.CreateVersion7()), null, Token);
        Guid unknown = await _rows.InsertAsync("sample.gone.v1", "{}", _clock.GetUtcNow(), Token);
        await PublishAsideAsync(Token);
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());

        (await JobsDriver.DispatchAsync(_host.Services, Token)).ShouldBe(3);

        (await _rows.CountAsync("dispatched_at IS NULL", Token)).ShouldBe(0);
        (await AsideWaitingAsync(Token)).ShouldBe(0);
        (await _jobs.WhereAsync("true", Token)).Select(job => job.DedupeKey).ShouldBe(
            [$"{note:D}:{OutboxProbes.Recorder}", $"{note:D}:{OutboxProbes.Second}"], ignoreOrder: true);
        meters.Sum("orvano.infrastructure.outbox.dispatched", "type", "unknown").ShouldBe(1);
        meters.Sum("orvano.infrastructure.outbox.dispatched", "type", AsideHappened.Type).ShouldBe(1);
        _logs.Count(LogEventIds.Infrastructure + 203, LogLevel.Warning).ShouldBe(1);
        _logs.Messages(LogEventIds.Infrastructure + 203).ShouldHaveSingleItem().ShouldContain("sample.gone.v1");
        _logs.Count(LogEventIds.Infrastructure + 201, LogLevel.Debug).ShouldBe(2);

        // A commit whose outcome was unknown is dispatched again: every delivery's dedupe key is taken (DLV-4).
        await _database.ExecuteAsync("UPDATE sample.outbox SET dispatched_at = NULL", Token);
        (await JobsDriver.DispatchAsync(_host.Services, Token)).ShouldBe(2);
        (await _jobs.CountAsync("true", Token)).ShouldBe(2);
        (await _rows.CountAsync("id = ANY($1) AND dispatched_at IS NOT NULL", Token, new[] { note, unknown })).ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "0013:AC-36"), Trait("Spec", "0013:DSP-5"), Trait("Spec", "0013:OTL-2")]
    public async Task A_batch_that_fails_after_its_inserts_leaves_no_job_and_no_mark_and_the_other_outbox_still_dispatches()
    {
        Guid[] ids = await OutboxHosts.PublishManyAsync(_host.Services, 3, _ => new SampleNoteAdded(Guid.CreateVersion7()), null, Token);
        await PublishAsideAsync(Token);
        _interceptor.FailMark = true; // the sample outbox's UPDATE, after its job inserts

        (await JobsDriver.DispatchAsync(_host.Services, Token)).ShouldBe(1); // the aside event

        (await _jobs.CountAsync("true", Token)).ShouldBe(0);
        (await _rows.CountAsync("dispatched_at IS NULL", Token)).ShouldBe(3);
        (await AsideWaitingAsync(Token)).ShouldBe(0);
        _logs.Count(LogEventIds.Infrastructure + 202, LogLevel.Error).ShouldBe(1);
        _logs.Messages(LogEventIds.Infrastructure + 202).ShouldHaveSingleItem().ShouldContain("sample");
        Dispatch.BatchSizeOf("sample").ShouldBe(50);
        Dispatch.BatchSizeOf(Aside).ShouldBe(OutboxDispatch.FullBatch);

        _interceptor.FailMark = false;
        (await JobsDriver.DispatchAsync(_host.Services, Token)).ShouldBe(3);
        (await _jobs.CountAsync("true", Token)).ShouldBe(6);
        (await _rows.CountAsync("id = ANY($1) AND dispatched_at IS NOT NULL", Token, ids)).ShouldBe(3);
        Dispatch.BatchSizeOf("sample").ShouldBe(OutboxDispatch.FullBatch);
    }

    [Fact]
    [Trait("Spec", "0013:AC-36"), Trait("Spec", "0013:DSP-5")]
    public async Task A_failing_row_halves_its_outboxs_batch_down_to_itself_and_full_batches_return_once_it_passes()
    {
        await OutboxHosts.PublishManyAsync(_host.Services, 120, _ => new SampleNoteAdded(Guid.CreateVersion7()), null, Token);
        Guid bad = (await _rows.WhereAsync("true", Token))[6].Id; // the 7th row in id order
        _interceptor.FailEvent = bad;

        int[] marked = new int[12];
        for (int i = 0; i < marked.Length; i++)
        {
            marked[i] = await JobsDriver.DispatchAsync(_host.Services, Token);
        }

        // 100, 50, 25, 12 hold the bad row and fail; 6 stops before it and commits, and the round after it starts at
        // 100 again and fails; then down to the bad row alone, which keeps failing at 1.
        _interceptor.Sizes.ShouldBe([100, 50, 25, 12, 6, 100, 50, 25, 12, 6, 3, 1, 1]);
        marked.ShouldBe([0, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0]);
        Dispatch.BatchSizeOf("sample").ShouldBe(1);
        (await _rows.CountAsync("dispatched_at IS NOT NULL", Token)).ShouldBe(6);

        _interceptor.FailEvent = null;
        _interceptor.Sizes.Clear();
        (await JobsDriver.DispatchAsync(_host.Services, Token)).ShouldBe(114);
        _interceptor.Sizes.ShouldBe([1, 100, 100]); // the bad row alone commits, and full batches return
        Dispatch.BatchSizeOf("sample").ShouldBe(OutboxDispatch.FullBatch);
        (await _jobs.CountAsync("true", Token)).ShouldBe(240);
    }

    [Fact]
    [Trait("Spec", "0013:AC-36"), Trait("Spec", "0013:DSP-5"), Trait("Spec", "0013:DSP-6")]
    public async Task A_batch_cut_by_the_bound_halves_the_size_a_shutdown_leaves_it_and_a_new_dispatch_starts_full()
    {
        await OutboxHosts.PublishAsync(_host.Services, new SampleNoteAdded(Guid.CreateVersion7()), null, Token);
        _interceptor.Hold = true;

        // The bound runs on the clock: the select waits until it passes.
        Task<int> cut = JobsDriver.DispatchAsync(_host.Services, Token);
        await _interceptor.Holding.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        _clock.Advance(OutboxDispatch.BatchBound);
        (await cut.WaitAsync(TimeSpan.FromSeconds(10), Token)).ShouldBe(0);
        Dispatch.BatchSizeOf("sample").ShouldBe(50);
        _logs.Count(LogEventIds.Infrastructure + 202, LogLevel.Error).ShouldBe(1);

        // A shutdown in the middle of a batch rolls it back and changes nothing, and is not logged.
        using var stopping = new CancellationTokenSource();
        Task<int> stopped = Dispatch.DispatchAsync(stopping.Token);
        await Eventually.TrueAsync(() => _interceptor.Sizes.Count == 2, TimeSpan.FromSeconds(10), "the second select never ran", Token);
        await stopping.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => stopped.WaitAsync(TimeSpan.FromSeconds(10), Token));
        Dispatch.BatchSizeOf("sample").ShouldBe(50);
        _logs.Count(LogEventIds.Infrastructure + 202, LogLevel.Error).ShouldBe(1);
        (await _rows.CountAsync("dispatched_at IS NULL", Token)).ShouldBe(1);

        // The sizes live in the singleton, per process: a new one starts every outbox full.
        ActivatorUtilities.CreateInstance<OutboxDispatch>(_host.Services).BatchSizeOf("sample").ShouldBe(OutboxDispatch.FullBatch);

        _interceptor.Hold = false;
        _interceptor.Release.SetResult();
        (await JobsDriver.DispatchAsync(_host.Services, Token)).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-36"), Trait("Spec", "0013:DSP-3"), Trait("Spec", "0013:DLV-4")]
    public async Task Two_dispatchers_on_the_same_300_events_give_each_subscriber_exactly_one_job_per_event()
    {
        OrvanoApiFactory other = Host();
        Guid[] ids = await OutboxHosts.PublishManyAsync(_host.Services, 300, _ => new SampleNoteAdded(Guid.CreateVersion7()), null, Token);

        int[] marked = await Task.WhenAll(
            Task.Run(() => JobsDriver.DispatchAsync(_host.Services, Token), Token),
            Task.Run(() => JobsDriver.DispatchAsync(other.Services, Token), Token));
        marked.Sum().ShouldBeLessThanOrEqualTo(300);
        marked.Sum().ShouldBe(300 - (int)await _rows.CountAsync("dispatched_at IS NULL", Token));
        while (await _rows.CountAsync("dispatched_at IS NULL", Token) > 0)
        {
            await JobsDriver.DispatchAsync(other.Services, Token); // whatever a skipped lock left behind
        }

        IReadOnlyList<JobRows.Row> jobs = await _jobs.WhereAsync("true", Token);
        jobs.Count.ShouldBe(600);
        jobs.Select(job => (job.Kind, job.DedupeKey)).Distinct().Count().ShouldBe(600);
        jobs.Select(job => job.DedupeKey![..36]).Distinct().Order().ShouldBe(ids.Select(id => id.ToString("D")).Order());
    }

    private OrvanoApiFactory Host()
    {
        OrvanoApiFactory host = OutboxHosts.Create(_settings, new OutboxProbes(), _clock, roles: "api", services =>
        {
            services.AddOutbox(Aside, outbox => outbox.Event(AsideJsonContext.Default.AsideHappened));
            services.AddSingleton<ILoggerProvider>(_logs);
            DispatchInterceptor.AddTo(services, _interceptor);
        });
        _hosts.Add(host);
        return host;
    }

    private async Task PublishAsideAsync(CancellationToken cancellationToken)
    {
        IOutbox outbox = _host.Services.GetRequiredService<IOutbox>();
        await using var db = SampleOutboxDbContext.On(_host.Services, Aside);
        await db.InTransactionAsync(
            async token =>
            {
                outbox.Publish(db, new AsideHappened(Guid.CreateVersion7()), null);
                await db.SaveChangesAsync(token);
            },
            cancellationToken);
    }

    private async Task<long> AsideWaitingAsync(CancellationToken cancellationToken)
    {
        await using Npgsql.NpgsqlCommand command = _database.DataSource.CreateCommand("SELECT count(*) FROM aside.outbox WHERE dispatched_at IS NULL");
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

/// <summary>The aside outbox's one type, which nothing subscribes to.</summary>
internal sealed record AsideHappened(Guid Id) : IOutboxEvent
{
    public static string Type => "aside.thing.happened.v1";
}

[JsonSerializable(typeof(AsideHappened))]
internal sealed partial class AsideJsonContext : JsonSerializerContext;

/// <summary>Collects the stopped activities of the <c>Orvano.Infrastructure</c> source.</summary>
internal sealed class ActivityCapture : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;

    public ActivityCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Orvano.Infrastructure",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_activities)
                {
                    _activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>The activities named <paramref name="name"/> in <paramref name="trace"/>, in the order they stopped; other tests run in parallel.</summary>
    public Activity[] Named(string name, ActivityTraceId trace)
    {
        lock (_activities)
        {
            return [.. _activities.Where(activity => activity.DisplayName == name && activity.TraceId == trace)];
        }
    }

    public Activity[] All()
    {
        lock (_activities)
        {
            return [.. _activities];
        }
    }

    public void Dispose() => _listener.Dispose();
}
