using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orvano.Server.Hosting;

namespace Orvano.Server.Tests.Hosting;

// Spec 0002, events and background work: realtime delivery is at most once. When the realtime role
// falls behind, the oldest event IDs are dropped, and each drop is counted so a busy role is visible.
public class RealtimeFanoutTests
{
    [Fact]
    public void Counts_each_event_id_dropped_past_capacity()
    {
        long dropped = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == RealtimeFanout.MeterName && instrument.Name == "orvano.realtime.events_dropped")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref dropped, value));
        listener.Start();

        // Never started, so nothing drains the queue and no connection is opened.
        using var db = NpgsqlDataSource.Create("Host=unused");
        var fanout = new RealtimeFanout(db, NullLogger<RealtimeFanout>.Instance);

        for (var id = 1; id <= RealtimeFanout.Capacity + 3; id++) fanout.OnNotify(id.ToString());
        fanout.OnNotify("not-a-number");

        Assert.Equal(3, Interlocked.Read(ref dropped));
    }
}
