using Microsoft.Extensions.Logging;

namespace Orvano.SharedKernel.Access;

// The access layer runs in the host's pipeline, so its events take IDs from the host's block (spec 0002 LOG-1).
internal static partial class AccessLog
{
    [LoggerMessage(EventId = LogEventIds.Host + 40, Level = LogLevel.Information,
        Message = "Credential refused by {Resolver}: {Reason}")]
    public static partial void CredentialRefused(this ILogger logger, string resolver, string reason);

    [LoggerMessage(EventId = LogEventIds.Host + 41, Level = LogLevel.Information,
        Message = "Credential refused: the {Kind} caller's project does not match the route's project")]
    public static partial void ProjectMismatch(this ILogger logger, CallerKind kind);

    [LoggerMessage(EventId = LogEventIds.Host + 42, Level = LogLevel.Error,
        Message = "Endpoint {Endpoint} refused with 404 because its access declaration is broken: {Problem}")]
    public static partial void EndpointUndeclared(this ILogger logger, string endpoint, string problem);
}
