using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;

using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api.OpenApi;

/// <summary>
/// Writes each operation's contract metadata (spec 0014 XTN-1, CTR-6): the <c>x-orvano-*</c> extensions from the
/// endpoint's <see cref="ContractEndpointExtensions"/> calls and access declaration, the shared <c>Problem</c> body on
/// every error response plus a <c>default</c> one (PRB-1), and the headers every <c>/v1</c> response carries (HDR-5,
/// HDR-6).
/// </summary>
internal sealed partial class ContractOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();

        if (metadata.OfType<SdkMetadata>().LastOrDefault() is { } sdk)
        {
            operation.Extensions[ContractExtensions.Sdk] = Node(new JsonObject { ["service"] = sdk.Service, ["method"] = sdk.Method });
        }

        if (context.DocumentName == ContractDocuments.Public)
        {
            var endpoint = new Endpoint(null, new EndpointMetadataCollection(metadata), operation.OperationId);
            if (AccessDeclaration.Audience(endpoint) is { } audience)
            {
                operation.Extensions[ContractExtensions.Audience] = Node(Array(audience));
            }

            if (metadata.OfType<ErrorsMetadata>().LastOrDefault() is { } errors)
            {
                operation.Extensions[ContractExtensions.Errors] = Node(Array(errors.Codes));
            }
        }

        if (metadata.OfType<MeterMetadata>().LastOrDefault() is { } meter)
        {
            operation.Extensions[ContractExtensions.Metered] = Node(JsonValue.Create(meter.Name));
        }

        if (metadata.OfType<PagingMetadata>().LastOrDefault() is { } paging)
        {
            operation.Extensions[ContractExtensions.Paging] = Node(new JsonObject { ["total"] = paging.Total });
            WritePagingParameters(operation);
        }

        bool concurrency = metadata.OfType<ConcurrencyMetadata>().Any();
        if (concurrency)
        {
            operation.Extensions[ContractExtensions.Concurrency] = Node(JsonValue.Create(true));
        }

        bool idempotent = metadata.OfType<IdempotencyMetadata>().Any();
        if (idempotent)
        {
            operation.Extensions[ContractExtensions.Idempotent] = Node(JsonValue.Create(true));
        }

        if (metadata.OfType<SdkTemplateMetadata>().LastOrDefault() is { } template)
        {
            operation.Extensions[ContractExtensions.SdkTemplate] = Node(JsonValue.Create(template.Name));
        }

        WritePathParameters(operation, context.Description.RelativePath);
        if (concurrency)
        {
            WriteConcurrency(operation, context.Document!);
        }

        if (idempotent)
        {
            // IDM-1: the marked operation documents the header it takes.
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameterReference(OrvanoHeaders.IdempotencyKey, context.Document!));
        }

        WriteResponses(operation, context.Document!);
        return Task.CompletedTask;
    }

    // PTH-2: every parameter in the path is in the operation, the ones a handler does not bind too (the group's
    // {projectId}). The group parameters are uuids (their routes use :guid) and the host owns their descriptions; a
    // handler's own parameters come with their type and XML docs.
    private static void WritePathParameters(OpenApiOperation operation, string? path)
    {
        if (path is null)
        {
            return;
        }

        operation.Parameters ??= [];
        foreach (Match match in RouteParameter().Matches(path))
        {
            string name = match.Groups["name"].Value;
            IOpenApiParameter? existing = operation.Parameters.FirstOrDefault(parameter => parameter.In == ParameterLocation.Path && parameter.Name == name);
            if (existing is null)
            {
                existing = new OpenApiParameter
                {
                    Name = name,
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = GroupParameters.ContainsKey(name) ? "uuid" : null },
                };
                operation.Parameters.Insert(0, existing);
            }

            if (existing is OpenApiParameter { Description: null } concrete && GroupParameters.TryGetValue(name, out string? description))
            {
                concrete.Description = description;
            }
        }
    }

    // PAG-1: the limits PageQuery validates, so the SDKs and docs show them.
    private static void WritePagingParameters(OpenApiOperation operation)
    {
        foreach (IOpenApiParameter parameter in operation.Parameters ?? [])
        {
            if (parameter is not OpenApiParameter { In: ParameterLocation.Query, Schema: OpenApiSchema schema } concrete)
            {
                continue;
            }

            switch (concrete.Name)
            {
                case "limit":
                    concrete.Description ??= "How many items to return, 1 to 100; 50 when not sent.";
                    schema.Minimum = "1";
                    schema.Maximum = PageQuery.MaxLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    schema.Default = PageQuery.DefaultLimit;
                    break;
                case "cursor":
                    concrete.Description ??= "The nextCursor of the previous page; omit it for the first page.";
                    schema.MaxLength = PageQuery.MaxCursorLength;
                    break;
                case "query":
                    concrete.Description ??= "Matches the name as a contains, ignoring case.";
                    break;
                default:
                    break;
            }
        }
    }

    private static readonly Dictionary<string, string> GroupParameters = new(StringComparer.Ordinal)
    {
        [AccessEndpointFilters.ProjectIdRouteValue] = "The project's ID.",
        [AccessEndpointFilters.OrganizationIdRouteValue] = "The organization's ID.",
    };

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex RouteParameter();

    // CNC-3, CNC-4: the write takes If-Match and can answer 412 version_conflict; its answer carries the ETag (CNC-1).
    private static void WriteConcurrency(OpenApiOperation operation, OpenApiDocument document)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameterReference(OrvanoHeaders.IfMatch, document));
        operation.Responses ??= [];
        operation.Responses[StatusCodes.Status412PreconditionFailed.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new OpenApiResponse();
    }

    // PRB-1: every error answer is the shared Problem body, whatever type the endpoint declared it with.
    private static void WriteResponses(OpenApiOperation operation, OpenApiDocument document)
    {
        operation.Responses ??= [];
        foreach ((string key, IOpenApiResponse response) in operation.Responses.ToList())
        {
            if (int.TryParse(key, out int status) && status >= 400)
            {
                operation.Responses[key] = ProblemResponse(document, ReasonPhrases.GetReasonPhrase(status), status);
            }
            else if (response is OpenApiResponse concrete)
            {
                concrete.Headers = ResponseHeaders(document, status: 200);
                if (status is >= 200 and < 300 and not 204 && operation.Extensions?.ContainsKey(ContractExtensions.Concurrency) == true)
                {
                    concrete.Headers[OrvanoHeaders.ETag] = new OpenApiHeaderReference(OrvanoHeaders.ETag, document);
                }
            }
        }

        operation.Responses["default"] = ProblemResponse(
            document, "An error. The code says which one; x-orvano-error-codes describes every code.", status: null);
    }

    private static OpenApiResponse ProblemResponse(OpenApiDocument document, string description, int? status) => new()
    {
        Description = description,
        Headers = ResponseHeaders(document, status),
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference(ContractComponentsTransformer.ProblemSchema, document) },
        },
    };

    // HDR-5, HDR-6: the request ID and the version used are on every /v1 response; Retry-After on a 429 or 503 (PRB-6).
    private static Dictionary<string, IOpenApiHeader> ResponseHeaders(OpenApiDocument document, int? status)
    {
        var headers = new Dictionary<string, IOpenApiHeader>
        {
            [OrvanoHeaders.RequestId] = new OpenApiHeaderReference(OrvanoHeaders.RequestId, document),
            [OrvanoHeaders.ApiVersion] = new OpenApiHeaderReference(OrvanoHeaders.ApiVersion, document),
        };
        if (status is null or StatusCodes.Status429TooManyRequests or StatusCodes.Status503ServiceUnavailable)
        {
            headers[OrvanoHeaders.RetryAfter] = new OpenApiHeaderReference(OrvanoHeaders.RetryAfter, document);
        }

        return headers;
    }

    private static JsonArray Array(IEnumerable<string> values) =>
        [.. values.Order(StringComparer.Ordinal).Select(value => (JsonNode)JsonValue.Create(value))];

    private static JsonNodeExtension Node(JsonNode node) => new(node);
}

/// <summary>The extension names (spec 0014 XTN-1), the only ones the generator and lint read.</summary>
internal static class ContractExtensions
{
    public const string Sdk = "x-orvano-sdk";

    public const string Audience = "x-orvano-audience";

    public const string Errors = "x-orvano-errors";

    public const string Metered = "x-orvano-metered";

    public const string Paging = "x-orvano-paging";

    public const string Concurrency = "x-orvano-concurrency";

    public const string Idempotent = "x-orvano-idempotent";

    public const string SdkTemplate = "x-orvano-sdk-template";

    public const string ErrorCodes = "x-orvano-error-codes";
}
