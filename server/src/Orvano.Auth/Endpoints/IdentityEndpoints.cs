using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Auth.Application;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using static Orvano.Auth.Endpoints.ApiMapping;
using Account = Orvano.Contract.AccountOperations;
using Api = Orvano.Contract;
using Users = Orvano.Contract.UsersOperations;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// Native ID token sign in and identities (spec 0012, AC-9, AC-13, AC-14): the <c>account</c> operations for the
/// signed in user, and the <c>users</c> operations for a server with an API key.
/// </summary>
internal static class IdentityEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost(Account.CreateIdTokenSession.Route, (HttpContext http, IdentityService identities, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.CreateIdTokenSessionRequest>(http, limits, ct, async request =>
            {
                var signIn = limits.Acquire(RateLimitPolicies.SignInPerIp, ConnectionIp.Key(http));
                if (!signIn.Allowed) return ApiProblem.RateLimited(http, signIn, Api.ErrorCode.RateLimited);
                return Created(http, await identities.SignInAsync(
                    PublicRequests.Project(http), Native(request), PublicRequests.Client(http), ConnectionIp.Key(http), ct), AuthResult);
            }, RateLimitPolicies.FailedOAuthRedeemPerIp))
            .WithName(Account.CreateIdTokenSession.Id)
            .RequireProject();

        v1.MapPost(Account.CreateOAuthLinkFlow.Route, async (
            HttpContext http, Api.CreateOAuthLinkFlowRequest request, IdentityService identities, RateLimits limits, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            // Every password sent counts, like the other password checks (spec 0004, rate limits).
            if (request.Password is not null)
            {
                var checkLimit = limits.Acquire(RateLimitPolicies.PasswordCheckPerUser, user.UserId.ToString());
                if (!checkLimit.Allowed) return ApiProblem.RateLimited(http, checkLimit, Api.ErrorCode.RateLimited);
            }

            var outcome = await identities.StartLinkAsync(PublicRequests.Project(http), user.UserId, user.SessionId, ProviderOrNull(request.Provider),
                request.RedirectUrl, request.CodeChallenge, request.Password, ConnectionIp.Key(http), ct);
            return Ok(http, outcome, url => new Api.OAuthFlow(url));
        })
            .WithName(Account.CreateOAuthLinkFlow.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Account.CompleteOAuthLink.Route, (HttpContext http, IdentityService identities, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.CompleteOAuthLinkRequest>(http, limits, ct, async request =>
                Created(http, await identities.CompleteLinkAsync(
                    PublicRequests.Project(http), PublicRequests.User(http).UserId, request.Code, request.CodeVerifier, ct), Identity),
                RateLimitPolicies.FailedOAuthRedeemPerIp))
            .WithName(Account.CompleteOAuthLink.Id)
            .RequireProject()
            .RequireUser();

        v1.MapPost(Account.CreateIdTokenIdentity.Route, (HttpContext http, IdentityService identities, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.CreateIdTokenIdentityRequest>(http, limits, ct, async request =>
            {
                var user = PublicRequests.User(http);
                // Every password sent counts, like the other password checks (spec 0004, rate limits).
                if (request.Password is not null)
                {
                    var checkLimit = limits.Acquire(RateLimitPolicies.PasswordCheckPerUser, user.UserId.ToString());
                    if (!checkLimit.Allowed) return ApiProblem.RateLimited(http, checkLimit, Api.ErrorCode.RateLimited);
                }

                var native = new NativeRequest(ProviderOf(request.Provider), request.IdToken, request.Nonce, request.AuthorizationCode, request.Name);
                return Created(http, await identities.LinkNativeAsync(PublicRequests.Project(http), user.UserId, user.SessionId, native, request.Password, ct),
                    Identity);
            }, RateLimitPolicies.FailedOAuthRedeemPerIp))
            .WithName(Account.CreateIdTokenIdentity.Id)
            .RequireProject()
            .RequireUser();

        v1.MapGet(Account.ListIdentities.Route, async (HttpContext http, IdentityService identities, CancellationToken ct) =>
            Ok(http, await identities.ListAsync(PublicRequests.Project(http), PublicRequests.User(http).UserId.ToString(), ct), IdentityList))
            .WithName(Account.ListIdentities.Id)
            .RequireProject()
            .RequireUser();

        v1.MapDelete(Account.DeleteIdentity.Route, async (HttpContext http, string identityId, IdentityService identities, CancellationToken ct) =>
        {
            var user = PublicRequests.User(http);
            return NoContent(http, await identities.DeleteAsync(
                PublicRequests.Project(http), user.UserId.ToString(), identityId, Identities.UnlinkReason.User, Actor.User(user.UserId), ct));
        })
            .WithName(Account.DeleteIdentity.Id)
            .RequireProject()
            .RequireUser();

        v1.MapGet(Users.ListIdentities.Route, async (HttpContext http, string userId, IdentityService identities, CancellationToken ct) =>
            Ok(http, await identities.ListAsync(PublicRequests.Project(http), userId, ct), IdentityList))
            .WithName(Users.ListIdentities.Id)
            .RequireProject()
            .RequireApiKey(Users.ListIdentities.Scope);

        v1.MapDelete(Users.DeleteIdentity.Route, async (HttpContext http, string userId, string identityId, IdentityService identities, CancellationToken ct) =>
            NoContent(http, await identities.DeleteAsync(
                PublicRequests.Project(http), userId, identityId, Identities.UnlinkReason.Server, Actor.ApiKey(PublicRequests.Key(http).KeyId), ct)))
            .WithName(Users.DeleteIdentity.Id)
            .RequireProject()
            .RequireApiKey(Users.DeleteIdentity.Scope);
    }

    private static NativeRequest Native(Api.CreateIdTokenSessionRequest request) =>
        new(ProviderOf(request.Provider), request.IdToken, request.Nonce, request.AuthorizationCode, request.Name);
}
