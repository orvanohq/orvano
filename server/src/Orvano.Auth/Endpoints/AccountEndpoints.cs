using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Auth.Application;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// The <c>account</c> service (spec 0004): the signed in user's own account. Each endpoint is a thin adapter: limits,
/// the request, one use case, the mapping. Project resolution and the bearer check run as endpoint filters.
/// </summary>
internal static class AccountEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost(Api.AccountOperations.Create.Route, async (HttpContext http, Api.CreateAccountRequest request, AccountService accounts, RateLimits limits, CancellationToken ct) =>
        {
            var limit = limits.Acquire(RateLimitPolicies.SignUpPerIp, ConnectionIp.Key(http));
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);

            var outcome = await accounts.SignUpAsync(PublicRequests.Project(http), request.Email, request.Password, request.Name, PublicRequests.Client(http), ct);
            return Created(http, outcome, AuthResult);
        })
            .WithName(Api.AccountOperations.Create.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreatePasswordSession.Route, async (HttpContext http, Api.CreatePasswordSessionRequest request, AccountService accounts, RateLimits limits, CancellationToken ct) =>
        {
            var projectId = PublicRequests.Project(http);
            // Both limits count every attempt, right or wrong (spec 0004, rate limits).
            var perEmail = limits.Acquire(RateLimitPolicies.SignInPerEmail, $"{projectId}\n{(request.Email ?? "").Trim().ToLowerInvariant()}");
            var perIp = limits.Acquire(RateLimitPolicies.SignInPerIp, ConnectionIp.Key(http));
            if (!perEmail.Allowed) return ApiProblem.RateLimited(http, perEmail, Api.ErrorCode.RateLimited);
            if (!perIp.Allowed) return ApiProblem.RateLimited(http, perIp, Api.ErrorCode.RateLimited);

            var outcome = await accounts.SignInAsync(projectId, request.Email, request.Password, PublicRequests.Client(http), ct);
            return Created(http, outcome, AuthResult);
        })
            .WithName(Api.AccountOperations.CreatePasswordSession.Id)
            .RequireProject();

        v1.MapGet(Api.AccountOperations.Get.Route, async (HttpContext http, AccountService accounts, CancellationToken ct) =>
            Ok(http, await accounts.GetAsync(PublicRequests.Project(http), PublicRequests.User(http).UserId, ct), User))
            .WithName(Api.AccountOperations.Get.Id)
            .RequireProject()
            .RequireUser();
    }
}
