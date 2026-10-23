using System.Buffers;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Orvano.Host.Api;

/// <summary>
/// Spec 0014 PRB-1: the one writer behind <see cref="IProblemDetailsService"/>, so every problem the server sends (an
/// <see cref="Error"/>, failed validation, a framework failure, an unhandled exception) has the same members in the same
/// order: <c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c>, <c>code</c>, <c>traceId</c>, then <c>errors</c>,
/// <c>retryAfter</c>, and the code's own members. It always writes <c>application/problem+json</c>, whatever the
/// request accepts.
/// </summary>
internal sealed class ProblemWriter(IOptions<HttpJsonOptions> jsonOptions) : IProblemDetailsWriter
{
    private static readonly Dictionary<string, string> Descriptions =
        ErrorCodes.All.ToDictionary(entry => entry.Code, entry => entry.Entry.Description, StringComparer.Ordinal);

    public bool CanWrite(ProblemDetailsContext context) => true;

    public async ValueTask WriteAsync(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        HttpContext http = context.HttpContext;
        ProblemDetails problem = context.ProblemDetails;
        JsonSerializerOptions json = jsonOptions.Value.SerializerOptions;

        int status = problem.Status ?? http.Response.StatusCode;
        object? errors = Member(problem, "errors");
        string? code = Member(problem, "code") as string;
        if (problem is HttpValidationProblemDetails { Errors.Count: > 0 } validation)
        {
            // PRB-5: failed validation from AddValidation, reshaped into field errors.
            errors = ValidationErrors.Reshape(validation.Errors, http.GetEndpoint(), json.PropertyNamingPolicy);
            code ??= ErrorCodes.ValidationFailed;
        }

        code ??= ErrorCodes.ForStatus(status);
        string detail = problem.Detail ?? Descriptions.GetValueOrDefault(code) ?? ReasonPhrases.GetReasonPhrase(status);

        var buffer = new ArrayBufferWriter<byte>(512);
        await using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", ErrorProblems.ProblemType);
            writer.WriteString("title", ReasonPhrases.GetReasonPhrase(status));
            writer.WriteNumber("status", status);
            writer.WriteString("detail", detail);
            writer.WriteString("code", code);
            writer.WriteString("traceId", ApiRequestId.For(http));
            if (errors is not null)
            {
                writer.WritePropertyName("errors");
                JsonSerializer.Serialize(writer, errors, errors.GetType(), json);
            }

            if (Member(problem, "retryAfter") is { } retryAfter)
            {
                writer.WritePropertyName("retryAfter");
                JsonSerializer.Serialize(writer, retryAfter, retryAfter.GetType(), json);
            }

            foreach ((string key, object? value) in problem.Extensions)
            {
                if (!ErrorProblems.ReservedMembers.Contains(key) && key != "requestId")
                {
                    writer.WritePropertyName(key);
                    JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), json);
                }
            }

            writer.WriteEndObject();
        }

        http.Response.StatusCode = status;
        http.Response.ContentType = "application/problem+json";
        http.Response.ContentLength = buffer.WrittenCount;
        await http.Response.Body.WriteAsync(buffer.WrittenMemory, http.RequestAborted);
    }

    private static object? Member(ProblemDetails problem, string name) =>
        problem.Extensions.TryGetValue(name, out object? value) ? value : null;
}
