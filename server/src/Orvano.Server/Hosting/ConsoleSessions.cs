using Orvano.Contract;

namespace Orvano.Server.Hosting;

/// <summary>
/// The console route rule (spec 0001, AC-17): a <c>/v1/console</c> request gets a 401
/// <c>console_session_required</c> unless it carries a valid console session and neither an API
/// key nor an app session. Until scope rows 7 and 8, the only valid sessions are the fixtures'
/// <c>consoleSessions</c> in the <c>Test</c> environment, so elsewhere every console route is 401.
/// </summary>
internal static class ConsoleSessions
{
    public static IApplicationBuilder UseConsoleSessions(this IApplicationBuilder app, TestFixtures fixtures) =>
        app.Use((context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/v1/console")) return next(context);

            var request = context.Request;
            var valid = !request.Headers.ContainsKey(OrvanoHeaders.ApiKey)
                && !request.Headers.ContainsKey(OrvanoHeaders.Session)
                && request.Cookies.TryGetValue(OrvanoHeaders.ConsoleCookie, out var token)
                && token is not null
                && fixtures.ConsoleSessions.Contains(token);

            return valid
                ? next(context)
                : Problems.WriteAsync(context, StatusCodes.Status401Unauthorized, ErrorCode.ConsoleSessionRequired,
                    "Console routes accept only a console session, never an API key or an app session.");
        });
}
