using Orvano.SharedKernel.Signals;

namespace Orvano.SharedKernel.Data;

/// <summary>
/// The signals and after commit actions one <c>DbContext</c>'s current transaction will send (spec 0013 SND-1, SND-4):
/// per kind, distinct values in first enlisted order. A scoped EF internal service, so one per context instance.
/// </summary>
internal sealed class PendingSignals
{
    private readonly List<SignalKind> _kinds = [];
    private readonly Dictionary<SignalKind, (List<string> Values, HashSet<string> Seen)> _values = [];
    private readonly List<Func<CancellationToken, ValueTask>> _afterCommit = [];

    /// <summary>True when there is nothing to send and nothing to run after the commit.</summary>
    public bool IsEmpty => _kinds.Count == 0 && _afterCommit.Count == 0;

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

    public void AddAfterCommit(Func<CancellationToken, ValueTask> action) => _afterCommit.Add(action);

    /// <summary>Runs the after commit actions in the order they were added. Each catches its own exceptions (SND-3).</summary>
    public async Task RunAfterCommitAsync(CancellationToken cancellationToken)
    {
        foreach (Func<CancellationToken, ValueTask> action in _afterCommit.ToArray())
        {
            await action(cancellationToken);
        }
    }

    public void Clear()
    {
        _kinds.Clear();
        _values.Clear();
        _afterCommit.Clear();
    }
}
