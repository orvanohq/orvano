using Microsoft.Extensions.Logging;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure;

// Spec 0013 LSN-7. Payload values hold user ids, so they appear only at Debug (security model).
internal static partial class InfrastructureLog
{
    [LoggerMessage(EventId = LogEventIds.Infrastructure + 1, Level = LogLevel.Information,
        Message = "Signal listener connected to channel {Channel}")]
    public static partial void SignalsConnected(this ILogger logger, string channel);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 2, Level = LogLevel.Warning,
        Message = "Signal listener disconnected ({Reason}); the cache is bypassed until it reconnects")]
    public static partial void SignalsDisconnected(this ILogger logger, string reason, Exception? exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 3, Level = LogLevel.Warning,
        Message = "Signal listener could not connect (attempt {Attempt}); next attempt in {Delay}")]
    public static partial void SignalsConnectFailed(this ILogger logger, int attempt, TimeSpan delay, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 4, Level = LogLevel.Warning,
        Message = "Ignored a malformed signal payload")]
    public static partial void SignalMalformed(this ILogger logger);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 5, Level = LogLevel.Warning,
        Message = "Ignored a signal of unknown kind {Kind}")]
    public static partial void SignalKindUnknown(this ILogger logger, string kind);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 6, Level = LogLevel.Debug,
        Message = "Ignored a {Kind} signal with {Count} values")]
    public static partial void SignalIgnored(this ILogger logger, SignalKind kind, int count);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 7, Level = LogLevel.Debug,
        Message = "Received a {Kind} signal: {Values}")]
    public static partial void SignalReceived(this ILogger logger, SignalKind kind, string values);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 8, Level = LogLevel.Error,
        Message = "Signal handler {Handler} failed on a {Kind} signal")]
    public static partial void SignalHandlerFailed(this ILogger logger, string handler, SignalKind kind, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 9, Level = LogLevel.Error,
        Message = "Signal connection observer {Observer} failed in {Callback}")]
    public static partial void SignalObserverFailed(this ILogger logger, string observer, string callback, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 10, Level = LogLevel.Warning,
        Message = "Signal dispatch queue holds {Count} items; a handler or observer is slow")]
    public static partial void SignalQueueLong(this ILogger logger, int count);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 11, Level = LogLevel.Error,
        Message = "Removing cache tags after a commit failed; the entries expire on their own")]
    public static partial void CacheRemovalFailed(this ILogger logger, Exception exception);
}
