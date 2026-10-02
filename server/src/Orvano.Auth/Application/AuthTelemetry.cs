using System.Diagnostics.Metrics;

namespace Orvano.Auth.Application;

/// <summary>The Auth module's metrics. They carry no user, email, or token.</summary>
internal static class AuthTelemetry
{
    /// <summary>The meter name to subscribe to in OpenTelemetry.</summary>
    public const string MeterName = "Orvano.Auth";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> OverFloor = Meter.CreateCounter<long>(
        "orvano.auth.open_send.over_floor", unit: "{request}",
        description: "Open email requests that took longer than the 500 ms floor, where a known and an unknown email may differ in time.");

    /// <summary>One open email request answered past the floor (spec 0010, AC-8).</summary>
    public static void RecordOverFloor() => OverFloor.Add(1);
}
