using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Orvano.Contract;

namespace Orvano.Server.Hosting;

/// <summary>
/// One error pattern (spec 0001, AC-6): every error is an RFC 9457 problem with the contract's
/// <c>Problem</c> shape, whoever writes it (a handler, routing, the exception handler). The request
/// ID is the OpenTelemetry trace ID, sent as <c>X-Request-Id</c> on every response.
/// </summary>
internal static class Problems
{
    private const string CodeKey = "code";
    private const string RequestIdKey = "requestId";
    private static readonly object RequestIdItem = new();

    /// <summary>Problem details for every error, normalized to the contract's <c>Problem</c>.</summary>
    public static IServiceCollection AddOrvanoProblems(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;
            var code = problem.Extensions.TryGetValue(CodeKey, out var value) && value is string { Length: > 0 } given
                ? given
                : DefaultCode(status);

            problem.Status = status;
            problem.Type = $"https://orvano.dev/errors/{code}";
            problem.Title = ReasonPhrases.GetReasonPhrase(status);
            problem.Instance = null;
            // Never an exception message, stack trace, or anything else a handler did not write.
            if (context.Exception is not null || status >= 500 && code == ErrorCode.InternalError) problem.Detail = null;
            problem.Extensions.Clear();
            problem.Extensions[CodeKey] = code;
            problem.Extensions[RequestIdKey] = RequestIdOf(context.HttpContext);
        });

    /// <summary>Gives every response an <c>X-Request-Id</c>: the current trace ID, never the client's.</summary>
    public static IApplicationBuilder UseRequestIds(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var id = RequestIdOf(context);
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[OrvanoHeaders.RequestId] = id;
                return Task.CompletedTask;
            });
            return next(context);
        });

    /// <summary>A problem result for a handler: the status, a stable code, and a safe sentence.</summary>
    public static ProblemHttpResult Result(int status, string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { [CodeKey] = code });

    /// <summary>Writes a problem from middleware, through the same normalization.</summary>
    public static Task WriteAsync(HttpContext context, int status, string code, string? detail = null) =>
        Result(status, code, detail).ExecuteAsync(context);

    private static string DefaultCode(int status) => status switch
    {
        StatusCodes.Status404NotFound => ErrorCode.NotFound,
        >= 400 and < 500 => ErrorCode.InvalidRequest,
        _ => ErrorCode.InternalError,
    };

    private static string RequestIdOf(HttpContext context)
    {
        if (context.Items.TryGetValue(RequestIdItem, out var saved) && saved is string id) return id;
        id = Activity.Current?.TraceId.ToHexString() ?? ActivityTraceId.CreateRandom().ToHexString();
        context.Items[RequestIdItem] = id;
        return id;
    }
}
