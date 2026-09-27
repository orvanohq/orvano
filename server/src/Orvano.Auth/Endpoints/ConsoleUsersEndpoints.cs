using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Orvano.Auth.Application;
using Orvano.Auth.Data;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using Orvano.Platform.Contracts;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;
using Keys = Orvano.Contract.ConsoleAuthKeysOperations;
using Ops = Orvano.Contract.ConsoleUsersOperations;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// The console's Users page and signing keys panel (spec 0004, AC-22, AC-29), for the project named by
/// <c>X-Orvano-Project</c>. The same use cases as the <c>users</c> service, acting as the console user. Every role
/// reads; owners and developers change users; only owners rotate keys. A project you can't see is 404.
/// </summary>
internal static class ConsoleUsersEndpoints
{
    private enum Need
    {
        Read,
        Write,
        Owner,
    }

    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapGet(Ops.List.Route, async (
                HttpContext http, string? email, string? status, DateTimeOffset? createdAfter, DateTimeOffset? createdBefore, string? cursor, int? limit,
                UsersService users, CancellationToken ct) =>
            Ok(http, await users.ListAsync(Project(http), new UserFilter(email, status, createdAfter, createdBefore), cursor, limit, ct), UserPage))
            .WithName(Ops.List.Id)
            .RequireRole(Need.Read);

        v1.MapGet(Ops.Get.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            Ok(http, await users.GetAsync(Project(http), userId, ct), User))
            .WithName(Ops.Get.Id)
            .RequireRole(Need.Read);

        v1.MapPost(Ops.Create.Route, async (HttpContext http, Api.CreateUserRequest request, UsersService users, RateLimits limits, CancellationToken ct) =>
        {
            var limit = limits.Acquire(RateLimitPolicies.SignUpPerIp, ConnectionIp.Key(http));
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Created(http, await users.CreateAsync(Project(http), request.Email, request.Password, request.Name, Me(http), ct), User);
        })
            .WithName(Ops.Create.Id)
            .RequireRole(Need.Write);

        v1.MapPost(Ops.Block.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            Ok(http, await users.BlockAsync(Project(http), userId, Me(http), ct), User))
            .WithName(Ops.Block.Id)
            .RequireRole(Need.Write);

        v1.MapPost(Ops.Unblock.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            Ok(http, await users.UnblockAsync(Project(http), userId, Me(http), ct), User))
            .WithName(Ops.Unblock.Id)
            .RequireRole(Need.Write);

        v1.MapDelete(Ops.Delete.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            NoContent(http, await users.DeleteAsync(Project(http), userId, Me(http), ct)))
            .WithName(Ops.Delete.Id)
            .RequireRole(Need.Write);

        v1.MapGet(Ops.ListSessions.Route, async (HttpContext http, string userId, string? cursor, int? limit, UsersService users, CancellationToken ct) =>
            Ok(http, await users.ListSessionsAsync(Project(http), userId, cursor, limit, ct), SessionPage))
            .WithName(Ops.ListSessions.Id)
            .RequireRole(Need.Read);

        v1.MapDelete(Ops.DeleteSessions.Route, async (HttpContext http, string userId, UsersService users, CancellationToken ct) =>
            NoContent(http, await users.DeleteSessionsAsync(Project(http), userId, Me(http), ct)))
            .WithName(Ops.DeleteSessions.Id)
            .RequireRole(Need.Write);

        v1.MapDelete(Ops.DeleteSession.Route, async (HttpContext http, string userId, string sessionId, UsersService users, CancellationToken ct) =>
            NoContent(http, await users.DeleteSessionAsync(Project(http), userId, sessionId, Me(http), ct)))
            .WithName(Ops.DeleteSession.Id)
            .RequireRole(Need.Write);

        v1.MapGet(Keys.List.Route, async (HttpContext http, SigningKeys keys, CancellationToken ct) =>
            TypedResults.Ok(SigningKeysView(await keys.ListAsync(Project(http), ct))))
            .WithName(Keys.List.Id)
            .RequireRole(Need.Read);

        v1.MapPost(Keys.Rotate.Route, async (HttpContext http, SigningKeys keys, CancellationToken ct) =>
        {
            await keys.RotateAsync(Project(http), Me(http), ct);
            return TypedResults.Ok(SigningKeysView(await keys.ListAsync(Project(http), ct)));
        })
            .WithName(Keys.Rotate.Id)
            .RequireRole(Need.Owner);
    }

    private static readonly object ProjectKey = new();

    private static string Project(HttpContext http) =>
        http.Items[ProjectKey] as string ?? throw new InvalidOperationException("The route has no role filter.");

    private static Actor Me(HttpContext http) => Actor.User(ConsoleUser.Get(http));

    private static Api.SigningKeys SigningKeysView(IReadOnlyList<SigningKeyRow> rows) => new(
        [.. rows.Select(k => new Api.SigningKey(
            k.Id,
            k.Status == SigningKeyStatuses.Active ? Api.SigningKeyStatus.Active : Api.SigningKeyStatus.Retiring,
            k.CreatedAt,
            k.RetireAfter))]);

    /// <summary>
    /// Resolves <c>X-Orvano-Project</c> (400 when missing) and the console user's role in its org through
    /// <see cref="IConsoleAccess"/>: no role is 404 <c>project_not_found</c>, too little is 403 <c>forbidden</c>.
    /// </summary>
    private static RouteHandlerBuilder RequireRole(this RouteHandlerBuilder builder, Need need) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Request.Headers[OrvanoHeaders.Project] is not [{ Length: > 0 } projectId])
                return ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "Send the project ID in the X-Orvano-Project header.");

            var role = await http.RequestServices.GetRequiredService<IConsoleAccess>().GetProjectRoleAsync(ConsoleUser.Get(http), projectId, http.RequestAborted);
            if (role is not { } granted)
                return ApiProblem.Result(StatusCodes.Status404NotFound, Api.ErrorCode.ProjectNotFound, "No such project.");

            var allowed = need switch
            {
                Need.Read => true,
                Need.Write => granted is OrgRole.Owner or OrgRole.Developer,
                _ => granted is OrgRole.Owner,
            };
            if (!allowed)
            {
                return ApiProblem.Result(StatusCodes.Status403Forbidden, Api.ErrorCode.Forbidden,
                    need == Need.Owner ? "Only owners can rotate signing keys." : "Viewers can look but not change.");
            }

            http.Items[ProjectKey] = projectId;
            return await next(context);
        });
}
