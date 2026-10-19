using System.CommandLine;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Extensions.Options;

using Orvano.Host.Configuration;
using Orvano.Host.Database;
using Orvano.Host.Startup;
using Orvano.Host.Telemetry;
using Orvano.Host.Web;
using Orvano.Infrastructure;
using Orvano.Infrastructure.RootKeys;
using Orvano.Infrastructure.Tls;
using Orvano.Modules.Auth;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Configuration;

namespace Orvano.Host.Commands;

/// <summary><c>orvano serve</c>: runs the roles in <c>ORVANO_ROLES</c> (spec 0007 RUN-2).</summary>
internal static class ServeCommand
{
    public static Command Create()
    {
        var command = new Command("serve", "Run the server (the default when no command is given).")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        command.SetAction((parseResult, cancellationToken) => RunAsync([.. parseResult.UnmatchedTokens], cancellationToken));
        return command;
    }

    public static async Task<int> RunAsync(string[] configArgs, CancellationToken cancellationToken)
    {
        WebApplication app;
        try
        {
            app = Build(configArgs);
        }
        catch (ConfigurationException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }

        await using (app)
        {
            try
            {
                await app.RunAsync(cancellationToken);
                return 0;
            }
            catch (OptionsValidationException ex)
            {
                await Console.Error.WriteLineAsync(OptionsErrors.Describe(ex));
            }
            catch (StartupFailedException ex)
            {
                app.Logger.StartupFailed(ex.Message);
                await Console.Error.WriteLineAsync(ex.Message);
            }

            return 1;
        }
    }

    private static WebApplication Build(string[] configArgs)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = configArgs });
        builder.Configuration.AddOrvanoConfiguration(configArgs);

        var roles = ServerRoles.Parse(builder.Configuration[ServerRoles.ConfigurationKey]);
        RootKey.EnsureConfigured(builder.Configuration);
        ConfigureTls(builder);

        builder.AddOrvanoLogging();
        builder.AddOrvanoTelemetry();
        builder.AddPlatformDatabase();
        builder.Services.AddInfrastructure();
        builder.Services.AddSingleton(roles);
        builder.Services.AddHostedService<StartupChecks>();
        builder.Services.AddValidation(); // spec 0002 VAL-1: once here, plus once in each module that maps endpoints
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddAccessContext(); // spec 0012 PRJ-3: the per request caller and project
        builder.Services.AddHealthChecks().AddCheck<PlatformDatabaseHealthCheck>("platform-database", tags: ["ready"]);

        string? trustedProxies = builder.Configuration[TrustedProxies.ConfigurationKey];
        bool trustsProxies = TrustedProxies.Configure(new ForwardedHeadersOptions(), trustedProxies); // fails on a bad entry
        builder.Services.Configure<ForwardedHeadersOptions>(options => TrustedProxies.Configure(options, trustedProxies));

        // Spec 0002 LAY-5: one call per module.
        builder.Services.AddAuthModule(builder.Configuration);

        WebApplication app = builder.Build();
        app.Logger.ServerStarting(HostTelemetry.Version, roles);

        if (trustsProxies)
        {
            app.UseForwardedHeaders();
        }

        app.UseExceptionHandler();
        app.UseSecurityHeaders();
        if (roles.Api)
        {
            app.UseConsoleFiles();
        }

        app.UseRouting();

        // Spec 0012 EPT-1: the access layer resolves the caller (resolvers registered with AddCallerResolver) and
        // enforces each endpoint's declaration before binding and validation.
        app.UseAccessRequirements();

        // Health checks answer in every role, so container health checks work for workers too (spec 0007 IMG-4).
        // Public (spec 0012 EPT-5): probes carry no credential, and the answer is the same for every caller.
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowPublicAccess();
        app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowPublicAccess();

        if (roles.Api)
        {
            app.MapConsole();
        }

        return app;
    }

    /// <summary>Spec 0007 TLS-2: with both PEM files set, Kestrel also serves HTTPS on 8443.</summary>
    private static void ConfigureTls(WebApplicationBuilder builder)
    {
        TlsOptions? tls = builder.Configuration.GetSection(TlsOptions.Section).Get<TlsOptions>();
        if (tls is not { IsEnabled: true, CertFile: { } certFile, KeyFile: { } keyFile })
        {
            return;
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509Certificate2.CreateFromPemFile(certFile, keyFile);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            throw new ConfigurationException(
                $"ORVANO_TLS__CERTFILE and ORVANO_TLS__KEYFILE could not be loaded as a PEM certificate and key: {ex.Message}", ex);
        }

        builder.WebHost.UseSetting(WebHostDefaults.HttpsPortsKey, "8443");
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate));
    }
}
