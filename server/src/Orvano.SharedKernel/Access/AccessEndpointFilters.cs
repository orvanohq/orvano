using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Orvano.SharedKernel.Access;

/// <summary>
/// CPL-3: the membership facts a console permission check needs. Feature 13 implements it over
/// <c>platform.membership</c> and <c>platform.project</c>.
/// </summary>
public interface IMembershipLookup
{
    /// <summary>The operator's <c>platform.membership.role</c> in the organization, or null when not a member.</summary>
    Task<string?> FindRoleAsync(Guid operatorId, Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The organization the project belongs to, or null when there is no such project.</summary>
    Task<Guid?> FindProjectOrganizationAsync(Guid projectId, CancellationToken cancellationToken);
}

/// <summary>
/// How endpoints declare their access (spec 0012 SCP-5, CPL-3, EPT-2). Each method adds endpoint metadata, on a
/// single endpoint or a <c>MapGroup</c>, and <see cref="AccessRequirementsMiddleware.UseAccessRequirements"/> enforces
/// it before binding and validation (DEN-5). Every endpoint needs one kind of requirement or
/// <see cref="AllowPublicAccess{TBuilder}"/>, never both, or the host will not start (EPT-3, EPT-4).
/// </summary>
public static class AccessEndpointFilters
{
    /// <summary>The route values the console permission check reads.</summary>
    public const string OrganizationIdRouteValue = "organizationId";

    public const string ProjectIdRouteValue = "projectId";

    /// <summary>A server route: only secret and run keys holding <paramref name="scope"/> pass (SCP-5, DEN-4).</summary>
    public static TBuilder RequireScope<TBuilder>(this TBuilder builder, string scope)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Scopes.Secret.Contains(scope))
        {
            throw new ArgumentException($"'{scope}' is not a secret key scope (SCP-1).", nameof(scope));
        }

        return builder.Require(new ScopeRequirement(scope), StatusCodes.Status401Unauthorized, StatusCodes.Status403Forbidden);
    }

    /// <summary>A client route: only guests and end users whose publishable key has <paramref name="clientApi"/> on.</summary>
    public static TBuilder RequireClientApi<TBuilder>(this TBuilder builder, string clientApi)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Scopes.Publishable.Contains(clientApi))
        {
            throw new ArgumentException($"'{clientApi}' is not a client API scope (SCP-2).", nameof(clientApi));
        }

        return builder.Require(new ClientApiRequirement(clientApi), StatusCodes.Status401Unauthorized, StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// A control plane route (CPL-3). The organization comes from the route, or from the project in the route. No
    /// membership, an unknown project, or a project outside the organization answers 404; a member without
    /// <paramref name="permission"/> gets 403. A project that passes becomes the request's project (PRJ-3). A route
    /// pattern with neither route value fails startup (EPT-4).
    /// </summary>
    public static TBuilder RequireConsolePermission<TBuilder>(this TBuilder builder, ConsolePermission permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Require(
            new ConsolePermissionRequirement(permission),
            StatusCodes.Status401Unauthorized,
            StatusCodes.Status403Forbidden,
            StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// EPT-5: the endpoint runs with no caller. Resolvers do not run, a credential sent with it is ignored, and
    /// <see cref="ICallerContext.Current"/> throws if read. Serve nothing that depends on a project or a caller, and
    /// say in a comment why the endpoint is public.
    /// </summary>
    public static TBuilder AllowPublicAccess<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new PublicAccess());
    }

    // The filter is only a guard: it fails closed when the middleware that enforces the requirement is missing. Builders
    // whose endpoints take no filters (MapHealthChecks) ignore it.
    private static TBuilder Require<TBuilder>(this TBuilder builder, AccessRequirement requirement, params int[] statuses)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(requirement);
        foreach (int status in statuses)
        {
            builder.WithMetadata(new ProducesResponseTypeMetadata(status, typeof(ProblemDetails), ["application/problem+json"]));
        }

        return builder.AddEndpointFilter((context, next) => context.HttpContext.Items.ContainsKey(AccessRequirementsMiddleware.CheckedKey)
            ? next(context)
            : throw new InvalidOperationException(
                "This endpoint declares an access requirement, but UseAccessRequirements is not in the pipeline."));
    }
}
