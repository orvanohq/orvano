using System.Text.Json;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.Web;

/// <summary>
/// Spec 0002 ERR-4 and spec 0014 PRB-4: anything unhandled becomes a 500 <c>internal</c> problem with a generic message
/// and the trace ID. A caller that left gets no body; any other cancellation is a server side <c>503 timeout</c>. A
/// request that could not be bound (malformed JSON, a wrong body type, an oversized body, a wrong content type) is
/// the caller's mistake: <c>400 invalid_request</c>, <c>413</c>, or <c>415</c>, and an unknown body property is
/// <c>400 validation_failed</c> with <c>unknown_field</c> (JSW-7).
/// </summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.RequestAborted();
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        if (exception is BadHttpRequestException badRequest)
        {
            logger.BadRequest(badRequest.StatusCode);
            if (UnknownField(badRequest) is { } field)
            {
                var unknown = Error.Validation(
                    ErrorCodes.ValidationFailed,
                    "The request body has a field this operation does not accept.",
                    [new FieldError(field, FieldLocation.Body, FieldCodes.UnknownField, ValidationMessages.For(FieldCodes.UnknownField, field))]);
                await unknown.ToProblem().ExecuteAsync(httpContext);
                return true;
            }

            return await WriteAsync(httpContext, badRequest.StatusCode, ErrorCodes.ForStatus(badRequest.StatusCode), detail: null);
        }

        bool timeout = exception is OperationCanceledException;
        if (timeout)
        {
            logger.ServerTimeout(exception);
            return await WriteAsync(httpContext, StatusCodes.Status503ServiceUnavailable, ErrorCodes.Timeout, "The server took too long to answer.");
        }

        logger.UnhandledException(exception);
        return await WriteAsync(httpContext, StatusCodes.Status500InternalServerError, ErrorCodes.Internal, "Something went wrong on the server.");
    }

    private async ValueTask<bool> WriteAsync(HttpContext httpContext, int status, string code, string? detail)
    {
        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Detail = detail, Extensions = { ["code"] = code } },
        });
    }

    // System.Text.Json reports an unmapped member (UnmappedMemberHandling.Disallow) only through its message, with the
    // path to the member, such as "$.address.zip". The wire name is that path without the root.
    internal static string? UnknownField(Exception exception)
    {
        for (Exception? inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is JsonException { Path: { Length: > 2 } path } json
                && json.Message.StartsWith(UnmappedMessageStart, StringComparison.Ordinal)
                && json.Message.Contains(UnmappedMessagePart, StringComparison.Ordinal))
            {
                return path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$');
            }
        }

        return null;
    }

    private const string UnmappedMessageStart = "The JSON property '";

    private const string UnmappedMessagePart = "could not be mapped to any .NET member";
}
