using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace Orvano.SdkGen.Contract;

/// <summary>
/// Turns the compiled OpenAPI document into the generator's model and checks every rule SdkGen
/// depends on (AC-2). It collects every problem before failing, so one run names them all.
/// </summary>
internal static partial class ContractReader
{
    private const string AudienceExtension = "x-orvano-audience";
    private const string ServiceExtension = "x-orvano-service";
    private const string IdempotentExtension = "x-orvano-idempotent";

    public static async Task<(ApiContract? Contract, IReadOnlyList<string> Errors)> ReadAsync(
        string openApiPath, string expectedVersion)
    {
        await using var stream = File.OpenRead(openApiPath);
        var result = await OpenApiDocument.LoadAsync(stream, "json");
        var errors = new List<string>();
        foreach (var e in result.Diagnostic?.Errors ?? []) errors.Add($"openapi.json: {e.Message} ({e.Pointer})");
        if (result.Document is null) return (null, errors);

        var reader = new Reader(result.Document, errors);
        var contract = reader.Read(expectedVersion);
        return (errors.Count == 0 ? contract : null, errors);
    }

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*\\.[a-z][a-zA-Z0-9]*$")]
    private static partial Regex OperationIdPattern();

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    private static partial Regex CamelCasePattern();

    [GeneratedRegex("^[A-Z][a-zA-Z0-9]*$")]
    private static partial Regex PascalCasePattern();

    private sealed class Reader(OpenApiDocument doc, List<string> errors)
    {
        private readonly IDictionary<string, IOpenApiSchema> _schemas =
            doc.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>();

        public ApiContract Read(string expectedVersion)
        {
            var version = doc.Info?.Version ?? "";
            if (version != expectedVersion)
                errors.Add($"contract info.version is '{version}' but VERSION is '{expectedVersion}'; set @info(#{{ version: \"{expectedVersion}\" }}) in contract/main.tsp");

            var models = new List<ContractModel>();
            var enums = new List<ContractEnum>();
            foreach (var (name, schema) in _schemas.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                if (!PascalCasePattern().IsMatch(name))
                {
                    errors.Add($"schema '{name}': model and enum names must be PascalCase");
                    continue;
                }

                if (schema.Enum is { Count: > 0 } values)
                {
                    enums.Add(ReadEnum(name, schema, values));
                    continue;
                }

                if (schema.Type is JsonSchemaType.Object && schema.Properties is { Count: > 0 })
                {
                    models.Add(ReadModel(name, schema));
                    continue;
                }

                errors.Add($"schema '{name}': only object models with properties and string enums are supported");
            }

            var operations = ReadOperations();
            return new ApiContract(version, operations, models, enums);
        }

        private ContractEnum ReadEnum(string name, IOpenApiSchema schema, IList<JsonNode> values)
        {
            var wire = new List<string>();
            foreach (var v in values)
            {
                if (v is JsonValue jv && jv.TryGetValue<string>(out var s)) wire.Add(s);
                else errors.Add($"enum '{name}': only string values are supported, found {v?.ToJsonString() ?? "null"}");
            }

            return new ContractEnum(name, schema.Description, wire);
        }

        private ContractModel ReadModel(string name, IOpenApiSchema schema)
        {
            if (schema.AllOf is { Count: > 0 } || schema.OneOf is { Count: > 0 } || schema.AnyOf is { Count: > 0 })
                errors.Add($"model '{name}': composition (allOf, oneOf, anyOf) is not supported yet");

            var required = schema.Required ?? new HashSet<string>();
            var properties = new List<ContractProperty>();
            foreach (var (propName, propSchema) in schema.Properties!)
            {
                var where = $"model '{name}', property '{propName}'";
                if (!CamelCasePattern().IsMatch(propName))
                {
                    errors.Add($"{where}: property names must be camelCase");
                    continue;
                }

                var (type, nullable) = ResolveType(propSchema, where);
                if (type is null) continue;
                properties.Add(new ContractProperty(propName, type, !required.Contains(propName), nullable, Describe(propSchema)));
            }

            return new ContractModel(name, schema.Description, properties);
        }

        private List<ContractOperation> ReadOperations()
        {
            var operations = new List<ContractOperation>();
            var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (path, item) in doc.Paths.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                foreach (var (method, op) in (item.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
                    .OrderBy(o => o.Key.Method, StringComparer.Ordinal))
                {
                    var httpMethod = method.Method.ToUpperInvariant();
                    var id = op.OperationId;
                    var where = $"operation '{id ?? "(no operationId)"}' ({httpMethod} {path})";
                    var count = errors.Count;

                    if (string.IsNullOrEmpty(id))
                        errors.Add($"{where}: missing operationId");
                    else if (!OperationIdPattern().IsMatch(id))
                        errors.Add($"{where}: operationId must look like 'service.method' in camelCase");
                    else if (seenIds.TryGetValue(id, out var first))
                        errors.Add($"{where}: duplicate operationId, already used by {first}");
                    else
                        seenIds[id] = $"{httpMethod} {path}";

                    if (!path.StartsWith("/v1/", StringComparison.Ordinal))
                        errors.Add($"{where}: every path must start with /v1/");

                    var audience = ReadAudience(op, where);
                    if (audience is Audience.Console != path.StartsWith("/v1/console/", StringComparison.Ordinal))
                        errors.Add($"{where}: console operations, and only they, live under /v1/console/");

                    var service = ReadString(op, ServiceExtension);
                    if (service is null)
                        errors.Add($"{where}: missing {ServiceExtension}");
                    else if (id is not null && !id.StartsWith(service + ".", StringComparison.Ordinal))
                        errors.Add($"{where}: operationId must start with its service '{service}.'");

                    var idempotent = ReadBool(op, IdempotentExtension, where);
                    var parameters = ReadParams(op, path, where);
                    var body = ReadBody(op, where);
                    var (status, result) = ReadSuccess(op, where);

                    if (errors.Count > count || id is null || service is null || audience is null) continue;
                    operations.Add(new ContractOperation(
                        id, service, id[(service.Length + 1)..], httpMethod, path, audience.Value,
                        op.Description ?? op.Summary, parameters, body, status, result, idempotent));
                }
            }

            return [.. operations.OrderBy(o => o.Id, StringComparer.Ordinal)];
        }

        private Audience? ReadAudience(OpenApiOperation op, string where)
        {
            var value = ReadString(op, AudienceExtension);
            switch (value)
            {
                case null:
                    errors.Add($"{where}: missing {AudienceExtension}");
                    return null;
                case "client": return Audience.Client;
                case "server": return Audience.Server;
                case "both": return Audience.Both;
                case "console": return Audience.Console;
                default:
                    errors.Add($"{where}: {AudienceExtension} is '{value}', expected client, server, both, or console");
                    return null;
            }
        }

        private static string? ReadString(OpenApiOperation op, string name) =>
            op.Extensions is not null && op.Extensions.TryGetValue(name, out var ext)
                && ext is JsonNodeExtension { Node: JsonValue v } && v.TryGetValue<string>(out var s)
                ? s
                : null;

        private bool ReadBool(OpenApiOperation op, string name, string where)
        {
            if (op.Extensions is null || !op.Extensions.TryGetValue(name, out var ext)) return false;
            if (ext is JsonNodeExtension { Node: JsonValue v } && v.TryGetValue<bool>(out var b)) return b;
            errors.Add($"{where}: {name} must be true or false");
            return false;
        }

        private List<ContractParam> ReadParams(OpenApiOperation op, string path, string where)
        {
            var result = new List<ContractParam>();
            foreach (var p in op.Parameters ?? [])
            {
                var name = p.Name ?? "";
                var location = p.In switch
                {
                    ParameterLocation.Path => ParamLocation.Path,
                    ParameterLocation.Query => ParamLocation.Query,
                    _ => (ParamLocation?)null,
                };
                if (location is null)
                {
                    errors.Add($"{where}: parameter '{name}' is in {p.In}; only path and query parameters are supported");
                    continue;
                }

                if (!CamelCasePattern().IsMatch(name))
                {
                    errors.Add($"{where}: parameter '{name}' must be camelCase");
                    continue;
                }

                var (type, _) = ResolveType(p.Schema, $"{where}, parameter '{name}'");
                if (type is null) continue;
                if (type is not PrimitiveType primitive)
                {
                    errors.Add($"{where}: parameter '{name}' must be a string, number, boolean, or date");
                    continue;
                }

                result.Add(new ContractParam(name, location.Value, primitive, p.Required || location == ParamLocation.Path, p.Description));
            }

            foreach (var segment in path.Split('/').Where(s => s.StartsWith('{')))
            {
                var name = segment.Trim('{', '}');
                if (!result.Any(p => p.In == ParamLocation.Path && p.Name == name))
                    errors.Add($"{where}: path segment {segment} has no matching path parameter");
            }

            // Path parameters first, in path order, then query parameters, required before optional.
            return
            [
                .. result.Where(p => p.In == ParamLocation.Path).OrderBy(p => path.IndexOf("{" + p.Name + "}", StringComparison.Ordinal)),
                .. result.Where(p => p.In == ParamLocation.Query).OrderBy(p => !p.Required),
            ];
        }

        private ModelType? ReadBody(OpenApiOperation op, string where)
        {
            if (op.RequestBody is null) return null;
            var content = op.RequestBody.Content;
            if (content is null || content.Count != 1 || !content.TryGetValue("application/json", out var media))
            {
                errors.Add($"{where}: a request body must be application/json only");
                return null;
            }

            if (media.Schema is OpenApiSchemaReference { Reference.Id: { } modelId } && _schemas.ContainsKey(modelId))
                return new ModelType(modelId);
            errors.Add($"{where}: a request body must be a named model");
            return null;
        }

        private (int Status, TypeRef? Result) ReadSuccess(OpenApiOperation op, string where)
        {
            var successes = (op.Responses ?? [])
                .Where(r => r.Key.Length == 3 && r.Key[0] == '2')
                .ToList();
            if (successes.Count != 1)
            {
                errors.Add($"{where}: needs exactly one 2xx response, found {successes.Count}");
                return (0, null);
            }

            var (code, response) = successes[0];
            var status = int.Parse(code, CultureInfo.InvariantCulture);
            if (response.Content is null || response.Content.Count == 0) return (status, null);
            if (!response.Content.TryGetValue("application/json", out var media) || response.Content.Count != 1)
            {
                errors.Add($"{where}: the {code} response must be application/json only");
                return (status, null);
            }

            var (type, nullable) = ResolveType(media.Schema, $"{where}, {code} response");
            if (nullable) errors.Add($"{where}: the {code} response body cannot be null");
            return (status, type);
        }

        /// <summary>Maps a schema to a <see cref="TypeRef"/>, reporting anything outside the mapping rules.</summary>
        private (TypeRef? Type, bool Nullable) ResolveType(IOpenApiSchema? schema, string where)
        {
            if (schema is null)
            {
                errors.Add($"{where}: missing schema");
                return (null, false);
            }

            if (schema is OpenApiSchemaReference reference)
            {
                var id = reference.Reference.Id ?? "";
                if (!_schemas.TryGetValue(id, out var target))
                {
                    errors.Add($"{where}: unknown schema reference '{id}'");
                    return (null, false);
                }

                return (target.Enum is { Count: > 0 } ? new EnumType(id) : new ModelType(id), false);
            }

            // `T | null` compiles to anyOf: [T, { type: null }].
            var variants = (schema.AnyOf ?? []).Concat(schema.OneOf ?? []).ToList();
            if (variants.Count > 0)
            {
                var nonNull = variants.Where(v => v.Type is not JsonSchemaType.Null).ToList();
                if (schema.Discriminator is not null)
                {
                    errors.Add($"{where}: discriminated unions are not supported by SdkGen yet");
                    return (null, false);
                }

                if (nonNull.Count == 1 && variants.Count == 2)
                {
                    var (inner, _) = ResolveType(nonNull[0], where);
                    return (inner, true);
                }

                errors.Add($"{where}: a union without a discriminator is not supported");
                return (null, false);
            }

            var type = schema.Type ?? 0;
            var nullable = (type & JsonSchemaType.Null) != 0;
            type &= ~JsonSchemaType.Null;

            TypeRef? mapped = type switch
            {
                JsonSchemaType.String when schema.Enum is { Count: > 0 } => Unsupported("inline enums; declare a named enum"),
                JsonSchemaType.String => new PrimitiveType(schema.Format == "date-time" ? PrimitiveKind.DateTime : PrimitiveKind.String),
                JsonSchemaType.Integer => new PrimitiveType(schema.Format == "int64" ? PrimitiveKind.Int64 : PrimitiveKind.Int32),
                JsonSchemaType.Number => new PrimitiveType(PrimitiveKind.Float64),
                JsonSchemaType.Boolean => new PrimitiveType(PrimitiveKind.Boolean),
                JsonSchemaType.Array => ResolveType(schema.Items, $"{where} items").Type is { } item ? new ArrayType(item) : null,
                JsonSchemaType.Object when schema.Properties is { Count: > 0 } => Unsupported("inline objects; declare a named model"),
                JsonSchemaType.Object when schema.AdditionalProperties is { } values =>
                    ResolveType(values, $"{where} values").Type is { } value ? new MapType(value) : null,
                _ => Unsupported($"schema type '{type}'"),
            };
            return (mapped, nullable);

            TypeRef? Unsupported(string what)
            {
                errors.Add($"{where}: {what} is not supported");
                return null;
            }
        }

        private static string? Describe(IOpenApiSchema schema) =>
            schema.Description ?? (schema is OpenApiSchemaReference r ? r.Target?.Description : null);
    }
}
