using Microsoft.EntityFrameworkCore;

using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Signals;

/// <summary>Enlists signals on a platform transaction (spec 0013 BUS-2) and counts the ones that commit (LSN-7).</summary>
internal sealed class SignalBus(InfrastructureTelemetry telemetry) : ISignalBus
{
    public void Enlist(DbContext context, SignalKind kind, IEnumerable<string> values) => Add(context, kind, values);

    /// <summary>Enlists, and returns whether this was the first enlist of the kind in the transaction.</summary>
    public bool Add(DbContext context, SignalKind kind, IEnumerable<string> values)
    {
        bool first = TransactionSignals.Enlist(context, kind, values);
        if (first)
        {
            TransactionSignals.AddAfterCommit(context, _ =>
            {
                telemetry.SignalSent(kind);
                return ValueTask.CompletedTask;
            });
        }

        return first;
    }
}
