using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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
/// How endpoints declare what they need (spec 0012 SCP-5, CPL-3). Each method adds the requirement as endpoint
/// metadata, and <see cref="AccessRequirementsMiddleware.UseAccessRequirements"/> enforces it before binding and
/// validation (DEN-5). The answers follow DEN-4 and CPL-3.
/// </summary>
public static class AccessEndpointFilters
{
    /// <summary>The route values the console permission check reads.</summary>
    public const string OrganizationIdRouteValue = "organizationId";

    public const string ProjectIdRouteValue = "projectId";

    /// <summary>A server route: only secret and run keys holding <paramref name="scope"/> pass (SCP-5, DEN-4).</summary>
    public static RouteHandlerBuilder RequireScope(this RouteHandlerBuilder builder, string scope)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Scopes.Secret.Contains(scope))
        {
            throw new ArgumentException($"'{scope}' is not a secret key scope (SCP-1).", nameof(scope));
        }

        return builder.Require(new ScopeRequirement(scope)).ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <summary>A client route: only guests and end users whose publishable key has <paramref name="clientApi"/> on.</summary>
    public static RouteHandlerBuilder RequireClientApi(this RouteHandlerBuilder builder, string clientApi)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Scopes.Publishable.Contains(clientApi))
        {
            throw new ArgumentException($"'{clientApi}' is not a client API scope (SCP-2).", nameof(clientApi));
        }

        return builder.Require(new ClientApiRequirement(clientApi)).ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// A control plane route (CPL-3). The organization comes from the route, or from the project in the route. No
    /// membership, an unknown project, or a project outside the organization answers 404; a member without
    /// <paramref name="permission"/> gets 403. A project that passes becomes the request's project (PRJ-3).
    /// </summary>
    public static RouteHandlerBuilder RequireConsolePermission(this RouteHandlerBuilder builder, ConsolePermission permission)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .Require(new ConsolePermissionRequirement(permission))
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // The filter is only a guard: it fails closed when the middleware that enforces the requirement is missing.
    private static RouteHandlerBuilder Require(this RouteHandlerBuilder builder, AccessRequirement requirement) =>
        builder
            .WithMetadata(requirement)
            .AddEndpointFilter((context, next) => context.HttpContext.Items.ContainsKey(AccessRequirementsMiddleware.CheckedKey)
                ? next(context)
                : throw new InvalidOperationException(
                    "This endpoint declares an access requirement, but UseAccessRequirements is not in the pipeline."));
}
