using Microsoft.Extensions.FileProviders;

using Orvano.SharedKernel.Access;

namespace Orvano.Host.Web;

/// <summary>
/// Serves the built console at <c>/console</c> from <c>wwwroot/console</c> (spec 0007 CSL-1, CSL-2): hashed assets are
/// cached for a year, and any other GET under <c>/console</c> that matches no file returns <c>index.html</c>.
/// </summary>
internal static class ConsoleFiles
{
    public const string BasePath = "/console";

    private const string HashedAssetsPath = "/assets/";

    /// <summary>
    /// Serves files that exist. Call before <c>UseRouting</c>: the static file middleware steps aside once routing has
    /// picked an endpoint, and the index fallback below matches every path under <c>/console</c>.
    /// </summary>
    public static WebApplication UseConsoleFiles(this WebApplication app)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = BasePath,
            FileProvider = Files(app),
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
                context.Context.Request.Path.Value?.StartsWith(BasePath + HashedAssetsPath, StringComparison.Ordinal) == true
                    ? "public, max-age=31536000, immutable"
                    : "no-cache",
        });
        return app;
    }

    /// <summary>The <c>/</c> redirect (spec 0007 RUN-4) and the <c>index.html</c> fallback for client side routes.</summary>
    public static WebApplication MapConsole(this WebApplication app)
    {
        // Public (spec 0012 EPT-5): the console page loads before anyone signs in, is the same for every caller, and
        // must not break on a stale session cookie; its API calls carry the access checks.
        IFileProvider files = Files(app);
        app.MapGet("/", () => TypedResults.Redirect(BasePath + "/")).ExcludeFromDescription().AllowPublicAccess();
        app.MapGet(BasePath, (HttpContext context) => IndexPage(context, files)).ExcludeFromDescription().AllowPublicAccess();
        app.MapGet(BasePath + "/{**path}", (HttpContext context) => IndexPage(context, files)).ExcludeFromDescription().AllowPublicAccess();
        return app;
    }

    private static IFileProvider Files(WebApplication app)
    {
        string root = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "console");
        return Directory.Exists(root) ? new PhysicalFileProvider(root) : new NullFileProvider();
    }

    private static IResult IndexPage(HttpContext context, IFileProvider files)
    {
        IFileInfo index = files.GetFileInfo("index.html");
        if (!index.Exists)
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers.CacheControl = "no-cache";
        return TypedResults.Stream(index.CreateReadStream(), "text/html; charset=utf-8");
    }
}
