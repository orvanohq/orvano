using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Orvano.Modules.Platform.Console;
using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Keys;
using Orvano.Modules.Platform.Projects;
using Orvano.Modules.Platform.Sessions;
using Orvano.Modules.Platform.Setup;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Projects;

namespace Orvano.Modules.Platform;

/// <summary>The platform module: the install's setup state, console accounts, organizations, projects, and API keys.</summary>
public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<PlatformDbContext>((provider, options) => options.UseOrvanoConventions(PlatformDbContext.Schema, provider));
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ConsoleOptions>().BindConfiguration(ConsoleOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ConsoleOptions>, ConsoleOptionsValidator>();
        services.AddOptions<InstanceOptions>().BindConfiguration(InstanceOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<InstanceOptions>, InstanceOptionsValidator>();
        services.AddOptions<KeysOptions>().BindConfiguration(KeysOptions.Section);

        services.AddScoped<SetupTokens>();
        services.AddSingleton<SetupCompletion>();
        services.AddScoped<OwnerSetup>();
        services.AddScoped<ProjectBootstrap>();

        services.AddSingleton<ConsoleSessionCookie>();
        services.AddSingleton<SignInThrottle>();
        services.AddScoped<ConsoleSessions>();
        services.AddScoped<ConsoleAudit>();
        services.AddScoped<IMembershipLookup, PlatformMembershipLookup>();
        services.AddCallerResolver<ConsoleSessionResolver>();

        services.AddScoped<ProjectCreation>();
        services.AddScoped<IProjectDirectory, ProjectDirectory>();

        services.AddSingleton<ApiKeyUsage>();
        services.AddCallerResolver<ApiKeyResolver>();

        services.AddErrorCodes(typeof(PlatformErrorCodes));
        services.Configure<RateLimiterOptions>(SetupTokenEndpoint.AddPolicy); // spec 0016 STP-8; the host adds the limiter
        services.Configure<RateLimiterOptions>(SessionEndpoints.AddPolicy); // spec 0017 LIM-2
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
        routes.Console.MapSetupOwner();
        routes.Console.MapConsoleSessions();
        routes.Console.MapOrganizations();
        routes.Console.MapProjects();
        routes.Console.MapApiKeys();
        routes.Project.MapCurrentKey();
        return routes;
    }
}
