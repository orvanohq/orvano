using Microsoft.Extensions.Options;

namespace Orvano.Host.ConsoleFiles;

/// <summary>
/// Serves the console's static export at <c>/console</c> (spec 0003, "Host: serving /console").
/// The cases and their order follow the spec's HTTP surface table, which the console's test server
/// (<c>console/scripts/serve-export.mjs</c>) mirrors. Nothing outside <c>/console</c> changes.
/// </summary>
internal static partial class ConsoleFilesEndpoints
{
    public const string PathPrefix = "/console";

    internal const string NotBuiltCode = "console_not_built";
    internal const string NotBuiltTitle = "Console not built";
    internal const string NotBuiltHint =
        "Build it with `pnpm --filter @orvano/console build` and point `Console:RootPath` at `console/out`, or open the console dev server from the AppHost.";

    private const string IndexFile = "index.html";
    private const string NotFoundFile = "404.html";
    private const string ImmutableAssetsPrefix = "/_next/static/";
    private const string NoCache = "no-cache";
    private const string Immutable = "public, max-age=31536000, immutable";

    public static WebApplication MapConsoleFiles(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ConsoleFilesOptions>>().Value;
        var root = Path.GetFullPath(options.RootPath, app.Environment.ContentRootPath);
        var files = new ConsoleRootFileProvider(root);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);

        if (!File.Exists(Path.Combine(root, IndexFile)))
        {
            LogConsoleNotBuilt(app.Logger, root);
        }

        app.Map(PathPrefix, branch =>
        {
            branch.Use(AddHeadersAsync);
            branch.Use((context, next) => HandleCasesBeforeFilesAsync(context, next, files));
            // The slash redirect happens above, with a relative Location: the framework's own writes an absolute
            // http:// address, which behind Caddy would send the browser through plain HTTP first.
            branch.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files, RedirectToAppendTrailingSlash = false });
            branch.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = files,
                OnPrepareResponse = file => file.Context.Response.Headers.CacheControl =
                    file.Context.Request.Path.StartsWithSegments(ImmutableAssetsPrefix.TrimEnd('/'), StringComparison.Ordinal)
                        ? Immutable
                        : NoCache,
            });
            branch.Run(context => WriteNotFoundAsync(context, files));
        });

        return app;
    }

    // Registered first, so redirects and errors carry the headers too.
    private static Task AddHeadersAsync(HttpContext context, Func<Task> next)
    {
        var response = context.Response;
        response.OnStarting(() =>
        {
            var headers = response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers.XFrameOptions = "DENY";
            headers.ContentSecurityPolicy = "frame-ancestors 'none'";

            // Static files and the 503 set their own; everything else (redirects, 404, 405) is no-cache.
            if (string.IsNullOrEmpty(headers.CacheControl))
            {
                headers.CacheControl = NoCache;
            }

            return Task.CompletedTask;
        });

        return next();
    }

    private static Task HandleCasesBeforeFilesAsync(HttpContext context, Func<Task> next, ConsoleRootFileProvider files)
    {
        var request = context.Request;
        var response = context.Response;

        // Checked per request, so building the console while the host runs works without a restart.
        if (!files.GetFileInfo(IndexFile).Exists)
        {
            response.Headers.CacheControl = "no-store";
            var problem = Results.Problem(
                type: "about:blank",
                title: NotBuiltTitle,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = NotBuiltCode,
                    ["hint"] = NotBuiltHint,
                });
            return problem.ExecuteAsync(context);
        }

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            response.Headers.Allow = "GET, HEAD";
            return Task.CompletedTask;
        }

        if (!request.Path.HasValue || request.Path.Value.Length == 0)
        {
            response.StatusCode = StatusCodes.Status308PermanentRedirect;
            response.Headers.Location = $"{request.PathBase}/{request.QueryString}";
            return Task.CompletedTask;
        }

        var path = request.Path.Value;
        if (!path.EndsWith('/') && files.GetFileInfo($"{path}/{IndexFile}").Exists)
        {
            response.StatusCode = StatusCodes.Status301MovedPermanently;
            response.Headers.Location = $"{request.PathBase}{path}/{request.QueryString}";
            return Task.CompletedTask;
        }

        return next();
    }

    private static async Task WriteNotFoundAsync(HttpContext context, ConsoleRootFileProvider files)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status404NotFound;

        var page = files.GetFileInfo(NotFoundFile);
        if (!page.Exists)
        {
            return;
        }

        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength = page.Length;
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await response.SendFileAsync(page, context.RequestAborted);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The console files are missing: no index.html in {ConsoleRoot}. Requests under /console answer 503 console_not_built until the folder holds a console build.")]
    private static partial void LogConsoleNotBuilt(ILogger logger, string consoleRoot);
}
