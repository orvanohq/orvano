using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api;

/// <summary>
/// The host's CORS policies (spec 0007 COR-1). The <see cref="Health"/> policy is fixed and needs no project lookup
/// (spec 0014 HLT-3); any other name falls back to the configured <see cref="CorsOptions"/>, where the per project
/// policy will plug in.
/// </summary>
internal sealed class OrvanoCorsPolicyProvider(IOptions<CorsOptions> options) : ICorsPolicyProvider
{
    public const string HealthPolicyName = "health";

    /// <summary>
    /// HLT-3: any origin, no credentials (so no <c>Vary: Origin</c>), GET only, the shared header lists (HDR-2), and a
    /// ten minute preflight cache.
    /// </summary>
    public static CorsPolicy Health { get; } = new CorsPolicyBuilder()
        .AllowAnyOrigin()
        .WithMethods(HttpMethods.Get)
        .WithHeaders([.. OrvanoHeaders.CorsRequestHeaders])
        .WithExposedHeaders([.. OrvanoHeaders.CorsExposedHeaders])
        .SetPreflightMaxAge(TimeSpan.FromSeconds(600))
        .Build();

    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName) =>
        Task.FromResult(policyName == HealthPolicyName ? Health : options.Value.GetPolicy(policyName ?? options.Value.DefaultPolicyName));
}
