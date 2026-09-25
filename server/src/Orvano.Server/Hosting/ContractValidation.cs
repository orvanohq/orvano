using Orvano.Contract;

namespace Orvano.Server.Hosting;

/// <summary>
/// Test environment only (spec 0001, AC-9): every <c>/v1</c> response is checked against the
/// contract, and a mismatch becomes a 500 <c>contract_violation</c> problem, so the scenario or
/// test that caused it fails.
/// </summary>
internal static class ContractValidation
{
    public const string ViolationCode = "contract_violation";

    public static IApplicationBuilder UseContractValidation(this IApplicationBuilder app)
    {
        ContractValidator validator;
        using (var json = ContractDocument.OpenJson()) validator = ContractValidator.Load(json);
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Orvano.ContractValidation");

        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/v1"))
            {
                await next(context);
                return;
            }

            var original = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await next(context);
            }
            finally
            {
                context.Response.Body = original;
            }

            // No endpoint matched (a 404 from routing): nothing the contract promises.
            var endpoint = context.GetEndpoint();
            var violation = endpoint is null
                ? null
                : validator.Check(
                    endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                    context.Response.StatusCode,
                    context.Response.ContentType,
                    buffer.GetBuffer().AsMemory(0, (int)buffer.Length));

            if (violation is null)
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(original, context.RequestAborted);
                return;
            }

            logger.LogError("Contract violation: {Violation}", violation);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await TypedResults.Problem(
                detail: violation,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The response does not match the API contract",
                extensions: new Dictionary<string, object?> { ["code"] = ViolationCode }).ExecuteAsync(context);
        });
    }
}
