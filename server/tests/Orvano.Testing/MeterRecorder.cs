using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Orvano.Testing;

/// <summary>
/// Records the measurements of one host's meters (those its <see cref="IMeterFactory"/> created), so another host in
/// the same test process never mixes in (spec 0002 LOG-5).
/// </summary>
public sealed class MeterRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public MeterRecorder(IMeterFactory factory)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == factory)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => _measurements.Enqueue((instrument.Name, value, tags.ToArray())));
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => _measurements.Enqueue((instrument.Name, value, tags.ToArray())));
        _listener.Start();
    }

    /// <summary>The sum of a counter's measurements, optionally only those with one tag value.</summary>
    public long Sum(string name, string? tag = null, string? value = null) => _measurements
        .Where(m => m.Name == name && (tag is null || m.Tags.Any(t => t.Key == tag && Equals(t.Value, value))))
        .Sum(m => m.Value);

    /// <summary>The current value of an observable gauge.</summary>
    public long Observe(string name)
    {
        _listener.RecordObservableInstruments();
        return _measurements.Last(m => m.Name == name).Value;
    }

    public void Dispose() => _listener.Dispose();
}
