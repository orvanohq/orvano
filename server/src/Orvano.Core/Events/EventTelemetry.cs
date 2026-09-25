using System.Diagnostics.Metrics;

namespace Orvano.Core.Events;

/// <summary>The <c>Orvano.Events</c> meter. Row 37 decides the alerts on it.</summary>
public static class EventTelemetry
{
    public const string MeterName = "Orvano.Events";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Tagged <c>event.type</c>, <c>consumer</c>, and <c>reason</c> (<c>threw</c>, <c>invalid_job</c>, or <c>rejected_by_database</c>).</summary>
    internal static readonly Counter<long> ConsumerFailures = Meter.CreateCounter<long>(
        "orvano.events.consumer_failures",
        unit: "{failure}",
        description: "Event consumers that failed on an event and were handed to an events.redispatch job.");
}
