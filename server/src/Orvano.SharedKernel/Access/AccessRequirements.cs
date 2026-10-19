using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orvano.SharedKernel.Access;

/// <summary>What an endpoint declared with one of the <see cref="AccessEndpointFilters"/> methods, kept as endpoint metadata.</summary>
internal abstract record AccessRequirement;

internal sealed record ScopeRequirement(string Scope) : AccessRequirement;

internal sealed record ClientApiRequirement(string ClientApi) : AccessRequirement;

internal sealed record ConsolePermissionRequirement(ConsolePermission Permission) : AccessRequirement;

/// <summary>
/// Enforces the endpoint's access requirements before the endpoint runs, so a refused caller gets its 403 or 404
/// before body binding and validation, never a 400 with field names (spec 0012 DEN-5). The answers follow DEN-4 and
/// CPL-3.
/// </summary>
public static class AccessRequirementsMiddleware
{
    // Set once the middleware ran, so an endpoint with requirements fails closed when the middleware is missing.
    internal static readonly object CheckedKey = new();

    /// <summary>
    /// Place after <c>UseRouting</c> and after the middleware that establishes the caller (features 13 and 14), so the
    /// endpoint and its caller are known.
    /// </summary>
    public static IApplicationBuilder UseAccessRequirements(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (http, next) =>
        {
            if (http.GetEndpoint()?.Metadata.GetOrderedMetadata<AccessRequirement>() is { Count: > 0 } requirements)
            {
                foreach (AccessRequirement requirement in requirements)
                {
                    if (await CheckAsync(http, requirement) is { } error)
                    {
                        await error.ToProblem().ExecuteAsync(http);
                        return;
                    }
                }
            }

            http.Items[CheckedKey] = true;
            await next(http);
        });
    }

    private static ValueTask<Error?> CheckAsync(HttpContext http, AccessRequirement requirement) => requirement switch
    {
        ScopeRequirement scope => ValueTask.FromResult(CheckScope(CallerOf(http), scope.Scope)),
        ClientApiRequirement clientApi => ValueTask.FromResult(CheckClientApi(CallerOf(http), clientApi.ClientApi)),
        ConsolePermissionRequirement console => new(CheckConsolePermissionAsync(http, console.Permission, http.RequestAborted)),
        _ => throw new InvalidOperationException($"Unknown access requirement {requirement.GetType().Name}."),
    };

    private static Error? CheckScope(Caller caller, string scope)
    {
        bool passes = caller.Kind is CallerKind.SecretKey or CallerKind.RunKey
            && Scopes.WithinCeiling(KeyType.Secret, caller.Scopes) // KEY-4, and FNC-2 for run keys
            && caller.Scopes.Contains(scope);
        return passes ? null : AccessErrors.InsufficientScope;
    }

    private static Error? CheckClientApi(Caller caller, string clientApi)
    {
        if (caller.Kind is not (CallerKind.Guest or CallerKind.EndUser))
        {
            return AccessErrors.Forbidden;
        }

        if (!Scopes.WithinCeiling(KeyType.Publishable, caller.Scopes))
        {
            return AccessErrors.InsufficientScope;
        }

        return caller.Scopes.Contains(clientApi) ? null : AccessErrors.Forbidden;
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
        if (http.Request.RouteValues.ContainsKey(AccessEndpointFilters.OrganizationIdRouteValue))
        {
            if (RouteGuid(http, AccessEndpointFilters.OrganizationIdRouteValue) is not { } fromRoute)
            {
                return AccessErrors.NotFound;
            }

            organizationId = fromRoute;
        }

        Guid? projectId = null;
        if (http.Request.RouteValues.ContainsKey(AccessEndpointFilters.ProjectIdRouteValue))
        {
            projectId = RouteGuid(http, AccessEndpointFilters.ProjectIdRouteValue);
            Guid? owner = projectId is { } id ? await memberships.FindProjectOrganizationAsync(id, cancellationToken) : null;
            if (owner is null || (organizationId is not null && organizationId != owner))
            {
                return AccessErrors.NotFound;
            }

            organizationId = owner;
        }

        if (organizationId is not { } organization)
        {
            // RequireConsolePermission refuses such a route when the endpoint is built; this is only the backstop.
            throw new InvalidOperationException(
                $"RequireConsolePermission needs an '{AccessEndpointFilters.OrganizationIdRouteValue}' or '{AccessEndpointFilters.ProjectIdRouteValue}' route value.");
        }

        // A known project costs a second lookup (the membership) and an unknown one stops early, so timing can tell
        // them apart. The ids are uuid v7 and the answers are identical; revisit if timing ever matters here.
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
