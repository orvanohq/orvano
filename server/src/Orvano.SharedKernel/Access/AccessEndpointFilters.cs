using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
/// How endpoints declare what they need (spec 0012 SCP-5, CPL-3). Each filter reads the caller the access layer
/// set; the answers follow DEN-4 and CPL-3.
/// </summary>
public static class AccessEndpointFilters
{
    /// <summary>The route values the console permission filter reads.</summary>
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

        return builder
            .AddEndpointFilter(async (context, next) =>
            {
                Caller caller = CallerOf(context.HttpContext);
                bool passes = caller.Kind is CallerKind.SecretKey or CallerKind.RunKey
                    && Scopes.WithinCeiling(KeyType.Secret, caller.Scopes) // KEY-4, and FNC-2 for run keys
                    && caller.Scopes.Contains(scope);
                return passes ? await next(context) : AccessErrors.InsufficientScope.ToProblem();
            })
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <summary>A client route: only guests and end users whose publishable key has <paramref name="clientApi"/> on.</summary>
    public static RouteHandlerBuilder RequireClientApi(this RouteHandlerBuilder builder, string clientApi)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Scopes.Publishable.Contains(clientApi))
        {
            throw new ArgumentException($"'{clientApi}' is not a client API scope (SCP-2).", nameof(clientApi));
        }

        return builder
            .AddEndpointFilter(async (context, next) =>
            {
                Caller caller = CallerOf(context.HttpContext);
                if (caller.Kind is not (CallerKind.Guest or CallerKind.EndUser))
                {
                    return AccessErrors.Forbidden.ToProblem();
                }

                if (!Scopes.WithinCeiling(KeyType.Publishable, caller.Scopes))
                {
                    return AccessErrors.InsufficientScope.ToProblem();
                }

                return caller.Scopes.Contains(clientApi) ? await next(context) : AccessErrors.Forbidden.ToProblem();
            })
            .ProducesProblem(StatusCodes.Status403Forbidden);
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
            .AddEndpointFilter(async (context, next) =>
            {
                HttpContext http = context.HttpContext;
                Error? error = await CheckConsolePermissionAsync(http, permission, http.RequestAborted);
                return error is null ? await next(context) : error.ToProblem();
            })
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Error?> CheckConsolePermissionAsync(
        HttpContext http,
        ConsolePermission permission,
        CancellationToken cancellationToken)
    {
        Caller caller = CallerOf(http);
        if (caller.Kind != CallerKind.Operator)
        {
            return AccessErrors.NotFound;
        }

        IMembershipLookup memberships = http.RequestServices.GetRequiredService<IMembershipLookup>();
        Guid? organizationId = null;
        if (http.Request.RouteValues.ContainsKey(OrganizationIdRouteValue))
        {
            if (RouteGuid(http, OrganizationIdRouteValue) is not { } fromRoute)
            {
                return AccessErrors.NotFound;
            }

            organizationId = fromRoute;
        }

        Guid? projectId = null;
        if (http.Request.RouteValues.ContainsKey(ProjectIdRouteValue))
        {
            projectId = RouteGuid(http, ProjectIdRouteValue);
            Guid? owner = projectId is { } id ? await memberships.FindProjectOrganizationAsync(id, cancellationToken) : null;
            if (owner is null || (organizationId is not null && organizationId != owner))
            {
                return AccessErrors.NotFound;
            }

            organizationId = owner;
        }

        if (organizationId is not { } organization)
        {
            throw new InvalidOperationException(
                $"RequireConsolePermission needs an '{OrganizationIdRouteValue}' or '{ProjectIdRouteValue}' route value.");
        }

        string? role = await memberships.FindRoleAsync(caller.OperatorId!.Value, organization, cancellationToken);
        if (role is null)
        {
            return AccessErrors.NotFound;
        }

        if (!RolePermissions.For(role).Contains(permission))
        {
            return AccessErrors.Forbidden;
        }

        if (projectId is not null)
        {
            http.RequestServices.GetRequiredService<AccessContext>().Establish(caller, projectId);
        }

        return null;
    }

    private static Caller CallerOf(HttpContext http) => http.RequestServices.GetRequiredService<ICallerContext>().Current;

    private static Guid? RouteGuid(HttpContext http, string name) =>
        Guid.TryParse(Convert.ToString(http.Request.RouteValues[name], CultureInfo.InvariantCulture), out Guid id) ? id : null;
}
