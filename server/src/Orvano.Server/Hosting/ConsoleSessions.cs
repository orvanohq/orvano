using Orvano.Contract;
using Orvano.Core.Http;

namespace Orvano.Server.Hosting;

/// <summary>
/// The console route rule (spec 0001, AC-17): a <c>/v1/console</c> request gets a 401
/// <c>console_session_required</c> unless it carries a valid console session and neither an API
/// key nor an app session. A valid session sets the <see cref="ConsoleUser"/> the endpoints act as.
/// Until scope row 8, the only valid sessions are the fixtures' <c>consoleSessions</c> in the
/// <c>Test</c> environment, so elsewhere every console route is 401. <c>consoleInstall.getSetup</c> is the one
/// console route that needs no session (spec 0006, AC-22).
/// </summary>
internal static class ConsoleSessions
{
    private static readonly PathString SetupStatus = "/v1" + ConsoleInstallOperations.GetSetup.Route;

    public static IApplicationBuilder UseConsoleSessions(this IApplicationBuilder app, TestFixtures fixtures) =>
        app.Use((context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/v1/console")) return next(context);
            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Equals(SetupStatus, StringComparison.OrdinalIgnoreCase)) return next(context);

            var userId = Guid.Empty;

            var request = context.Request;
            var valid = !request.Headers.ContainsKey(OrvanoHeaders.ApiKey)
                && !request.Headers.ContainsKey(OrvanoHeaders.Authorization)
                && request.Cookies.TryGetValue(OrvanoHeaders.ConsoleCookie, out var token)
                && token is not null
                && fixtures.ConsoleSessions.TryGetValue(token, out userId);

            if (!valid)
            {
                return Problems.WriteAsync(context, StatusCodes.Status401Unauthorized, ErrorCode.ConsoleSessionRequired,
                    "Console routes accept only a console session, never an API key or an app session.");
            }

            ConsoleUser.Set(context, userId);
            return next(context);
        });
}
