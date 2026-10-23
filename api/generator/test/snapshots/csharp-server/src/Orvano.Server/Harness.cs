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
