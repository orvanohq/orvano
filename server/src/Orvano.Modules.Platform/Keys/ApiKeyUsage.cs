using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Keys;

/// <summary>
/// Spec 0017 AKY-11: <c>last_used_at</c> at most once a minute per key (KEY-3). This dictionary skips the database, and
/// the conditional update covers other replicas. It doesn't touch <c>updated_at</c>, and a failed write only logs.
/// </summary>
internal sealed class ApiKeyUsage(TimeProvider clock, ILogger<ApiKeyUsage> logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _written = new();

    public async Task RecordAsync(PlatformDbContext db, Guid keyId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        if (_written.TryGetValue(keyId, out DateTimeOffset last) && now - last < Interval)
        {
            return;
        }

        _written[keyId] = now;
        DateTimeOffset stale = now - Interval;
        try
        {
            // Deviation DAT-2a: FLT-1's API key lookup; the access layer hasn't set the project yet.
            await db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name])
                .Where(x => x.Id == keyId && (x.LastUsedAt == null || x.LastUsedAt <= stale))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastUsedAt, now), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.KeyUsageNotWritten(keyId, ex.GetType().Name);
        }
    }
}
