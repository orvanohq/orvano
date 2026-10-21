using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Orvano.Host.IntegrationTests.Jobs;
using Orvano.Host.IntegrationTests.Signals;
using Orvano.SharedKernel.Access;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Outbox;

// Spec 0013 build plan steps 27 and 28: each subscriber gets the event as a job of its own kind, in the event's project
// scope, with the job queue's retries and dead marking; JobsDriver dispatches and runs in one call. One api host on a
// ManualClock with the sample outbox and its subscribers.
public sealed class OutboxDeliveryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly OutboxProbes _probes = new();
    private readonly ManualClock _clock = new();
    private readonly RecordingLogs _logs = new();
    private OrvanoApiFactory _host = null!;
    private TestDatabase _database = null!;
    private JobRows _jobs = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string> settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "delivery", Token);
        settings["Logging:LogLevel:Default"] = "Debug";
        _database = TestDatabase.For(postgres, settings);
        _jobs = _database.Rows();
        _host = OutboxHosts.Create(settings, _probes, _clock, roles: "api", services => services.AddSingleton<ILoggerProvider>(_logs));
        await SampleOutboxDbContext.CreateTablesAsync(_host.Services, Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:AC-38"), Trait("Spec", "0013:DLV-5"), Trait("Spec", "0013:OBX-4"), Trait("Spec", "0013:OTL-3"), Trait("Spec", "0013:AC-41")]
    public async Task Each_subscriber_gets_the_event_and_its_context_as_the_system_caller_in_the_events_project_linked_to_the_publishing_trace()
    {
        const string marker = "payload-marker-91c2";
        var project = Guid.CreateVersion7();
        var note = Guid.CreateVersion7();
        using var activities = new ActivityCapture(); // so the job attempts run in activities (JTL-3)
        using var publishing = new Activity("test publish");
        publishing.SetIdFormat(ActivityIdFormat.W3C);
        publishing.Start();
        Guid id = await OutboxHosts.PublishAsync(_host.Services, new SampleNoteAdded(note, marker), project, Token);
        publishing.Stop();
        Guid instanceWide = await OutboxHosts.PublishAsync(_host.Services, new SampleNoteAdded(Guid.CreateVersion7()), null, Token);
        DateTimeOffset occurredAt = _clock.GetUtcNow();
        _clock.Advance(TimeSpan.FromSeconds(1));

        IReadOnlyList<JobRun> runs = await JobsDriver.RunDueAsync(_host.Services, Token);

        runs.Count.ShouldBe(4);
        runs.ShouldAllBe(run => run.Outcome == "succeeded");
        Delivery first = _probes.Of(OutboxProbes.Recorder).Single(delivery => delivery.NoteId == note);
        Delivery second = _probes.Of(OutboxProbes.Second).Single(delivery => delivery.NoteId == note);
        foreach (Delivery delivery in new[] { first, second })
        {
            delivery.Context.EventId.ShouldBe(id);
            delivery.Context.Type.ShouldBe(SampleNoteAdded.Type);
            delivery.Context.OccurredAt.ShouldBe(occurredAt);
            delivery.Context.ProjectId.ShouldBe(project);
            delivery.Context.Job.ProjectId.ShouldBe(project);
            delivery.Context.Job.Attempt.ShouldBe(1);
            delivery.Caller.ShouldBe(CallerKind.System);
            delivery.Project.ShouldBe(project);
            delivery.Links.ShouldHaveSingleItem().TraceId.ShouldBe(publishing.TraceId);
        }

        first.Context.Job.Kind.ShouldBe(OutboxProbes.Recorder);
        first.Context.Job.MaxAttempts.ShouldBe(2);
        second.Context.Job.Kind.ShouldBe(OutboxProbes.Second);
        Delivery wide = _probes.Of(OutboxProbes.Recorder).Single(delivery => delivery.Context.EventId == instanceWide);
        wide.Context.ProjectId.ShouldBeNull();
        wide.Project.ShouldBeNull();
        wide.ProjectError.ShouldNotBeNull();
        _logs.TextAtOrAbove(LogLevel.Information).ShouldNotContain(text => text.Contains(marker, StringComparison.Ordinal));
        _logs.TextAtOrAbove(LogLevel.Debug).ShouldContain(text => text.Contains(marker, StringComparison.Ordinal)); // the capture sees Debug
    }

    [Fact]
    [Trait("Spec", "0013:AC-38"), Trait("Spec", "0013:DLV-5"), Trait("Spec", "0013:DLV-6")]
    public async Task A_throwing_subscriber_retries_with_backoff_and_dies_after_its_attempts_while_the_other_subscriber_succeeds()
    {
        var note = Guid.CreateVersion7();
        await OutboxHosts.PublishAsync(_host.Services, new SampleNoteAdded(note, NoteMode.Throw), null, Token);

        IReadOnlyList<JobRun> first = await JobsDriver.RunDueAsync(_host.Services, Token);

        first.Select(run => (run.Kind, run.Outcome)).ShouldBe(
            [(OutboxProbes.Recorder, "failed"), (OutboxProbes.Second, "succeeded")], ignoreOrder: true);
        JobRows.Row retry = (await _jobs.WhereAsync("kind = $1", Token, OutboxProbes.Recorder)).ShouldHaveSingleItem();
        retry.Status.ShouldBe("queued");
        retry.LastError!.ShouldContain("boom");
        (retry.RunAt - _clock.GetUtcNow()).ShouldBeInRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)); // backoff d / 2 to d

        _clock.Advance(TimeSpan.FromSeconds(10));
        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun(OutboxProbes.Recorder, retry.Id, "dead")]);
        (await _jobs.FindAsync(retry.Id, Token))!.Status.ShouldBe("dead"); // MaxAttempts 2, the subscriber's own option
        _probes.Of(OutboxProbes.Recorder).Count.ShouldBe(2);
        _probes.Of(OutboxProbes.Second).ShouldHaveSingleItem();
    }

    [Fact]
    [Trait("Spec", "0013:AC-38"), Trait("Spec", "0013:DLV-5")]
    public async Task PermanentJobFailureException_in_a_subscriber_makes_its_delivery_dead_at_once()
    {
        await OutboxHosts.PublishAsync(_host.Services, new SampleNoteAdded(Guid.CreateVersion7(), NoteMode.Permanent), null, Token);

        IReadOnlyList<JobRun> runs = await JobsDriver.RunDueAsync(_host.Services, Token);

        runs.Select(run => (run.Kind, run.Outcome)).ShouldBe(
            [(OutboxProbes.Recorder, "dead"), (OutboxProbes.Second, "succeeded")], ignoreOrder: true);
        (await _jobs.WhereAsync("kind = $1", Token, OutboxProbes.Recorder)).ShouldHaveSingleItem().Attempt.ShouldBe(1);
    }

    [Theory]
    [Trait("Spec", "0013:AC-38"), Trait("Spec", "0013:DLV-2"), Trait("Spec", "0013:DLV-5")]
    [InlineData("""{"eventId":"01a15408-6a00-7000-8000-000000000001","type":"sample.note.added.v1","occurredAt":"2026-10-19T12:00:00+00:00","data":{"NoteId":"not a guid"}}""")]
    [InlineData("""{"eventId":"01a15408-6a00-7000-8000-000000000001","type":"sample.gone.v1","occurredAt":"2026-10-19T12:00:00+00:00","data":{}}""")]
    [InlineData("""{"eventId":"01a15408-6a00-7000-8000-000000000001","type":"sample.note.added.v1","occurredAt":"2026-10-19T12:00:00+00:00","data":null}""")]
    public async Task A_delivery_whose_data_does_not_read_or_whose_type_is_not_registered_is_a_failed_attempt(string payload)
    {
        Guid id = await _jobs.InsertQueuedAsync(OutboxProbes.Second, payload, _clock.GetUtcNow(), Token);

        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBe([new JobRun(OutboxProbes.Second, id, "failed")]);

        JobRows.Row row = (await _jobs.FindAsync(id, Token))!;
        row.Status.ShouldBe("queued");
        row.Attempt.ShouldBe(1);
        _probes.Deliveries.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:AC-43"), Trait("Spec", "0013:OTS-2")]
    public async Task A_subscriber_that_publishes_a_second_event_completes_with_its_subscriber_in_one_RunDueAsync_call()
    {
        var note = Guid.CreateVersion7();
        var project = Guid.CreateVersion7();
        await OutboxHosts.PublishAsync(_host.Services, new SampleNoteAdded(note, NoteMode.Chain), project, Token);

        IReadOnlyList<JobRun> runs = await JobsDriver.RunDueAsync(_host.Services, Token);

        runs.Select(run => run.Kind).ShouldBe([OutboxProbes.Recorder, OutboxProbes.Second, OutboxProbes.Chained], ignoreOrder: true);
        runs.ShouldAllBe(run => run.Outcome == "succeeded");
        Delivery chained = _probes.Of(OutboxProbes.Chained).ShouldHaveSingleItem();
        chained.NoteId.ShouldBe(note);
        chained.Project.ShouldBe(project);
        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:AC-43"), Trait("Spec", "0013:OTS-2")]
    public async Task DispatchAsync_alone_marks_the_pending_events_and_runs_no_handler()
    {
        await OutboxHosts.PublishManyAsync(_host.Services, 2, _ => new SampleNoteAdded(Guid.CreateVersion7()), null, Token);

        (await JobsDriver.DispatchAsync(_host.Services, Token)).ShouldBe(2);

        _probes.Deliveries.ShouldBeEmpty();
        (await _jobs.CountAsync("status = 'queued'", Token)).ShouldBe(4);
        (await JobsDriver.DispatchAsync(_host.Services, Token)).ShouldBe(0);
        (await JobsDriver.RunDueAsync(_host.Services, Token)).Count.ShouldBe(4);
    }
}
