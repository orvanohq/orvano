// Written by hand: the surface of sdk-dotnet's hand written runtime that the generated code calls (spec 0014 GNR-5),
// with no behavior, so the snapshot builds here. sdk-dotnet's CI builds and tests the real runtime against the same
// generated code; keep these signatures in step with it.
#nullable enable

using System.Text.Json.Serialization.Metadata;

namespace Orvano.Server;

/// <summary>What went wrong, from the HTTP status alone (spec 0014 KND-2).</summary>
public enum OrvanoErrorKind
{
    /// <summary>400, 413, 415, 422.</summary>
    Validation,

    /// <summary>401.</summary>
    Unauthenticated,

    /// <summary>403.</summary>
    Forbidden,

    /// <summary>404.</summary>
    NotFound,

    /// <summary>408.</summary>
    Timeout,

    /// <summary>409, 412, 428.</summary>
    Conflict,

    /// <summary>429.</summary>
    RateLimited,

    /// <summary>Any other 4xx or 5xx.</summary>
    Server,
}

/// <summary>One problem with one field of the request.</summary>
public sealed record OrvanoFieldError(string Field, string Location, string Code, string Message);

/// <summary>An error response.</summary>
public class OrvanoException : Exception
{
    /// <summary>The kind.</summary>
    public OrvanoErrorKind Kind { get; }

    /// <summary>The HTTP status.</summary>
    public int StatusCode { get; }

    /// <summary>The error code.</summary>
    public string Code { get; } = OrvanoErrorCodes.Unknown;

    /// <summary>The request ID.</summary>
    public string? TraceId { get; }

    /// <summary>The field errors.</summary>
    public IReadOnlyList<OrvanoFieldError> FieldErrors { get; } = [];
}

/// <summary>A 429.</summary>
public sealed class OrvanoRateLimitedException : OrvanoException
{
    /// <summary>How long to wait.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>Retry settings.</summary>
public sealed class OrvanoRetryOptions
{
    /// <summary>The most retries.</summary>
    public int MaxRetries { get; init; } = 2;
}

/// <summary>Per call options.</summary>
public sealed class OrvanoRequestOptions
{
    /// <summary>The attempt timeout.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>The retry settings.</summary>
    public OrvanoRetryOptions? Retry { get; init; }

    /// <summary>Extra headers.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>The version to send as <c>If-Match</c>.</summary>
    internal string? IfMatch { get; init; }

    /// <summary>A copy of <paramref name="options"/> that sends <paramref name="version"/> as <c>If-Match</c>.</summary>
    internal static OrvanoRequestOptions? WithIfMatch(OrvanoRequestOptions? options, string? version) => options;
}

/// <summary>The client.</summary>
public sealed class OrvanoClient : OrvanoServices
{
    private OrvanoClient()
        : base(new OrvanoTransport())
    {
    }
}

internal sealed class OrvanoTransport
{
    public string ProjectId => throw new NotSupportedException();

    public Task<T> SendAsync<T>(OrvanoCall call, JsonTypeInfo<T> responseType, OrvanoRequestOptions? options, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task SendAsync(OrvanoCall call, OrvanoRequestOptions? options, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>One page of a list (spec 0014 PAG-2).</summary>
/// <typeparam name="T">The type of one item.</typeparam>
public sealed record ResultPage<T>
{
    /// <summary>The items on this page, in the list's order.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>The cursor for the next page, or null on the last page.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("nextCursor")]
    public required string? NextCursor { get; init; }

    /// <summary>How many items the whole list has, or null when the operation does not count them.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("total")]
    public required int? Total { get; init; }
}

/// <summary>A partial update's field (spec 0014 JSW-10): absent by default, and then not sent.</summary>
/// <typeparam name="T">The field's type.</typeparam>
public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    /// <summary>A field set to <paramref name="value"/>.</summary>
    /// <param name="value">The value.</param>
    public Optional(T value)
    {
        Value = value;
        HasValue = true;
    }

    /// <summary>Whether the field is set.</summary>
    public bool HasValue { get; }

    /// <summary>The value set.</summary>
    public T Value { get; }

    /// <summary>Sets the field.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Optional<T>(T value) => new(value);

    /// <summary>Compares two fields.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    /// <summary>Compares two fields.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(Optional<T> other) => HasValue == other.HasValue && EqualityComparer<T>.Default.Equals(Value, other.Value);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(HasValue, Value);
}

/// <summary>Writes an <see cref="Optional{T}"/> as its value.</summary>
/// <typeparam name="T">The field's type.</typeparam>
public sealed class OptionalJsonConverter<T> : System.Text.Json.Serialization.JsonConverter<Optional<T>>
{
    /// <inheritdoc/>
    public override Optional<T> Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => default;

    /// <inheritdoc/>
    public override void Write(System.Text.Json.Utf8JsonWriter writer, Optional<T> value, System.Text.Json.JsonSerializerOptions options)
    {
    }
}
