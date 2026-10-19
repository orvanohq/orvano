namespace Orvano.SharedKernel.Signals;

/// <summary>
/// Hears when signals may have been lost and when they flow again (spec 0013 BUS-4, HND-3). Register it with
/// <c>services.AddSignalConnectionObserver&lt;T&gt;()</c>. The listener owns the timing; observers never run timers.
/// </summary>
public interface ISignalConnectionObserver
{
    /// <summary>Signals flow again. Anything sent before now may have been lost, so resync in full.</summary>
    ValueTask OnConnectedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Signals may be lost from now on.</summary>
    ValueTask OnDisconnectedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// While disconnected: poll the database for what signals would have said. Runs right after a disconnect, then
    /// every <c>ORVANO_SIGNALS__FALLBACKPOLLINTERVAL</c> until the listener connects again.
    /// </summary>
    ValueTask OnFallbackPollAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
