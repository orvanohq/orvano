using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// Spec 0014 JSW-9: a body field declared <c>Optional&lt;T&gt;</c> with a non nullable <c>T</c> (<c>Optional&lt;string&gt;</c>,
/// <c>Optional&lt;int&gt;</c>) refuses a JSON <c>null</c> with the field error <c>required</c>. The nullable annotations
/// are read once, when the endpoint is built; endpoints whose body has no such field get no filter.
/// </summary>
internal static class OptionalNullCheck
{
    private static readonly NullabilityInfoContext Nullability = new();

    public static EndpointFilterDelegate Factory(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        var checks = new List<(int Index, Field[] Fields)>();
        ParameterInfo[] parameters = context.MethodInfo.GetParameters();
        for (int index = 0; index < parameters.Length; index++)
        {
            if (IsBody(parameters[index]) && FieldsOf(parameters[index].ParameterType) is { Length: > 0 } fields)
            {
                checks.Add((index, fields));
            }
        }

        if (checks.Count == 0)
        {
            return next;
        }

        return invocation =>
        {
            List<FieldError>? errors = null;
            foreach ((int index, Field[] fields) in checks)
            {
                if (invocation.Arguments[index] is not { } body)
                {
                    continue;
                }

                foreach (Field field in fields)
                {
                    if (field.SentNull(body))
                    {
                        (errors ??= []).Add(new FieldError(field.Name, FieldLocation.Body, FieldCodes.Required, ValidationMessages.For(FieldCodes.Required, field.Name)));
                    }
                }
            }

            return errors is null
                ? next(invocation)
                : ValueTask.FromResult<object?>(Error.Validation(ErrorCodes.ValidationFailed, "The request is not valid.", errors).ToProblem());
        };
    }

    private static bool IsBody(ParameterInfo parameter)
    {
        object[] attributes = parameter.GetCustomAttributes(inherit: true);
        if (attributes.OfType<IFromBodyMetadata>().Any())
        {
            return true;
        }

        Type type = parameter.ParameterType;
        return !attributes.OfType<IFromQueryMetadata>().Any() && !attributes.OfType<IFromRouteMetadata>().Any()
            && !attributes.OfType<IFromHeaderMetadata>().Any() && !attributes.OfType<IFromServiceMetadata>().Any()
            && type.IsClass && type != typeof(string) && !typeof(HttpContext).IsAssignableFrom(type) && !type.IsAbstract;
    }

    private static Field[] FieldsOf(Type type) =>
    [
        .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(Optional<>)
                && !AllowsNull(property))
            .Select(property => new Field(
                property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                property,
                property.PropertyType.GetProperty(nameof(Optional<int>.SentNull), BindingFlags.NonPublic | BindingFlags.Instance)!)),
    ];

    // Optional<int?> and Optional<string?> may be cleared; Optional<int> and Optional<string> may not.
    private static bool AllowsNull(PropertyInfo property)
    {
        Type value = property.PropertyType.GetGenericArguments()[0];
        if (value.IsValueType)
        {
            return Nullable.GetUnderlyingType(value) is not null;
        }

        return Nullability.Create(property).GenericTypeArguments[0].ReadState != NullabilityState.NotNull;
    }

    private sealed record Field(string Name, PropertyInfo Property, PropertyInfo SentNullProperty)
    {
        public bool SentNull(object body) => SentNullProperty.GetValue(Property.GetValue(body)) is true;
    }
}
