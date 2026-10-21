using Microsoft.Extensions.Logging;

using Orvano.SharedKernel.Signals;

namespace Orvano.SharedKernel.Data;

/// <summary>
/// The signals and actions one <c>DbContext</c>'s current transaction will send and run (spec 0013 SND-1, SND-4, SND-5):
/// per kind, distinct values in first enlisted order, the before commit checks (PUB-3), the after commit and on
/// committed actions, and whether the transaction was joined rather than begun. A scoped EF internal service, so one per
/// context instance.
/// </summary>
internal sealed class PendingSignals
{
    private readonly List<SignalKind> _kinds = [];
    private readonly Dictionary<SignalKind, (List<string> Values, HashSet<string> Seen)> _values = [];
    private readonly List<Func<CancellationToken, ValueTask>> _afterCommit = [];
    private readonly List<Func<CancellationToken, ValueTask>> _onCommitted = [];
    private readonly List<(object Key, Action Check)> _beforeCommit = [];

    /// <summary>True when there is nothing to check before the commit, nothing to send, and nothing to run after it.</summary>
    public bool IsEmpty => _kinds.Count == 0 && _beforeCommit.Count == 0 && _afterCommit.Count == 0 && _onCommitted.Count == 0;

    /// <summary>
    /// True while the context's current transaction was joined with <c>UseTransaction</c> rather than begun (SND-5).
    /// Set when the transaction is used, cleared when one is started and at every transaction end.
    /// </summary>
    public bool Joined { get; set; }

    /// <summary>The kinds present, in first enlisted order, each with its values.</summary>
    public IEnumerable<(SignalKind Kind, IReadOnlyList<string> Values)> Batches =>
        _kinds.Select(kind => (kind, (IReadOnlyList<string>)_values[kind].Values));

    /// <summary>
    /// Adds values of a kind and returns whether this made the kind present. A <c>job</c> only marks itself present; an
    /// empty list of another kind adds nothing.
    /// </summary>
    public bool Add(SignalKind kind, IReadOnlyList<string> values)
    {
        bool present = _values.TryGetValue(kind, out (List<string> Values, HashSet<string> Seen) entry);
        if (!present)
        {
            if (kind != SignalKind.Job && values.Count == 0)
            {
                return false;
            }

            entry = ([], new HashSet<string>(StringComparer.Ordinal));
            _values[kind] = entry;
            _kinds.Add(kind);
        }

        if (kind != SignalKind.Job)
        {
            foreach (string value in values)
            {
                if (entry.Seen.Add(value))
                {
                    entry.Values.Add(value);
                }
            }
        }

        return !present;
    }

    public IReadOnlyList<string> ValuesOf(SignalKind kind) =>
        _values.TryGetValue(kind, out (List<string> Values, HashSet<string> Seen) entry) ? entry.Values : [];

    /// <summary>Adds a check unless one with <paramref name="key"/> is already in this transaction (PUB-3).</summary>
    public void AddBeforeCommit(object key, Action check)
    {
        if (!_beforeCommit.Exists(entry => Equals(entry.Key, key)))
        {
            _beforeCommit.Add((key, check));
        }
    }

    /// <summary>Runs the before commit checks in add order; one that throws fails the commit (PUB-3).</summary>
    public void RunBeforeCommit()
    {
        foreach ((object _, Action check) in _beforeCommit.ToArray())
        {
            check();
        }
    }

    public void AddAfterCommit(Func<CancellationToken, ValueTask> action) => _afterCommit.Add(action);

    public void AddOnCommitted(Func<CancellationToken, ValueTask> action) => _onCommitted.Add(action);

    /// <summary>Runs the after commit actions, for a confirmed commit or one with an unknown outcome (SND-3).</summary>
    public Task RunAfterCommitAsync(Lazy<ILogger> logger, CancellationToken cancellationToken) =>
        RunAsync(_afterCommit, logger, cancellationToken);

    /// <summary>Runs the on committed actions, only for a confirmed commit (SND-1, SND-3).</summary>
    public Task RunOnCommittedAsync(Lazy<ILogger> logger, CancellationToken cancellationToken) =>
        RunAsync(_onCommitted, logger, cancellationToken);

    public void Clear()
    {
        _kinds.Clear();
        _values.Clear();
        _beforeCommit.Clear();
        _afterCommit.Clear();
        _onCommitted.Clear();
        Joined = false;
    }

    /// <summary>
    /// Runs actions in the order they were added. Each catches its own exceptions (SND-3); one that throws anyway is
    /// logged at Error here, so it never turns a commit that happened into a failed one.
    /// </summary>
    private static async Task RunAsync(List<Func<CancellationToken, ValueTask>> actions, Lazy<ILogger> logger, CancellationToken cancellationToken)
    {
        foreach (Func<CancellationToken, ValueTask> action in actions.ToArray())
        {
            try
            {
                await action(cancellationToken);
            }
            catch (Exception ex) // spec 0013 SND-3: an after commit action never fails a commit that happened
            {
                logger.Value.AfterCommitActionFailed(ex);
            }
        }
    }
}
