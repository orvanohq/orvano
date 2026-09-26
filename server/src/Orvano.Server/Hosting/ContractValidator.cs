using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Orvano.Server.Hosting;

/// <summary>
/// Checks response bodies against the compiled contract (spec 0001, AC-9). It reads the raw
/// <c>openapi.json</c>, strips keywords only OpenAPI uses, and builds one JSON Schema 2020-12
/// document per declared response with every component schema under <c>$defs</c>, so
/// <c>#/components/schemas/X</c> references resolve. Object models are closed
/// (<c>unevaluatedProperties: false</c>), so a field the contract lacks is caught too. An error
/// status is checked against the operation's <c>default</c> response, the <c>Problem</c> model.
/// </summary>
internal sealed class ContractValidator
{
    private const string DefaultStatus = "default";
    private static readonly string[] OpenApiOnlyKeywords = ["discriminator", "example", "xml", "externalDocs"];

    /// <summary>A declared response: its one media type and schema, or neither for no content.</summary>
    private sealed record Declared(string? MediaType, JsonSchema? Schema);

    // Status is null for the `default` response.
    private readonly Dictionary<(string OperationId, int? Status), Declared> _responses;

    private ContractValidator(Dictionary<(string, int?), Declared> responses)
    {
        _responses = responses;
        OperationIds = responses.Keys.Select(k => k.Item1).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The operationIds the contract declares.</summary>
    public IReadOnlySet<string> OperationIds { get; }

    public static ContractValidator Load(Stream openApiJson)
    {
        var doc = JsonNode.Parse(openApiJson)?.AsObject() ?? throw new InvalidDataException("openapi.json is empty");
        var defs = new JsonObject();
        foreach (var (name, schema) in doc["components"]?["schemas"]?.AsObject() ?? [])
            defs[name] = Prepare(schema!.DeepClone());

        var responses = new Dictionary<(string, int?), Declared>();
        foreach (var (path, item) in doc["paths"]?.AsObject() ?? [])
        {
            foreach (var (method, operation) in item!.AsObject())
            {
                var id = operation?["operationId"]?.GetValue<string>()
                    ?? throw new InvalidDataException($"{method} {path} has no operationId");
                foreach (var (status, response) in operation["responses"]?.AsObject() ?? [])
                {
                    int? code = status == DefaultStatus ? null
                        : int.TryParse(status, out var parsed) ? parsed
                        : throw new InvalidDataException($"{id} declares a response '{status}', which is not a status");
                    // SdkGen allows one media type per response (JSON, or problem+json for errors).
                    var content = response?["content"]?.AsObject().FirstOrDefault();
                    responses[(id, code)] = content?.Value?["schema"] is { } schema
                        ? new Declared(content.Value.Key, Build(schema, defs, id, status))
                        : new Declared(null, null);
                }
            }
        }

        return new ContractValidator(responses);
    }

    /// <summary>
    /// Returns why the response breaks the contract, or null when it conforms. A status the
    /// operation does not declare is checked against its <c>default</c> response when it is an
    /// error; an undeclared 2xx is always a violation.
    /// </summary>
    public string? Check(string? operationId, int status, string? contentType, ReadOnlyMemory<byte> body)
    {
        if (operationId is null || !OperationIds.Contains(operationId))
            return $"the endpoint '{operationId ?? "(unnamed)"}' is not in the contract";

        if (!_responses.TryGetValue((operationId, status), out var declared))
        {
            if (status is >= 200 and < 300) return $"{operationId} returned {status}, which the contract does not declare";
            if (!_responses.TryGetValue((operationId, null), out declared)) return null;
        }

        if (declared.Schema is null)
            return body.IsEmpty ? null : $"{operationId} {status} has a body, but the contract declares none";

        if (contentType is null || !MediaTypeOf(contentType).Equals(declared.MediaType, StringComparison.OrdinalIgnoreCase))
            return $"{operationId} {status} has content type '{contentType}', expected {declared.MediaType}";
        var schema = declared.Schema;

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

    private static string MediaTypeOf(string contentType) =>
        contentType.Split(';', 2)[0].Trim();

    private static JsonSchema Build(JsonNode schema, JsonObject defs, string operationId, string status)
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
