using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Orvano;

/// <summary>One HTTP call, as a generated service describes it.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path under the endpoint, starting with <c>/v1/</c>.</param>
/// <param name="Query">Query parameters; null values are left out.</param>
/// <param name="Content">The JSON body, or null.</param>
/// <param name="Idempotent">Marked <c>x-orvano-idempotent</c> in the contract, so it is safe to retry.</param>
internal sealed record OrvanoRequest(
    string Method,
    string Path,
    IReadOnlyList<KeyValuePair<string, string?>>? Query,
    HttpContent? Content,
    bool Idempotent)
{
    /// <summary>Serializes <paramref name="value"/> as a UTF-8 JSON body with source generated metadata.</summary>
    public static HttpContent Json<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }
}
