using System.ComponentModel.DataAnnotations;

namespace Orvano.Infrastructure.Signals;

/// <summary>
/// Timing of the signal listener (spec 0013 SOP-1, spec 0007 SIG-2). Operators normally leave these alone; tests
/// shorten them.
/// </summary>
public sealed class SignalOptions : IValidatableObject
{
    public const string Section = "Signals";

    private static readonly TimeSpan Minimum = TimeSpan.FromSeconds(1);

    /// <summary>How often each instance sends a heartbeat on the signal channel.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Silence on the channel after which the instance counts as disconnected. At least twice the interval.</summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>The longest wait between reconnect attempts.</summary>
    public TimeSpan ReconnectBackoffCap { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often connection observers poll the database while disconnected.</summary>
    public TimeSpan FallbackPollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest one signal handler or connection observer call may run before it is cancelled and the dispatch loop
    /// moves on (spec 0013 LSN-6). At most the heartbeat timeout, so one call alone never trips the lag rule (LSN-8).
    /// </summary>
    public TimeSpan HandlerTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach ((string name, TimeSpan value) in new[]
        {
            (nameof(HeartbeatInterval), HeartbeatInterval),
            (nameof(HeartbeatTimeout), HeartbeatTimeout),
            (nameof(ReconnectBackoffCap), ReconnectBackoffCap),
            (nameof(FallbackPollInterval), FallbackPollInterval),
            (nameof(HandlerTimeout), HandlerTimeout),
        })
        {
            if (value < Minimum)
            {
                yield return new ValidationResult("must be at least 1 second (00:00:01)", [name]);
            }
        }

        if (HeartbeatTimeout < HeartbeatInterval * 2)
        {
            yield return new ValidationResult(
                "must be at least twice ORVANO_SIGNALS__HEARTBEATINTERVAL", [nameof(HeartbeatTimeout)]);
        }

        if (HandlerTimeout > HeartbeatTimeout)
        {
            yield return new ValidationResult(
                "must be at most ORVANO_SIGNALS__HEARTBEATTIMEOUT", [nameof(HandlerTimeout)]);
        }
    }
}
