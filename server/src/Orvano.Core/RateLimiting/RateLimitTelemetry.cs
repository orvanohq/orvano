using System.Diagnostics.Metrics;

namespace Orvano.Core.RateLimiting;

/// <summary>The limiter's metric (spec 0014, AC-39): refusals by policy name, never by key.</summary>
public static class RateLimitTelemetry
{
    /// <summary>The meter name to subscribe to in OpenTelemetry.</summary>
    public const string MeterName = "Orvano.RateLimits";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Refused = Meter.CreateCounter<long>(
        "orvano.auth.limit_refused", unit: "{request}", description: "Requests a named rate limit refused, tagged with the policy name only.");

    internal static void RecordRefused(string policy) => Refused.Add(1, new KeyValuePair<string, object?>("policy", policy));
}
