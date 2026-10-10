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
                HttpContext http, string? email, string? status, DateTimeOffset? createdAfter, DateTimeOffset? createdBefore, bool? emailVerified, string? mfa,
                bool? anonymous, string? cursor, int? limit, UsersService users, CancellationToken ct) =>
            Ok(http, await users.ListAsync(
                PublicRequests.Project(http), new UserFilter(email, status, createdAfter, createdBefore, emailVerified, mfa, anonymous), cursor, limit, ct), UserPage))
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
            var limit = limits.Acquire(ProjectLimits.SignUpPerIp(PublicRequests.Policies(http).Auth), PublicRequests.LimitKey(http));
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);

            var outcome = await users.CreateAsync(
                PublicRequests.Project(http), request.Email, request.Password, request.Name, KeyActor(http), ct, request.EmailVerified ?? false);
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

        v1.MapPut(Ops.UpdateEmailVerification.Route, async (HttpContext http, string userId, Api.UpdateEmailVerificationRequest request, UsersService users, CancellationToken ct) =>
            Ok(http, await users.UpdateEmailVerificationAsync(PublicRequests.Project(http), userId, request.Verified, KeyActor(http), ct), User))
            .WithName(Ops.UpdateEmailVerification.Id)
            .RequireProject()
            .RequireApiKey(Ops.UpdateEmailVerification.Scope);

        v1.MapPost(Ops.CreateVerification.Route, async (HttpContext http, string userId, Api.CreateUserVerificationRequest request, UsersService users, CancellationToken ct) =>
            Accepted(http, await users.CreateVerificationAsync(PublicRequests.Project(http), userId, request.RedirectUrl, KeyActor(http), PublicRequests.LimitKey(http), ct)))
            .WithName(Ops.CreateVerification.Id)
            .RequireProject()
            .RequireApiKey(Ops.CreateVerification.Scope);

        v1.MapPost(Ops.CreateRecovery.Route, async (HttpContext http, string userId, Api.CreateUserRecoveryRequest request, UsersService users, CancellationToken ct) =>
            Accepted(http, await users.CreateRecoveryAsync(PublicRequests.Project(http), userId, request.RedirectUrl, KeyActor(http), PublicRequests.LimitKey(http), ct)))
            .WithName(Ops.CreateRecovery.Id)
            .RequireProject()
            .RequireApiKey(Ops.CreateRecovery.Scope);

        v1.MapPut(Ops.UpdateEmail.Route, async (HttpContext http, string userId, Api.UpdateUserEmailRequest request, UsersService users, CancellationToken ct) =>
            Ok(http, await users.UpdateEmailAsync(PublicRequests.Project(http), userId, request.Email, request.EmailVerified ?? false, KeyActor(http), ct), User))
            .WithName(Ops.UpdateEmail.Id)
            .RequireProject()
            .RequireApiKey(Ops.UpdateEmail.Scope);

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

        // Spec 0013, AC-27: a user's MFA state, a user who lost every factor, and their passkeys.
        v1.MapGet(Ops.GetMfa.Route, async (HttpContext http, string userId, MfaService mfa, CancellationToken ct) =>
            Ok(http, Guid.TryParse(userId, out var id) ? await mfa.GetAsync(PublicRequests.Project(http), id, ct) : Failure.UserNotFound, MfaStatus))
            .WithName(Ops.GetMfa.Id)
            .RequireProject()
            .RequireApiKey(Ops.GetMfa.Scope);

        v1.MapPost(Ops.ResetMfa.Route, async (HttpContext http, string userId, MfaResets resets, CancellationToken ct) =>
            NoContent(http, await resets.ResetAsync(PublicRequests.Project(http), userId, KeyActor(http), ct)))
            .WithName(Ops.ResetMfa.Id)
            .RequireProject()
            .RequireApiKey(Ops.ResetMfa.Scope);

        v1.MapGet(Ops.ListPasskeys.Route, async (HttpContext http, string userId, PasskeyService passkeys, CancellationToken ct) =>
            Ok(http, Guid.TryParse(userId, out var id) ? await passkeys.ListAsync(PublicRequests.Project(http), id, ct) : Failure.UserNotFound, PasskeyList))
            .WithName(Ops.ListPasskeys.Id)
            .RequireProject()
            .RequireApiKey(Ops.ListPasskeys.Scope);

        v1.MapDelete(Ops.DeletePasskey.Route, async (HttpContext http, string userId, string passkeyId, PasskeyService passkeys, CancellationToken ct) =>
            NoContent(http, Guid.TryParse(userId, out var id)
                ? await passkeys.DeleteAsync(PublicRequests.Project(http), id, passkeyId, KeyActor(http), MfaResets.ByServer, ct)
                : Failure.UserNotFound))
            .WithName(Ops.DeletePasskey.Id)
            .RequireProject()
            .RequireApiKey(Ops.DeletePasskey.Scope);
    }

    private static Actor KeyActor(HttpContext http) => Actor.ApiKey(PublicRequests.Key(http).KeyId);
}
