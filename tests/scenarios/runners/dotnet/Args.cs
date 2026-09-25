using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orvano.Scenarios;

/// <summary>Reads SDK arguments out of a step's input, and turns SDK results back into JSON.</summary>
internal static class Args
{
    // The SDK's models carry [JsonPropertyName], so reflection reads and writes the wire shape.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static T Required<T>(JsonObject input, string name) =>
        input[name].Deserialize<T>(Options) ?? throw new ScenarioFailure($"input.{name} is required");

    public static T? Optional<T>(JsonObject input, string name) =>
        input[name] is null ? default : input[name].Deserialize<T>(Options);

    public static JsonNode? ToJson<T>(T value) => JsonSerializer.SerializeToNode(value, Options);
}
