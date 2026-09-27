using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Orvano.Auth.Application;
using Orvano.Core.Http;
using Orvano.Platform.Contracts;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>The signed in user a bearer request acts as, set by <see cref="PublicRequests.RequireUser"/>.</summary>
internal sealed record CurrentUser(Guid UserId, Guid SessionId);

/// <summary>The API key a server request acts as, set by <see cref="PublicRequests.RequireApiKey"/>.</summary>
internal sealed record CurrentKey(Guid KeyId);

/// <summary>
/// Request rules for the public, project scoped routes (spec 0003 AC-4, spec 0004 AC-7): the project from
/// <c>X-Orvano-Project</c> (or the path, for the JWKS) must be servable, and <c>account</c> operations need a valid
/// bearer token of that project whose session is still active.
/// </summary>
internal static class PublicRequests
{
    private static readonly object ProjectKey = new();
    private static readonly object UserKey = new();
    private static readonly object ApiKeyKey = new();

    /// <summary>The servable project this request is for; set by <see cref="RequireProject"/>.</summary>
    public static string Project(HttpContext http) =>
        http.Items[ProjectKey] as string ?? throw new InvalidOperationException("The route has no project filter.");

    /// <summary>The signed in user; set by <see cref="RequireUser"/>.</summary>
    public static CurrentUser User(HttpContext http) =>
        http.Items[UserKey] as CurrentUser ?? throw new InvalidOperationException("The route has no bearer filter.");

    /// <summary>The API key; set by <see cref="RequireApiKey"/>.</summary>
    public static CurrentKey Key(HttpContext http) =>
        http.Items[ApiKeyKey] as CurrentKey ?? throw new InvalidOperationException("The route has no API key filter.");

    /// <summary>
    /// Resolves <c>X-Orvano-Project</c>: 400 when missing, 404 or 409 when not servable (spec 0003 AC-4), and 403
    /// <c>origin_not_allowed</c> when a browser's <c>Origin</c> matches none of the project's web platforms (AC-13). A
    /// request without <c>Origin</c> (servers, mobile and desktop apps) is not checked.
    /// </summary>
    public static TBuilder RequireProject<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Request.Headers[OrvanoHeaders.Project] is not [{ Length: > 0 } projectId])
                return ApiProblem.Result(StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "Send the project ID in the X-Orvano-Project header.");

            var refusal = await ServableAsync(http, projectId) ?? await OriginRefusalAsync(http, projectId);
            if (refusal is not null) return refusal;
            http.Items[ProjectKey] = projectId;
            return await next(context);
        });

    /// <summary>Resolves the <c>{projectId}</c> path value the same way (the JWKS routes).</summary>
    public static TBuilder RequireProjectInPath<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var projectId = http.Request.RouteValues["projectId"] as string ?? "";
            var refusal = await ServableAsync(http, projectId);
            if (refusal is not null) return refusal;
            http.Items[ProjectKey] = projectId;
            return await next(context);
        });

    /// <summary>
    /// Needs <c>Authorization: Bearer &lt;access token&gt;</c> of the request's project whose session is still active
    /// (AC-7): 401 <c>session_required</c> without one, <c>token_expired</c> when expired, else <c>invalid_token</c>.
    /// Add it after <see cref="RequireProject"/>: filters run in the order they are added.
    /// </summary>
    public static TBuilder RequireUser<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var token = BearerToken(http.Request);
            if (token is null)
                return ApiProblem.Result(StatusCodes.Status401Unauthorized, Api.ErrorCode.SessionRequired, "Sign in first: send the access token as Authorization: Bearer.");

            var projectId = Project(http);
            var services = http.RequestServices;
            var check = await services.GetRequiredService<AccessTokens>().ValidateAsync(token, projectId, http.RequestAborted);
            if (check.Identity is not { } identity)
            {
                return check.Rejection == TokenRejection.Expired
                    ? ApiProblem.Result(StatusCodes.Status401Unauthorized, Api.ErrorCode.TokenExpired, "The access token has expired; refresh the session.")
                    : InvalidToken();
            }

            if (!await services.GetRequiredService<SessionChecks>().IsActiveAsync(identity.SessionId, identity.UserId, projectId, http.RequestAborted))
                return InvalidToken();

            http.Items[UserKey] = new CurrentUser(identity.UserId, identity.SessionId);
            return await next(context);
        });

    /// <summary>
    /// Needs <c>X-Orvano-Key</c> of the request's project holding <paramref name="scope"/> (spec 0003 AC-5, AC-12):
    /// 401 <c>invalid_api_key</c> for a missing, unknown, expired, or other project's key, 403
    /// <c>insufficient_scope</c> without the scope. Pass the operation's generated <c>Scope</c>. Add it after
    /// <see cref="RequireProject"/>.
    /// </summary>
    public static TBuilder RequireApiKey<TBuilder>(this TBuilder builder, string scope) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Request.Headers[OrvanoHeaders.ApiKey] is not [{ Length: > 0 } secret])
                return InvalidApiKey();

            var key = await http.RequestServices.GetRequiredService<IApiKeyVerifier>().VerifyAsync(Project(http), secret, http.RequestAborted);
            if (!key.Valid || key.KeyId is not { } keyId) return InvalidApiKey();
            if (!key.Scopes.Contains(scope))
                return ApiProblem.Result(StatusCodes.Status403Forbidden, Api.ErrorCode.InsufficientScope, $"The API key lacks the {scope} scope.");

            http.Items[ApiKeyKey] = new CurrentKey(keyId);
            return await next(context);
        });

    /// <summary>
    /// Where the request came from, for the session record (AC-31): <c>X-Orvano-Client-UA</c> and
    /// <c>X-Orvano-Client-IP</c> when present (sent by <c>@orvano/nextjs</c> on the server), else the request's own
    /// user agent and connection IP.
    /// </summary>
    public static ClientInfo Client(HttpContext http)
    {
        var headers = http.Request.Headers;
        var forwardedIp = headers[OrvanoHeaders.ClientIp] is [{ Length: > 0 } ip] && IPAddress.TryParse(ip.Trim(), out var parsed) ? parsed : null;
        var userAgent = headers[OrvanoHeaders.ClientUserAgent] is [{ Length: > 0 } ua] ? ua : headers.UserAgent.ToString();
        return ClientInfo.Of(userAgent, headers[OrvanoHeaders.Sdk].ToString(), forwardedIp ?? ConnectionIp.Of(http));
    }

    /// <summary>The token of an <c>Authorization: Bearer</c> header, or null when there is none.</summary>
    public static string? BearerToken(HttpRequest request) =>
        request.Headers[OrvanoHeaders.Authorization] is [{ } value]
            && value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && value[7..].Trim() is { Length: > 0 } token
            ? token
            : null;

    private static IResult InvalidApiKey() =>
        ApiProblem.Result(StatusCodes.Status401Unauthorized, Api.ErrorCode.InvalidApiKey, "Send a valid API key of this project as X-Orvano-Key.");

    private static IResult InvalidToken() =>
        ApiProblem.Result(StatusCodes.Status401Unauthorized, Api.ErrorCode.InvalidToken, "The access token is not valid for this project, or its session has ended.");

    private static async Task<IResult?> OriginRefusalAsync(HttpContext http, string projectId) =>
        http.Request.Headers.Origin is not [{ } origin]
            || await http.RequestServices.GetRequiredService<IWebOriginPolicy>().AllowsAsync(projectId, origin, http.RequestAborted)
            ? null
            : ApiProblem.Result(StatusCodes.Status403Forbidden, Api.ErrorCode.OriginNotAllowed, "This origin is not a web platform of the project.");

    private static async Task<IResult?> ServableAsync(HttpContext http, string projectId) =>
        await http.RequestServices.GetRequiredService<IProjectDirectory>().GetServableAsync(projectId, http.RequestAborted) switch
        {
            ProjectLookup.Servable => null,
            ProjectLookup.NotReady => ApiProblem.Result(StatusCodes.Status409Conflict, Api.ErrorCode.ProjectNotReady, "The project is not ready yet."),
            _ => ApiProblem.Result(StatusCodes.Status404NotFound, Api.ErrorCode.ProjectNotFound, "No such project."),
        };
}
