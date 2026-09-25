using System.Text.Json.Nodes;

namespace Orvano.Scenarios;

/// <summary>One operation in the generated dispatch table. A null call means the .NET SDK has none.</summary>
internal sealed record DispatchEntry(int Status, Func<OrvanoClient, JsonObject, CancellationToken, Task<JsonNode?>>? Call);
