using Microsoft.Extensions.Caching.Hybrid;

using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Caching;

/// <summary>
/// Removes received cache tags on every instance, the writer included (spec 0013 CAC-4). <c>["*"]</c> clears the
/// whole cache through HybridCache's reserved tag (CAC-6).
/// </summary>
internal sealed class CacheSignalHandler(
    HybridCache cache,
    InfrastructureTelemetry telemetry) : ISignalHandler
{
    public SignalKind Kind => SignalKind.Cache;

    public async ValueTask HandleAsync(IReadOnlyList<string> values, CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync(values, cancellationToken);
        if (values.Contains(SignalPayload.Everything, StringComparer.Ordinal))
        {
            telemetry.CacheCleared(InfrastructureTelemetry.ClearOnWildcard);
        }
    }
}
