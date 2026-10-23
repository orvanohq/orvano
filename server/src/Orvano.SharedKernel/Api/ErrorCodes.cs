using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Http;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// The shared error codes (spec 0014 PRB-8). Codes are snake_case, at most 64 characters (PRB-7), and never reused for
/// another meaning. <c>unknown</c> is reserved for SDKs (spec 0004 NET-10) and never sent by the server.
/// </summary>
public static partial class ErrorCodes
{
    [ErrorCode(ErrorKind.Validation, "A field in the request is not valid; errors lists each one.", Members = ["errors"])]
    public const string ValidationFailed = "validation_failed";

    [ErrorCode(ErrorKind.Validation, "The request could not be read: malformed JSON, a wrong body type, or a bad header.")]
    public const string InvalidRequest = "invalid_request";

    [ErrorCode(ErrorKind.Validation, "The cursor does not parse, or was issued for another filter or sort.")]
    public const string InvalidCursor = "invalid_cursor";

    [ErrorCode(ErrorKind.Validation, "The Orvano-Api-Version header is malformed or not supported; supportedVersions lists the ones that are.", Members = ["supportedVersions"])]
    public const string UnsupportedApiVersion = "unsupported_api_version";

    [ErrorCode(ErrorKind.Unauthorized, "The credential is missing, invalid, expired, or not for this project.")]
    public const string Unauthorized = "unauthorized";

    [ErrorCode(ErrorKind.Unauthorized, "The access token expired; refresh the session once and retry.")]
    public const string SessionExpired = "session_expired";

    [ErrorCode(ErrorKind.Forbidden, "The caller is not allowed to do this; on a console route, requiredRole names the role that would allow it.", Members = ["requiredRole"])]
    public const string Forbidden = "forbidden";

    [ErrorCode(ErrorKind.Forbidden, "The credential does not have the scope this needs.")]
    public const string InsufficientScope = "insufficient_scope";

    [ErrorCode(ErrorKind.NotFound, "The resource or route was not found.")]
    public const string NotFound = "not_found";

    [ErrorCode(StatusCodes.Status405MethodNotAllowed, "The route does not accept this method.")]
    public const string MethodNotAllowed = "method_not_allowed";

    [ErrorCode(ErrorKind.Conflict, "The request conflicts with the resource's current state.")]
    public const string Conflict = "conflict";

    [ErrorCode(ErrorKind.Conflict, "A request with this idempotency key is still running (reserved).")]
    public const string IdempotencyInProgress = "idempotency_in_progress";

    [ErrorCode(ErrorKind.PreconditionFailed, "If-Match does not match the resource's current version; currentVersion holds it.", Members = ["currentVersion"])]
    public const string VersionConflict = "version_conflict";

    [ErrorCode(StatusCodes.Status413PayloadTooLarge, "The request body is too large.")]
    public const string PayloadTooLarge = "payload_too_large";

    [ErrorCode(StatusCodes.Status415UnsupportedMediaType, "The request body's content type is not accepted here.")]
    public const string UnsupportedMediaType = "unsupported_media_type";

    [ErrorCode(StatusCodes.Status422UnprocessableEntity, "This idempotency key was used with a different request (reserved).")]
    public const string IdempotencyKeyReused = "idempotency_key_reused";

    [ErrorCode(ErrorKind.RateLimited, "Too many requests; wait retryAfter seconds.", Members = ["retryAfter"])]
    public const string RateLimited = "rate_limited";

    [ErrorCode(StatusCodes.Status500InternalServerError, "Something went wrong on the server.")]
    public const string Internal = "internal";

    [ErrorCode(ErrorKind.Unavailable, "The server cannot answer right now; retryAfter says when to come back, when known.", Members = ["retryAfter"])]
    public const string Unavailable = "unavailable";

    [ErrorCode(ErrorKind.Unavailable, "The server took too long to answer.")]
    public const string Timeout = "timeout";

    /// <summary>
    /// The transport codes any operation can return (spec 0014 XTN-3). They are implied, so an operation never lists
    /// them in <c>WithErrors</c>; the generator and docs add them.
    /// </summary>
    public static IReadOnlyList<string> Transport { get; } =
    [
        InvalidRequest, UnsupportedApiVersion, Unauthorized, SessionExpired, RateLimited, Internal, Unavailable, Timeout,
        MethodNotAllowed, PayloadTooLarge, UnsupportedMediaType,
    ];

    /// <summary>Every shared code with its catalogue entry, in declaration order.</summary>
    public static IReadOnlyList<(string Code, ErrorCodeAttribute Entry)> All { get; } = Catalogue(typeof(ErrorCodes));

    /// <summary>The code for a response that carries only a status (an unmatched route, a 405, a bodyless 400; PRB-4).</summary>
    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status412PreconditionFailed => VersionConflict,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => RateLimited,
        StatusCodes.Status503ServiceUnavailable => Unavailable,
        StatusCodes.Status504GatewayTimeout => Timeout,
        >= 400 and < 500 => InvalidRequest,
        _ => Internal,
    };

    /// <summary>Whether <paramref name="code"/> follows PRB-7: <c>^[a-z][a-z0-9_]*$</c>, at most 64 characters.</summary>
    public static bool IsWellFormed(string? code) => code is { Length: > 0 and <= 64 } && CodePattern().IsMatch(code);

    /// <summary>The <see cref="ErrorCodeAttribute"/> marked constants of <paramref name="type"/>, in declaration order.</summary>
    public static IReadOnlyList<(string Code, ErrorCodeAttribute Entry)> Catalogue(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return
        [
            .. type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (Code: (string)field.GetRawConstantValue()!, Entry: field.GetCustomAttribute<ErrorCodeAttribute>()))
                .Where(entry => entry.Entry is not null)
                .Select(entry => (entry.Code, entry.Entry!)),
        ];
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
