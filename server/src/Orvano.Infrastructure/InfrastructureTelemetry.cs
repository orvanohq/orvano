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

    private readonly Counter<long> _sent;
    private readonly Counter<long> _received;
    private readonly Counter<long> _clears;

    public InfrastructureTelemetry(IMeterFactory meters)
    {
        Meter = meters.Create(Name);
        _sent = Meter.CreateCounter<long>("orvano.infrastructure.signals.sent", "{signal}", "Signals sent by committed transactions.");
        _received = Meter.CreateCounter<long>("orvano.infrastructure.signals.received", "{signal}", "Signals received, heartbeats not counted.");
        _clears = Meter.CreateCounter<long>("orvano.infrastructure.cache.clears", "{clear}", "Times the whole local cache was cleared.");
    }

    public static ActivitySource ActivitySource { get; } = new(Name);

    public Meter Meter { get; }

    public void SignalSent(SignalKind kind) => _sent.Add(1, new KeyValuePair<string, object?>("kind", SignalPayload.WireName(kind)));

    public void SignalReceived(SignalKind kind) => _received.Add(1, new KeyValuePair<string, object?>("kind", SignalPayload.WireName(kind)));

    public void CacheCleared(string reason) => _clears.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
