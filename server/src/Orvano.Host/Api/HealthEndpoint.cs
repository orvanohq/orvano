using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api;

/// <summary>The body of a ready <c>GET /v1/health</c> (spec 0014 HLT-2). It never names the server build.</summary>
internal sealed record HealthResponse(string Status, IReadOnlyList<string> ApiVersions);

/// <summary><c>GET /v1/health</c> (spec 0014 HLT-1 to HLT-5), mapped in every role.</summary>
internal static class HealthEndpoint
{
    internal static Error NotReady { get; } = Error.Unavailable(ErrorCodes.Unavailable, "The server is not ready.");

    public static ApiRoutes MapHealth(this ApiRoutes routes)
    {
        // Public (spec 0012 EPT-5, spec 0014 HLT-4): the same answer for every caller and project, so any credential
        // sent is ignored and an SDK with a bad key still learns it reaches the server. Not metered.
        routes.Instance.MapGet("/health", GetAsync).AllowPublicAccess().RequireCors(OrvanoCorsPolicyProvider.HealthPolicyName);
        return routes;
    }

    // HLT-2: ready means the same checks as /health/ready (spec 0002 LOG-6).
    internal static async Task<Results<Ok<HealthResponse>, ErrorProblemResult>> GetAsync(
        HealthCheckService health, CancellationToken cancellationToken)
    {
        HealthReport report = await health.CheckHealthAsync(check => check.Tags.Contains("ready"), cancellationToken);
        return report.Status == HealthStatus.Unhealthy
            ? NotReady.ToProblem()
            : TypedResults.Ok(new HealthResponse("ok", ApiVersions.Supported));
    }
}
