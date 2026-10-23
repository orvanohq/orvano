using Microsoft.AspNetCore.Cors.Infrastructure;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.Api;

/// <summary>The services behind the wire format (spec 0014 VRS, JSW, PRB) and the host's CORS policies.</summary>
internal static class ApiServices
{
    public static IServiceCollection AddApiWireFormat(this IServiceCollection services)
    {
        services.AddScoped<ApiVersionContext>();
        services.AddScoped<IApiVersionContext>(provider => provider.GetRequiredService<ApiVersionContext>());

        // JSW-1 to JSW-3, JSW-7: camelCase, snake_case enums, UTC timestamps, unknown members rejected. Names must
        // match exactly and numbers must be numbers, so a misspelled or mistyped field is refused, not ignored.
        services.ConfigureHttpJsonOptions(options =>
        {
            ApiJson.Configure(options.SerializerOptions);
            options.SerializerOptions.PropertyNameCaseInsensitive = false;
            options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
        });

        // PRB-4: a body that does not bind throws, so the exception handler can answer with a code (and with
        // unknown_field for an unmapped member, JSW-7) instead of a bare 400.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        // PRB-1: registered before AddProblemDetails adds the default writer, so this one writes every problem.
        services.AddSingleton<IProblemDetailsWriter, ProblemWriter>();
        services.AddProblemDetails();

        // Spec 0007 COR-1: CORS headers only where an endpoint asks for a policy; HLT-3's fixed health policy for now.
        services.AddCors();
        services.AddSingleton<ICorsPolicyProvider, OrvanoCorsPolicyProvider>();
        return services;
    }
}
