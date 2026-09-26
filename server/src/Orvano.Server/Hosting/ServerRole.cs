using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Orvano.Core;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Notifications;
using Orvano.Core.Scheduling;
using Orvano.Server.Modules;

namespace Orvano.Server.Hosting;

/// <summary>The long running roles: api, worker, and realtime. Each serves /internal health on HTTP.</summary>
internal static class ServerRole
{
    public static async Task<int> RunAsync(OrvanoRole role, string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var config = builder.Configuration;
        var serviceName = config["OTEL_SERVICE_NAME"] ?? $"orvano-{role.Name()}";
        builder.AddOrvanoTelemetry(serviceName);

        builder.Services.AddOrvanoProblems();
        var modules = OrvanoModules.For(builder.Environment);
        var fixtures = TestFixtures.Load(builder.Environment, config);

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

        foreach (var module in modules) module.ConfigureServices(builder.Services, config);

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
        var appDb = app.Services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App);
        if (!await StartupChecks.SchemaMatchesAsync(appDb, logger, app.Lifetime.ApplicationStopping)) return 1;

        app.UseRequestIds();
        // Outside the error handlers, so it checks the problem bodies they write too.
        if (app.Environment.IsEnvironment(OrvanoEnvironments.Test)) app.UseContractValidation();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseConsoleSessions(fixtures);

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
        // Admin connections are for provisioning jobs only.
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
