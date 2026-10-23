using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Orvano.Host.Api.OpenApi;

/// <summary>
/// The types whose JSON comes from the wire format's own converters (spec 0014 JSW-2, JSW-3), which the built in
/// schema exporter cannot see through: enums are snake_case strings and timestamps are RFC 3339 UTC strings.
/// </summary>
internal sealed class WireSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        Type type = context.JsonTypeInfo.Type;
        Type? underlying = Nullable.GetUnderlyingType(type);
        Type value = underlying ?? type;
        JsonSchemaType nullable = underlying is null ? 0 : JsonSchemaType.Null;

        if (value.IsEnum)
        {
            schema.Type = JsonSchemaType.String | nullable;
        }
        else if (value == typeof(DateTimeOffset))
        {
            schema.Type = JsonSchemaType.String | nullable;
            schema.Format = "date-time";
        }

        return Task.CompletedTask;
    }
}
