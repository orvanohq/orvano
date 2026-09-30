namespace Orvano.Core.Jobs;

/// <summary>
/// Thrown by a job handler that knows when the next attempt should run. The job is retried after
/// <see cref="Delay"/> in place of the default exponential backoff, and still goes <c>dead</c> once its attempts
/// run out.
/// </summary>
/// <param name="delay">How long to wait before the next attempt.</param>
/// <param name="message">What failed. It is stored as the job's last error, so keep secrets and personal data out.</param>
/// <param name="innerException">The cause, if any.</param>
public sealed class JobRetryException(TimeSpan delay, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>How long to wait before the next attempt. Never negative.</summary>
    public TimeSpan Delay { get; } = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
}
