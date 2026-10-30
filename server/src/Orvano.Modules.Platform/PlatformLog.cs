using Microsoft.Extensions.Logging;

using Orvano.SharedKernel;

namespace Orvano.Modules.Platform;

/// <summary>The platform module's log messages, in its event ID block (spec 0002 LOG-1).</summary>
internal static partial class PlatformLog
{
    [LoggerMessage(EventId = LogEventIds.Platform + 1, Level = LogLevel.Warning,
        Message = "platform.instance does not exist yet; run orvano migrate")]
    public static partial void InstanceNotMigrated(this ILogger logger);
}
