using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Orvano.SharedKernel.Api;

namespace Orvano.SharedKernel.Access;

/// <summary>A requirement an endpoint declared with one of the <see cref="AccessEndpointFilters"/> methods, kept as endpoint metadata.</summary>
internal abstract record AccessRequirement;

internal sealed record ScopeRequirement(string Scope) : AccessRequirement;

internal sealed record ClientApiRequirement(string ClientApi) : AccessRequirement;

internal sealed record ConsolePermissionRequirement(ConsolePermission Permission) : AccessRequirement;

internal sealed record OperatorRequirement : AccessRequirement;

internal sealed record ProjectKeyRequirement : AccessRequirement;

/// <summary>What <see cref="AccessEndpointFilters.AllowPublicAccess{TBuilder}"/> adds (EPT-5).</summary>
internal sealed record PublicAccess;

/// <summary>What <see cref="AccessEndpointFilters.RequireRecentReauth{TBuilder}"/> adds: a modifier, not a requirement kind (spec 0017 SES-8).</summary>
internal sealed record RecentReauthModifier;

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
        if (RequiresRecentReauth(endpoint) && !(RequiresOperator(endpoint) && ChangesStateOnly(endpoint)))
        {
            return "RequireRecentReauth belongs on an operator route that changes state (spec 0017 SES-8)";
        }

        if (requirements.Count == 0)
        {
            if (isPublic && ChangesConsoleStateWithoutOrigin(endpoint))
            {
                return "it is a public console endpoint that changes state; add RequireConsoleOrigin (spec 0017 ACX-4)";
            }

            return isPublic ? null : "it declares no access; add RequireScope, RequireClientApi, RequireConsolePermission, RequireOperator, RequireProjectKey, or AllowPublicAccess";
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

        if (requirements[0] is ProjectKeyRequirement
            && endpoint is RouteEndpoint { RoutePattern: var keyPattern }
            && keyPattern.GetParameter(AccessEndpointFilters.ProjectIdRouteValue) is null)
        {
            return $"RequireProjectKey needs a '{{{AccessEndpointFilters.ProjectIdRouteValue}}}' route value";
        }

        return null;
    }

    /// <summary>
    /// Whether only an operator can pass the endpoint: <c>RequireOperator</c> or <c>RequireConsolePermission</c>. The
    /// session resolver reads its cookie only here, and an API key is refused here (spec 0017 SES-4, AKY-10).
    /// </summary>
    public static bool RequiresOperator(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.Metadata.GetOrderedMetadata<AccessRequirement>().Any(requirement => requirement is OperatorRequirement or ConsolePermissionRequirement);
    }

    /// <summary>Whether the endpoint needs a recent password check, which the state change check enforces (spec 0017 SES-8).</summary>
    public static bool RequiresRecentReauth(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.Metadata.GetMetadata<RecentReauthModifier>() is not null;
    }

    /// <summary>Whether <paramref name="method"/> changes state (spec 0017 SES-7, ACX-4).</summary>
    public static bool ChangesState(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    /// <summary>Whether the endpoint carries <see cref="AccessEndpointFilters.AllowPublicAccess{TBuilder}"/> (EPT-5).</summary>
    public static bool IsPublic(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.Metadata.GetMetadata<PublicAccess>() is not null;
    }

    /// <summary>
    /// The SDK audience the declaration implies (spec 0014 XTN-1): <c>RequireClientApi</c> is <c>client</c>,
    /// <c>RequireScope</c> is <c>server</c>, <c>AllowPublicAccess</c> and <c>RequireProjectKey</c> are both. Console routes
    /// and endpoints that declare their access wrongly have none (null).
    /// </summary>
    public static IReadOnlyList<string>? Audience(Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Problem(endpoint) is not null)
        {
            return null;
        }

        return IsPublic(endpoint)
            ? [Audiences.Client, Audiences.Server]
            : endpoint.Metadata.GetMetadata<AccessRequirement>() switch
            {
                ClientApiRequirement => [Audiences.Client],
                ScopeRequirement => [Audiences.Server],
                ProjectKeyRequirement => [Audiences.Client, Audiences.Server],
                _ => null,
            };
    }

    private static bool ChangesStateOnly(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { HttpMethods: { Count: > 0 } methods } && methods.All(ChangesState);

    // ACX-4: nothing but the Origin check stands between a browser and a public console endpoint that changes state.
    private static bool ChangesConsoleStateWithoutOrigin(Endpoint endpoint) =>
        endpoint is RouteEndpoint { RoutePattern.RawText: { } route }
        && new PathString("/" + route.TrimStart('/')).StartsWithSegments(ApiPaths.Console, StringComparison.OrdinalIgnoreCase)
        && endpoint.Metadata.GetMetadata<ConsoleOriginMetadata>() is null
        && endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } methods
        && methods.HttpMethods.Any(ChangesState);

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
/// DEN-4, PRJ-2, and CPL-3. A request that matched no endpoint gets routing's 404 here, so nothing placed after the
/// access layer can answer a request without an endpoint, and so without a declaration.
/// </summary>
public static class AccessRequirementsMiddleware
{
    // Set once the middleware ran, so an endpoint with requirements fails closed when the middleware is missing.
    internal static readonly object CheckedKey = new();

    // Failed reasons come from resolver code and may hold request data; the log keeps at most this many characters.
    internal const int MaxReasonLength = 100;

    // The display names of routing's own 405 and 415 endpoints (HttpMethodMatcherPolicy, AcceptsMatcherPolicy),
    // which carry no metadata.
    internal static readonly string[] RoutingRejectionNames = ["405 HTTP Method Not Supported", "415 HTTP Unsupported Media Type"];

    /// <summary>
    /// Place after <c>UseRouting</c> and <c>UseCors</c> (which answers a preflight without a credential, spec 0007
    /// COR-1), before anything that runs an endpoint. Only endpoints may answer after it.
    /// </summary>
    public static IApplicationBuilder UseAccessRequirements(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        ILogger logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AccessRequirementsMiddleware));

        return app.Use(async (http, next) =>
        {
            if (http.GetEndpoint() is not { } endpoint)
            {
                // Routing's own 404, answered here rather than by passing on, so a later branch, Map, or Run delegate
                // cannot answer without an endpoint and skip default deny (EPT-3).
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (endpoint is not RouteEndpoint && RoutingRejectionNames.Contains(endpoint.DisplayName) && ApiPaths.IsApi(http.Request.Path))
            {
                // Spec 0014 PRB-4: an API route answers a wrong method with 405 method_not_allowed and a wrong body type
                // with 415 unsupported_media_type. Routing's endpoint sets only the status (and Allow); the host's wire
                // format middleware writes the problem body.
                await next(http);
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

            (Caller? established, Error? refusal) = await EstablishCallerAsync(http, logger);
            if (established is not { } caller)
            {
                await (refusal ?? AccessErrors.Unauthorized).ToProblem().ExecuteAsync(http);
                return;
            }

            ConsoleMembership? membership = null; // read once, however many console permissions the endpoint needs
            foreach (AccessRequirement requirement in endpoint.Metadata.GetOrderedMetadata<AccessRequirement>())
            {
                Error? error = requirement switch
                {
                    ScopeRequirement scope => CheckScope(caller, scope.Scope),
                    ClientApiRequirement clientApi => CheckClientApi(caller, clientApi.ClientApi),
                    OperatorRequirement => caller.Kind == CallerKind.Operator ? null : AccessErrors.Forbidden,
                    ProjectKeyRequirement => CheckProjectKey(caller),
                    ConsolePermissionRequirement console => CheckConsolePermission(
                        membership ??= await FindConsoleMembershipAsync(http, caller, http.RequestAborted), console.Permission),
                    _ => throw new InvalidOperationException($"Unknown access requirement {requirement.GetType().Name}."),
                };
                if (error is not null)
                {
                    await error.ToProblem().ExecuteAsync(http);
                    return;
                }
            }

            if (membership is { ProjectId: { } projectId })
            {
                // CPL-3: the operator's project is set only once every console permission passed.
                http.RequestServices.GetRequiredService<AccessContext>().Establish(caller, projectId);
            }

            http.Items[CheckedKey] = true;
            await next(http);
        });
    }

    // RSV-2 to RSV-5: runs the chain, compares the project, and establishes the caller. A null caller means answer 401,
    // with the resolver's own error when it named one (ACX-3).
    private static async Task<(Caller? Caller, Error? Refusal)> EstablishCallerAsync(HttpContext http, ILogger logger)
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
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.CredentialRefused(resolver.GetType().Name, Truncate(failed.Reason));
                    }

                    // PRJ-4: no fallback
                    return (null, failed.Code == ErrorCodes.SessionExpired ? AccessErrors.SessionExpired : null);
                default:
                    throw new InvalidOperationException($"{resolver.GetType().Name} returned an unknown resolution.");
            }
        }

        if (caller is null)
        {
            return (null, null); // RSV-5
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
                    return (null, null); // PRJ-2
                }

                context.Establish(caller, routeProjectId);
                break;
            default:
                throw new InvalidOperationException($"A resolver returned a {caller.Kind} caller, which never comes from HTTP (CLR-3).");
        }

        return (caller, null);
    }

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

    // ACX-2: every key of the project, run keys aside, with its stored scopes back inside the ceiling (KEY-4).
    private static Error? CheckProjectKey(Caller caller) => caller.Kind switch
    {
        CallerKind.Guest or CallerKind.EndUser =>
            Scopes.WithinCeiling(KeyType.Publishable, caller.Scopes) ? null : AccessErrors.InsufficientScope,
        CallerKind.SecretKey => Scopes.WithinCeiling(KeyType.Secret, caller.Scopes) ? null : AccessErrors.InsufficientScope,
        _ => AccessErrors.Forbidden,
    };

    // CPL-3: the operator's membership for the route's organization (or the project's organization), or the refusal.
    private static async Task<ConsoleMembership> FindConsoleMembershipAsync(HttpContext http, Caller caller, CancellationToken cancellationToken)
    {
        if (caller.Kind != CallerKind.Operator)
        {
            return ConsoleMembership.Refused(AccessErrors.NotFound);
        }

        IMembershipLookup memberships = http.RequestServices.GetRequiredService<IMembershipLookup>();
        Guid? organizationId = null;
        if (http.Request.RouteValues.ContainsKey(AccessEndpointFilters.OrganizationIdRouteValue))
        {
            if (RouteGuid(http, AccessEndpointFilters.OrganizationIdRouteValue) is not { } fromRoute)
            {
                return ConsoleMembership.Refused(AccessErrors.NotFound);
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
                return ConsoleMembership.Refused(AccessErrors.NotFound);
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
        return role is null ? ConsoleMembership.Refused(AccessErrors.NotFound) : new ConsoleMembership(null, role, projectId);
    }

    private static Error? CheckConsolePermission(ConsoleMembership membership, ConsolePermission permission)
    {
        if (membership.Refusal is { } refusal)
        {
            return refusal;
        }

        return RolePermissions.For(membership.Role!).Contains(permission) ? null : AccessErrors.Forbidden;
    }

    private static string Truncate(string reason) =>
        reason.Length <= MaxReasonLength ? reason : string.Concat(reason.AsSpan(0, MaxReasonLength), "…");

    // RSV-2: only the canonical form (lower case, hyphenated, no braces or spaces), so one project has one spelling
    // for anything that keys on the raw route value.
    private static Guid? RouteGuid(HttpContext http, string name)
    {
        string? value = Convert.ToString(http.Request.RouteValues[name], CultureInfo.InvariantCulture);
        return Guid.TryParseExact(value, "D", out Guid id) && string.Equals(id.ToString("D"), value, StringComparison.Ordinal) ? id : null;
    }

    // The membership the console permissions of one request are checked against: a refusal, or the role and project.
    private sealed record ConsoleMembership(Error? Refusal, string? Role, Guid? ProjectId)
    {
        public static ConsoleMembership Refused(Error refusal) => new(refusal, null, null);
    }
}
