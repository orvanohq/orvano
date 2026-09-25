using Npgsql;
using Orvano.Core;
using Orvano.Core.Data;
using Orvano.Core.Migrations;

namespace Orvano.Server.Hosting;

/// <summary>One shot: applies platform migrations as orvano_admin, then exits 0.</summary>
public static class MigrateRole
{
    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
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
