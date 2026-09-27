using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Orvano;

/// <summary>One HTTP call, as a generated service describes it.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path under the endpoint, starting with <c>/v1/</c>.</param>
/// <param name="Query">Query parameters; null values are left out.</param>
/// <param name="Body">The UTF-8 JSON body, or null. Bytes, so a retry can send it again.</param>
/// <param name="Idempotent">Marked <c>x-orvano-idempotent</c> in the contract, so it is safe to retry.</param>
internal sealed record OrvanoRequest(
    string Method,
    string Path,
    IReadOnlyList<KeyValuePair<string, string?>>? Query,
    byte[]? Body,
    bool Idempotent)
{
    /// <summary>
    /// A user's access token to send as <c>Authorization: Bearer</c> instead of the client's API key, so a call made as
    /// that user never carries the key.
    /// </summary>
    public string? Bearer { get; init; }

    /// <summary>Sends <c>Cache-Control: no-cache</c>, so no cache in between answers from before a change.</summary>
    public bool NoCache { get; init; }

    /// <summary>Serializes <paramref name="value"/> as UTF-8 JSON with source generated metadata.</summary>
    public static byte[] Json<T>(T value, JsonTypeInfo<T> typeInfo) => JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);

    /// <summary>GET, HEAD, and operations marked idempotent may be retried.</summary>
    public bool Retryable => Idempotent || Method is "GET" or "HEAD";
}
