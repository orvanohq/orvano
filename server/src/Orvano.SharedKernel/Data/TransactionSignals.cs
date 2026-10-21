using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

using Orvano.SharedKernel.Signals;

namespace Orvano.SharedKernel.Data;

/// <summary>
/// The plumbing between a <see cref="DbContext"/>'s open transaction and the signals it sends on commit (spec 0013
/// SND-1, SND-5). <c>Orvano.Infrastructure</c>'s <c>SignalBus</c> and <c>CacheInvalidator</c> call it; modules use
/// <see cref="ISignalBus"/> and <see cref="ICacheInvalidator"/>.
/// </summary>
public static class TransactionSignals
{
    /// <summary>The longest signal value (spec 0013 BUS-2).</summary>
    public const int MaxValueLength = 200;

    /// <summary>
    /// Adds values to the context's batch and returns whether this is the first enlist of <paramref name="kind"/> in
    /// the current transaction. Values are checked before anything is added.
    /// </summary>
    public static bool Enlist(DbContext context, SignalKind kind, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown signal kind.");
        }

        List<string> list = [.. values];
        foreach (string value in list)
        {
            if (!IsValidValue(value))
            {
                throw new ArgumentException(
                    $"Signal values are 1 to {MaxValueLength} printable ASCII characters with no spaces (spec 0013 BUS-2).", nameof(values));
            }
        }

        return BatchOf(context, requireTransaction: true).Add(kind, list);
    }

    /// <summary>
    /// Adds an action that runs after the transaction commits, before <c>CommitAsync</c> returns, and also when the
    /// commit fails with an unknown outcome (SND-3). It must catch its own exceptions.
    /// </summary>
    public static void AddAfterCommit(DbContext context, Func<CancellationToken, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        BatchOf(context, requireTransaction: true).AddAfterCommit(action);
    }

    /// <summary>
    /// Adds a check that runs just before the transaction commits, before any signal is sent, at most one per
    /// <paramref name="key"/> per transaction (spec 0013 PUB-3). A check that throws fails the commit, so the
    /// transaction rolls back and nothing is sent; the on committed actions then never run.
    /// </summary>
    public static void AddBeforeCommit(DbContext context, object key, Action<DbContext> check)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(check);

        // The interceptor holds no context, so the check carries the one it was given.
        BatchOf(context, requireTransaction: true).AddBeforeCommit(key, () => check(context));
    }

    /// <summary>
    /// Adds an action that runs only after a confirmed commit, after the after commit actions, before <c>CommitAsync</c>
    /// returns; never for a commit with an unknown outcome (SND-1, SND-3). Use it for what must not happen for a commit
    /// that may not have gone through, such as counting a signal as sent. It must catch its own exceptions.
    /// </summary>
    public static void AddOnCommitted(DbContext context, Func<CancellationToken, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        BatchOf(context, requireTransaction: true).AddOnCommitted(action);
    }

    /// <summary>
    /// The SND-5 checks alone, for work that joins the transaction the way a signal does (spec 0013 ENQ-2): an open
    /// transaction, a platform database context, and a transaction this context began rather than joined.
    /// </summary>
    /// <exception cref="InvalidOperationException">One of the checks fails; the message names the fix.</exception>
    public static void EnsureEnlistable(DbContext context) => BatchOf(context, requireTransaction: true);

    /// <summary>The values of <paramref name="kind"/> enlisted so far in the current transaction, for an after commit action.</summary>
    public static IReadOnlyList<string> Enlisted(DbContext context, SignalKind kind) =>
        BatchOf(context, requireTransaction: false).ValuesOf(kind);

    /// <summary>BUS-2's value rule, also applied to values received on the wire (<see cref="SignalPayload.TryDecode"/>).</summary>
    internal static bool IsValidValue(string? value) =>
        value is { Length: > 0 and <= MaxValueLength } && value.All(c => c is >= '\x21' and <= '\x7E');

    private static PendingSignals BatchOf(DbContext context, bool requireTransaction)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (OrvanoConventions.SchemaOf(context.GetService<IDbContextOptions>()) is null)
        {
            throw new InvalidOperationException("Only platform database contexts send signals (spec 0007 SIG-3).");
        }

        if (requireTransaction && context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Signals join a transaction. Wrap the write in InTransactionAsync (spec 0013 SND-6).");
        }

        PendingSignals batch = context.GetService<PendingSignals>();
        if (requireTransaction && batch.Joined)
        {
            // SND-5: signals belong to the context that began the transaction, which is also the one that commits it.
            throw new InvalidOperationException(
                "This context joined another context's transaction with UseTransaction. Enlist on the context that began it (spec 0013 SND-5).");
        }

        return batch;
    }
}
