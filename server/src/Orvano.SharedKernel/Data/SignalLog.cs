using Microsoft.Extensions.Logging;

namespace Orvano.SharedKernel.Data;

// Spec 0013 SND-3. Shared kernel logs take the host block, like AccessLog.
internal static partial class SignalLog
{
    [LoggerMessage(EventId = LogEventIds.Host + 50, Level = LogLevel.Error,
        Message = "An after commit action threw; the commit stands and the remaining actions still run")]
    public static partial void AfterCommitActionFailed(this ILogger logger, Exception exception);
}
