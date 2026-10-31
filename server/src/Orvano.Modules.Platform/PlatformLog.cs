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

    // Spec 0017 AUS-3: the exception's type only, since a message could hold what the event held.
    [LoggerMessage(EventId = LogEventIds.Platform + 3, Level = LogLevel.Warning, Message = "Audit event {Action} was not recorded: {ExceptionType}")]
    public static partial void AuditNotRecorded(this ILogger logger, string action, string exceptionType);

    // Spec 0017 AKY-11: a failed write never fails the request.
    [LoggerMessage(EventId = LogEventIds.Platform + 4, Level = LogLevel.Warning, Message = "last_used_at was not written for API key {KeyId}: {ExceptionType}")]
    public static partial void KeyUsageNotWritten(this ILogger logger, Guid keyId, string exceptionType);
}
