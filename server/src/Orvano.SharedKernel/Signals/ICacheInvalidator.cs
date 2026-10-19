using Microsoft.EntityFrameworkCore;

namespace Orvano.SharedKernel.Signals;

/// <summary>
/// Removes cache tags on every instance once the write that changed them commits (spec 0013 BUS-3, CAC-3). The only
/// way modules invalidate the cache: <c>HybridCache.RemoveAsync</c> and <c>RemoveByTagAsync</c> are banned in modules.
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>
    /// Enlists <paramref name="tags"/> as a <c>cache</c> signal on <paramref name="context"/>'s open transaction. This
    /// instance removes them right after the commit, every other instance when the signal arrives. <c>"*"</c> means
    /// every entry.
    /// </summary>
    /// <exception cref="InvalidOperationException">The context has no open transaction, or is not a platform database context.</exception>
    /// <exception cref="ArgumentException">A tag is empty, too long, or not printable ASCII.</exception>
    void Invalidate(DbContext context, IEnumerable<string> tags);

    /// <inheritdoc cref="Invalidate(DbContext, IEnumerable{string})"/>
    void Invalidate(DbContext context, params string[] tags);
}
