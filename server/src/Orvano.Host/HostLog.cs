using Orvano.Host.Configuration;
using Orvano.SharedKernel;

namespace Orvano.Host;

internal static partial class HostLog
{
    [LoggerMessage(EventId = LogEventIds.Host + 1, Level = LogLevel.Information,
        Message = "Orvano {Version} starting with roles {Roles}")]
    public static partial void ServerStarting(this ILogger logger, string version, ServerRoles roles);

    [LoggerMessage(EventId = LogEventIds.Host + 2, Level = LogLevel.Error, Message = "Startup failed: {Reason}")]
    public static partial void StartupFailed(this ILogger logger, string reason);

    [LoggerMessage(EventId = LogEventIds.Host + 10, Level = LogLevel.Information,
        Message = "Applying migrations for {Context}")]
    public static partial void MigrationsApplying(this ILogger logger, string context);

    [LoggerMessage(EventId = LogEventIds.Host + 11, Level = LogLevel.Information,
        Message = "Migrations complete: {Count} schemas up to date")]
    public static partial void MigrationsComplete(this ILogger logger, int count);

    [LoggerMessage(EventId = LogEventIds.Host + 20, Level = LogLevel.Error, Message = "Unhandled exception")]
    public static partial void UnhandledException(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Host + 21, Level = LogLevel.Error, Message = "Request timed out on the server")]
    public static partial void ServerTimeout(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Host + 22, Level = LogLevel.Debug, Message = "Request cancelled by the caller")]
    public static partial void RequestAborted(this ILogger logger);

    [LoggerMessage(EventId = LogEventIds.Host + 23, Level = LogLevel.Debug, Message = "Request could not be bound: {Status}")]
    public static partial void BadRequest(this ILogger logger, int status);
}
