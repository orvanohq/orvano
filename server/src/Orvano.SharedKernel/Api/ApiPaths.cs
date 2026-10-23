using Microsoft.AspNetCore.Http;

namespace Orvano.SharedKernel.Api;

/// <summary>The URL layout (spec 0014 PTH-1). Modules never spell these; they map under the <see cref="ApiRoutes"/> groups.</summary>
public static class ApiPaths
{
    /// <summary>Every API route starts here.</summary>
    public const string Prefix = "/v1";

    /// <summary>The data plane of one project; the access layer and CORS read the <c>projectId</c> route value (PTH-5).</summary>
    public const string Project = Prefix + "/projects/{projectId:guid}";

    /// <summary>The control plane.</summary>
    public const string Console = Prefix + "/console";

    /// <summary>Whether the request is an API request, so the wire rules (version, headers, problem bodies) apply.</summary>
    public static bool IsApi(PathString path) => path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase);
}
