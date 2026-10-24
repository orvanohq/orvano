using System.ComponentModel;
using System.Reflection;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api.OpenApi;

/// <summary>
/// Spec 0014 JSW-9: an <see cref="Optional{T}"/> property is its <c>T</c>'s schema, nullable only when
/// <c>T</c> is (<c>string?</c>, <c>int?</c>), and never in its object's <c>required</c> list. CNC-1: a property marked
/// <c>[ReadOnly(true)]</c>, such as a resource's <c>version</c>, is <c>readOnly</c>.
/// </summary>
internal sealed class OptionalSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>Schema metadata (never written) that marks an Optional property, so the request rule leaves it out of <c>required</c>.</summary>
    public const string OptionalMetadata = "orvano-optional";

    private static readonly NullabilityInfoContext Nullability = new();

    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        Type type = context.JsonTypeInfo.Type;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>))
        {
            Type value = type.GetGenericArguments()[0];
            OpenApiSchema inner = await context.GetOrCreateSchemaAsync(Nullable.GetUnderlyingType(value) ?? value, null, cancellationToken);
            Copy(inner, schema);
            schema.Metadata = new Dictionary<string, object>(inner.Metadata ?? new Dictionary<string, object>()) { [OptionalMetadata] = true };
            if (AllowsNull(value, context.JsonPropertyInfo?.AttributeProvider as PropertyInfo) && schema.Type is { } kind)
            {
                schema.Type = kind | JsonSchemaType.Null;
            }

            return;
        }

        if (context.JsonPropertyInfo?.AttributeProvider is MemberInfo member && member.GetCustomAttribute<ReadOnlyAttribute>() is { IsReadOnly: true })
        {
            schema.ReadOnly = true;
        }

        foreach (System.Text.Json.Serialization.Metadata.JsonPropertyInfo property in context.JsonTypeInfo.Properties)
        {
            if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(Optional<>))
            {
                schema.Required?.Remove(property.Name);
            }
        }
    }

    private static bool AllowsNull(Type value, PropertyInfo? property) =>
        value.IsValueType
            ? Nullable.GetUnderlyingType(value) is not null
            : property is null || Nullability.Create(property).GenericTypeArguments[0].ReadState != NullabilityState.NotNull;

    private static void Copy(OpenApiSchema from, OpenApiSchema to)
    {
        to.Type = from.Type;
        to.Format = from.Format;
        to.Items = from.Items;
        to.Properties = from.Properties;
        to.Required = from.Required;
        to.AdditionalProperties = from.AdditionalProperties;
        to.Enum = from.Enum;
        to.MinLength = from.MinLength;
        to.MaxLength = from.MaxLength;
        to.Minimum = from.Minimum;
        to.Maximum = from.Maximum;
        to.Pattern = from.Pattern;
    }
}
