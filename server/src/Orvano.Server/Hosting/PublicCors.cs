using Microsoft.Net.Http.Headers;
using Orvano.Core.Http;

namespace Orvano.Server.Hosting;

/// <summary>
/// CORS for the public API (spec 0003, web origin matching). A preflight can't carry the project header's value, so it
/// answers with the request's own origin; the real request is then checked against the header project's web platforms
/// (403 <c>origin_not_allowed</c>, in the project filter). Every public answer to a browser carries its origin, so the
/// SDK can read a refusal as a typed error. Console routes are same origin and get no CORS headers.
/// </summary>
internal static class PublicCors
{
    private const string Methods = "GET, POST, PUT, PATCH, DELETE";
    private static readonly string Exposed = string.Join(", ", OrvanoHeaders.Version, OrvanoHeaders.RequestId, HeaderNames.RetryAfter);

    /// <summary>How long a browser may cache a preflight answer.</summary>
    public static readonly TimeSpan PreflightCache = TimeSpan.FromMinutes(10);

    public static IApplicationBuilder UsePublicCors(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var request = context.Request;
            if (!request.Path.StartsWithSegments("/v1") || request.Path.StartsWithSegments("/v1/console")
                || request.Headers.Origin is not [{ Length: > 0 } origin])
            {
                return next(context);
            }

            if (HttpMethods.IsOptions(request.Method) && request.Headers.ContainsKey(HeaderNames.AccessControlRequestMethod))
            {
                var headers = context.Response.Headers;
                headers.AccessControlAllowOrigin = origin;
                headers.AccessControlAllowMethods = Methods;
                headers.AccessControlAllowHeaders = request.Headers.AccessControlRequestHeaders;
                headers.AccessControlMaxAge = ((int)PreflightCache.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                headers.Vary = HeaderNames.Origin;
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }

            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.AccessControlAllowOrigin = origin;
                headers.AccessControlExposeHeaders = Exposed;
                headers.Append(HeaderNames.Vary, HeaderNames.Origin);
                return Task.CompletedTask;
            });
            return next(context);
        });
}
