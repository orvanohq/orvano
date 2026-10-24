using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api.OpenApi;

/// <summary>
/// Spec 0014 EVS-1, EVS-2: the public document's <c>webhooks</c>, one per registered public event, keyed by its type.
/// Each is a <c>post</c> whose body is the payload's schema, named <see cref="PublicEvent.SchemaName"/>. With no
/// public event, the document has no <c>webhooks</c>.
/// </summary>
internal sealed class WebhooksTransformer(IPublicEventCatalog catalog) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.DocumentName != ContractDocuments.Public || catalog.Events.Count == 0)
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Webhooks ??= new Dictionary<string, IOpenApiPathItem>();
        foreach (PublicEvent entry in catalog.Events)
        {
            OpenApiSchema schema = await context.GetOrCreateSchemaAsync(entry.Payload, null, cancellationToken);
            schema.Description = entry.Description;
            document.Components.Schemas[entry.SchemaName] = schema;
            document.Webhooks[entry.Type] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>
                {
                    [HttpMethod.Post] = new()
                    {
                        OperationId = $"Events_{entry.SchemaName}",
                        Summary = $"The {entry.Type} event.",
                        Description = entry.Description,
                        RequestBody = new OpenApiRequestBody
                        {
                            Required = true,
                            Description = $"The {entry.Type} payload.",
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new() { Schema = new OpenApiSchemaReference(entry.SchemaName, document) },
                            },
                        },
                        Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "The event was received." } },
                    },
                },
            };
        }
    }
}
