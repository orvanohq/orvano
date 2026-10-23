using System.Diagnostics;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api;

/// <summary>
/// The host middleware for <c>/v1</c> requests (spec 0014 VRS-4, HDR-5, HDR-6, PRB-4): the request ID, then the version
/// check, then the rest of the pipeline; on the way out, the Orvano response headers, and a problem body for any error
/// that has none (an unmatched route, a 405, a bodyless 400).
/// </summary>
internal static class ApiWireFormat
{
    // HDR-3: Orvano-Client is tagged for telemetry, cut to this many printable ASCII characters, never rejected.
    internal const int MaxClientLength = 64;

    private const string NoStore = "no-store";

    /// <summary>Place first, before the exception handler and routing, so every <c>/v1</c> response gets the headers.</summary>
    public static IApplicationBuilder UseApiWireFormat(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            if (!ApiPaths.IsApi(http.Request.Path))
            {
                await next(http);
                return;
            }

            ApiRequestId.For(http);
            ApiVersionContext version = http.RequestServices.GetRequiredService<ApiVersionContext>();

            // OnStarting survives the exception handler clearing the response, so errors get the headers too.
            http.Response.OnStarting(static state => SetResponseHeaders((HttpContext)state), http);

            Activity? activity = Activity.Current;
            if (activity is not null && http.Request.Headers[OrvanoHeaders.Client] is [{ } client])
            {
                activity.SetTag("orvano.client", ClientTag(client)); // VRS-6
            }

            StringValues requested = http.Request.Headers[OrvanoHeaders.ApiVersion];
            if (requested.Count > 0)
            {
                string? value = requested.Count == 1 ? requested[0] : null;
                if (!ApiVersions.IsSupported(value))
                {
                    activity?.SetTag("orvano.api_version", ClientTag(requested.ToString()));
                    await UnsupportedVersion.ToProblem().ExecuteAsync(http);
                    return;
                }

                version.Version = value!;
            }

            activity?.SetTag("orvano.api_version", version.Version);
            await next(http);

            HttpResponse response = http.Response;
            if (!response.HasStarted && response.StatusCode >= StatusCodes.Status400BadRequest
                && response.ContentLength is null or 0 && string.IsNullOrEmpty(response.ContentType))
            {
                // PRB-4: routing's 404 and 405, and framework answers with only a status, become problems too.
                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails { Status = response.StatusCode },
                });
            }
        });

    /// <summary>VRS-4: the answer to a malformed or unsupported <c>Orvano-Api-Version</c>.</summary>
    internal static Error UnsupportedVersion { get; } = new(
        ErrorCodes.UnsupportedApiVersion,
        $"The {OrvanoHeaders.ApiVersion} header must be one of the supported versions.",
        ErrorKind.Validation,
        Extensions: new Dictionary<string, object?> { ["supportedVersions"] = ApiVersions.Supported });

    internal static string ClientTag(string value)
    {
        ReadOnlySpan<char> cut = value.AsSpan(0, Math.Min(value.Length, MaxClientLength));
        return string.Create(cut.Length, cut.ToString(), static (span, source) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = source[i] is >= ' ' and <= '~' ? source[i] : '?';
            }
        });
    }

    private static Task SetResponseHeaders(HttpContext http)
    {
        IHeaderDictionary headers = http.Response.Headers;
        headers[OrvanoHeaders.RequestId] = ApiRequestId.For(http);
        headers[OrvanoHeaders.ApiVersion] = http.RequestServices.GetRequiredService<ApiVersionContext>().Version;
        headers.CacheControl = http.GetEndpoint()?.Metadata.GetMetadata<CacheControlMetadata>()?.Value ?? NoStore;
        return Task.CompletedTask;
    }
}
