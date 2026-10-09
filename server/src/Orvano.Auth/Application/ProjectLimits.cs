using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;

namespace Orvano.Auth.Application;

/// <summary>
/// The editable limits of spec 0014 (AC-21) with a project's own numbers: each is the built in policy with the
/// project's limit (and window), so a changed value starts fresh counters on the next request. Every other limit keeps
/// its fixed value from <see cref="RateLimitPolicies"/>.
/// </summary>
internal static class ProjectLimits
{
    public static RateLimitPolicy SignUpPerIp(AuthPolicies policies) =>
        RateLimitPolicies.SignUpPerIp with { PermitLimit = policies.SignUpPerIp };

    public static RateLimitPolicy FailedSignInPerIp(AuthPolicies policies) =>
        RateLimitPolicies.FailedSignInPerIp with { PermitLimit = policies.SignInFailedPerIp };

    public static RateLimitPolicy FailedSignInPerEmailIp(AuthPolicies policies) => RateLimitPolicies.FailedSignInPerEmailIp with
    {
        PermitLimit = policies.SignInFailedPerEmailIp.Limit,
        Window = TimeSpan.FromMinutes(policies.SignInFailedPerEmailIp.WindowMinutes),
    };

    public static RateLimitPolicy AnonymousPerIp(AuthPolicies policies) =>
        RateLimitPolicies.AnonymousPerIp with { PermitLimit = policies.AnonymousPerIp };

    public static RateLimitPolicy EmailSendPerIp(AuthPolicies policies) =>
        RateLimitPolicies.EmailSendPerIp with { PermitLimit = policies.EmailSendPerIp };

    /// <summary>A limit key of one email at one limit address: the project, the limit IP, and the lowercased email.</summary>
    public static string EmailKey(string limitKey, string? email) => $"{limitKey}\n{(email ?? "").Trim().ToLowerInvariant()}";
}
