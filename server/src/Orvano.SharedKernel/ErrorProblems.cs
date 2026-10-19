using System.Diagnostics;
using System.Globalization;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Orvano.SharedKernel;

/// <summary>
/// Spec 0002 ERR-4: the one place an <see cref="Error"/> becomes ProblemDetails (RFC 9457), with <c>code</c>,
/// <c>traceId</c>, field errors, and the error's extensions. Feature 10 fixes the final body shape.
/// </summary>
public static class ErrorProblems
{
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The extension a <see cref="ErrorKind.RateLimited"/> error carries its wait in.</summary>
    public const string RetryAfterSecondsExtension = "retryAfterSeconds";

    public static ErrorProblemResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (Activity.Current is { } activity)
        {
            extensions["traceId"] = activity.TraceId.ToString();
        }

        if (error.Fields is { Count: > 0 } fields)
        {
            extensions["errors"] = fields;
        }

        foreach ((string key, object? value) in error.Extensions ?? new Dictionary<string, object?>())
        {
            extensions[key] = value;
        }

        int? retryAfterSeconds = error.Kind == ErrorKind.RateLimited
            && error.Extensions?.GetValueOrDefault(RetryAfterSecondsExtension) is int seconds and >= 0
                ? seconds
                : null;
        return new ErrorProblemResult(
            TypedResults.Problem(detail: error.Message, statusCode: StatusFor(error.Kind), extensions: extensions),
            retryAfterSeconds);
    }
}

/// <summary>
/// The problem an <see cref="Error"/> answers with. A rate limited error also sets <c>Retry-After</c> from its
/// <c>retryAfterSeconds</c> extension (ERR-4); without one, there is no header.
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
