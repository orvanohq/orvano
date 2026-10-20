using Microsoft.EntityFrameworkCore;

using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Signals;

/// <summary>Enlists signals on a platform transaction (spec 0013 BUS-2) and counts the ones that commit (LSN-7).</summary>
internal sealed class SignalBus(InfrastructureTelemetry telemetry) : ISignalBus
{
    public void Enlist(DbContext context, SignalKind kind, IEnumerable<string> values)
    {
        // BUS-2: cache tags go through ICacheInvalidator, which also removes them on this instance after the commit
        // (CAC-3). Sent from here, the writer would serve the old entry until its own echo arrives.
        if (kind == SignalKind.Cache)
        {
            throw new ArgumentException("Cache tags go through ICacheInvalidator (spec 0013 BUS-2).", nameof(kind));
        }

        Add(context, kind, values);
    }

    /// <summary>Enlists, and returns whether this was the first enlist of the kind in the transaction.</summary>
    public bool Add(DbContext context, SignalKind kind, IEnumerable<string> values)
    {
        bool first = TransactionSignals.Enlist(context, kind, values);
        if (first)
        {
            // LSN-7: only a confirmed commit counts; one with an unknown outcome may or may not have sent.
            TransactionSignals.AddOnCommitted(context, _ =>
            {
                telemetry.SignalSent(kind);
                return ValueTask.CompletedTask;
            });
        }

        return first;
    }
}
