using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using Orvano.Auth.Application;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// Provider sign in by redirect (spec 0012): <c>account.createOAuthFlow</c>, <c>account.createOAuthSession</c>, and
/// the provider callback (<c>oauth.callback</c>, <c>oauth.callbackForm</c>), which needs no credentials and has no
/// origin check, since the provider's redirect brings the browser there.
/// </summary>
internal static class OAuthEndpoints
{
    /// <summary>The callback's operation IDs. The <c>browser</c> audience is not generated, so they are named here.</summary>
    public const string CallbackId = "oauth.callback";
    public const string CallbackFormId = "oauth.callbackForm";

    private const string PageHtml =
        """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Sign in didn't finish</title>
        <style>body{font-family:system-ui,sans-serif;margin:0;padding:2rem 1rem;color:#1a1a1a;background:#fff}main{max-width:32rem;margin:0 auto}@media (prefers-color-scheme:dark){body{color:#f2f2f2;background:#111}}</style>
        </head><body><main><h1>Sign in didn't finish</h1><p>Go back to the app and try again.</p></main></body></html>
        """;

    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost(Api.AccountOperations.CreateOAuthFlow.Route, async (HttpContext http, Api.CreateOAuthFlowRequest request, OAuthService oauth, CancellationToken ct) =>
        {
            var provider = request.Provider == Api.OAuthProvider.Unknown ? (Domain.OAuthProvider?)null : ProviderOf(request.Provider);
            var outcome = await oauth.StartAsync(PublicRequests.Project(http), provider, request.RedirectUrl, request.CodeChallenge, ConnectionIp.Key(http), ct);
            return Ok(http, outcome, url => new Api.OAuthFlow(url));
        })
            .WithName(Api.AccountOperations.CreateOAuthFlow.Id)
            .RequireProject();

        v1.MapPost(Api.AccountOperations.CreateOAuthSession.Route, (HttpContext http, OAuthService oauth, RateLimits limits, CancellationToken ct) =>
            EmailRequests.RedeemAsync<Api.CreateOAuthSessionRequest>(http, limits, ct, async request =>
            {
                var signIn = limits.Acquire(RateLimitPolicies.SignInPerIp, ConnectionIp.Key(http));
                if (!signIn.Allowed) return ApiProblem.RateLimited(http, signIn, Api.ErrorCode.RateLimited);
                return Created(http, await oauth.RedeemAsync(
                    PublicRequests.Project(http), request.Code, request.CodeVerifier, PublicRequests.Client(http), ConnectionIp.Key(http), ct), AuthResult);
            }, RateLimitPolicies.FailedOAuthRedeemPerIp))
            .WithName(Api.AccountOperations.CreateOAuthSession.Id)
            .RequireProject();

        v1.MapGet(OAuthCallbacks.Route, async (HttpContext http, string projectId, string provider, OAuthService oauth, CancellationToken ct) =>
        {
            var query = http.Request.Query;
            var answer = await oauth.CallbackAsync(
                projectId, provider, Single(query["state"]), Single(query["code"]), Single(query["error"]), appleUser: null, ConnectionIp.Key(http), ct);
            return Answer(http, answer, StatusCodes.Status302Found);
        })
            .WithName(CallbackId);

        v1.MapPost(OAuthCallbacks.Route, async (HttpContext http, string projectId, string provider, OAuthService oauth, CancellationToken ct) =>
        {
            if (!http.Request.HasFormContentType) return Answer(http, new CallbackAnswer.Page(StatusCodes.Status400BadRequest), StatusCodes.Status303SeeOther);
            var form = await http.Request.ReadFormAsync(ct);
            var answer = await oauth.CallbackAsync(
                projectId, provider, Single(form["state"]), Single(form["code"]), Single(form["error"]), Single(form["user"]), ConnectionIp.Key(http), ct);
            return Answer(http, answer, StatusCodes.Status303SeeOther);
        })
            .WithName(CallbackFormId);
    }

    /// <summary>
    /// The callback's answer (AC-5): a redirect (302 after GET, 303 after POST), or the static page. Neither is cached
    /// or sends a referrer; the page also forbids everything but its own inline style and asks not to be indexed.
    /// </summary>
    private static IResult Answer(HttpContext http, CallbackAnswer answer, int redirectStatus)
    {
        var headers = http.Response.Headers;
        headers.CacheControl = "no-store";
        headers["Referrer-Policy"] = "no-referrer";
        switch (answer)
        {
            case CallbackAnswer.Redirect redirect:
                headers.Location = redirect.Url;
                return TypedResults.StatusCode(redirectStatus);
            case CallbackAnswer.Page page:
                headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; style-src 'unsafe-inline'";
                headers["X-Robots-Tag"] = "noindex";
                return TypedResults.Content(PageHtml, "text/html; charset=utf-8", Encoding.UTF8, page.Status);
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, null);
        }
    }

    private static string? Single(Microsoft.Extensions.Primitives.StringValues values) => values.Count == 1 ? values[0] : null;
}
