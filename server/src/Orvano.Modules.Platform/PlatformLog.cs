using Microsoft.Extensions.Logging;

using Orvano.SharedKernel;

namespace Orvano.Modules.Platform;

/// <summary>The platform module's log messages, in its event ID block (spec 0002 LOG-1).</summary>
internal static partial class PlatformLog
{
    [LoggerMessage(EventId = LogEventIds.Platform + 1, Level = LogLevel.Warning,
        Message = "platform.instance does not exist yet; run orvano migrate")]
    public static partial void InstanceNotMigrated(this ILogger logger);

    // Spec 0016 STP-3: never the token. The exception's type and message come from the database or EF, which never
    // hold parameter values (sensitive data logging stays off).
    [LoggerMessage(EventId = LogEventIds.Platform + 2, Level = LogLevel.Warning,
        Message = "No setup token was issued at start ({ExceptionType}: {Reason}); run orvano admin setup-token once the database is ready")]
    public static partial void SetupTokenNotIssued(this ILogger logger, string exceptionType, string reason);
}
