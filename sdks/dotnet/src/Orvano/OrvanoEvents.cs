using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Orvano;

public static partial class OrvanoEvents
{
    /// <summary>
    /// Decodes a raw realtime event payload into its typed model by event name. Returns null for an
    /// event this SDK does not know yet (a newer server), so you can ignore it safely.
    /// </summary>
    /// <param name="name">The event name, for example <c>users.created</c>.</param>
    /// <param name="payload">The payload JSON, which must be an object.</param>
    /// <param name="registry">The events to know; defaults to <see cref="Registry"/>.</param>
    public static object? Decode(string name, JsonElement payload, IReadOnlyDictionary<string, JsonTypeInfo>? registry = null)
    {
        if (!(registry ?? Registry).TryGetValue(name, out var typeInfo)) return null;
        if (payload.ValueKind != JsonValueKind.Object)
            throw new JsonException($"The payload of event {name} must be a JSON object.");
        return JsonSerializer.Deserialize(payload, typeInfo);
    }
}
