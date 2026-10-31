using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Orvano.SharedKernel.Access;

/// <summary>The caller of the current request, set once by the access layer.</summary>
public interface ICallerContext
{
    /// <summary>Throws when read before the access layer ran, so a route without it fails loudly.</summary>
    Caller Current { get; }

    /// <summary>The caller, or null on a public endpoint (spec 0017 SES-7's check needs to ask, and <see cref="Current"/> throws).</summary>
    Caller? Established { get; }
}

/// <summary>
/// The current project (spec 0002 API-9). Only the access layer sets it, after the project check passes (spec 0012
/// PRJ-3); the <c>"project"</c> query filter reads it (DAT-2a).
/// </summary>
public interface IProjectContext
{
    /// <summary>Throws on a route that has no project, or before the access layer set it.</summary>
    Guid Id { get; }
}

/// <summary>
/// LBL-2: an end user's current labels. The Auth module implements it with HybridCache (feature 14).
/// </summary>
public interface ICallerRolesSource
{
    ValueTask<IReadOnlyList<string>> GetLabelsAsync(Guid projectId, Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// The one implementation of both contexts. Its setter is internal to this assembly, so nothing outside the access
/// layer can set the project (PRJ-3); an architecture test keeps it that way.
/// </summary>
internal sealed class AccessContext : ICallerContext, IProjectContext
{
    private Caller? _caller;
    private Guid? _projectId;

    public Caller Current => _caller ?? throw new InvalidOperationException(
        "The caller is not known yet: the access layer has not run for this request.");

    public Caller? Established => _caller;

    public Guid Id => _projectId ?? throw new InvalidOperationException(
        "This request has no project: the route has none, or the access layer has not set it.");

    /// <summary>Sets the caller, and the project once its check passed. Each may be set once per request.</summary>
    internal void Establish(Caller caller, Guid? projectId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (_caller is not null && !ReferenceEquals(_caller, caller))
        {
            throw new InvalidOperationException("The caller of a request is set once.");
        }

        if (projectId is not null && _projectId is not null && _projectId != projectId)
        {
            throw new InvalidOperationException("The project of a request is set once.");
        }

        _caller = caller;
        _projectId ??= projectId;
    }
}

public static class AccessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the per request caller and project contexts, and a startup check that builds every endpoint and fails
    /// startup naming each one whose access declaration breaks spec 0012 EPT-3 (EPT-4).
    /// </summary>
    public static IServiceCollection AddAccessContext(this IServiceCollection services)
    {
        services.AddHostedService<AccessEndpointCheck>();
        services.AddScoped<AccessContext>();
        services.AddScoped<ICallerContext>(provider => provider.GetRequiredService<AccessContext>());
        services.AddScoped<IProjectContext>(provider => provider.GetRequiredService<AccessContext>());
        return services;
    }

    /// <summary>
    /// Registers a credential resolver (spec 0012 RSV-1), scoped. The access layer middleware runs every registered
    /// resolver in <see cref="ICallerResolver.Order"/>; registering the same type twice adds it once.
    /// </summary>
    public static IServiceCollection AddCallerResolver<T>(this IServiceCollection services)
        where T : class, ICallerResolver
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICallerResolver, T>());
        return services;
    }
}

/// <summary>
/// The startup check found endpoints that break spec 0012 EPT-3; the message names each one. <c>orvano serve</c> prints
/// it and exits 1.
/// </summary>
public sealed class AccessDeclarationException(string message) : InvalidOperationException(message);

// EPT-4 (1): every endpoint of the host declares its access, or the host does not start. Hosted services start before
// the web host wires the app's endpoints, so the check runs in StartedAsync, once they exist; until then the access
// layer answers 404 for any undeclared endpoint (EPT-4 (3)).
internal sealed class AccessEndpointCheck(IServiceProvider services) : IHostedLifecycleService
{
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        string[] problems = [.. services.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(endpoint => AccessDeclaration.Problem(endpoint) is { } problem ? $"{AccessDeclaration.Name(endpoint)}: {problem}" : null)
            .OfType<string>()];
        if (problems.Length > 0)
        {
            throw new AccessDeclarationException(
                "Every endpoint must declare its access (spec 0012 EPT-3). Fix these endpoints:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(problem => "  " + problem)));
        }

        return Task.CompletedTask;
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
