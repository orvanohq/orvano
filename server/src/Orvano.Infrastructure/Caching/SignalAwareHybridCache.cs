using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Caching;

/// <summary>
/// The <see cref="HybridCache"/> modules inject (spec 0013 CAC-1, CAC-2). While this instance might miss a signal
/// (before the first connect, while disconnected, or with no listener) every read and write skips the local cache,
/// so a module never serves an entry another instance has invalidated. Removals always pass through.
/// </summary>
internal sealed class SignalAwareHybridCache(
    HybridCache inner,
    ISignalConnectionState connection,
    IOptions<HybridCacheOptions> options) : HybridCache
{
    /// <summary>
    /// The key of the default <see cref="HybridCache"/> this decorates (spec 0013 CAC-1). An object, not a string, so a
    /// module cannot name it in <c>[FromKeyedServices]</c> and reach the cache without the bypass.
    /// </summary>
    internal static readonly object InnerKey = new();

    /// <summary>The undecorated cache, for Infrastructure's own consumers: the invalidator, the cache handler, the listener.</summary>
    internal static HybridCache Inner(IServiceProvider services) => services.GetRequiredKeyedService<HybridCache>(InnerKey);

    private const HybridCacheEntryFlags Bypass = HybridCacheEntryFlags.DisableLocalCacheRead | HybridCacheEntryFlags.DisableLocalCacheWrite;

    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        inner.GetOrCreateAsync(key, state, factory, Effective(options), tags, cancellationToken);

    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        inner.SetAsync(key, value, Effective(options), tags, cancellationToken);

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        inner.RemoveAsync(key, cancellationToken);

    public override ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        inner.RemoveAsync(keys, cancellationToken);

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        inner.RemoveByTagAsync(tag, cancellationToken);

    public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default) =>
        inner.RemoveByTagAsync(tags, cancellationToken);

    /// <summary>The caller's options when connected; else the same expiry with the bypass flags added (CAC-2).</summary>
    private HybridCacheEntryOptions? Effective(HybridCacheEntryOptions? entry)
    {
        if (connection.IsConnected)
        {
            return entry;
        }

        HybridCacheEntryFlags flags = entry?.Flags ?? options.Value.DefaultEntryOptions?.Flags ?? HybridCacheEntryFlags.None;
        return new HybridCacheEntryOptions
        {
            Expiration = entry?.Expiration,
            LocalCacheExpiration = entry?.LocalCacheExpiration,
            Flags = flags | Bypass,
        };
    }
}
