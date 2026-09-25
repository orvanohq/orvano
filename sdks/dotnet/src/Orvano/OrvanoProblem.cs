using System.Text.Json.Serialization;

namespace Orvano;

/// <summary>The RFC 9457 problem details body the server sends with a failure.</summary>
internal sealed record OrvanoProblem(
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("detail")] string? Detail = null,
    [property: JsonPropertyName("code")] string? Code = null,
    [property: JsonPropertyName("requestId")] string? RequestId = null);
