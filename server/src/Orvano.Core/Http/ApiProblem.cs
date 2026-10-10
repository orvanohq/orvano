using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Orvano.Core.RateLimiting;

namespace Orvano.Core.Http;

/// <summary>
/// A problem result for a module's handler (spec 0001, AC-6): a status, a stable code from the contract's
/// <c>ErrorCode</c> catalog, and a short safe sentence. The host normalizes it to the contract's <c>Problem</c>.
/// </summary>
public static class ApiProblem
{
    /// <summary>The problem details extension member that carries the stable error code.</summary>
    public const string CodeKey = "code";

    /// <summary>The problem details extension member that lists every bad field (the contract's <c>Problem.errors</c>).</summary>
    public const string ErrorsKey = "errors";

    /// <summary>One bad field of a request: its path and a few words. Never a value.</summary>
    /// <param name="Field">The field's path, such as <c>title</c> or <c>rows[3].title</c>.</param>
    /// <param name="Message">What is wrong with it.</param>
    public sealed record FieldError(
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("message")] string Message);

    /// <summary>A problem with <paramref name="status"/>, <paramref name="code"/>, and an optional safe <paramref name="detail"/>.</summary>
    public static ProblemHttpResult Result(int status, string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { [CodeKey] = code });

    /// <summary>A problem that also lists every bad field in <c>errors</c>; the first one should lead <paramref name="detail"/>.</summary>
    public static ProblemHttpResult Result(int status, string code, string? detail, IReadOnlyList<FieldError> errors) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { [CodeKey] = code, [ErrorsKey] = errors });

    /// <summary>A 429 with <paramref name="code"/> (the contract's <c>rate_limited</c>) and <c>Retry-After</c> in whole seconds.</summary>
    public static ProblemHttpResult RateLimited(HttpContext http, RateLimitDecision decision, string code)
    {
        http.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Result(StatusCodes.Status429TooManyRequests, code, "Too many requests. Wait and try again.");
    }
}
