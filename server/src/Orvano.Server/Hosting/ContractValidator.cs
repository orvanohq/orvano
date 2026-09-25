using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Orvano.Server.Hosting;

/// <summary>
/// Checks response bodies against the compiled contract (spec 0001, AC-9). It reads the raw
/// <c>openapi.json</c>, strips keywords only OpenAPI uses, and builds one JSON Schema 2020-12
/// document per declared response with every component schema under <c>$defs</c>, so
/// <c>#/components/schemas/X</c> references resolve. Object models are closed
/// (<c>unevaluatedProperties: false</c>), so a field the contract lacks is caught too.
/// </summary>
internal sealed class ContractValidator
{
    private static readonly string[] OpenApiOnlyKeywords = ["discriminator", "example", "xml", "externalDocs"];

    private readonly Dictionary<(string OperationId, int Status), JsonSchema?> _responses;

    private ContractValidator(Dictionary<(string, int), JsonSchema?> responses) => _responses = responses;

    /// <summary>The operationIds the contract declares.</summary>
    public IReadOnlySet<string> OperationIds => _responses.Keys.Select(k => k.OperationId).ToHashSet(StringComparer.Ordinal);

    public static ContractValidator Load(Stream openApiJson)
    {
        var doc = JsonNode.Parse(openApiJson)?.AsObject() ?? throw new InvalidDataException("openapi.json is empty");
        var defs = new JsonObject();
        foreach (var (name, schema) in doc["components"]?["schemas"]?.AsObject() ?? [])
            defs[name] = Prepare(schema!.DeepClone());

        var responses = new Dictionary<(string, int), JsonSchema?>();
        foreach (var (path, item) in doc["paths"]?.AsObject() ?? [])
        {
            foreach (var (method, operation) in item!.AsObject())
            {
                var id = operation?["operationId"]?.GetValue<string>()
                    ?? throw new InvalidDataException($"{method} {path} has no operationId");
                foreach (var (status, response) in operation["responses"]?.AsObject() ?? [])
                {
                    if (!int.TryParse(status, out var code)) continue;
                    var schema = response?["content"]?["application/json"]?["schema"];
                    responses[(id, code)] = schema is null ? null : Build(schema, defs, id, code);
                }
            }
        }

        return new ContractValidator(responses);
    }

    /// <summary>
    /// Returns why the response breaks the contract, or null when it conforms. Error statuses the
    /// contract does not declare are not checked (problem details join the contract later).
    /// </summary>
    public string? Check(string? operationId, int status, string? contentType, ReadOnlyMemory<byte> body)
    {
        if (operationId is null || !OperationIds.Contains(operationId))
            return $"the endpoint '{operationId ?? "(unnamed)"}' is not in the contract";

        if (!_responses.TryGetValue((operationId, status), out var schema))
            return status is >= 200 and < 300 ? $"{operationId} returned {status}, which the contract does not declare" : null;

        if (schema is null)
            return body.IsEmpty ? null : $"{operationId} {status} has a body, but the contract declares none";

        if (contentType is null || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            return $"{operationId} {status} has content type '{contentType}', expected application/json";

        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return $"{operationId} {status} body is not valid JSON";
        }

        using (json)
        {
            var result = schema.Evaluate(json.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (result.IsValid) return null;
            // Locations and keywords only, never the values: a body may hold user data.
            var problems = (result.Details ?? [])
                .Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Keys.Select(keyword => $"{d.InstanceLocation} ({keyword})"))
                .Distinct()
                .Take(10);
            return $"{operationId} {status} body does not match the contract at {string.Join(", ", problems)}";
        }
    }

    private static JsonSchema Build(JsonNode schema, JsonObject defs, string operationId, int status)
    {
        var root = Prepare(schema.DeepClone()).AsObject();
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        root["$defs"] = defs.DeepClone();
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        var baseUri = new Uri($"https://contract.orvano.invalid/{Uri.EscapeDataString(operationId)}/{status}");
        return JsonSchema.Build(JsonSerializer.SerializeToElement(root), options, baseUri);
    }

    /// <summary>Strips OpenAPI only keywords, points refs at <c>$defs</c>, and closes object models.</summary>
    private static JsonNode Prepare(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var keyword in OpenApiOnlyKeywords) obj.Remove(keyword);
                if (obj["$ref"] is JsonValue r && r.GetValue<string>() is { } reference
                    && reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                    obj["$ref"] = "#/$defs/" + reference["#/components/schemas/".Length..];
                if (obj["properties"] is JsonObject && !obj.ContainsKey("additionalProperties") && !obj.ContainsKey("unevaluatedProperties"))
                    obj["unevaluatedProperties"] = false;
                foreach (var (_, child) in obj.ToList())
                    if (child is not null) Prepare(child);
                break;
            case JsonArray array:
                foreach (var child in array)
                    if (child is not null) Prepare(child);
                break;
        }

        return node;
    }
}
