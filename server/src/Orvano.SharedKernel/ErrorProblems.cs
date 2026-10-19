using System.Diagnostics;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

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

    public static ProblemHttpResult ToProblem(this Error error)
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

        return TypedResults.Problem(detail: error.Message, statusCode: StatusFor(error.Kind), extensions: extensions);
    }
}
