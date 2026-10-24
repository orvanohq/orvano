using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// The route groups the host hands to each module's <c>Map&lt;Module&gt;Endpoints</c> (spec 0014 PTH-2). A module maps
/// its own group under the one it is given (<c>routes.Project.MapGroup("/auth")</c>) and never writes <c>/v1</c>.
/// </summary>
/// <param name="Instance"><c>/v1</c>: instance routes, such as <c>/v1/health</c>.</param>
/// <param name="Project"><c>/v1/projects/{projectId:guid}</c>: the data plane.</param>
/// <param name="Console"><c>/v1/console</c>: the control plane.</param>
public sealed record ApiRoutes(RouteGroupBuilder Instance, RouteGroupBuilder Project, RouteGroupBuilder Console)
{
    /// <summary>Creates the three groups on <paramref name="routes"/>. The host calls this once.</summary>
    public static ApiRoutes Create(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        // JSW-9: null on a non nullable Optional<T> body field answers validation_failed with required.
        return new ApiRoutes(
            routes.MapGroup(ApiPaths.Prefix).AddEndpointFilterFactory(OptionalNullCheck.Factory),
            routes.MapGroup(ApiPaths.Project).AddEndpointFilterFactory(OptionalNullCheck.Factory),
            routes.MapGroup(ApiPaths.Console).AddEndpointFilterFactory(OptionalNullCheck.Factory));
    }
}
