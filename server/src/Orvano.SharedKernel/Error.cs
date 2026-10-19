namespace Orvano.SharedKernel;

/// <summary>What kind of expected failure an <see cref="Error"/> is; it picks the HTTP status (spec 0002 ERR-4).</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    RateLimited,
    Unavailable,
}

/// <summary>
/// The one error vocabulary for expected failures (spec 0002 ERR-1). Codes are stable strings; messages are plain
/// English and safe to show to callers (ERR-3). <paramref name="Fields"/> holds per field messages for validation.
/// </summary>
#pragma warning disable CA1716 // ERR-1: spec 0002 names this type Error; the server has no VB consumers.
public sealed record Error(
    string Code,
    string Message,
    ErrorKind Kind,
    IReadOnlyDictionary<string, string[]>? Fields = null,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fields = null) =>
        new(code, message, ErrorKind.Validation, fields);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error RateLimited(string code, string message, int retryAfterSeconds) =>
        new(code, message, ErrorKind.RateLimited, Extensions: new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfterSeconds });

    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);
}
#pragma warning restore CA1716
