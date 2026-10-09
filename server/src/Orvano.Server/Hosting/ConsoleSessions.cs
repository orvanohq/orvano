using Orvano.Auth.Contracts;
using Orvano.Contract;
using Orvano.Core.Http;

namespace Orvano.Server.Hosting;

/// <summary>
/// The <c>/v1/console</c> rules, in order (spec 0004, AC-27, AC-28; spec 0001, AC-17):
/// <list type="number">
/// <item>CSRF: a request with an unsafe method needs <c>Sec-Fetch-Site: same-origin</c>, or, when the browser sent no
/// <c>Sec-Fetch-Site</c>, an <c>Origin</c> equal to <c>ORVANO_PUBLIC_URL</c>'s; else 403 <c>csrf_rejected</c>. This
/// covers sign up and sign in too.</item>
/// <item>Session: every console route but the four account session operations, <c>consoleInstall.getSetup</c>, and
/// <c>consoleInvitations.preview</c>
/// needs a valid <c>orvano_console</c> cookie and neither an API key nor a bearer token; else 401
/// <c>console_session_required</c>, or <c>token_expired</c> for an expired one (the console client refreshes). A
/// valid session sets the <see cref="ConsoleUser"/> the endpoints act as.</item>
/// </list>
/// </summary>
internal static class ConsoleSessions
{
    /// <summary>The console routes that need no session: method and path under the host.</summary>
    private static readonly (string Method, PathString Path)[] Open =
    [
        (ConsoleInstallOperations.GetSetup.Method, "/v1" + ConsoleInstallOperations.GetSetup.Route),
        (ConsoleInvitationsOperations.Preview.Method, "/v1" + ConsoleInvitationsOperations.Preview.Route),
        (ConsoleAccountOperations.Create.Method, "/v1" + ConsoleAccountOperations.Create.Route),
        (ConsoleAccountOperations.CreateSession.Method, "/v1" + ConsoleAccountOperations.CreateSession.Route),
        (ConsoleAccountOperations.RefreshSession.Method, "/v1" + ConsoleAccountOperations.RefreshSession.Route),
        (ConsoleAccountOperations.DeleteSession.Method, "/v1" + ConsoleAccountOperations.DeleteSession.Route),
        (ConsoleAccountOperations.CreateMfaSession.Method, "/v1" + ConsoleAccountOperations.CreateMfaSession.Route),
        (ConsoleAccountOperations.CreateMfaPasskeyChallenge.Method, "/v1" + ConsoleAccountOperations.CreateMfaPasskeyChallenge.Route),
        (ConsoleAccountOperations.CreatePasskeyChallenge.Method, "/v1" + ConsoleAccountOperations.CreatePasskeyChallenge.Route),
        (ConsoleAccountOperations.CreatePasskeySession.Method, "/v1" + ConsoleAccountOperations.CreatePasskeySession.Route),
    ];

    public static IApplicationBuilder UseConsoleSessions(this IApplicationBuilder app)
    {
        var publicOrigin = app.ApplicationServices.GetRequiredService<PublicUrl>().Origin;
        return app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (!request.Path.StartsWithSegments("/v1/console"))
            {
                await next(context);
                return;
            }

            if (!IsSafe(request.Method) && !SameOrigin(request, publicOrigin))
            {
                await Problems.WriteAsync(context, StatusCodes.Status403Forbidden, ErrorCode.CsrfRejected,
                    "Console changes are accepted only from the console's own origin.");
                return;
            }

            if (Open.Any(o => HttpMethods.Equals(o.Method, request.Method) && request.Path.Equals(o.Path, StringComparison.OrdinalIgnoreCase)))
            {
                await next(context);
                return;
            }

            var check = request.Headers.ContainsKey(OrvanoHeaders.ApiKey) || request.Headers.ContainsKey(OrvanoHeaders.Authorization)
                ? new ConsoleSessionCheck(ConsoleSessionStatus.Invalid, Guid.Empty)
                : await context.RequestServices.GetRequiredService<IConsoleSessions>()
                    .CheckAsync(request.Cookies[OrvanoHeaders.ConsoleCookie], context.RequestAborted);

            switch (check.Status)
            {
                case ConsoleSessionStatus.Valid:
                    ConsoleUser.Set(context, check.UserId, check.SessionId);
                    await next(context);
                    return;
                case ConsoleSessionStatus.Expired:
                    await Problems.WriteAsync(context, StatusCodes.Status401Unauthorized, ErrorCode.TokenExpired,
                        "The console session's access token has expired; refresh the session.");
                    return;
                default:
                    await Problems.WriteAsync(context, StatusCodes.Status401Unauthorized, ErrorCode.ConsoleSessionRequired,
                        "Console routes accept only a console session, never an API key or an app session.");
                    return;
            }
        });
    }

    private static bool IsSafe(string method) => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    /// <summary><c>Sec-Fetch-Site: same-origin</c>, or with no <c>Sec-Fetch-Site</c> at all, the public origin as <c>Origin</c>.</summary>
    private static bool SameOrigin(HttpRequest request, string publicOrigin)
    {
        if (request.Headers["Sec-Fetch-Site"] is [{ } site]) return string.Equals(site, "same-origin", StringComparison.Ordinal);
        return request.Headers.Origin is [{ } origin] && string.Equals(origin, publicOrigin, StringComparison.OrdinalIgnoreCase);
    }
}
