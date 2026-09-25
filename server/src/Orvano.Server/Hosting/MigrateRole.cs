using Npgsql;
using Orvano.Core;
using Orvano.Core.Data;
using Orvano.Core.Migrations;

namespace Orvano.Server.Hosting;

/// <summary>One shot: applies platform migrations as orvano_admin, then exits 0.</summary>
internal static class MigrateRole
{
    public static async Task<int> RunAsync(string[] args)
    {
        // The generic host reads only DOTNET_ host settings. Seed ASPNETCORE_ ones first, as
        // WebApplication.CreateBuilder does, so ASPNETCORE_ENVIRONMENT picks the environment here too.
        var hostConfig = new ConfigurationManager();
        hostConfig.AddEnvironmentVariables(prefix: "ASPNETCORE_");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, Configuration = hostConfig });
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "orvano-migrate";
        builder.AddOrvanoTelemetry(serviceName);

        var adminUrl = OrvanoConfig.Required(builder.Configuration, "ORVANO_DB_ADMIN_URL");
        builder.Services.AddKeyedSingleton(OrvanoDb.Admin, (_, _) => OrvanoDb.Create(adminUrl, ConnectionBudget.MigrateAdmin, serviceName));
        builder.Services.AddSingleton(sp => new MigrationRunner(
            sp.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.Admin), sp.GetRequiredService<ILogger<MigrationRunner>>()));

        using var host = builder.Build();
        await host.StartAsync(); // starts telemetry and Ctrl+C handling; there are no hosted services
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Orvano.Migrate");
        var ct = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

        try
        {
            var db = host.Services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.Admin);
            if (await StartupChecks.WaitForDatabaseAsync(db, logger, ct, _ => Task.FromResult(true)) is null) return 1;

            await host.Services.GetRequiredService<MigrationRunner>().RunAsync(PlatformSchema.Migrations, ct);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Platform migration failed");
            return 1;
        }
        finally
        {
            await host.StopAsync(); // flushes telemetry
        }
    }
}
