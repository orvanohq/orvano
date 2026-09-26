using System.Text.Json.Nodes;

namespace Orvano.Scenarios;

/// <summary>One operation in the generated dispatch table. A null call means the .NET SDK has none.</summary>
/// <param name="Status">The success status the contract declares; the SDK returns the body, not the status.</param>
/// <param name="Call">One call, its result as JSON.</param>
/// <param name="All">For a list operation: every item through the async iterator, as <c>{ items: [...] }</c>.</param>
internal sealed record DispatchEntry(
    int Status,
    Func<OrvanoClient, JsonObject, CancellationToken, Task<JsonNode?>>? Call,
    Func<OrvanoClient, JsonObject, CancellationToken, Task<JsonNode?>>? All);
