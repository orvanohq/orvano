using Microsoft.AspNetCore.Http;
using Orvano.Auth.Contracts;
using Orvano.Core.Http;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// The three callers of an operation that accepts any (spec 0015, AC-22), checked by the same code as
/// <see cref="PublicRequests.RequireUser"/> and <see cref="PublicRequests.RequireApiKey"/>, so every 401 and 403 reads
/// exactly as on the other public routes.
/// </summary>
internal sealed class AppCallerResolver : IAppCallerResolver
{
    public async Task<AppCallerResult> ResolveAsync(HttpContext http, string projectId, string keyScope, CancellationToken ct)
    {
        var headers = http.Request.Headers;
        var hasBearer = headers.ContainsKey(OrvanoHeaders.Authorization);
        var hasKey = headers.ContainsKey(OrvanoHeaders.ApiKey);
        if (hasBearer && hasKey)
        {
            return new AppCallerResult.Rejected(ApiProblem.Result(
                StatusCodes.Status400BadRequest, Api.ErrorCode.InvalidRequest, "Send either Authorization or X-Orvano-Key, not both."));
        }

        if (hasKey)
        {
            if (headers[OrvanoHeaders.ApiKey] is not [{ Length: > 0 } secret]) return new AppCallerResult.Rejected(PublicRequests.InvalidApiKey());
            var (key, keyRefusal) = await PublicRequests.CheckApiKeyAsync(http, projectId, secret, keyScope);
            return key is null ? new AppCallerResult.Rejected(keyRefusal) : new AppCallerResult.Resolved(new AppCaller.ApiKey(key.KeyId));
        }

        if (hasBearer)
        {
            // An Authorization header that is not a bearer token is a bad token, never a silent anonymous call.
            if (PublicRequests.BearerToken(http.Request) is not { } token)
            {
                return new AppCallerResult.Rejected(ApiProblem.Result(
                    StatusCodes.Status401Unauthorized, Api.ErrorCode.InvalidToken, "The access token is not valid for this project, or its session has ended."));
            }

            var (user, userRefusal) = await PublicRequests.CheckBearerAsync(http, projectId, token);
            return user is null
                ? new AppCallerResult.Rejected(userRefusal)
                : new AppCallerResult.Resolved(new AppCaller.AppUser(user.UserId, user.SessionId));
        }

        return new AppCallerResult.Resolved(AppCaller.Anonymous.Instance);
    }
}
