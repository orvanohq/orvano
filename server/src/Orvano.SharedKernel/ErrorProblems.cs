using System.Diagnostics;
using System.Globalization;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;


namespace Orvano.SharedKernel;

/// <summary>
/// Spec 0002 ERR-4 and spec 0014 PRB-1: the one place an <see cref="Error"/> becomes RFC 9457 problem details, with
/// <c>code</c>, <c>traceId</c>, field <c>errors</c>, <c>retryAfter</c>, and the error's own extensions. The host's
/// problem writer gives every problem the final member order and the request's trace ID.
/// </summary>
public static class ErrorProblems
{
    /// <summary>KND-1: the status each kind answers with.</summary>
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>PRB-1: the problem <c>type</c>; the code is the identity.</summary>
    public const string ProblemType = "about:blank";

    /// <summary>The members only the error itself supplies, never its extensions.</summary>
    public static IReadOnlySet<string> ReservedMembers { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "type", "title", "status", "detail", "instance", "code", "traceId", "errors", "retryAfter",
    };

    public static ErrorProblemResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, object? value) in error.Extensions ?? new Dictionary<string, object?>())
        {
            if (!ReservedMembers.Contains(key))
            {
                extensions[key] = value;
            }
        }

        extensions["code"] = error.Code;
        if (Activity.Current is { } activity)
        {
            extensions["traceId"] = activity.TraceId.ToString();
        }

        if (error.Fields is { Count: > 0 } fields)
        {
            extensions["errors"] = fields;
        }

        // PRB-6: only a 429, or a 503 that knows when to come back, carries a wait; whole seconds, rounded up.
        int? retryAfterSeconds = error is { Kind: ErrorKind.RateLimited or ErrorKind.Unavailable, RetryAfter: { } wait } && wait >= TimeSpan.Zero
            ? (int)Math.Ceiling(wait.TotalSeconds)
            : null;
        if (retryAfterSeconds is { } seconds)
        {
            extensions["retryAfter"] = seconds;
        }

        int status = StatusFor(error.Kind);
        return new ErrorProblemResult(
            TypedResults.Problem(
                detail: error.Message, statusCode: status, title: ReasonPhrases.GetReasonPhrase(status), type: ProblemType, extensions: extensions),
            retryAfterSeconds);
    }
}

/// <summary>
/// The problem an <see cref="Error"/> answers with. A wait also sets <c>Retry-After</c> to the body's <c>retryAfter</c>
/// (PRB-2); without one, there is no header.
/// </summary>
public sealed class ErrorProblemResult : IResult, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<ProblemDetails>, IContentTypeHttpResult
{
    private readonly ProblemHttpResult _problem;

    internal ErrorProblemResult(ProblemHttpResult problem, int? retryAfterSeconds)
    {
        _problem = problem;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public ProblemDetails ProblemDetails => _problem.ProblemDetails;

    public int StatusCode => _problem.StatusCode;

    /// <summary>The <c>Retry-After</c> value in seconds, or null when the response has no such header.</summary>
    public int? RetryAfterSeconds { get; }

    public string ContentType => _problem.ContentType;

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    ProblemDetails? IValueHttpResult<ProblemDetails>.Value => ProblemDetails;

    object? IValueHttpResult.Value => ProblemDetails;

    string? IContentTypeHttpResult.ContentType => ContentType;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (RetryAfterSeconds is { } seconds)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return _problem.ExecuteAsync(httpContext);
    }
}
