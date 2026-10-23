using Orvano.SharedKernel.Api;

namespace Orvano.SharedKernel;

/// <summary>What kind of expected failure an <see cref="Error"/> is; it picks the HTTP status (spec 0014 KND-1).</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    PreconditionFailed,
    RateLimited,
    Unavailable,
}

/// <summary>
/// The one error vocabulary for expected failures (spec 0002 ERR-1). Codes are stable snake_case strings from the
/// catalogue (spec 0014 PRB-7); messages are plain English and safe to show to callers (ERR-3). <paramref name="Fields"/>
/// holds the field errors of a validation failure (PRB-3); <paramref name="RetryAfter"/> is when a rate limited or
/// unavailable caller may come back (PRB-2, PRB-6).
/// </summary>
#pragma warning disable CA1716 // ERR-1: spec 0002 names this type Error; the server has no VB consumers.
public sealed record Error(
    string Code,
    string Message,
    ErrorKind Kind,
    IReadOnlyList<FieldError>? Fields = null,
    IReadOnlyDictionary<string, object?>? Extensions = null,
    TimeSpan? RetryAfter = null)
{
    public static Error Validation(string code, string message, IReadOnlyList<FieldError>? fields = null) =>
        new(code, message, ErrorKind.Validation, fields);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error PreconditionFailed(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(code, message, ErrorKind.PreconditionFailed, Extensions: extensions);

    public static Error RateLimited(string code, string message, TimeSpan retryAfter) =>
        new(code, message, ErrorKind.RateLimited, RetryAfter: retryAfter);

    public static Error Unavailable(string code, string message, TimeSpan? retryAfter = null) =>
        new(code, message, ErrorKind.Unavailable, RetryAfter: retryAfter);
}
#pragma warning restore CA1716
