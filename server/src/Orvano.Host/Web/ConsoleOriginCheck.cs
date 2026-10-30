using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.Web;

/// <summary>
/// Spec 0006 CON-2's <c>Origin</c> check, for endpoints marked <see cref="ConsoleOriginExtensions.RequireConsoleOrigin{TBuilder}"/>.
/// It runs after the access layer and before the endpoint, so a foreign origin gets 403 before the body is read.
/// </summary>
internal static class ConsoleOriginCheck
{
    private static readonly Error ForeignOrigin =
        Error.Forbidden(ErrorCodes.Forbidden, "Open the console at its own address (ORVANO_PUBLIC_URL) and try again.");

    /// <summary>Place after the access layer. <paramref name="consoleOrigin"/> comes from <see cref="SharedKernel.Configuration.ConsoleAddress.Origin"/>; null refuses every request.</summary>
    public static IApplicationBuilder UseConsoleOriginCheck(this IApplicationBuilder app, string? consoleOrigin) =>
        app.Use(async (http, next) =>
        {
            if (http.GetEndpoint()?.Metadata.GetMetadata<ConsoleOriginMetadata>() is null)
            {
                await next(http);
                return;
            }

            if (!Matches(http.Request.Headers.Origin.ToString(), consoleOrigin))
            {
                await ForeignOrigin.ToProblem().ExecuteAsync(http);
                return;
            }

            http.Items[ConsoleOriginMetadata.CheckedKey] = true;
            await next(http);
        });

    // One Origin value only, compared as scheme, host, and port (so a default port written out still matches).
    internal static bool Matches(string origin, string? consoleOrigin) =>
        consoleOrigin is not null
        && !origin.Contains(',', StringComparison.Ordinal)
        && Uri.TryCreate(origin, UriKind.Absolute, out Uri? url)
        && url.Scheme is "http" or "https"
        && url.AbsolutePath == "/" && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.UserInfo)
        && string.Equals(SharedKernel.Configuration.ConsoleAddress.OriginOf(url), consoleOrigin, StringComparison.Ordinal);
}
