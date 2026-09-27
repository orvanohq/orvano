using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Auth.Application;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;
using Ops = Orvano.Contract.UsersOperations;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// The <c>users</c> service (spec 0004, AC-17, AC-18): a server manages the project's users with an API key holding
/// the operation's scope. Every change is recorded with the key as the actor.
/// </summary>
internal static class UsersEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapGet(Ops.List.Route, async (
                HttpContext http, string? email, string? status, DateTimeOffset? createdAfter, DateTimeOffset? createdBefore, string? cursor, int? limit,
                UsersService users, CancellationToken ct) =>
            Ok(http, await users.ListAsync(PublicRequests.Project(http), new UserFilter(email, status, createdAfter, createdBefore), cursor, limit, ct), UserPage))
            .WithName(Ops.List.Id)
            .RequireProject()
            .RequireApiKey(Ops.List.Scope);

        v1.MapGet(Ops.Get.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            Ok(http, await users.GetAsync(PublicRequests.Project(http), userId, ct), User))
            .WithName(Ops.Get.Id)
            .RequireProject()
            .RequireApiKey(Ops.Get.Scope);

        v1.MapPost(Ops.Create.Route, async (HttpContext http, Api.CreateUserRequest request, UsersService users, RateLimits limits, CancellationToken ct) =>
        {
            var limit = limits.Acquire(RateLimitPolicies.SignUpPerIp, ConnectionIp.Key(http));
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);

            var outcome = await users.CreateAsync(PublicRequests.Project(http), request.Email, request.Password, request.Name, KeyActor(http), ct);
            return Created(http, outcome, User);
        })
            .WithName(Ops.Create.Id)
            .RequireProject()
            .RequireApiKey(Ops.Create.Scope);

        v1.MapPost(Ops.Block.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            Ok(http, await users.BlockAsync(PublicRequests.Project(http), userId, KeyActor(http), ct), User))
            .WithName(Ops.Block.Id)
            .RequireProject()
            .RequireApiKey(Ops.Block.Scope);

        v1.MapPost(Ops.Unblock.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            Ok(http, await users.UnblockAsync(PublicRequests.Project(http), userId, KeyActor(http), ct), User))
            .WithName(Ops.Unblock.Id)
            .RequireProject()
            .RequireApiKey(Ops.Unblock.Scope);

        v1.MapDelete(Ops.Delete.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            NoContent(http, await users.DeleteAsync(PublicRequests.Project(http), userId, KeyActor(http), ct)))
            .WithName(Ops.Delete.Id)
            .RequireProject()
            .RequireApiKey(Ops.Delete.Scope);

        v1.MapGet(Ops.ListSessions.Route, async (HttpContext http, string userId, string? cursor, int? limit, UsersService users, CancellationToken ct) =>
            Ok(http, await users.ListSessionsAsync(PublicRequests.Project(http), userId, cursor, limit, ct), SessionPage))
            .WithName(Ops.ListSessions.Id)
            .RequireProject()
            .RequireApiKey(Ops.ListSessions.Scope);

        v1.MapDelete(Ops.DeleteSessions.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            NoContent(http, await users.DeleteSessionsAsync(PublicRequests.Project(http), userId, KeyActor(http), ct)))
            .WithName(Ops.DeleteSessions.Id)
            .RequireProject()
            .RequireApiKey(Ops.DeleteSessions.Scope);

        v1.MapDelete(Ops.DeleteSession.Route, async (HttpContext http, string userId, string sessionId, UsersService users, CancellationToken ct) =>
            NoContent(http, await users.DeleteSessionAsync(PublicRequests.Project(http), userId, sessionId, KeyActor(http), ct)))
            .WithName(Ops.DeleteSession.Id)
            .RequireProject()
            .RequireApiKey(Ops.DeleteSession.Scope);
    }

    private static Actor KeyActor(HttpContext http) => Actor.ApiKey(PublicRequests.Key(http).KeyId);
}
