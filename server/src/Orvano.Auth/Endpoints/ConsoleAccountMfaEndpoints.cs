using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Auth.Application;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using Orvano.Platform.Contracts;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;
using Ops = Orvano.Contract.ConsoleAccountOperations;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// MFA and passkeys for console accounts (spec 0013, AC-41, AC-42): the <c>consoleAccount.*</c> twins of the
/// <c>account.*</c> MFA and passkey operations, for project <c>console</c>, with the same use cases and limits. The
/// ticket between the two sign in steps lives only in the <c>orvano_console_mfa</c> cookie, and operations that raise
/// the session's strength set a new <c>orvano_console</c> cookie instead of returning tokens.
/// </summary>
internal static class ConsoleAccountMfaEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        MapSignIn(v1);
        MapMfa(v1);
        MapPasskeys(v1);
    }

    /// <summary>The open routes: the second step of sign in and passkey sign in (listed in <c>ConsoleSessions.Open</c>).</summary>
    private static void MapSignIn(RouteGroupBuilder v1)
    {
        v1.MapPost(Ops.CreateMfaSession.Route, async (
            HttpContext http, Api.CreateConsoleMfaSessionRequest request, MfaService mfa, IInstallAdmins admins, PublicUrl publicUrl, CancellationToken ct) =>
        {
            if (ConsoleCookies.MfaTicket(http) is not { } ticket)
            {
                ConsoleCookies.ClearMfa(http, publicUrl);
                return Problem(http, Failure.InvalidMfaTicket);
            }

            var answer = new FactorAnswer(request.TotpCode, request.RecoveryCode, PasskeyAnswer(request.Passkey));
            var outcome = await mfa.CompleteAsync(ConsoleProject.Id, ticket, answer, ConnectionIp.Key(http), ct,
                ticketEnded: () => ConsoleCookies.ClearMfa(http, publicUrl));
            if (!outcome.Succeeded)
            {
                if (outcome.Failure!.Code == Api.ErrorCode.InvalidMfaTicket) ConsoleCookies.ClearMfa(http, publicUrl);
                return Problem(http, outcome.Failure);
            }

            ConsoleCookies.ClearMfa(http, publicUrl);
            return await SignedInAsync(http, outcome.Value!, admins, publicUrl, ct);
        })
            .WithName(Ops.CreateMfaSession.Id);

        v1.MapPost(Ops.CreateMfaPasskeyChallenge.Route, async (HttpContext http, PasskeyService passkeys, RateLimits limits, PublicUrl publicUrl, CancellationToken ct) =>
        {
            var ip = ConnectionIp.Key(http);
            var limit = limits.Acquire(RateLimitPolicies.PasskeyPerIp, ip);
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            if (ConsoleCookies.MfaTicket(http) is not { } ticket)
            {
                ConsoleCookies.ClearMfa(http, publicUrl);
                return Problem(http, Failure.InvalidMfaTicket);
            }

            return Ok(http, await passkeys.CreateMfaChallengeAsync(ConsoleProject.Id, ticket, ip, ct), PasskeyChallenge);
        })
            .WithName(Ops.CreateMfaPasskeyChallenge.Id);

        v1.MapPost(Ops.CreatePasskeyChallenge.Route, async (HttpContext http, PasskeyService passkeys, RateLimits limits, CancellationToken ct) =>
        {
            var limit = limits.Acquire(RateLimitPolicies.PasskeyPerIp, ConnectionIp.Key(http));
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Ok(http, await passkeys.CreateSignInChallengeAsync(ConsoleProject.Id, ct), PasskeyChallenge);
        })
            .WithName(Ops.CreatePasskeyChallenge.Id);

        v1.MapPost(Ops.CreatePasskeySession.Route, async (
            HttpContext http, Api.CreatePasskeySessionRequest request, PasskeyService passkeys, IInstallAdmins admins, RateLimits limits, PublicUrl publicUrl,
            CancellationToken ct) =>
        {
            var ip = ConnectionIp.Key(http);
            var limit = limits.Acquire(RateLimitPolicies.PasskeyPerIp, ip);
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            var outcome = await passkeys.SignInAsync(ConsoleProject.Id, request.ChallengeId, Assertion(request.Credential), PublicRequests.Client(http), ip, ct);
            return outcome.Succeeded ? await SignedInAsync(http, outcome.Value!, admins, publicUrl, ct) : Problem(http, outcome.Failure!);
        })
            .WithName(Ops.CreatePasskeySession.Id);
    }

    /// <summary>The account's MFA state, TOTP, recovery codes, and step up (AC-42).</summary>
    private static void MapMfa(RouteGroupBuilder v1)
    {
        v1.MapGet(Ops.GetMfa.Route, async (HttpContext http, MfaService mfa, CancellationToken ct) =>
            Ok(http, await mfa.GetAsync(ConsoleProject.Id, ConsoleUser.Get(http), ct), MfaStatus))
            .WithName(Ops.GetMfa.Id);

        v1.MapPost(Ops.CreateTotp.Route, async (HttpContext http, MfaService mfa, RateLimits limits, CancellationToken ct) =>
        {
            var userId = ConsoleUser.Get(http);
            var limit = limits.Acquire(RateLimitPolicies.MfaEnrollPerUser, userId.ToString());
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Created(http, await mfa.CreateTotpAsync(ConsoleProject.Id, userId, ConsoleUser.GetSession(http), ct), TotpSetup);
        })
            .WithName(Ops.CreateTotp.Id);

        v1.MapPost(Ops.ConfirmTotp.Route, async (HttpContext http, Api.ConfirmTotpRequest request, MfaService mfa, PublicUrl publicUrl, CancellationToken ct) =>
        {
            var outcome = await mfa.ConfirmTotpAsync(ConsoleProject.Id, ConsoleUser.Get(http), ConsoleUser.GetSession(http), request.Code, ct);
            if (!outcome.Succeeded) return Problem(http, outcome.Failure!);
            ConsoleCookies.Set(http, outcome.Value!.Session, publicUrl);
            return TypedResults.Ok(new Api.ConsoleTotpConfirmation(outcome.Value.RecoveryCodes));
        })
            .WithName(Ops.ConfirmTotp.Id);

        v1.MapDelete(Ops.DeleteTotp.Route, async (HttpContext http, MfaService mfa, CancellationToken ct) =>
            NoContent(http, await mfa.DeleteTotpAsync(ConsoleProject.Id, ConsoleUser.Get(http), ConsoleUser.GetSession(http), ct)))
            .WithName(Ops.DeleteTotp.Id);

        v1.MapPost(Ops.CreateRecoveryCodes.Route, async (HttpContext http, MfaService mfa, RateLimits limits, CancellationToken ct) =>
        {
            var userId = ConsoleUser.Get(http);
            var limit = limits.Acquire(RateLimitPolicies.MfaEnrollPerUser, userId.ToString());
            if (!limit.Allowed) return ApiProblem.RateLimited(http, limit, Api.ErrorCode.RateLimited);
            return Created(http, await mfa.CreateRecoveryCodesAsync(ConsoleProject.Id, userId, ConsoleUser.GetSession(http), ct),
                codes => new Api.RecoveryCodes(codes));
        })
            .WithName(Ops.CreateRecoveryCodes.Id);

        v1.MapPost(Ops.VerifyMfa.Route, async (HttpContext http, Api.VerifyMfaRequest request, MfaService mfa, PublicUrl publicUrl, CancellationToken ct) =>
        {
            var answer = new FactorAnswer(request.TotpCode, request.RecoveryCode, PasskeyAnswer(request.Passkey));
            var outcome = await mfa.VerifyAsync(ConsoleProject.Id, ConsoleUser.Get(http), ConsoleUser.GetSession(http), answer, ct);
            if (!outcome.Succeeded) return Problem(http, outcome.Failure!);
            ConsoleCookies.Set(http, outcome.Value!, publicUrl);
            return TypedResults.NoContent();
        })
            .WithName(Ops.VerifyMfa.Id);

        v1.MapPost(Ops.CreateStepUpPasskeyChallenge.Route, async (HttpContext http, PasskeyService passkeys, RateLimits limits, CancellationToken ct) =>
        {
            var userId = ConsoleUser.Get(http);
            var perIp = limits.Acquire(RateLimitPolicies.PasskeyPerIp, ConnectionIp.Key(http));
            if (!perIp.Allowed) return ApiProblem.RateLimited(http, perIp, Api.ErrorCode.RateLimited);
            var perUser = limits.Acquire(RateLimitPolicies.PasskeyChallengePerUser, userId.ToString());
            if (!perUser.Allowed) return ApiProblem.RateLimited(http, perUser, Api.ErrorCode.RateLimited);
            return Ok(http, await passkeys.CreateStepUpChallengeAsync(ConsoleProject.Id, userId, ct), PasskeyChallenge);
        })
            .WithName(Ops.CreateStepUpPasskeyChallenge.Id);
    }

    /// <summary>The account's passkeys: registration, list, rename, and delete (AC-42).</summary>
    private static void MapPasskeys(RouteGroupBuilder v1)
    {
        v1.MapPost(Ops.CreatePasskeyRegistration.Route, async (HttpContext http, PasskeyService passkeys, RateLimits limits, CancellationToken ct) =>
        {
            var userId = ConsoleUser.Get(http);
            var enroll = limits.Acquire(RateLimitPolicies.MfaEnrollPerUser, userId.ToString());
            if (!enroll.Allowed) return ApiProblem.RateLimited(http, enroll, Api.ErrorCode.RateLimited);
            var perUser = limits.Acquire(RateLimitPolicies.PasskeyChallengePerUser, userId.ToString());
            if (!perUser.Allowed) return ApiProblem.RateLimited(http, perUser, Api.ErrorCode.RateLimited);
            return Ok(http, await passkeys.CreateRegistrationAsync(ConsoleProject.Id, userId, ConsoleUser.GetSession(http), ct), PasskeyRegistration);
        })
            .WithName(Ops.CreatePasskeyRegistration.Id);

        v1.MapPost(Ops.CompletePasskeyRegistration.Route, async (
            HttpContext http, Api.CompletePasskeyRegistrationRequest request, PasskeyService passkeys, CancellationToken ct) =>
        {
            var outcome = await passkeys.CompleteRegistrationAsync(ConsoleProject.Id, ConsoleUser.Get(http), ConsoleUser.GetSession(http),
                request.ChallengeId, Attestation(request.Credential), request.Name, ct);
            return Created(http, outcome, Passkey);
        })
            .WithName(Ops.CompletePasskeyRegistration.Id);

        v1.MapGet(Ops.ListPasskeys.Route, async (HttpContext http, PasskeyService passkeys, CancellationToken ct) =>
            Ok(http, await passkeys.ListAsync(ConsoleProject.Id, ConsoleUser.Get(http), ct), PasskeyList))
            .WithName(Ops.ListPasskeys.Id);

        v1.MapPatch(Ops.UpdatePasskey.Route, async (
            HttpContext http, string passkeyId, Api.UpdatePasskeyRequest request, PasskeyService passkeys, CancellationToken ct) =>
            Ok(http, await passkeys.RenameAsync(ConsoleProject.Id, ConsoleUser.Get(http), passkeyId, request.Name, ct), Passkey))
            .WithName(Ops.UpdatePasskey.Id);

        v1.MapDelete(Ops.DeletePasskey.Route, async (HttpContext http, string passkeyId, PasskeyService passkeys, CancellationToken ct) =>
            NoContent(http, await passkeys.DeleteOwnAsync(ConsoleProject.Id, ConsoleUser.Get(http), ConsoleUser.GetSession(http), passkeyId, ct)))
            .WithName(Ops.DeletePasskey.Id);
    }

    /// <summary>Sets both session cookies for a finished sign in and answers 201 with the console account.</summary>
    private static async Task<IResult> SignedInAsync(HttpContext http, SignedIn signedIn, IInstallAdmins admins, PublicUrl publicUrl, CancellationToken ct)
    {
        ConsoleCookies.Set(http, signedIn.Session!, publicUrl);
        return TypedResults.Created((string?)null, ConsoleAccount(signedIn.User!, await admins.IsInstallAdminAsync(signedIn.User!.Id, ct)));
    }
}
