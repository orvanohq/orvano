using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Auth.Application;
using Orvano.Core.RateLimiting;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// The MFA operations of the <c>account</c> service (spec 0013): the second step of sign in, the user's MFA state, TOTP
/// enrollment and removal, recovery codes, and step up. Thin adapters: the request, one use case, the mapping.
/// </summary>
internal static class MfaEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost(Api.AccountOperations.CreateMfaSession.Route, async (HttpContext http, Api.CreateMfaSessionRequest request, MfaService mfa, CancellationToken ct) =>
        {
            var answer = new FactorAnswer(request.TotpCode, request.RecoveryCode, PasskeyAnswer(request.Passkey));
            return Created(http, await mfa.CompleteAsync(PublicRequests.Project(http), request.Ticket, answer, PublicRequests.LimitKey(http), ct), AuthResult);
        })
            .WithName(Api.AccountOperations.CreateMfaSession.Id)
            .RequireProject();

        v1.MapGet(Api.AccountOperations.GetMfa.Route, async (HttpContext http, MfaService mfa, CancellationToken ct) =>
            Ok(http, await mfa.GetAsync(PublicRequests.Project(http), PublicRequests.User(http).UserId, ct), MfaStatus))
            .WithName(Api.AccountOperations.GetMfa.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.CreateTotp.Route, async (HttpContext http, Api.CreateTotpRequest request, MfaService mfa, RateLimits limits, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);

            return Created(http, await mfa.CreateTotpAsync(PublicRequests.Project(http), user.UserId, user.SessionId, request.Password, ct), TotpSetup);
        })
            .WithName(Api.AccountOperations.CreateTotp.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.ConfirmTotp.Route, async (HttpContext http, Api.ConfirmTotpRequest request, MfaService mfa, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            return Ok(http, await mfa.ConfirmTotpAsync(PublicRequests.Project(http), user.UserId, user.SessionId, request.Code, PublicRequests.LimitKey(http), ct), TotpConfirmation);
        })
            .WithName(Api.AccountOperations.ConfirmTotp.Id)
            .RequireProject()
            .RequireUser();

        v1.MapDelete(Api.AccountOperations.DeleteTotp.Route, async (HttpContext http, MfaService mfa, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            return NoContent(http, await mfa.DeleteTotpAsync(PublicRequests.Project(http), user.UserId, user.SessionId, ct));
        })
            .WithName(Api.AccountOperations.DeleteTotp.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.CreateRecoveryCodes.Route, async (HttpContext http, MfaService mfa, RateLimits limits, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);

            return Created(http, await mfa.CreateRecoveryCodesAsync(PublicRequests.Project(http), user.UserId, user.SessionId, ct),
                codes => new Api.RecoveryCodes(codes));
        })
            .WithName(Api.AccountOperations.CreateRecoveryCodes.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Api.AccountOperations.VerifyMfa.Route, async (HttpContext http, Api.VerifyMfaRequest request, MfaService mfa, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            var answer = new FactorAnswer(request.TotpCode, request.RecoveryCode, PasskeyAnswer(request.Passkey));
            return Ok(http, await mfa.VerifyAsync(PublicRequests.Project(http), user.UserId, user.SessionId, answer, PublicRequests.LimitKey(http), ct), RaisedSession);
        })
            .WithName(Api.AccountOperations.VerifyMfa.Id)
            .RequireProject()
            .RequireUser();
    }
}
