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
/// The passkey operations of the <c>account</c> service (spec 0013, AC-11, AC-19 to AC-24): sign in, the challenges for
/// step two and step up, registration, and the user's list, rename, and delete. Thin adapters: the limits, one use
/// case, the mapping.
/// </summary>
internal static class PasskeyEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost(Api.AccountOperations.CreatePasskeyChallenge.Route, async (HttpContext http, PasskeyService passkeys, RateLimits limits, CancellationToken ct) =>
        {
            var limit = limits.Acquire(RateLimitPolicies.PasskeyPerIp, ConnectionIp.Key(http));
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Ok(http, await passkeys.CreateSignInChallengeAsync(PublicRequests.Project(http), ct), PasskeyChallenge);
        })
            .WithName(Api.AccountOperations.CreatePasskeyChallenge.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreatePasskeySession.Route, async (
            HttpContext http, Api.CreatePasskeySessionRequest request, PasskeyService passkeys, RateLimits limits, CancellationToken ct) =>
        {
            var ip = ConnectionIp.Key(http);
            var limit = limits.Acquire(RateLimitPolicies.PasskeyPerIp, ip);
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            var outcome = await passkeys.SignInAsync(
                PublicRequests.Project(http), request.ChallengeId, Assertion(request.Credential), PublicRequests.Client(http), ip, ct);
            return Created(http, outcome, AuthResult);
        })
            .WithName(Api.AccountOperations.CreatePasskeySession.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreateMfaPasskeyChallenge.Route, async (
            HttpContext http, Api.CreateMfaPasskeyChallengeRequest request, PasskeyService passkeys, RateLimits limits, CancellationToken ct) =>
        {
            var ip = ConnectionIp.Key(http);
            var limit = limits.Acquire(RateLimitPolicies.PasskeyPerIp, ip);
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Ok(http, await passkeys.CreateMfaChallengeAsync(PublicRequests.Project(http), request.Ticket, ip, ct), PasskeyChallenge);
        })
            .WithName(Api.AccountOperations.CreateMfaPasskeyChallenge.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreateStepUpPasskeyChallenge.Route, async (HttpContext http, PasskeyService passkeys, RateLimits limits, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            var perIp = limits.Acquire(RateLimitPolicies.PasskeyPerIp, ConnectionIp.Key(http));
            if (!perIp.Allowed) return ApiProblem.RateLimited(http, perIp, Api.ErrorCode.RateLimited);
            var perUser = limits.Acquire(RateLimitPolicies.PasskeyChallengePerUser, user.UserId.ToString());
            if (!perUser.Allowed) return ApiProblem.RateLimited(http, perUser, Api.ErrorCode.RateLimited);
            return Ok(http, await passkeys.CreateStepUpChallengeAsync(PublicRequests.Project(http), user.UserId, ct), PasskeyChallenge);
        })
            .WithName(Api.AccountOperations.CreateStepUpPasskeyChallenge.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.CreatePasskeyRegistration.Route, async (
            HttpContext http, Api.CreatePasskeyRegistrationRequest request, PasskeyService passkeys, RateLimits limits, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            var enroll = limits.Acquire(RateLimitPolicies.MfaEnrollPerUser, user.UserId.ToString());
            if (!enroll.Allowed) return ApiProblem.RateLimited(http, enroll, Api.ErrorCode.RateLimited);
            var perUser = limits.Acquire(RateLimitPolicies.PasskeyChallengePerUser, user.UserId.ToString());
            if (!perUser.Allowed) return ApiProblem.RateLimited(http, perUser, Api.ErrorCode.RateLimited);
            // Every password sent counts, like the other password checks (spec 0004, rate limits).
            if (request.Password is not null)
            {
                var checkLimit = limits.Acquire(RateLimitPolicies.PasswordCheckPerUser, user.UserId.ToString());
                if (!checkLimit.Allowed) return ApiProblem.RateLimited(http, checkLimit, Api.ErrorCode.RateLimited);
            }

            return Ok(http, await passkeys.CreateRegistrationAsync(PublicRequests.Project(http), user.UserId, user.SessionId, request.Password, ct),
                PasskeyRegistration);
        })
            .WithName(Api.AccountOperations.CreatePasskeyRegistration.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.CompletePasskeyRegistration.Route, async (
            HttpContext http, Api.CompletePasskeyRegistrationRequest request, PasskeyService passkeys, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            var outcome = await passkeys.CompleteRegistrationAsync(
                PublicRequests.Project(http), user.UserId, request.ChallengeId, Attestation(request.Credential), request.Name, ct);
            return Created(http, outcome, Passkey);
        })
            .WithName(Api.AccountOperations.CompletePasskeyRegistration.Id)
            .RequireProject()
            .RequireUser();

        v1.MapGet(Api.AccountOperations.ListPasskeys.Route, async (HttpContext http, PasskeyService passkeys, CancellationToken ct) =>
            Ok(http, await passkeys.ListAsync(PublicRequests.Project(http), PublicRequests.User(http).UserId, ct), PasskeyList))
            .WithName(Api.AccountOperations.ListPasskeys.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPatch(Api.AccountOperations.UpdatePasskey.Route, async (
            HttpContext http, string passkeyId, Api.UpdatePasskeyRequest request, PasskeyService passkeys, CancellationToken ct) =>
            Ok(http, await passkeys.RenameAsync(PublicRequests.Project(http), PublicRequests.User(http).UserId, passkeyId, request.Name, ct), Passkey))
            .WithName(Api.AccountOperations.UpdatePasskey.Id)
            .RequireProject()
            .RequireUser();

        v1.MapDelete(Api.AccountOperations.DeletePasskey.Route, async (HttpContext http, string passkeyId, PasskeyService passkeys, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            return NoContent(http, await passkeys.DeleteOwnAsync(PublicRequests.Project(http), user.UserId, user.SessionId, passkeyId, ct));
        })
            .WithName(Api.AccountOperations.DeletePasskey.Id)
            .RequireProject()
            .RequireUser();
    }
}
