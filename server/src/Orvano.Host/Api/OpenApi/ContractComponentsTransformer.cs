using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api.OpenApi;

/// <summary>
/// The shared components (spec 0014 CTR-6): the <c>Problem</c> and <c>FieldError</c> schemas (PRB-1, PRB-3), every
/// request header from <see cref="OrvanoHeaders"/> in <c>components.parameters</c>, and every response header in
/// <c>components.headers</c> (HDR-4). <c>ContractHeaderTests</c> fails when these lists and the class differ.
/// </summary>
internal sealed class ContractComponentsTransformer : IOpenApiDocumentTransformer
{
    public const string ProblemSchema = "Problem";

    public const string FieldErrorSchema = "FieldError";

    // FieldLocation's wire names, in declaration order.
    private static readonly string[] Locations =
    [
        .. Enum.GetNames<FieldLocation>().Select(name =>
            typeof(FieldLocation).GetField(name)!.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? name),
    ];

    /// <summary>HDR-1's request headers, with what the caller must know about each.</summary>
    public static IReadOnlyDictionary<string, string> RequestHeaders { get; } = new Dictionary<string, string>
    {
        [OrvanoHeaders.Authorization] = "`Bearer <access token>` for a signed in end user.",
        [OrvanoHeaders.Key] = "The project's publishable (`orv_pk_…`) or secret (`orv_sk_…`) key.",
        [OrvanoHeaders.ApiVersion] = "The API version the client was built for, `YYYY-MM-DD`. Without it the server uses its current version.",
        [OrvanoHeaders.Client] = "The SDK and its version, `<package>/<version>`. Used for telemetry only.",
        [OrvanoHeaders.Csrf] = "The console session's CSRF token, on requests that change state.",
        [OrvanoHeaders.IdempotencyKey] = "1 to 255 printable ASCII characters that make a retried request safe, on operations marked `x-orvano-idempotent`.",
        [OrvanoHeaders.IfMatch] = "The `ETag` of the version you read. A write to a changed resource answers `412 version_conflict`.",
        [OrvanoHeaders.TraceParent] = "W3C trace context, to join the request to your trace.",
        [OrvanoHeaders.TraceState] = "W3C trace state, sent with `traceparent`.",
    };

    /// <summary>HDR-1's response headers, with what the caller must know about each.</summary>
    public static IReadOnlyDictionary<string, string> ResponseHeaders { get; } = new Dictionary<string, string>
    {
        [OrvanoHeaders.RequestId] = "The request's 32 hex character trace ID. Equals the problem body's `traceId`; quote it when you report an issue.",
        [OrvanoHeaders.ApiVersion] = "The API version the server answered with.",
        [OrvanoHeaders.ETag] = "The strong ETag of the resource's version. Send it back as `If-Match` to write safely.",
        [OrvanoHeaders.RetryAfter] = "Seconds to wait before trying again, on a 429 or a 503 that knows when to come back.",
        [OrvanoHeaders.IdempotentReplayed] = "`true` when the answer is a replay of an earlier request with the same `Idempotency-Key`.",
    };

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas[FieldErrorSchema] = FieldError();
        document.Components.Schemas[ProblemSchema] = Problem(document);

        document.Components.Parameters = RequestHeaders.ToDictionary(
            header => header.Key,
            header => (IOpenApiParameter)new OpenApiParameter
            {
                Name = header.Key,
                In = ParameterLocation.Header,
                Description = header.Value,
                Schema = String(),
            });
        document.Components.Headers = ResponseHeaders.ToDictionary(
            header => header.Key,
            header => (IOpenApiHeader)new OpenApiHeader
            {
                Description = header.Value,
                Schema = header.Key == OrvanoHeaders.RetryAfter
                    ? new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "0" }
                    : String(),
            });
        return Task.CompletedTask;
    }

    // PRB-3.
    private static OpenApiSchema FieldError() => new()
    {
        Type = JsonSchemaType.Object,
        Description = "One problem with one field of the request.",
        Required = new HashSet<string> { "field", "location", "code", "message" },
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["field"] = String("The field's wire name; in a body, a dotted camelCase path with indexes, such as `address.city` or `tags[2]`."),
            ["location"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "Where the field is.",
                Enum = [.. Locations.Select(value => (JsonNode)JsonValue.Create(value))],
            },
            ["code"] = String("What is wrong, such as `required` or `too_long`."),
            ["message"] = String("An English sentence describing the problem, safe to show."),
        },
    };

    // PRB-1: the members every problem has, then the ones some codes add (x-orvano-error-codes names which).
    private static OpenApiSchema Problem(OpenApiDocument document) => new()
    {
        Type = JsonSchemaType.Object,
        Description = "The error body of every non 2xx response (RFC 9457 problem details). `code` identifies the error.",
        Required = new HashSet<string> { "type", "title", "status", "detail", "code", "traceId" },
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["type"] = String("Always `about:blank`: the code is the error's identity."),
            ["title"] = String("The HTTP reason phrase, such as `Not Found`."),
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Description = "The HTTP status." },
            ["detail"] = String("An English message safe to show to the user."),
            ["code"] = String("The snake_case error code; x-orvano-error-codes lists every code."),
            ["traceId"] = String("Equals the `Orvano-Request-Id` header."),
            ["errors"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Description = "On `validation_failed`: each field that is not valid.",
                Items = new OpenApiSchemaReference(FieldErrorSchema, document),
            },
            ["retryAfter"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Description = "On a 429 or a 503 that knows when to come back: seconds to wait, equal to `Retry-After`.",
            },
            ["requiredRole"] = String("On a console 403: the console role that would allow the action."),
            ["currentVersion"] = String("On `version_conflict`: the resource's current version, as its `ETag` without quotes."),
            ["supportedVersions"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Description = "On `unsupported_api_version`: the versions the server serves.",
                Items = String(),
            },
        },
    };

    private static OpenApiSchema String(string? description = null) => new() { Type = JsonSchemaType.String, Description = description };
}
