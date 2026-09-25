namespace Orvano.Core.Jobs;

/// <summary>
/// Thrown by a job handler when retrying can never help. The job goes <c>dead</c> at once, no matter how many
/// attempts are left; every other exception keeps the normal retry and backoff.
/// </summary>
public sealed class PermanentJobFailureException(string message, Exception? innerException = null)
    : Exception(message, innerException);
