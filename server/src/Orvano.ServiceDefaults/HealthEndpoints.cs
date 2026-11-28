using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The operational health endpoints (spec 0002). Bodies carry the <c>status</c> field only:
/// no check name, version or error text reaches the caller. Probes read the status code.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>The liveness path, "is the process up".</summary>
    public const string LivenessPath = "/healthz";

    private const string OkBody = """{"status":"ok"}""";
    private const string UnavailableBody = """{"status":"unavailable"}""";

    /// <summary>
    /// Maps <c>GET /healthz</c>. It runs no checks: if the process can answer, it answers <c>200</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapLiveness(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync,
        });

    /// <summary>
    /// Writes <c>{"status":"ok"}</c> for healthy and degraded, <c>{"status":"unavailable"}</c> for unhealthy.
    /// The status code comes from <see cref="HealthCheckOptions.ResultStatusCodes"/> (200, 200 and 503 by default).
    /// </summary>
    public static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(report.Status == HealthStatus.Unhealthy ? UnavailableBody : OkBody);
    }
}
