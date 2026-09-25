using System.Net;
using System.Text.Json;
using Orvano.Server.Hosting;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Hosting;

// Runs the real orvano binary, as compose and Aspire do: spec 0002 roles, S-1 (health), S-3 (readyz
// on every role), S-6 (orvano healthcheck, migrate is safe to run again), and value sourcing.
public class OrvanoBinaryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Exits_1_with_a_clear_message_when_no_role_is_given()
    {
        await using var orvano = await OrvanoProcess.RunAsync([]);

        Assert.Equal(1, orvano.ExitCode);
        Assert.Contains("No role given. Set ORVANO_ROLE or pass one of: api, worker, realtime, migrate, executor.", orvano.Output);
    }

    [Fact]
    public async Task Exits_1_when_ORVANO_ROLE_and_the_argument_disagree()
    {
        await using var orvano = await OrvanoProcess.RunAsync(["worker"], Env(("ORVANO_ROLE", "api")));

        Assert.Equal(1, orvano.ExitCode);
        Assert.Contains("ORVANO_ROLE is 'api' but the argument says 'worker'.", orvano.Output);
    }

    [Fact]
    public async Task Exits_1_for_an_unknown_role()
    {
        await using var orvano = await OrvanoProcess.RunAsync(["nope"]);

        Assert.Equal(1, orvano.ExitCode);
        Assert.Contains("Unknown role 'nope'.", orvano.Output);
    }

    [Fact]
    public async Task Exits_1_for_the_reserved_executor_role()
    {
        await using var orvano = await OrvanoProcess.RunAsync(["executor"]);

        Assert.Equal(1, orvano.ExitCode);
        Assert.Contains("The executor role is reserved for functions", orvano.Output);
    }

    [Theory]
    [InlineData("api", "ORVANO_DB_URL")]
    [InlineData("migrate", "ORVANO_DB_ADMIN_URL")]
    public async Task Exits_1_and_names_the_missing_database_setting(string role, string setting)
    {
        await using var orvano = await OrvanoProcess.RunAsync([role]);

        Assert.Equal(1, orvano.ExitCode);
        Assert.Contains($"{setting} is not set.", orvano.Output);
    }

    [Fact]
    public async Task Exits_1_when_the_worker_has_no_admin_url()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await using var orvano = await OrvanoProcess.RunAsync(["worker"], Env(("ORVANO_DB_URL", database.AppUrl)));

        Assert.Equal(1, orvano.ExitCode);
        Assert.Contains("ORVANO_DB_ADMIN_URL is not set.", orvano.Output);
    }

    [Fact]
    public async Task Migrates_an_empty_database_and_exits_0()
    {
        await using var database = await postgres.NewDatabaseAsync();

        await using var orvano = await OrvanoProcess.RunAsync(["migrate"], Env(("ORVANO_DB_ADMIN_URL", database.AdminUrl)));

        Assert.Equal(0, orvano.ExitCode);
        Assert.Contains($"{PlatformSchema.Migrations.Count} migration(s) applied this run", orvano.Output);
    }

    [Fact]
    public async Task Applies_nothing_when_migrate_runs_again()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await using var orvano = await OrvanoProcess.RunAsync(["migrate"], Env(("ORVANO_DB_ADMIN_URL", database.AdminUrl)));

        Assert.Equal(0, orvano.ExitCode);
        Assert.Contains("0 migration(s) applied this run", orvano.Output);
    }

    [Fact]
    public async Task Accepts_a_postgres_url_for_the_database_setting()
    {
        await using var database = await postgres.NewDatabaseAsync();
        var url = $"postgres://orvano_admin:{PostgresFixture.AdminPassword}@{postgres.Host}:{postgres.Port}/{database.Name}";

        await using var orvano = await OrvanoProcess.RunAsync(["migrate"], Env(("ORVANO_DB_ADMIN_URL", url)));

        Assert.Equal(0, orvano.ExitCode);
    }

    [Fact]
    public async Task Writes_only_JSON_log_lines_outside_Development()
    {
        await using var database = await postgres.NewDatabaseAsync();

        await using var orvano = await OrvanoProcess.RunAsync(["migrate"], Env(("ORVANO_DB_ADMIN_URL", database.AdminUrl)));

        Assert.Equal(0, orvano.ExitCode);
        Assert.All(orvano.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries), line => Assert.StartsWith("{", line));
    }

    [Fact]
    public async Task Migrate_honors_ASPNETCORE_ENVIRONMENT_as_the_Aspire_AppHost_sets_it()
    {
        await using var database = await postgres.NewDatabaseAsync();

        await using var orvano = await OrvanoProcess.RunAsync(["migrate"], Env(
            ("ORVANO_DB_ADMIN_URL", database.AdminUrl), ("ASPNETCORE_ENVIRONMENT", "Development")));

        Assert.Equal(0, orvano.ExitCode);
        Assert.Contains("Hosting environment: Development", orvano.Output);
        Assert.DoesNotContain(orvano.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries), line => line.StartsWith('{'));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Refuses_to_start_the_api_against_another_schema_version(int version)
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await TestDatabase.ExecuteAsync(database.Admin, "UPDATE orvano.schema_migrations SET version = @v", ("v", version));

        await using var orvano = OrvanoProcess.Start(["api"], Env(("ORVANO_DB_URL", database.AppUrl)), listen: true);
        await orvano.WaitForExitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, orvano.ExitCode);
        Assert.Contains($"Database schema version is {version} but this build expects {PlatformSchema.ExpectedVersion}", orvano.Output);
    }

    [Fact]
    public async Task Serves_health_with_the_version_from_the_VERSION_file()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await using var api = await StartRoleAsync("api", database);
        using var http = api.Http();

        using var response = await http.GetAsync("/v1/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(File.ReadAllText(RepoPaths.Combine("VERSION")).Trim(), body.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Answers_404_for_a_v1_route_that_does_not_exist()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await using var api = await StartRoleAsync("api", database);
        using var http = api.Http();

        using var response = await http.GetAsync("/v1/projects", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("worker")]
    [InlineData("realtime")]
    public async Task Reports_ready_on_every_long_running_role(string role)
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await using var orvano = await StartRoleAsync(role, database);
        using var http = orvano.Http();

        using var response = await http.GetAsync("/internal/readyz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("realtime")]
    public async Task Serves_product_routes_only_on_the_api_role(string role)
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await using var orvano = await StartRoleAsync(role, database);
        using var http = orvano.Http();

        using var response = await http.GetAsync("/v1/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reports_not_ready_when_the_schema_changes_under_a_running_role()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await using var api = await StartRoleAsync("api", database);
        using var http = api.Http();

        await TestDatabase.ExecuteAsync(database.Admin, "UPDATE orvano.schema_migrations SET version = 2");
        using var response = await http.GetAsync("/internal/readyz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Healthcheck_command_exits_0_while_the_role_is_ready()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await using var api = await StartRoleAsync("api", database);

        // Compose runs this inside a container whose ORVANO_ROLE is set, so the role must not interfere.
        await using var check = await OrvanoProcess.RunAsync(["healthcheck"],
            Env(("ASPNETCORE_HTTP_PORTS", api.Port!.Value.ToString()), ("ORVANO_ROLE", "api")));

        Assert.Equal(0, check.ExitCode);
    }

    [Fact]
    public async Task Healthcheck_command_exits_1_when_nothing_answers()
    {
        await using var check = await OrvanoProcess.RunAsync(["healthcheck"],
            Env(("ASPNETCORE_HTTP_PORTS", OrvanoProcess.FreePort().ToString())));

        Assert.Equal(1, check.ExitCode);
    }

    [Fact]
    public async Task Healthcheck_command_exits_1_when_the_role_is_not_ready()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await using var api = await StartRoleAsync("api", database);
        await TestDatabase.ExecuteAsync(database.Admin, "UPDATE orvano.schema_migrations SET version = 2");

        await using var check = await OrvanoProcess.RunAsync(["healthcheck"], Env(("ASPNETCORE_HTTP_PORTS", api.Port!.Value.ToString())));

        Assert.Equal(1, check.ExitCode);
    }

    [Fact]
    public async Task Rejects_a_retention_setting_that_is_not_a_positive_number()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await using var orvano = await OrvanoProcess.RunAsync(["worker"], Env(
            ("ORVANO_DB_URL", database.AppUrl), ("ORVANO_DB_ADMIN_URL", database.AdminUrl), ("ORVANO_EVENT_RETENTION_DAYS", "0")));

        Assert.Equal(1, orvano.ExitCode);
        Assert.Contains("ORVANO_EVENT_RETENTION_DAYS must be a positive whole number, got '0'.", orvano.Output);
    }

    private static async Task<OrvanoProcess> StartRoleAsync(string role, TestDatabase database)
    {
        var env = Env(("ORVANO_DB_URL", database.AppUrl), ("ORVANO_DB_ADMIN_URL", database.AdminUrl));
        var orvano = OrvanoProcess.Start([role], env, listen: true);
        try
        {
            await orvano.WaitUntilListeningAsync();
            return orvano;
        }
        catch
        {
            await orvano.DisposeAsync();
            throw;
        }
    }

    private static Dictionary<string, string> Env(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);
}
