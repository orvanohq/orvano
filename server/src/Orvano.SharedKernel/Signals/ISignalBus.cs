using Microsoft.EntityFrameworkCore;

namespace Orvano.SharedKernel.Signals;

/// <summary>Tells every instance about a platform database change, once that change commits (spec 0013 BUS-2).</summary>
public interface ISignalBus
{
    /// <summary>
    /// Adds <paramref name="values"/> to the signal of <paramref name="kind"/> that <paramref name="context"/>'s open
    /// transaction sends just before it commits. Synchronous, no I/O. Each value is 1 to 200 printable ASCII characters
    /// (no spaces). An empty list does nothing for <see cref="SignalKind.Cache"/> and <see cref="SignalKind.Session"/>
    /// and marks <see cref="SignalKind.Job"/> present. Cache tags go through <see cref="ICacheInvalidator"/> instead.
    /// </summary>
    /// <exception cref="InvalidOperationException">The context has no open transaction, or is not a platform database context.</exception>
    /// <exception cref="ArgumentException">A value is empty, too long, or not printable ASCII, or the kind is <see cref="SignalKind.Cache"/>.</exception>
    void Enlist(DbContext context, SignalKind kind, IEnumerable<string> values);
}
