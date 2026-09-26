using System.Reflection;

namespace Orvano.Server.Hosting;

internal static class OrvanoVersion
{
    /// <summary>From the repo's VERSION file, stamped at build time.</summary>
    public static string Current { get; } =
        typeof(OrvanoVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>
    /// Gives every response an <c>X-Orvano-Version</c>, so SDKs can warn when their major.minor
    /// differs from the server's (spec 0001, AC-11).
    /// </summary>
    public static IApplicationBuilder UseVersionHeader(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[OrvanoHeaders.Version] = Current;
                return Task.CompletedTask;
            });
            return next(context);
        });
}
