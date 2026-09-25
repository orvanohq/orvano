using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Orvano.Core.Migrations;

namespace Orvano.Server.Hosting;

/// <summary><c>/internal/readyz</c>: the database is reachable and at the schema version this binary expects.</summary>
internal sealed class SchemaReadyCheck(NpgsqlDataSource db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var version = await SchemaVersion.ReadAsync(db, ct);
            return version == PlatformSchema.ExpectedVersion
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Schema version {version}, expected {PlatformSchema.ExpectedVersion}");
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Database unreachable", ex);
        }
    }
}
