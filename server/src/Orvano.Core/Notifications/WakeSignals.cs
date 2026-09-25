namespace Orvano.Core.Notifications;

/// <summary>An auto reset wake up: many Set calls before a wait collapse into one wake.</summary>
public sealed class WakeSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

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
    public WakeSignal Events { get; } = new();
    public WakeSignal Jobs { get; } = new();
}
