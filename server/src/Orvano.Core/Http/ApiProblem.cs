using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Orvano.Core.Http;

/// <summary>
/// A problem result for a module's handler (spec 0001, AC-6): a status, a stable code from the contract's
/// <c>ErrorCode</c> catalog, and a short safe sentence. The host normalizes it to the contract's <c>Problem</c>.
/// </summary>
public static class ApiProblem
{
    /// <summary>The problem details extension member that carries the stable error code.</summary>
    public const string CodeKey = "code";

    /// <summary>A problem with <paramref name="status"/>, <paramref name="code"/>, and an optional safe <paramref name="detail"/>.</summary>
    public static ProblemHttpResult Result(int status, string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { [CodeKey] = code });
}
