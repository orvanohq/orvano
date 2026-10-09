using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Orvano.Auth.Application;
using Orvano.Auth.Fixtures;
using Orvano.Core;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Http;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Notifications;
using Orvano.Core.RateLimiting;
using Orvano.Core.Scheduling;
using Orvano.Core.Secrets;
using Orvano.Messaging.Application;
using Orvano.Messaging.Fixtures;
using Orvano.Platform.Application;
using Orvano.Platform.Contracts;
using Orvano.Platform.Domain;
using Orvano.Platform.Fixtures;
using Orvano.Server.Modules;

namespace Orvano.Server.Hosting;

/// <summary>The long running roles: api, worker, and realtime. Each serves /internal health on HTTP.</summary>
internal static class ServerRole
{
    /// <summary>
    /// The largest request body any role accepts. Every route takes small JSON, so 1 MiB is plenty; a bigger body is
    /// refused with 413 before a handler reads it. A route that needs more, such as an upload, raises it for itself.
    /// </summary>
    public const long MaxRequestBodyBytes = 1024 * 1024;

    public static async Task<int> RunAsync(OrvanoRole role, string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
        var config = builder.Configuration;
        var serviceName = config["OTEL_SERVICE_NAME"] ?? $"orvano-{role.Name()}";
        builder.AddOrvanoTelemetry(serviceName);

        builder.Services.AddOrvanoProblems();
        var modules = OrvanoModules.For(builder.Environment);
        var fixtures = TestFixtures.Load(builder.Environment, config);
        var mailpit = TestMailpit.Load(builder.Environment, config);
        builder.Services.AddSingleton(mailpit);
        var oauthProvider = TestOAuthProvider.Load(builder.Environment, config);
        builder.Services.AddSingleton(oauthProvider);
        var pwnedPasswords = TestPwnedPasswords.Load(builder.Environment, config);

        var appUrl = OrvanoConfig.Required(config, "ORVANO_DB_URL");
        var appPool = role switch
        {
            OrvanoRole.Api => ConnectionBudget.ApiApp,
            OrvanoRole.Worker => ConnectionBudget.WorkerApp,
            _ => ConnectionBudget.RealtimeApp,
        };
        builder.Services.AddKeyedSingleton(OrvanoDb.App, (_, _) => OrvanoDb.Create(appUrl, appPool, serviceName));

        builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
            "database",
            sp => new SchemaReadyCheck(sp.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App)),
            HealthStatus.Unhealthy,
            ["ready"]));

        AddKernel(builder, role);
        foreach (var module in modules) module.ConfigureServices(builder.Services, config);
        if (role == OrvanoRole.Api)
        {
            foreach (var module in modules) module.ConfigureApiServices(builder.Services, config);
        }

        switch (role)
        {
            case OrvanoRole.Worker:
                AddWorker(builder, modules, appUrl, serviceName);
                break;
            case OrvanoRole.Realtime:
                AddRealtime(builder, modules, appUrl, serviceName);
                break;
        }

        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Orvano.Startup");

        if (!StartupChecks.TimeZonesAvailable(logger)) return 1;
        if (!StartupChecks.TestFixturesUsable(fixtures, logger)) return 1;
        if (!StartupChecks.TestMailpitUsable(mailpit, logger)) return 1;
        if (!StartupChecks.TestOAuthProviderUsable(oauthProvider, logger)) return 1;
        if (!StartupChecks.TestPwnedPasswordsUsable(pwnedPasswords, logger)) return 1;
        if (role == OrvanoRole.Api && !StartupChecks.PasswordHashingAvailable(app.Services, logger)) return 1;
        var appDb = app.Services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App);
        if (!await StartupChecks.SchemaMatchesAsync(appDb, logger, app.Lifetime.ApplicationStopping)) return 1;
        if (role == OrvanoRole.Api && !await StartupChecks.FirstAdminProtectedAsync(app.Environment, config, app.Services.GetRequiredService<IInstallSetupState>(), logger, app.Lifetime.ApplicationStopping)) return 1;
        if (role == OrvanoRole.Api)
        {
            foreach (var module in modules) await module.OnApiStartingAsync(app.Services, app.Lifetime.ApplicationStopping);
        }

        if (role == OrvanoRole.Api && fixtures.ConsoleUsers.Count > 0)
        {
            var stopping = app.Lifetime.ApplicationStopping;
            var authStore = app.Services.GetRequiredService<AuthStore>();
            var accounts = app.Services.GetRequiredService<AccountService>();
            var owner = await AuthFixtures.SeedConsoleUsersAsync(authStore, accounts, fixtures.ConsoleUsers, config[InstallSetupToken.Setting], logger, stopping);
            await PlatformFixtures.SeedAsync(
                app.Services.GetRequiredService<PlatformStore>(), owner!.Value, fixtures.Projects, fixtures.ApiKeys, fixtures.Platforms, logger, stopping);
            await AuthFixtures.SeedAsync(authStore, accounts, fixtures.Users, logger, stopping);
            if (fixtures.OAuthProviders is { Count: > 0 } providers)
                await AuthFixtures.SeedOAuthProvidersAsync(app.Services.GetRequiredService<ProviderSettings>(), providers, logger, stopping);
            if (fixtures.InstallSmtp is { } installSmtp)
                await MessagingFixtures.SeedInstallSmtpAsync(app.Services.GetRequiredService<MessagingStore>(), owner.Value, installSmtp, logger, stopping);
            // After the install's SMTP: required MFA needs an email server (spec 0014, AC-2).
            if (fixtures.MethodSettings is { Count: > 0 } methods)
                await AuthFixtures.SeedMethodSettingsAsync(app.Services.GetRequiredService<MethodSettingsService>(), methods, logger, stopping);
            if (fixtures.AuthPolicies is { Count: > 0 } policies)
                await AuthFixtures.SeedPoliciesAsync(app.Services.GetRequiredService<AuthPoliciesService>(), policies, logger, stopping);
        }

        if (role == OrvanoRole.Api) app.UseForwardedHeaders();
        app.UseRequestIds();
        app.UseVersionHeader();
        if (role == OrvanoRole.Api) app.UsePublicCors();
        // Outside the error handlers, so it checks the problem bodies they write too.
        if (app.Environment.IsEnvironment(OrvanoEnvironments.Test)) app.UseContractValidation();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        if (role == OrvanoRole.Api) app.UseConsoleSessions();

        app.MapHealthChecks("/internal/healthz", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/internal/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

        if (role == OrvanoRole.Api)
        {
            var v1 = app.MapGroup("/v1");
            foreach (var module in modules) module.MapApi(v1);
        }

        logger.LogInformation("Starting Orvano {Version} as {Role}", OrvanoVersion.Current, role.Name());
        await app.RunAsync();
        return 0;
    }

    /// <summary>
    /// The kernel services modules share, with their settings checked here so a role refuses to start on a bad value:
    /// envelope encryption (<c>ORVANO_MASTER_KEYS</c>, api and worker), the public URL and trusted proxies (api), the
    /// in memory cache, and the rate limits.
    /// </summary>
    internal static void AddKernel(WebApplicationBuilder builder, OrvanoRole role)
    {
        var config = builder.Configuration;
        var services = builder.Services;
        if (role is OrvanoRole.Api or OrvanoRole.Worker) services.AddSingleton(new SecretBox(MasterKeys.FromConfig(config)));

        if (role == OrvanoRole.Api)
        {
            services.AddSingleton(PublicUrl.FromConfig(config));
            var proxies = TrustedProxies.FromConfig(config);
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var network in proxies.Networks) options.KnownIPNetworks.Add(network);
                // Walk back through every trusted hop to the first address that is not a trusted proxy.
                options.ForwardLimit = null;
            });
        }

        services.AddHybridCache();
        services.AddSingleton<RateLimits>();
    }

    private static void AddWorker(WebApplicationBuilder builder, IReadOnlyList<IOrvanoModule> modules, string appUrl, string serviceName)
    {
        var config = builder.Configuration;
        var adminUrl = OrvanoConfig.Required(config, "ORVANO_DB_ADMIN_URL");
        var retentionDays = OrvanoConfig.PositiveInt(config, "ORVANO_EVENT_RETENTION_DAYS", 7);

        var work = new WorkRegistry();
        CoreWork.Register(work, retentionDays);
        foreach (var module in modules) module.RegisterWork(work);

        var services = builder.Services;
        services.AddSingleton(work);
        services.AddSingleton<WakeSignals>();
        // Admin connections are for the jobs that issue DDL only: project provisioning and purge.
        services.AddKeyedSingleton(OrvanoDb.Admin, (_, _) => OrvanoDb.Create(adminUrl, ConnectionBudget.WorkerAdmin, serviceName));
        services.AddKeyedSingleton(OrvanoDb.Dedicated, (_, _) => OrvanoDb.CreateDedicated(appUrl, serviceName));

        services.AddHostedService(sp =>
        {
            var signals = sp.GetRequiredService<WakeSignals>();
            return new PgNotificationListener(
                sp.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.Dedicated),
                new Dictionary<string, Action<string>>
                {
                    [Outbox.Channel] = _ => signals.Events.Set(),
                    [JobQueue.Channel] = _ => signals.Jobs.Set(),
                },
                onConnected: () => { signals.Events.Set(); signals.Jobs.Set(); },
                sp.GetRequiredService<ILogger<PgNotificationListener>>());
        });
        services.AddHostedService<EventDispatcher>();
        services.AddHostedService<JobLoop>();
        services.AddHostedService<LeaderScheduler>();
    }

    private static void AddRealtime(WebApplicationBuilder builder, IReadOnlyList<IOrvanoModule> modules, string appUrl, string serviceName)
    {
        var realtime = new RealtimeRegistry();
        foreach (var module in modules) module.RegisterRealtime(realtime);

        var services = builder.Services;
        services.AddSingleton(realtime);
        services.AddKeyedSingleton(OrvanoDb.Dedicated, (_, _) => OrvanoDb.CreateDedicated(appUrl, serviceName));
        services.AddSingleton<RealtimeFanout>();
        services.AddHostedService(sp => sp.GetRequiredService<RealtimeFanout>());
        services.AddHostedService(sp =>
        {
            var fanout = sp.GetRequiredService<RealtimeFanout>();
            return new PgNotificationListener(
                sp.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.Dedicated),
                new Dictionary<string, Action<string>> { [Outbox.Channel] = fanout.OnNotify },
                onConnected: () => { }, // events missed during a gap are not replayed; clients resync
                sp.GetRequiredService<ILogger<PgNotificationListener>>());
        });
    }
}
