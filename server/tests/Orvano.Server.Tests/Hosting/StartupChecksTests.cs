using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orvano.Server.Hosting;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Hosting;

// Spec 0002 value sourcing, expected schema version: a role refuses to run against any other version.
public class StartupChecksTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Passes_when_the_database_is_at_the_expected_version()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        Assert.True(await StartupChecks.SchemaMatchesAsync(database.App, NullLogger.Instance, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task Refuses_a_database_at_any_other_version(int offset)
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await database.SetSchemaVersionAsync(PlatformSchema.ExpectedVersion + offset);

        Assert.False(await StartupChecks.SchemaMatchesAsync(database.App, NullLogger.Instance, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Refuses_a_database_that_was_never_migrated()
    {
        await using var database = await postgres.NewDatabaseAsync();

        Assert.False(await StartupChecks.SchemaMatchesAsync(database.App, NullLogger.Instance, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Finds_named_time_zones_on_this_machine()
    {
        Assert.True(StartupChecks.TimeZonesAvailable(NullLogger.Instance));
    }

    [Fact]
    public async Task Reports_ready_when_the_schema_matches()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        var result = await new SchemaReadyCheck(database.App).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Reports_not_ready_with_both_versions_when_the_schema_differs()
    {
        await using var database = await postgres.NewDatabaseAsync();

        var result = await new SchemaReadyCheck(database.App).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal($"Schema version 0, expected {PlatformSchema.ExpectedVersion}", result.Description);
    }

    [Fact]
    public async Task Reports_not_ready_instead_of_throwing_when_the_database_is_down()
    {
        await using var unreachable = NpgsqlDataSource.Create(
            $"Host=127.0.0.1;Port={OrvanoProcess.FreePort()};Username=x;Password=x;Timeout=2");

        var result = await new SchemaReadyCheck(unreachable).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Database unreachable", result.Description);
    }
}
