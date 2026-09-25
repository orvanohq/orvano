namespace Orvano.Core.Notifications;

/// <summary>An auto reset wake up: many Set calls before a wait collapse into one wake.</summary>
public sealed class WakeSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    /// <summary>Wakes the next or current waiter. Safe to call any number of times.</summary>
    public void Set()
    {
        try { _semaphore.Release(); }
        catch (SemaphoreFullException) { }
    }

    /// <summary>Returns when set or after <paramref name="timeout"/>; throws only when cancelled.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct) => await _semaphore.WaitAsync(timeout, ct);
}

/// <summary>Worker wake ups fed by the LISTEN connection.</summary>
public sealed class WakeSignals
{
    /// <summary>Set on each <see cref="Events.Outbox.Channel"/> notification; wakes the event dispatcher.</summary>
    public WakeSignal Events { get; } = new();

    /// <summary>Set on each <see cref="Jobs.JobQueue.Channel"/> notification; wakes the job loop.</summary>
    public WakeSignal Jobs { get; } = new();
}
