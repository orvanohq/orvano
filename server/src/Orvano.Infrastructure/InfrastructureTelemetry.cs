using System.Diagnostics;
using System.Diagnostics.Metrics;

using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure;

/// <summary>
/// The <c>Orvano.Infrastructure</c> meter and activity source (spec 0002 LOG-5, spec 0013 LSN-7). The meter comes
/// from the host's <see cref="IMeterFactory"/>, so two hosts in one process keep their measurements apart.
/// </summary>
internal sealed class InfrastructureTelemetry
{
    public const string Name = "Orvano.Infrastructure";

    public const string ClearOnConnect = "connect";

    public const string ClearOnWildcard = "wildcard";

    // Spec 0013 JTL-1: the default boundaries suit milliseconds; jobs take seconds to an hour.
    private static readonly InstrumentAdvice<double> SecondsAdvice = new()
    {
        HistogramBucketBoundaries = [0.01, 0.05, 0.1, 0.5, 1, 5, 30, 60, 300, 1800, 3600],
    };

    private readonly Counter<long> _sent;
    private readonly Counter<long> _received;
    private readonly Counter<long> _clears;
    private readonly Counter<long> _jobsEnqueued;
    private readonly Counter<long> _jobAttempts;
    private readonly Histogram<double> _jobDuration;
    private readonly Histogram<double> _jobLag;
    private readonly Counter<long> _schedulesFired;
    private readonly Counter<long> _eventsPublished;
    private readonly Counter<long> _eventsDispatched;
    private readonly Histogram<double> _outboxLag;

    public InfrastructureTelemetry(IMeterFactory meters)
    {
        Meter = meters.Create(Name);
        _sent = Meter.CreateCounter<long>("orvano.infrastructure.signals.sent", "{signal}", "Signals sent by committed transactions.");
        _received = Meter.CreateCounter<long>("orvano.infrastructure.signals.received", "{signal}", "Signals received, heartbeats not counted.");
        _clears = Meter.CreateCounter<long>("orvano.infrastructure.cache.clears", "{clear}", "Times the whole local cache was cleared.");

        // Spec 0013 JTL-1: kinds, outcomes, and schedule names come from code, so the tags stay low in cardinality.
        _jobsEnqueued = Meter.CreateCounter<long>("orvano.infrastructure.jobs.enqueued", "{job}", "Jobs enqueued by committed transactions.");
        _jobAttempts = Meter.CreateCounter<long>("orvano.infrastructure.jobs.attempts", "{attempt}", "Job attempts, by outcome.");
        _jobDuration = Meter.CreateHistogram("orvano.infrastructure.jobs.duration", "s", "From handler start to outcome.", advice: SecondsAdvice);
        _jobLag = Meter.CreateHistogram("orvano.infrastructure.jobs.lag", "s", "From a job's due time to its claim.", advice: SecondsAdvice);
        _schedulesFired = Meter.CreateCounter<long>("orvano.infrastructure.jobs.schedules.fired", "{fire}", "Schedule occurrences enqueued.");

        // Spec 0013 OTL-1: types and schemas come from registrations, never from a row's text; no ids.
        _eventsPublished = Meter.CreateCounter<long>("orvano.infrastructure.outbox.published", "{event}", "Events published by committed transactions.");
        _eventsDispatched = Meter.CreateCounter<long>("orvano.infrastructure.outbox.dispatched", "{event}", "Events whose deliveries were enqueued.");
        _outboxLag = Meter.CreateHistogram("orvano.infrastructure.outbox.lag", "s", "From an event's publish to its dispatch commit.", advice: SecondsAdvice);
    }

    public static ActivitySource ActivitySource { get; } = new(Name);

    public Meter Meter { get; }

    public void SignalSent(SignalKind kind) => _sent.Add(1, new KeyValuePair<string, object?>("kind", SignalPayload.WireName(kind)));

    public void SignalReceived(SignalKind kind) => _received.Add(1, new KeyValuePair<string, object?>("kind", SignalPayload.WireName(kind)));

    public void CacheCleared(string reason) => _clears.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void JobEnqueued(string kind) => _jobsEnqueued.Add(1, new KeyValuePair<string, object?>("kind", kind));

    /// <summary>One attempt's outcome and, when a handler ran, its duration (JTL-1).</summary>
    public void JobAttempted(string kind, string outcome, TimeSpan? duration)
    {
        var tags = new TagList { { "kind", kind }, { "outcome", outcome } };
        _jobAttempts.Add(1, tags);
        if (duration is { } elapsed)
        {
            _jobDuration.Record(elapsed.TotalSeconds, tags);
        }
    }

    public void JobClaimed(string kind, TimeSpan lag) =>
        _jobLag.Record(Math.Max(0, lag.TotalSeconds), new KeyValuePair<string, object?>("kind", kind));

    public void EventPublished(string type) => _eventsPublished.Add(1, new KeyValuePair<string, object?>("type", type));

    public void EventDispatched(string type) => _eventsDispatched.Add(1, new KeyValuePair<string, object?>("type", type));

    public void EventDispatchLag(string outbox, TimeSpan lag) =>
        _outboxLag.Record(Math.Max(0, lag.TotalSeconds), new KeyValuePair<string, object?>("outbox", outbox));

    public void ScheduleFired(string schedule) => _schedulesFired.Add(1, new KeyValuePair<string, object?>("schedule", schedule));
}
