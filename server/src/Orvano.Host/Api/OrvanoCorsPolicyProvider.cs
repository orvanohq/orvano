using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Projects;

namespace Orvano.Host.Api;

/// <summary>
/// The host's CORS policies (spec 0007 COR-1). The <see cref="Health"/> policy is fixed and needs no project lookup
/// (spec 0014 HLT-3). The <see cref="ProjectPolicyName"/> policy allows exactly the origins of the route's project, and only
/// while it's active (spec 0017 PJT-6). Any other name falls back to the configured <see cref="CorsOptions"/>.
/// </summary>
internal sealed class OrvanoCorsPolicyProvider(IOptions<CorsOptions> options) : ICorsPolicyProvider
{
    public const string HealthPolicyName = "health";
    public const string ProjectPolicyName = "project";

    private const string ProjectIdRouteValue = "projectId";
    private const int PreflightMaxAgeSeconds = 600;

    // An empty policy, not a null one: a refused preflight is answered here with no CORS headers and goes no further.
    private static readonly CorsPolicy NoOrigins = new();

    /// <summary>
    /// HLT-3: any origin, no credentials (so no <c>Vary: Origin</c>), GET only, the shared header lists (HDR-2), and a
    /// ten minute preflight cache.
    /// </summary>
    public static CorsPolicy Health { get; } = new CorsPolicyBuilder()
        .AllowAnyOrigin()
        .WithMethods(HttpMethods.Get)
        .WithHeaders([.. OrvanoHeaders.CorsRequestHeaders])
        .WithExposedHeaders([.. OrvanoHeaders.CorsExposedHeaders])
        .SetPreflightMaxAge(TimeSpan.FromSeconds(PreflightMaxAgeSeconds))
        .Build();

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        ArgumentNullException.ThrowIfNull(context);
        return policyName switch
        {
            HealthPolicyName => Health,
            ProjectPolicyName => await ProjectPolicyAsync(context),
            _ => options.Value.GetPolicy(policyName ?? options.Value.DefaultPolicyName),
        };
    }

    /// <summary>COR-2: every response on a project route varies by <c>Origin</c>, also one to a request with no <c>Origin</c>.</summary>
    public static IApplicationBuilder UseProjectVary(IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<IEnableCorsAttribute>()?.PolicyName == ProjectPolicyName)
            {
                context.Response.Headers.Vary = HeaderNames.Origin;
            }

            return next(context);
        });

    // COR-1, COR-2, COR-3: exact origins, the shared header lists, no credentials.
    private static async Task<CorsPolicy> ProjectPolicyAsync(HttpContext context)
    {
        if (!Guid.TryParse(context.Request.RouteValues[ProjectIdRouteValue]?.ToString(), out Guid projectId))
        {
            return NoOrigins;
        }

        IProjectDirectory directory = context.RequestServices.GetRequiredService<IProjectDirectory>();
        ActiveProject? project = await directory.FindActiveAsync(projectId, context.RequestAborted);
        if (project is not { AllowedOrigins.Count: > 0 })
        {
            return NoOrigins;
        }

        return new CorsPolicyBuilder()
            .WithOrigins([.. project.AllowedOrigins])
            .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete)
            .WithHeaders([.. OrvanoHeaders.CorsRequestHeaders])
            .WithExposedHeaders([.. OrvanoHeaders.CorsExposedHeaders])
            .SetPreflightMaxAge(TimeSpan.FromSeconds(PreflightMaxAgeSeconds))
            .Build();
    }
}
