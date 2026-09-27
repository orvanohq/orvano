using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;
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

        v1.MapPatch(Api.AccountOperations.Update.Route, async (HttpContext http, JsonElement body, AccountService accounts, CancellationToken ct) =>
        {
            // Bound as JSON first: a name left out stays, while "name": null removes it.
            if (!TryRead(body, out Api.UpdateAccountRequest? request))
                return ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "Send a JSON object with name and metadata.");
            if (body.TryGetProperty("metadata", out var metadata) && metadata.ValueKind != JsonValueKind.Object)
                return ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "The metadata must be a JSON object.");

            var changes = new AccountChanges(body.TryGetProperty("name", out _), request.Name,
                request.Metadata is null ? null : JsonSerializer.Serialize(request.Metadata));
            var user = PublicRequests.User(http);
            return Ok(http, await accounts.UpdateAsync(PublicRequests.Project(http), user.UserId, changes, ct), User);
        })
            .WithName(Api.AccountOperations.Update.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPut(Api.AccountOperations.UpdatePassword.Route, async (HttpContext http, Api.UpdatePasswordRequest request, AccountService accounts, RateLimits limits, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            var limit = limits.Acquire(RateLimitPolicies.PasswordCheckPerUser, user.UserId.ToString());
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);

            return NoContent(http, await accounts.UpdatePasswordAsync(
                PublicRequests.Project(http), user.UserId, user.SessionId, request.CurrentPassword, request.NewPassword, ct));
        })
            .WithName(Api.AccountOperations.UpdatePassword.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.Delete.Route, async (HttpContext http, Api.DeleteAccountRequest request, AccountService accounts, RateLimits limits, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            var limit = limits.Acquire(RateLimitPolicies.PasswordCheckPerUser, user.UserId.ToString());
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);

            return NoContent(http, await accounts.DeleteAsync(PublicRequests.Project(http), user.UserId, request.Password, ct));
        })
            .WithName(Api.AccountOperations.Delete.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.RefreshSession.Route, async (HttpContext http, Api.RefreshSessionRequest request, SessionService sessions, RateLimits limits, CancellationToken ct) =>
        {
            // Only refreshes answered 401 count against the IP, so a busy server's good refreshes never do.
            var ip = ConnectionIp.Key(http);
            var failures = limits.Check(RateLimitPolicies.FailedRefreshPerIp, ip);
            if (!failures.Allowed) return ApiProblem.RateLimited(http, failures, Api.ErrorCode.RateLimited);
            if (RefreshToken.TryParse(request.RefreshToken, out var token))
            {
                var perSession = limits.Acquire(RateLimitPolicies.RefreshPerSession, token.SessionId.ToString());
                if (!perSession.Allowed) return ApiProblem.RateLimited(http, perSession, Api.ErrorCode.RateLimited);
            }

            var outcome = await sessions.RefreshAsync(PublicRequests.Project(http), request.RefreshToken, PublicRequests.Client(http), ct);
            if (outcome.Failure?.Kind == FailureKind.Unauthorized) limits.Acquire(RateLimitPolicies.FailedRefreshPerIp, ip);
            return Ok(http, outcome, SessionTokens);
        })
            .WithName(Api.AccountOperations.RefreshSession.Id)
            .RequireProject();

        v1.MapGet(Api.AccountOperations.ListSessions.Route, async (HttpContext http, string? cursor, int? limit, SessionService sessions, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            return Ok(http, await sessions.ListAsync(PublicRequests.Project(http), user.UserId, user.SessionId, cursor, limit, ct), SessionPage);
        })
            .WithName(Api.AccountOperations.ListSessions.Id)
            .RequireProject()
            .RequireUser();

        v1.MapDelete(Api.AccountOperations.DeleteCurrentSession.Route, async (HttpContext http, SessionService sessions, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            return NoContent(http, await sessions.SignOutAsync(PublicRequests.Project(http), user.UserId, user.SessionId, ct));
        })
            .WithName(Api.AccountOperations.DeleteCurrentSession.Id)
            .RequireProject()
            .RequireUser();

        v1.MapDelete(Api.AccountOperations.DeleteSession.Route, async (HttpContext http, string sessionId, SessionService sessions, CancellationToken ct) =>
            NoContent(http, await sessions.EndAsync(PublicRequests.Project(http), PublicRequests.User(http).UserId, sessionId, ct)))
            .WithName(Api.AccountOperations.DeleteSession.Id)
            .RequireProject()
            .RequireUser();

        v1.MapDelete(Api.AccountOperations.DeleteOtherSessions.Route, async (HttpContext http, SessionService sessions, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            return NoContent(http, await sessions.EndOthersAsync(PublicRequests.Project(http), user.UserId, user.SessionId, ct));
        })
            .WithName(Api.AccountOperations.DeleteOtherSessions.Id)
            .RequireProject()
            .RequireUser();
    }

    private static bool TryRead<T>(JsonElement body, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? value) where T : class
    {
        value = null;
        if (body.ValueKind != JsonValueKind.Object) return false;
        try
        {
            value = body.Deserialize<T>(JsonSerializerOptions.Web);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
