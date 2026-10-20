using System.Threading.Channels;

using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Jobs;

/// <summary>
/// The wakes every worker loop shares (spec 0013 WAK-1, WAK-2, WAK-5): the <c>job</c> signal, the poll, a signal
/// reconnect, and a claim that may have left more due. Each subscriber gets its own channel of capacity 1, so any
/// number of wakes between two of its passes coalesce into one. The job runner is the first subscriber; the outbox
/// dispatcher (milestone 3) subscribes the same way and needs no listener code of its own.
/// </summary>
internal sealed class WorkerWake
{
    private readonly Lock _lock = new();
    private readonly List<Subscription> _subscriptions = [];

    public Subscription Subscribe()
    {
        var subscription = new Subscription();
        lock (_lock)
        {
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    /// <summary>Wakes every subscriber: a <c>job</c> signal, a reconnect, or a claim that filled every slot it asked for.</summary>
    public void Wake()
    {
        foreach (Subscription subscription in Snapshot())
        {
            subscription.Wake(poll: false);
        }
    }

    /// <summary>Wakes every subscriber for a poll tick, which a subscriber can tell apart (the runner fires schedules on it).</summary>
    public void Poll()
    {
        foreach (Subscription subscription in Snapshot())
        {
            subscription.Wake(poll: true);
        }
    }

    private Subscription[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _subscriptions];
        }
    }

    /// <summary>One loop's wakes.</summary>
    public sealed class Subscription
    {
        private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

        private int _pollDue;

        /// <summary>Waits for the next wake; wakes since the last call are one.</summary>
        public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) => _channel.Reader.WaitToReadAsync(cancellationToken);

        /// <summary>Takes the pending wake, and returns whether a poll tick was among the wakes it stands for.</summary>
        public bool Take()
        {
            _channel.Reader.TryRead(out _);
            return Interlocked.Exchange(ref _pollDue, 0) == 1;
        }

        internal void Wake(bool poll)
        {
            if (poll)
            {
                Interlocked.Exchange(ref _pollDue, 1);
            }

            _channel.Writer.TryWrite(true);
        }
    }
}

/// <summary>WAK-2 (1): a <c>job</c> signal only wakes the workers; the claim runs on their own loops (HND-4).</summary>
internal sealed class JobSignalHandler(WorkerWake wake) : ISignalHandler
{
    public SignalKind Kind => SignalKind.Job;

    public ValueTask HandleAsync(IReadOnlyList<string> values, CancellationToken cancellationToken)
    {
        wake.Wake();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// WAK-2 (3): signals sent while the listener was away are lost, so a reconnect claims at once (HND-3). No fallback poll
/// of its own: the runner's poll is always on (WAK-3).
/// </summary>
internal sealed class JobConnectionObserver(WorkerWake wake) : ISignalConnectionObserver
{
    public ValueTask OnConnectedAsync(CancellationToken cancellationToken)
    {
        wake.Wake();
        return ValueTask.CompletedTask;
    }
}
