using System.Reflection;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing.Patterns;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api;

/// <summary>
/// Spec 0014 PRB-5: turns the errors <c>AddValidation()</c> reports (member path to messages) into PRB-3 field errors.
/// .NET 10 reports only the messages, not the failing attribute, so each message is the attribute's
/// <see cref="FieldCodes"/> code, or <c>code:message</c> from a <see cref="FieldValidationResult"/>.
/// </summary>
internal static class ValidationErrors
{
    public static IReadOnlyList<FieldError> Reshape(IDictionary<string, string[]> errors, Endpoint? endpoint, JsonNamingPolicy? naming)
    {
        ParameterInfo[] parameters = endpoint?.Metadata.GetMetadata<MethodInfo>()?.GetParameters() ?? [];
        RoutePattern? route = (endpoint as RouteEndpoint)?.RoutePattern;

        var result = new List<FieldError>();
        foreach ((string key, string[] messages) in errors)
        {
            (FieldLocation location, string field) = Locate(key, parameters, route, naming);
            foreach (string message in messages)
            {
                (string code, string text) = Parse(message, field);
                result.Add(new FieldError(field, location, code, text));
            }
        }

        return result;
    }

    // A message is a code (an attribute's ErrorMessage), "code:message" (FieldValidationResult), or anything else,
    // which is answered as invalid_value without echoing it.
    internal static (string Code, string Message) Parse(string message, string field)
    {
        int separator = message.IndexOf(FieldValidationResult.Separator, StringComparison.Ordinal);
        if (separator > 0 && ErrorCodes.IsWellFormed(message[..separator]) && separator < message.Length - 1)
        {
            return (message[..separator], message[(separator + 1)..]);
        }

        string code = ErrorCodes.IsWellFormed(message) ? message : FieldCodes.InvalidValue;
        return (code, ValidationMessages.For(code, field));
    }

    // A key whose first segment names a parameter bound from the query, route, or a header is that parameter;
    // anything else is a body member path, written with the JSON naming policy per segment.
    private static (FieldLocation Location, string Field) Locate(
        string key, ParameterInfo[] parameters, RoutePattern? route, JsonNamingPolicy? naming)
    {
        int end = key.IndexOfAny(['.', '[']);
        string head = end < 0 ? key : key[..end];
        ParameterInfo? parameter = parameters.FirstOrDefault(p => string.Equals(p.Name, head, StringComparison.OrdinalIgnoreCase));
        if (parameter is not null && Source(parameter, route) is { } source)
        {
            return source;
        }

        return (FieldLocation.Body, BodyPath(parameter is null ? key : key[head.Length..].TrimStart('.'), naming));
    }

    private static (FieldLocation, string)? Source(ParameterInfo parameter, RoutePattern? route)
    {
        object[] attributes = parameter.GetCustomAttributes(inherit: true);
        string name = parameter.Name!;
        if (attributes.OfType<IFromHeaderMetadata>().FirstOrDefault() is { } header)
        {
            return (FieldLocation.Header, header.Name ?? name);
        }

        if (attributes.OfType<IFromRouteMetadata>().FirstOrDefault() is { } fromRoute)
        {
            return (FieldLocation.Path, fromRoute.Name ?? name);
        }

        if (attributes.OfType<IFromQueryMetadata>().FirstOrDefault() is { } query)
        {
            return (FieldLocation.Query, query.Name ?? name);
        }

        if (attributes.OfType<IFromBodyMetadata>().Any())
        {
            return null;
        }

        if (route?.GetParameter(name) is not null)
        {
            return (FieldLocation.Path, name);
        }

        Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        bool simple = type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(decimal)
            || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(TimeSpan);
        return simple ? (FieldLocation.Query, name) : null;
    }

    private static string BodyPath(string path, JsonNamingPolicy? naming)
    {
        if (naming is null || path.Length == 0)
        {
            return path;
        }

        var result = new StringBuilder(path.Length);
        int start = 0;
        for (int i = 0; i <= path.Length; i++)
        {
            if (i == path.Length || path[i] is '.' or '[' or ']')
            {
                if (i > start)
                {
                    string segment = path[start..i];
                    result.Append(segment.All(char.IsAsciiDigit) ? segment : naming.ConvertName(segment));
                }

                if (i < path.Length)
                {
                    result.Append(path[i]);
                }

                start = i + 1;
            }
        }

        return result.ToString();
    }
}
