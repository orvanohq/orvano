using Microsoft.AspNetCore.Http;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// The <c>Idempotency-Key</c> request header (spec 0014 IDM-1): 1 to 255 printable ASCII characters, chosen by the
/// caller. An operation that calls <c>.WithIdempotency()</c> refuses a malformed key with <c>400 invalid_request</c>; an
/// unmarked operation ignores the header (IDM-2).
/// </summary>
public static class IdempotencyKey
{
    /// <summary>The longest key.</summary>
    public const int MaxLength = 255;

    internal static Error MalformedError { get; } = Error.Validation(
        ErrorCodes.InvalidRequest, "Idempotency-Key must be 1 to 255 printable ASCII characters.");

    /// <summary>Whether <paramref name="key"/> is 1 to 255 printable ASCII characters (space to <c>~</c>).</summary>
    public static bool IsWellFormed(string? key) =>
        key is { Length: > 0 and <= MaxLength } && key.AsSpan().IndexOfAnyExceptInRange(' ', '~') < 0;

    /// <summary>The request's key, or null when it sent none. A key sent twice, or malformed, is not read.</summary>
    internal static bool TryRead(HttpRequest request, out string? key)
    {
        Microsoft.Extensions.Primitives.StringValues values = request.Headers[OrvanoHeaders.IdempotencyKey];
        key = values.Count == 1 ? values[0] : null;
        return values.Count == 0 || IsWellFormed(key);
    }
}
