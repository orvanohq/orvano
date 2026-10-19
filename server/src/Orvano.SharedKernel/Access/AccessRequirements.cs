using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orvano.SharedKernel.Access;

/// <summary>A requirement an endpoint declared with one of the <see cref="AccessEndpointFilters"/> methods, kept as endpoint metadata.</summary>
internal abstract record AccessRequirement;

internal sealed record ScopeRequirement(string Scope) : AccessRequirement;

internal sealed record ClientApiRequirement(string ClientApi) : AccessRequirement;

internal sealed record ConsolePermissionRequirement(ConsolePermission Permission) : AccessRequirement;

/// <summary>What <see cref="AccessEndpointFilters.AllowPublicAccess{TBuilder}"/> adds (EPT-5).</summary>
internal sealed record PublicAccess;

/// <summary>
/// EPT-3: whether an endpoint declares its access properly. The startup check, the access layer middleware, and the
/// endpoint list test in <c>Orvano.Host.IntegrationTests</c> (EPT-4) all ask this.
/// </summary>
public static class AccessDeclaration
{
    /// <summary>What is wrong with the endpoint's declaration, or null when it declares its access properly.</summary>
    public static string? Problem(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        IReadOnlyList<AccessRequirement> requirements = endpoint.Metadata.GetOrderedMetadata<AccessRequirement>();
        bool isPublic = IsPublic(endpoint);
        if (requirements.Count == 0)
        {
            return isPublic ? null : "it declares no access; add RequireScope, RequireClientApi, RequireConsolePermission, or AllowPublicAccess";
        }

        if (isPublic)
        {
            return "it declares both a requirement and AllowPublicAccess";
        }

        if (requirements.Select(requirement => requirement.GetType()).Distinct().Skip(1).Any())
        {
            return "it mixes requirement kinds, which would admit no caller";
        }

        if (requirements[0] is ConsolePermissionRequirement
            && endpoint is RouteEndpoint { RoutePattern: var pattern }
            && pattern.GetParameter(AccessEndpointFilters.OrganizationIdRouteValue) is null
            && pattern.GetParameter(AccessEndpointFilters.ProjectIdRouteValue) is null)
        {
            return $"RequireConsolePermission needs an '{{{AccessEndpointFilters.OrganizationIdRouteValue}}}' or '{{{AccessEndpointFilters.ProjectIdRouteValue}}}' route value";
        }

        return null;
    }

    /// <summary>Whether the endpoint carries <see cref="AccessEndpointFilters.AllowPublicAccess{TBuilder}"/> (EPT-5).</summary>
    public static bool IsPublic(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.Metadata.GetMetadata<PublicAccess>() is not null;
    }

    /// <summary>The endpoint's route pattern, else its display name, for error messages.</summary>
    public static string Name(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? endpoint.DisplayName ?? "(unnamed endpoint)";
    }
}

/// <summary>
/// The access layer (spec 0012 EPT-1): one middleware that, for a request that matched an endpoint, refuses an
/// undeclared endpoint, skips a public one, resolves the caller through the <see cref="ICallerResolver"/> chain,
/// checks the project and establishes the caller, and enforces the endpoint's requirements. All of it happens before
/// body binding and validation, so a refused caller never gets a 400 with field names (DEN-5). The answers follow
/// DEN-4, PRJ-2, and CPL-3.
/// </summary>
public static class AccessRequirementsMiddleware
{
    // Set once the middleware ran, so an endpoint with requirements fails closed when the middleware is missing.
    internal static readonly object CheckedKey = new();

    /// <summary>Place right after <c>UseRouting</c>, before anything that runs an endpoint.</summary>
    public static IApplicationBuilder UseAccessRequirements(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        ILogger logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AccessRequirementsMiddleware));

        return app.Use(async (http, next) =>
        {
            if (http.GetEndpoint() is not { } endpoint)
            {
                await next(http); // routing's own 404
                return;
            }

            if (AccessDeclaration.Problem(endpoint) is { } problem)
            {
                // EPT-4: the startup check refuses these, so this endpoint was added after startup. Routing's own
                // rejections (a 405 for the wrong method) are plain endpoints with no metadata; refuse them too, quietly.
                if (endpoint is RouteEndpoint)
                {
                    logger.EndpointUndeclared(AccessDeclaration.Name(endpoint), problem);
                }

                await AccessErrors.NotFound.ToProblem().ExecuteAsync(http);
                return;
            }

            if (AccessDeclaration.IsPublic(endpoint))
            {
                await next(http); // EPT-5: no caller, resolvers do not run
                return;
            }

            if (await EstablishCallerAsync(http, logger) is not { } caller)
            {
                await AccessErrors.Unauthorized.ToProblem().ExecuteAsync(http);
                return;
            }

            foreach (AccessRequirement requirement in endpoint.Metadata.GetOrderedMetadata<AccessRequirement>())
            {
                if (await CheckAsync(http, caller, requirement) is { } error)
                {
                    await error.ToProblem().ExecuteAsync(http);
                    return;
                }
            }

            http.Items[CheckedKey] = true;
            await next(http);
        });
    }

    // RSV-2 to RSV-5: runs the chain, compares the project, and establishes the caller. Null means answer 401.
    private static async Task<Caller?> EstablishCallerAsync(HttpContext http, ILogger logger)
    {
        bool routeHasProject = http.Request.RouteValues.ContainsKey(AccessEndpointFilters.ProjectIdRouteValue);
        Guid? routeProjectId = routeHasProject ? RouteGuid(http, AccessEndpointFilters.ProjectIdRouteValue) : null;

        Caller? caller = null;
        IEnumerable<ICallerResolver> resolvers = http.RequestServices.GetServices<ICallerResolver>();
        foreach (ICallerResolver resolver in resolvers.OrderBy(resolver => resolver.Order)) // stable: ties keep registration order
        {
            CallerResolution resolution = await resolver.ResolveAsync(
                new CallerResolutionContext(http, routeProjectId, caller), http.RequestAborted);
            switch (resolution)
            {
                case CallerResolution.NotPresent:
                    break;
                case CallerResolution.Resolved resolved:
                    caller = resolved.Caller ?? throw new InvalidOperationException($"{resolver.GetType().Name} resolved a null caller.");
                    break;
                case CallerResolution.Failed failed:
                    logger.CredentialRefused(resolver.GetType().Name, failed.Reason);
                    return null; // PRJ-4: no fallback
                default:
                    throw new InvalidOperationException($"{resolver.GetType().Name} returned an unknown resolution.");
            }
        }

        if (caller is null)
        {
            return null; // RSV-5
        }

        AccessContext context = http.RequestServices.GetRequiredService<AccessContext>();
        switch (caller.Kind)
        {
            case CallerKind.Operator:
                context.Establish(caller, null); // CPL-3 sets the project after the membership check
                break;
            case CallerKind.Guest or CallerKind.EndUser or CallerKind.SecretKey or CallerKind.RunKey:
                if (routeHasProject && (routeProjectId is null || caller.ProjectId != routeProjectId))
                {
                    logger.ProjectMismatch(caller.Kind);
                    return null; // PRJ-2
                }

                context.Establish(caller, routeProjectId);
                break;
            default:
                throw new InvalidOperationException($"A resolver returned a {caller.Kind} caller, which never comes from HTTP (CLR-3).");
        }

        return caller;
    }

    private static ValueTask<Error?> CheckAsync(HttpContext http, Caller caller, AccessRequirement requirement) => requirement switch
    {
        ScopeRequirement scope => ValueTask.FromResult(CheckScope(caller, scope.Scope)),
        ClientApiRequirement clientApi => ValueTask.FromResult(CheckClientApi(caller, clientApi.ClientApi)),
        ConsolePermissionRequirement console => new(CheckConsolePermissionAsync(http, caller, console.Permission, http.RequestAborted)),
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
        Caller caller,
        ConsolePermission permission,
        CancellationToken cancellationToken)
    {
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
            // The startup check refuses such a route (AccessDeclaration); this is only the backstop.
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

    private static Guid? RouteGuid(HttpContext http, string name) =>
        Guid.TryParse(Convert.ToString(http.Request.RouteValues[name], CultureInfo.InvariantCulture), out Guid id) ? id : null;
}
