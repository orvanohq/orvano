using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orvano.SharedKernel.Signals;

/// <summary>The two hook points later features plug into (spec 0013 BUS-5).</summary>
public static class SignalServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="T"/> as a singleton signal handler. Handlers of a kind run in registration order.
    /// A handler that needs a database creates a scope through <c>IServiceScopeFactory</c>.
    /// </summary>
    public static IServiceCollection AddSignalHandler<T>(this IServiceCollection services)
        where T : class, ISignalHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalHandler, T>());
        return services;
    }

    /// <summary>Registers <typeparamref name="T"/> as a singleton connection observer. Observers run in registration order.</summary>
    public static IServiceCollection AddSignalConnectionObserver<T>(this IServiceCollection services)
        where T : class, ISignalConnectionObserver
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISignalConnectionObserver, T>());
        return services;
    }
}
