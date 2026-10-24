using System.Text.Json.Nodes;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api.OpenApi;

/// <summary>
/// CTR-4: the title, the version (<see cref="ApiVersions.Current"/>, VRS-2), the license, and no <c>servers</c>, since
/// each install has its own URL.
/// </summary>
internal sealed class ContractInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        bool isPublic = context.DocumentName == ContractDocuments.Public;
        document.Info = new OpenApiInfo
        {
            Title = isPublic ? "Orvano API" : "Orvano Console API",
            Version = ApiVersions.Current,
            Description = isPublic
                ? "The Orvano API that the client and server SDKs call. Send the version in `Orvano-Api-Version`; every error is a problem body with a `code`."
                : "The control plane API that the Orvano console calls. It ships with the server and has no SDK.",
            License = new OpenApiLicense { Name = "Apache 2.0", Identifier = "Apache-2.0" },
        };
        document.Servers = [];
        return Task.CompletedTask;
    }
}

/// <summary>
/// XTN-1: <c>x-orvano-error-codes</c> on the public document, every code in the <see cref="ErrorCatalogue"/> with its
/// status, description, and the problem members it adds. The generator emits <c>OrvanoErrorCodes</c> from it.
/// </summary>
internal sealed class ErrorCatalogueTransformer(ErrorCatalogue catalogue) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.DocumentName != ContractDocuments.Public)
        {
            return Task.CompletedTask;
        }

        var codes = new JsonObject();
        foreach ((string code, ErrorCodeAttribute entry) in catalogue.Entries)
        {
            var value = new JsonObject { ["status"] = entry.Status, ["description"] = entry.Description };
            if (entry.Members.Length > 0)
            {
                value["members"] = new JsonArray([.. entry.Members.Order(StringComparer.Ordinal).Select(member => (JsonNode)JsonValue.Create(member))]);
            }

            codes[code] = value;
        }

        document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        document.Extensions[ContractExtensions.ErrorCodes] = new JsonNodeExtension(codes);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Tags each operation with its SDK service (the console's with its first path segment under <c>/v1/console</c>), so
/// reference docs group operations the way the SDKs do, and declares every tag once at the document level.
/// </summary>
internal sealed class ContractTagsTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        foreach ((string path, IOpenApiPathItem item) in document.Paths)
        {
            foreach (OpenApiOperation operation in item.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            {
                string tag = Service(operation) ?? ConsoleArea(path);
                tags.Add(tag);
                operation.Tags = new HashSet<OpenApiTagReference> { new(tag, document) };
            }
        }

        document.Tags = new HashSet<OpenApiTag>(tags.Select(tag => new OpenApiTag { Name = tag }));
        return Task.CompletedTask;
    }

    private static string? Service(OpenApiOperation operation) =>
        operation.Extensions?.TryGetValue(ContractExtensions.Sdk, out IOpenApiExtension? sdk) == true && sdk is JsonNodeExtension node
            ? node.Node["service"]?.GetValue<string>()
            : null;

    private static string ConsoleArea(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(2).FirstOrDefault(segment => !segment.StartsWith('{')) ?? "console";
}

/// <summary>
/// The body schema rules, by direction (JSW-11 keeps each schema in one). Response schemas list every property in
/// <c>required</c>, since a nullable one is sent as <c>null</c>, never omitted (JSW-6), and drop
/// <c>additionalProperties: false</c>, which comes from the request rule (JSW-7): fields are added to responses, and
/// clients ignore the ones they do not know. Request schemas require only their non nullable properties, since an
/// optional one may be omitted (JSW-6), and not a partial update's <c>Optional&lt;T&gt;</c> fields (JSW-9), and keep
/// refusing unknown ones.
/// </summary>
internal sealed class BodySchemaTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        OpenApiOperation[] operations = [.. document.Paths.Values.SelectMany(item => item.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())];
        // EVS-1: an event payload is sent by the server, so it follows the response rule.
        IEnumerable<IOpenApiSchema> events = (document.Webhooks?.Values ?? Enumerable.Empty<IOpenApiPathItem>())
            .SelectMany(item => item.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            .SelectMany(operation => operation.RequestBody?.Content?.Values ?? [])
            .Select(media => media.Schema)
            .OfType<IOpenApiSchema>();
        IEnumerable<IOpenApiSchema> responses = operations
            .SelectMany(operation => operation.Responses?.Values ?? Enumerable.Empty<IOpenApiResponse>())
            .SelectMany(response => response.Content?.Values ?? [])
            .Select(media => media.Schema)
            .OfType<IOpenApiSchema>()
            .Concat(events);
        IEnumerable<IOpenApiSchema> requests = operations
            .SelectMany(operation => operation.RequestBody?.Content?.Values ?? [])
            .Select(media => media.Schema)
            .OfType<IOpenApiSchema>();

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (IOpenApiSchema schema in responses)
        {
            Visit(schema, document, visited, Response);
        }

        visited.Clear();
        foreach (IOpenApiSchema schema in requests)
        {
            Visit(schema, document, visited, Request);
        }

        return Task.CompletedTask;
    }

    private static void Response(OpenApiSchema schema)
    {
        schema.Required = new HashSet<string>(schema.Properties!.Keys, StringComparer.Ordinal);
        schema.AdditionalPropertiesAllowed = true;
        schema.AdditionalProperties = null;
    }

    private static void Request(OpenApiSchema schema) =>
        schema.Required = new HashSet<string>(
            schema.Properties!.Where(property => !IsNullable(property.Value) && !IsOptional(property.Value)).Select(property => property.Key), StringComparer.Ordinal);

    // JSW-9: a partial update's Optional<T> field may be omitted, nullable or not.
    private static bool IsOptional(IOpenApiSchema schema) =>
        schema is OpenApiSchema { Metadata: { } metadata } && metadata.ContainsKey(OptionalSchemaTransformer.OptionalMetadata);

    private static bool IsNullable(IOpenApiSchema schema) =>
        schema.Type?.HasFlag(JsonSchemaType.Null) == true
        || schema.OneOf?.Any(option => option.Type == JsonSchemaType.Null) == true
        || schema.AnyOf?.Any(option => option.Type == JsonSchemaType.Null) == true;

    private static void Visit(IOpenApiSchema schema, OpenApiDocument document, HashSet<string> visited, Action<OpenApiSchema> apply)
    {
        if (schema is OpenApiSchemaReference reference)
        {
            string? id = reference.Reference.Id;
            if (id is null || id == ContractComponentsTransformer.ProblemSchema || !visited.Add(id)
                || document.Components?.Schemas?.TryGetValue(id, out IOpenApiSchema? target) != true)
            {
                return;
            }

            Visit(target!, document, visited, apply);
            return;
        }

        if (schema is not OpenApiSchema concrete)
        {
            return;
        }

        if (concrete.Properties is { Count: > 0 })
        {
            apply(concrete);
        }

        IEnumerable<IOpenApiSchema?> children = (concrete.Properties?.Values ?? [])
            .Concat(concrete.AllOf ?? []).Concat(concrete.AnyOf ?? []).Concat(concrete.OneOf ?? [])
            .Append(concrete.Items).Append(concrete.AdditionalProperties);
        foreach (IOpenApiSchema child in children.OfType<IOpenApiSchema>())
        {
            Visit(child, document, visited, apply);
        }
    }
}

/// <summary>
/// Drops component schemas nothing references, such as the <c>ProblemDetails</c> that <c>.ProducesProblem</c> and the
/// access filters declare before <see cref="ContractOperationTransformer"/> swaps in <c>Problem</c>.
/// </summary>
internal sealed class UnusedSchemaTransformer : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Components?.Schemas is not { Count: > 0 } schemas)
        {
            return;
        }

        // References are easiest to find in the written form, so drop and repeat until nothing changes.
        while (true)
        {
            string json = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, cancellationToken);
            string[] unused = [.. schemas.Keys.Where(id => !json.Contains($"\"#/components/schemas/{id}\"", StringComparison.Ordinal))];
            if (unused.Length == 0)
            {
                return;
            }

            foreach (string id in unused)
            {
                schemas.Remove(id);
            }
        }
    }
}
