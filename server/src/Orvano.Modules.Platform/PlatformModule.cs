using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orvano.Modules.Platform.Console;
using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Setup;
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
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SetupTokens>();
        services.AddErrorCodes(typeof(PlatformErrorCodes));
        services.Configure<RateLimiterOptions>(SetupTokenEndpoint.AddPolicy); // spec 0016 STP-8; the host adds the limiter
        services.AddValidation(); // spec 0002 VAL-1
        return services;
    }

    /// <summary>
    /// Spec 0016 STP-3: the setup token issue and banner at start. The <c>api</c> role only; register it after the host's
    /// startup checks, which it runs after.
    /// </summary>
    public static IServiceCollection AddSetupTokenAtStart(this IServiceCollection services)
    {
        services.TryAddSingleton(SetupOutput.Standard);
        services.AddHostedService<SetupTokenAtStart>();
        return services;
    }

    /// <summary>Maps the module's endpoints under the host's groups (spec 0014 PTH-2). The <c>api</c> role only.</summary>
    public static ApiRoutes MapPlatformEndpoints(this ApiRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.Console.MapConsoleConfig();
        routes.Console.MapSetupTokenCheck();
        return routes;
    }
}
