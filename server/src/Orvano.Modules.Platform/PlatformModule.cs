using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orvano.Modules.Platform.Console;
using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform;

/// <summary>The platform module: the install's setup state now, console accounts and projects with feature 13.</summary>
public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<PlatformDbContext>((provider, options) => options.UseOrvanoConventions(PlatformDbContext.Schema, provider));
        services.AddValidation(); // spec 0002 VAL-1
        return services;
    }

    /// <summary>Maps the module's endpoints under the host's groups (spec 0014 PTH-2). The <c>api</c> role only.</summary>
    public static ApiRoutes MapPlatformEndpoints(this ApiRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.Console.MapConsoleConfig();
        return routes;
    }
}
