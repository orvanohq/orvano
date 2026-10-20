using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Caching;

/// <summary>
/// Enlists cache tags as a <c>cache</c> signal and removes them from this instance once the transaction commits
/// (spec 0013 BUS-3, CAC-3). Nothing is removed before the commit: a reader could refill the entry from the old,
/// still committed row.
/// </summary>
internal sealed class CacheInvalidator(
    SignalBus bus,
    HybridCache cache,
    InfrastructureTelemetry telemetry,
    ILogger<CacheInvalidator> logger) : ICacheInvalidator
{
    public void Invalidate(DbContext context, IEnumerable<string> tags)
    {
        // One removal per transaction, for every tag enlisted by then, added on the first cache enlist.
        if (bus.Add(context, SignalKind.Cache, tags))
        {
            TransactionSignals.AddAfterCommit(context, cancellationToken => RemoveAsync(context, cancellationToken));
        }
    }

    public void Invalidate(DbContext context, params string[] tags) => Invalidate(context, (IEnumerable<string>)tags);

    private async ValueTask RemoveAsync(DbContext context, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string> tags = SignalPayload.WireValues(SignalKind.Cache, TransactionSignals.Enlisted(context, SignalKind.Cache));
            await cache.RemoveByTagAsync(tags, cancellationToken);
            if (tags.Contains(SignalPayload.Everything, StringComparer.Ordinal))
            {
                telemetry.CacheCleared(InfrastructureTelemetry.ClearOnWildcard);
            }
        }
        catch (Exception ex) // spec 0013 SND-3: an after commit action catches its own errors; it never fails a commit that happened
        {
            logger.CacheRemovalFailed(ex);
        }
    }
}
