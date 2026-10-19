using Microsoft.Extensions.DependencyInjection;

namespace Orvano.SharedKernel.Access;

/// <summary>The caller of the current request, set once by the access layer.</summary>
public interface ICallerContext
{
    /// <summary>Throws when read before the access layer ran, so a route without it fails loudly.</summary>
    Caller Current { get; }
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
    /// <summary>Registers the per request caller and project contexts.</summary>
    public static IServiceCollection AddAccessContext(this IServiceCollection services)
    {
        services.AddScoped<AccessContext>();
        services.AddScoped<ICallerContext>(provider => provider.GetRequiredService<AccessContext>());
        services.AddScoped<IProjectContext>(provider => provider.GetRequiredService<AccessContext>());
        return services;
    }
}
