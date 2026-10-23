using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api;

// Spec 0014 HLT-2: it never names the server build. The XML docs below are the contract's descriptions (CLT-3).

/// <summary>The server is up and ready to serve requests.</summary>
/// <param name="Status">Always <c>ok</c>.</param>
/// <param name="ApiVersions">The API versions the server serves, <c>YYYY-MM-DD</c>; send one in <c>Orvano-Api-Version</c>.</param>
internal sealed record HealthResponse(string Status, IReadOnlyList<string> ApiVersions);

/// <summary><c>GET /v1/health</c> (spec 0014 HLT-1 to HLT-5), mapped in every role.</summary>
internal static class HealthEndpoint
{
    internal static Error NotReady { get; } = Error.Unavailable(ErrorCodes.Unavailable, "The server is not ready.");

    private static readonly PathString HealthPath = new(ApiPaths.Prefix + "/health");

    /// <summary>Whether a request is for <c>/v1/health</c>, with or without a trailing slash (PTH-4).</summary>
    internal static bool IsHealthPath(PathString path) =>
        path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase, out PathString rest) && rest.Value is null or "" or "/";

    public static ApiRoutes MapHealth(this ApiRoutes routes)
    {
        // Public (spec 0012 EPT-5, spec 0014 HLT-4): the same answer for every caller and project, so any credential
        // sent is ignored and an SDK with a bad key still learns it reaches the server. Not metered.
        routes.Instance.MapGet("/health", GetAsync)
            .AllowPublicAccess()
            .RequireCors(OrvanoCorsPolicyProvider.HealthPolicyName)
            .WithName("Host_Health")
            .WithSummary("Check that the server is reachable and ready.")
            .WithDescription(
                "Answers 200 with the API versions the server serves, or 503 `unavailable` while it is not ready. "
                + "It needs no key and ignores any credential sent, so it works from a browser on any origin.")
            .WithSdk("health", "get")
            .WithErrors() // unavailable, its only error, is a transport code every operation implies (XTN-3)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
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
