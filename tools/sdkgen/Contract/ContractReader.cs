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
    private const string TestExtension = "x-orvano-test";
    private const string EventExtension = "x-orvano-event";

    /// <summary>The error code catalogs (AC-6). They become constants, never enum types.</summary>
    private const string ErrorCatalog = "ErrorCode";
    private const string TestErrorCatalog = "TestErrorCode";

    /// <summary>The problem details model every operation declares as its <c>default</c> response.</summary>
    private const string ProblemModel = "Problem";
    private const string ProblemMediaType = "application/problem+json";

    /// <summary>Names the generated methods use for their own parameters.</summary>
    private static readonly string[] ReservedParams = ["body", "query", "options", "cancellationToken"];

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

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*(\\.[a-z][a-zA-Z0-9]*)+$")]
    private static partial Regex EventNamePattern();

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$")]
    private static partial Regex SnakeCasePattern();

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
            var errorCodes = new List<ContractErrorCode>();
            foreach (var (name, schema) in _schemas.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                if (!PascalCasePattern().IsMatch(name))
                {
                    errors.Add($"schema '{name}': model and enum names must be PascalCase");
                    continue;
                }

                if (name is ErrorCatalog or TestErrorCatalog)
                {
                    errorCodes.AddRange(ReadErrorCatalog(name, schema));
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

            if (!_schemas.ContainsKey(ErrorCatalog))
                errors.Add($"the contract needs an enum {ErrorCatalog} (contract/errors.tsp): the catalog of stable error codes");
            foreach (var duplicate in errorCodes.GroupBy(c => c.Code, StringComparer.Ordinal).Where(g => g.Count() > 1))
                errors.Add($"error code '{duplicate.Key}' is in both {ErrorCatalog} and {TestErrorCatalog}");
            CheckProblem(models);
            CheckEvents(models);

            var operations = ReadOperations();
            var contract = new ApiContract(version, operations, models, enums, [.. errorCodes.OrderBy(c => c.Code, StringComparer.Ordinal)]);
            CheckTestIsolation(contract);
            return contract;
        }

        private IEnumerable<ContractErrorCode> ReadErrorCatalog(string name, IOpenApiSchema schema)
        {
            var test = IsTest(schema.Extensions, $"enum '{name}'");
            if (test != (name == TestErrorCatalog))
                errors.Add($"enum '{name}': {TestErrorCatalog}, and only it, is marked {TestExtension}");

            foreach (var v in schema.Enum ?? [])
            {
                if (v is JsonValue jv && jv.TryGetValue<string>(out var code) && SnakeCasePattern().IsMatch(code))
                    yield return new ContractErrorCode(code, test);
                else
                    errors.Add($"enum '{name}': error codes must be snake_case strings, found {v?.ToJsonString() ?? "null"}");
            }
        }

        /// <summary>The <c>Problem</c> model must carry every member spec 0001 requires (AC-6).</summary>
        private void CheckProblem(List<ContractModel> models)
        {
            var problem = models.FirstOrDefault(m => m.Name == ProblemModel);
            if (problem is null)
            {
                errors.Add($"the contract needs a model {ProblemModel} (contract/errors.tsp): every operation's default response");
                return;
            }

            foreach (var required in new[] { "type", "title", "status", "code", "requestId" })
                if (!problem.Properties.Any(p => p.Name == required && !p.Optional && !p.Nullable))
                    errors.Add($"model '{ProblemModel}': needs the required property '{required}'");
        }

        private void CheckEvents(List<ContractModel> models)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in models.Where(m => m.Event is not null))
            {
                var where = $"model '{m.Name}', event '{m.Event}'";
                if (!EventNamePattern().IsMatch(m.Event!))
                    errors.Add($"{where}: event names look like 'service.event' in camelCase");
                if (seen.TryGetValue(m.Event!, out var first))
                    errors.Add($"{where}: duplicate event name, already used by model '{first}'");
                else
                    seen[m.Event!] = m.Name;
                if (m.Test != m.Event!.StartsWith("test.", StringComparison.Ordinal))
                    errors.Add($"{where}: test events, and only they, are named 'test.*' and marked {TestExtension}");
            }
        }

        /// <summary>
        /// Test code never leaks into a published package, and code only tests use is marked (AC-18):
        /// a public operation or event may not reach a test model, and a model or enum that only test
        /// operations and events reach must carry <c>x-orvano-test</c>.
        /// </summary>
        private void CheckTestIsolation(ApiContract contract)
        {
            var events = contract.Events;
            var publicReach = contract.Reach(contract.Operations.Where(o => !o.Test), events.Where(e => !e.Test));
            var testReach = contract.Reach(contract.Operations.Where(o => o.Test), events.Where(e => e.Test));
            var flagged = contract.Models.Where(m => m.Test).Select(m => m.Name)
                .Concat(contract.Enums.Where(e => e.Test).Select(e => e.Name))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var name in publicReach.Where(flagged.Contains).Order(StringComparer.Ordinal))
                errors.Add($"schema '{name}' is marked {TestExtension} but a public operation or event uses it");
            foreach (var name in testReach.Where(n => !publicReach.Contains(n) && !flagged.Contains(n)).Order(StringComparer.Ordinal))
                errors.Add($"schema '{name}' is used only by test operations or events; mark it {TestExtension}");
        }

        private ContractEnum ReadEnum(string name, IOpenApiSchema schema, IList<JsonNode> values)
        {
            var wire = new List<string>();
            foreach (var v in values)
            {
                if (v is JsonValue jv && jv.TryGetValue<string>(out var s)) wire.Add(s);
                else errors.Add($"enum '{name}': only string values are supported, found {v?.ToJsonString() ?? "null"}");
            }

            return new ContractEnum(name, schema.Description, wire, IsTest(schema.Extensions, $"enum '{name}'"));
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
                properties.Add(new ContractProperty(propName, type, !required.Contains(propName), nullable, Describe(propSchema), ExampleOf(propSchema)));
            }

            return new ContractModel(
                name,
                schema.Description,
                properties,
                IsTest(schema.Extensions, $"model '{name}'"),
                ReadString(schema.Extensions, EventExtension));
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

                    var service = ReadString(op.Extensions, ServiceExtension);
                    if (service is null)
                        errors.Add($"{where}: missing {ServiceExtension}");
                    else if (id is not null && !id.StartsWith(service + ".", StringComparison.Ordinal))
                        errors.Add($"{where}: operationId must start with its service '{service}.'");

                    var test = IsTest(op.Extensions, where);
                    var testPath = path.StartsWith("/v1/test/", StringComparison.Ordinal) || path.StartsWith("/v1/console/test/", StringComparison.Ordinal);
                    if (test != testPath)
                        errors.Add($"{where}: {TestExtension} operations, and only they, live under /v1/test/ or /v1/console/test/");
                    if (service is not null && test != (service == "test"))
                        errors.Add($"{where}: {TestExtension} operations, and only they, are in the 'test' service");

                    var idempotent = ReadBool(op.Extensions, IdempotentExtension, where);
                    var parameters = ReadParams(op, path, where);
                    var body = ReadBody(op, where);
                    var (status, result) = ReadSuccess(op, where);
                    ReadDefault(op, where);
                    var pageItem = ReadPage(httpMethod, parameters, result, where);

                    if (errors.Count > count || id is null || service is null || audience is null) continue;
                    operations.Add(new ContractOperation(
                        id, service, id[(service.Length + 1)..], httpMethod, path, audience.Value,
                        op.Description ?? op.Summary, parameters, body, status, result, idempotent, test, pageItem));
                }
            }

            return [.. operations.OrderBy(o => o.Id, StringComparer.Ordinal)];
        }

        private Audience? ReadAudience(OpenApiOperation op, string where)
        {
            var value = ReadString(op.Extensions, AudienceExtension);
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

        private static string? ReadString(IDictionary<string, IOpenApiExtension>? extensions, string name) =>
            extensions is not null && extensions.TryGetValue(name, out var ext)
                && ext is JsonNodeExtension { Node: JsonValue v } && v.TryGetValue<string>(out var s)
                ? s
                : null;

        private bool ReadBool(IDictionary<string, IOpenApiExtension>? extensions, string name, string where)
        {
            if (extensions is null || !extensions.TryGetValue(name, out var ext)) return false;
            if (ext is JsonNodeExtension { Node: JsonValue v } && v.TryGetValue<bool>(out var b)) return b;
            errors.Add($"{where}: {name} must be true or false");
            return false;
        }

        private bool IsTest(IDictionary<string, IOpenApiExtension>? extensions, string where) =>
            ReadBool(extensions, TestExtension, where);

        /// <summary>Every operation's <c>default</c> response is <c>Problem</c> as problem+json (AC-6).</summary>
        private void ReadDefault(OpenApiOperation op, string where)
        {
            if (op.Responses is null || !op.Responses.TryGetValue("default", out var response)
                || response.Content is not { Count: 1 } content
                || !content.TryGetValue(ProblemMediaType, out var media)
                || media.Schema is not OpenApiSchemaReference { Reference.Id: ProblemModel })
            {
                errors.Add($"{where}: the default response must be {ProblemModel} as {ProblemMediaType}; return `T | {ProblemModel}`");
            }

            foreach (var code in (op.Responses ?? []).Keys.Where(k => k != "default" && k[0] != '2'))
                errors.Add($"{where}: declare errors through the default {ProblemModel} response, not a {code} response");
        }

        /// <summary>
        /// A cursor list operation (AC-7) takes an optional <c>cursor</c> and <c>limit</c> and returns
        /// <c>{ items, nextCursor }</c>. Returns the item type for a list operation, else null.
        /// </summary>
        private TypeRef? ReadPage(string httpMethod, List<ContractParam> parameters, TypeRef? result, string where)
        {
            var cursor = parameters.FirstOrDefault(p => p.Name == "cursor");
            var page = result is ModelType m && _schemas.TryGetValue(m.Name, out var schema) ? PageShape(schema) : null;
            if (cursor is null && page is null) return null;

            var limit = parameters.FirstOrDefault(p => p.Name == "limit");
            if (httpMethod != "GET"
                || cursor is not { In: ParamLocation.Query, Required: false, Type.Kind: PrimitiveKind.String }
                || limit is not { In: ParamLocation.Query, Required: false, Type.Kind: PrimitiveKind.Int32 }
                || page is null)
            {
                errors.Add($"{where}: a list operation is a GET with optional query parameters `cursor` (string) and `limit` (int32) that returns a model of exactly `items: T[]` and `nextCursor: string | null`");
                return null;
            }

            return page;
        }

        /// <summary>The item type when <paramref name="schema"/> is exactly <c>{ items: T[], nextCursor: string | null }</c>.</summary>
        private TypeRef? PageShape(IOpenApiSchema schema)
        {
            if (schema.Properties is not { Count: 2 } properties
                || !properties.TryGetValue("items", out var items) || !properties.TryGetValue("nextCursor", out var next)
                || schema.Required is not { } required || !required.Contains("items") || !required.Contains("nextCursor"))
            {
                return null;
            }

            var scratch = new List<string>();
            var itemType = new Reader(doc, scratch).ResolveType(items, "items");
            var nextType = new Reader(doc, scratch).ResolveType(next, "nextCursor");
            return scratch.Count == 0 && itemType is { Type: ArrayType array, Nullable: false }
                && nextType is { Type: PrimitiveType { Kind: PrimitiveKind.String }, Nullable: true }
                ? array.Item
                : null;
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

                if (ReservedParams.Contains(name, StringComparer.Ordinal))
                {
                    errors.Add($"{where}: parameter '{name}' is reserved for the generated methods ({string.Join(", ", ReservedParams)})");
                    continue;
                }

                var (type, _) = ResolveType(p.Schema, $"{where}, parameter '{name}'");
                if (type is null) continue;
                if (type is not PrimitiveType primitive)
                {
                    errors.Add($"{where}: parameter '{name}' must be a string, number, boolean, or date");
                    continue;
                }

                result.Add(new ContractParam(name, location.Value, primitive, p.Required || location == ParamLocation.Path, p.Description,
                    p.Example ?? (p.Schema is null ? null : ExampleOf(p.Schema))));
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
        public (TypeRef? Type, bool Nullable) ResolveType(IOpenApiSchema? schema, string where)
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

                if (id is ErrorCatalog or TestErrorCatalog)
                {
                    errors.Add($"{where}: {id} is a catalog of constants; use `string` (see Problem.code)");
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
                JsonSchemaType.Object when MapValues(schema) is { } values =>
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

        /// <summary>
        /// The value schema of a <c>Record&lt;T&gt;</c>. TypeSpec's OpenAPI 3.1 emitter writes it as
        /// <c>unevaluatedProperties</c>; other tools write <c>additionalProperties</c>.
        /// </summary>
        private static IOpenApiSchema? MapValues(IOpenApiSchema schema) =>
            schema.AdditionalProperties ?? (schema as IOpenApiSchemaMissingProperties)?.UnevaluatedPropertiesSchema;

        /// <summary>The schema's first <c>@example</c> value, for docs snippets (AC-15).</summary>
        private static JsonNode? ExampleOf(IOpenApiSchema schema) => schema.Examples?.FirstOrDefault();

        private static string? Describe(IOpenApiSchema schema) =>
            schema.Description ?? (schema is OpenApiSchemaReference r ? r.Target?.Description : null);
    }
}
