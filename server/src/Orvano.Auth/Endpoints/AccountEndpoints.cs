using System.Diagnostics;
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
            var started = Stopwatch.StartNew();
            var limit = limits.Acquire(ProjectLimits.SignUpPerIp(PublicRequests.Policies(http).Auth), PublicRequests.LimitKey(http));
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);

            var outcome = await accounts.SignUpAsync(
                PublicRequests.Project(http), request.Email, request.Password, request.Name, PublicRequests.Client(http), ct,
                verificationRedirectUrl: request.VerificationRedirectUrl, limitKey: PublicRequests.LimitKey(http));
            // Spec 0014, AC-12: the pending answer is the same for a new and a known email, in time too.
            if (outcome.Value is { VerificationRequired: true }) await EmailRequests.HoldToFloorAsync(http, started, ct);
            return Created(http, outcome, AuthResult);
        })
            .WithName(Api.AccountOperations.Create.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreatePasswordSession.Route, async (HttpContext http, Api.CreatePasswordSessionRequest request, AccountService accounts, RateLimits limits, CancellationToken ct) =>
        {
            var projectId = PublicRequests.Project(http);
            using var failures = SignInLimits.Take(http, limits, PublicRequests.LimitKey(http), PublicRequests.Policies(http).Auth, request.Email, out var refused);
            if (refused is not null) return refused;

            var outcome = await accounts.SignInAsync(
                projectId, request.Email, request.Password, PublicRequests.Client(http), ct, request.VerificationRedirectUrl, PublicRequests.LimitKey(http));
            SignInLimits.Settle(failures!, outcome.Failure);
            return Created(http, outcome, AuthResult);
        })
            .WithName(Api.AccountOperations.CreatePasswordSession.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreateRecovery.Route, async (HttpContext http, Api.CreateRecoveryRequest request, RecoveryService recovery, CancellationToken ct) =>
        {
            var started = Stopwatch.StartNew();
            var outcome = await recovery.RequestAsync(PublicRequests.Project(http), request.Email, request.RedirectUrl, PublicRequests.LimitKey(http), ct);
            return await EmailRequests.OpenAcceptedAsync(http, outcome, started, ct);
        })
            .WithName(Api.AccountOperations.CreateRecovery.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CompleteRecovery.Route, (HttpContext http, RecoveryService recovery, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.CompleteRecoveryRequest>(http, limits, ct, async request =>
                Created(http, await recovery.CompleteAsync(PublicRequests.Project(http), request.Token, request.Password, PublicRequests.Client(http), ct), AuthResult)))
            .WithName(Api.AccountOperations.CompleteRecovery.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreateVerification.Route, async (HttpContext http, Api.CreateVerificationRequest request, VerificationService verification, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            return Accepted(http, await verification.RequestAsync(PublicRequests.Project(http), user.UserId, request.RedirectUrl, Actor.User(user.UserId), PublicRequests.LimitKey(http), ct));
        })
            .WithName(Api.AccountOperations.CreateVerification.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.VerifyEmail.Route, (HttpContext http, VerificationService verification, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.VerifyEmailRequest>(http, limits, ct, async request =>
                Ok(http, await verification.VerifyAsync(PublicRequests.Project(http), request.Token, ct), User)))
            .WithName(Api.AccountOperations.VerifyEmail.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.RejectEmailVerification.Route, (HttpContext http, VerificationService verification, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.RejectEmailVerificationRequest>(http, limits, ct, async request =>
                NoContent(http, await verification.RejectAsync(PublicRequests.Project(http), request.Token, ct))))
            .WithName(Api.AccountOperations.RejectEmailVerification.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreateMagicLink.Route, async (HttpContext http, Api.CreateMagicLinkRequest request, PasswordlessService passwordless, CancellationToken ct) =>
        {
            var started = Stopwatch.StartNew();
            var outcome = await passwordless.RequestLinkAsync(
                PublicRequests.Project(http), request.Email, request.RedirectUrl, request.CreateUser, PublicRequests.LimitKey(http), ct);
            return await EmailRequests.OpenAcceptedAsync(http, outcome, started, ct);
        })
            .WithName(Api.AccountOperations.CreateMagicLink.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreateMagicLinkSession.Route, (HttpContext http, PasswordlessService passwordless, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.CreateMagicLinkSessionRequest>(http, limits, ct, async request =>
                Created(http, await passwordless.SignInWithLinkAsync(
                    PublicRequests.Project(http), request.Token, PublicRequests.Client(http), PublicRequests.LimitKey(http), ct), AuthResult)))
            .WithName(Api.AccountOperations.CreateMagicLinkSession.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreateEmailCode.Route, async (HttpContext http, Api.CreateEmailCodeRequest request, PasswordlessService passwordless, CancellationToken ct) =>
        {
            var started = Stopwatch.StartNew();
            var outcome = await passwordless.RequestCodeAsync(PublicRequests.Project(http), request.Email, request.CreateUser, PublicRequests.LimitKey(http), ct);
            return await EmailRequests.OpenAcceptedAsync(http, outcome, started, ct);
        })
            .WithName(Api.AccountOperations.CreateEmailCode.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreateEmailCodeSession.Route, (HttpContext http, PasswordlessService passwordless, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.CreateEmailCodeSessionRequest>(http, limits, ct, async request =>
                Created(http, await passwordless.SignInWithCodeAsync(
                    PublicRequests.Project(http), request.Email, request.Code, PublicRequests.Client(http), PublicRequests.LimitKey(http), ct), AuthResult)))
            .WithName(Api.AccountOperations.CreateEmailCodeSession.Id)
            .RequireProject();

        v1.MapPut(Api.AccountOperations.UpdateEmail.Route, async (HttpContext http, Api.UpdateEmailRequest request, EmailChangeService emailChange, RateLimits limits, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            // Every attempt counts, like the other password checks (spec 0004, rate limits).
            var limit = limits.Acquire(RateLimitPolicies.PasswordCheckPerUser, user.UserId.ToString());
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);

            return Accepted(http, await emailChange.RequestAsync(
                PublicRequests.Project(http), user.UserId, user.SessionId, request.Email, request.RedirectUrl, request.Password, PublicRequests.LimitKey(http), ct));
        })
            .WithName(Api.AccountOperations.UpdateEmail.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.ConfirmEmailChange.Route, (HttpContext http, EmailChangeService emailChange, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.ConfirmEmailChangeRequest>(http, limits, ct, async request =>
                Ok(http, await emailChange.ConfirmAsync(PublicRequests.Project(http), request.Token, ct), User)))
            .WithName(Api.AccountOperations.ConfirmEmailChange.Id)
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

            return NoContent(http, await accounts.DeleteAsync(PublicRequests.Project(http), user.UserId, user.SessionId, request.Password, ct));
        })
            .WithName(Api.AccountOperations.Delete.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.RefreshSession.Route, async (HttpContext http, Api.RefreshSessionRequest request, SessionService sessions, RateLimits limits, CancellationToken ct) =>
        {
            // Only refreshes answered 401 count against the IP, so a busy server's good refreshes never do.
            var ip = PublicRequests.LimitKey(http);
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
        {
            var user = PublicRequests.User(http);
            return NoContent(http, await sessions.EndAsync(PublicRequests.Project(http), user.UserId, sessionId, Actor.User(user.UserId), ct));
        })
            .WithName(Api.AccountOperations.DeleteSession.Id)
            .RequireProject()
            .RequireUser();

        v1.MapDelete(Api.AccountOperations.DeleteOtherSessions.Route, async (HttpContext http, SessionService sessions, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            return NoContent(http, await sessions.EndAllAsync(PublicRequests.Project(http), user.UserId, user.SessionId, Actor.User(user.UserId), ct));
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
