using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Orvano.Infrastructure.Caching;
using Orvano.Infrastructure.Signals;
using Orvano.Infrastructure.Tls;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure;

/// <summary>Registers what <c>Orvano.Infrastructure</c> owns (spec 0007 REP-2), one call from <c>Orvano.Host</c>.</summary>
public static class InfrastructureServices
{
    /// <summary>The key of the <c>NpgsqlDataSource</c> the signal listener opens its connection from (spec 0013 LSN-1).</summary>
    public const string SignalDataSourceKey = "signals";

    /// <summary>The default expiry of a cache entry that sets none, the security cap (spec 0013 CAC-1, spec 0007 CCH-2).</summary>
    public static readonly TimeSpan DefaultCacheExpiration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// TLS options, the cache, the cache invalidator, the signal bus, and a connection state that stays disconnected
    /// until <see cref="AddSignalListener"/> replaces it (spec 0013 LSN-1). Every command calls this.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<TlsOptions>().BindConfiguration(TlsOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TlsOptions>, TlsOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();
        services.TryAddSingleton<InfrastructureTelemetry>();

        services.AddOptions<SignalOptions>().BindConfiguration(SignalOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<SignalOptions>, SignalOptionsValidator>();
        services.TryAddSingleton<ISignalConnectionState, NoSignalListener>();
        services.TryAddSingleton<SignalBus>();
        services.TryAddSingleton<ISignalBus>(provider => provider.GetRequiredService<SignalBus>());
        services.TryAddSingleton<IHeartbeatSender, NotifyHeartbeatSender>();

        AddCache(services);
        services.TryAddSingleton<ICacheInvalidator>(
            provider => ActivatorUtilities.CreateInstance<CacheInvalidator>(provider, SignalAwareHybridCache.Inner(provider)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalHandler, CacheSignalHandler>(
            provider => ActivatorUtilities.CreateInstance<CacheSignalHandler>(provider, SignalAwareHybridCache.Inner(provider))));
        return services;
    }

    /// <summary>The signal listener (spec 0013 LSN-1). Only <c>serve</c> calls this, in every role.</summary>
    public static IServiceCollection AddSignalListener(this IServiceCollection services)
    {
        services.TryAddSingleton(provider => ActivatorUtilities.CreateInstance<SignalListener>(provider, SignalAwareHybridCache.Inner(provider)));
        services.AddHostedService(provider => provider.GetRequiredService<SignalListener>());
        services.Replace(ServiceDescriptor.Singleton<ISignalConnectionState>(provider => provider.GetRequiredService<SignalListener>()));
        return services;
    }

    /// <summary>
    /// CAC-1: HybridCache with the memory level only, its default registration moved to a keyed service, and the
    /// signal aware decorator as the <see cref="HybridCache"/> everything else injects.
    /// </summary>
    private static void AddCache(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(HybridCache) && descriptor.IsKeyedService))
        {
            return;
        }

        services.AddHybridCache(options => options.DefaultEntryOptions = new HybridCacheEntryOptions
        {
            Expiration = DefaultCacheExpiration,
            LocalCacheExpiration = DefaultCacheExpiration,
        });

        ServiceDescriptor inner = services.Last(descriptor => descriptor.ServiceType == typeof(HybridCache) && !descriptor.IsKeyedService);
        services.Remove(inner);
        services.Add(inner switch
        {
            { ImplementationType: { } type } => ServiceDescriptor.KeyedSingleton(typeof(HybridCache), SignalAwareHybridCache.InnerKey, type),
            { ImplementationInstance: HybridCache instance } => ServiceDescriptor.KeyedSingleton(SignalAwareHybridCache.InnerKey, instance),
            { ImplementationFactory: { } factory } => ServiceDescriptor.KeyedSingleton<HybridCache>(
                SignalAwareHybridCache.InnerKey, (provider, _) => (HybridCache)factory(provider)),
            _ => throw new InvalidOperationException(
                "AddHybridCache registered HybridCache in a way the signal aware decorator cannot wrap (spec 0013 CAC-1)."),
        });
        services.AddSingleton<HybridCache>(
            provider => ActivatorUtilities.CreateInstance<SignalAwareHybridCache>(provider, SignalAwareHybridCache.Inner(provider)));
    }
}
