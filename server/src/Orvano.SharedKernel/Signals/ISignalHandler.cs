namespace Orvano.SharedKernel.Signals;

/// <summary>
/// Receives every signal of one kind on this instance (spec 0013 BUS-4, HND-1, HND-2). Register it with
/// <c>services.AddSignalHandler&lt;T&gt;()</c>. Handlers share one loop, so keep them quick or hand the work off (HND-4).
/// </summary>
public interface ISignalHandler
{
    /// <summary>The kind this handler receives.</summary>
    SignalKind Kind { get; }

    /// <summary>Handles the values as sent, including <c>["*"]</c>.</summary>
    ValueTask HandleAsync(IReadOnlyList<string> values, CancellationToken cancellationToken);
}
