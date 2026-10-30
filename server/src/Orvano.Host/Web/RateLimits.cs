using System.Threading.RateLimiting;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.Web;

/// <summary>
/// ASP.NET Core's rate limiter, per process, for the endpoints that name a policy with <c>RequireRateLimiting</c>
/// (spec 0016 STP-8). A refusal is one 429 <c>rate_limited</c> problem with <c>Retry-After</c> (spec 0014 PRB-1).
/// Feature 23 replaces it with the shared engine.
/// </summary>
internal static class RateLimits
{
    public static IServiceCollection AddOrvanoRateLimiter(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                TimeSpan retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan wait) ? wait : TimeSpan.FromMinutes(1);
                var limited = Error.RateLimited(ErrorCodes.RateLimited, "Too many requests; wait and try again.", retryAfter);
                return new ValueTask(limited.ToProblem().ExecuteAsync(context.HttpContext));
            };
        });
}
