using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orvano.Core.Migrations;
using Orvano.Server.Hosting;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Migrations;

// Spec 0002 S-2 (the first migration creates the platform schema) and the migration lock.
public class MigrationRunnerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Applies_every_migration_to_an_empty_database()
    {
        await using var database = await postgres.NewDatabaseAsync();

        var applied = await database.MigrateAsync();

        Assert.Equal(PlatformSchema.Migrations.Count, applied);
        Assert.Equal(PlatformSchema.ExpectedVersion, await SchemaVersion.ReadAsync(database.Admin, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Creates_schema_orvano_owned_by_orvano_admin_with_the_three_platform_tables()
    {
        await using var database = await postgres.NewDatabaseAsync();

        await database.MigrateAsync();

        var owner = await TestDatabase.ScalarAsync<string>(database.Superuser,
            "SELECT pg_get_userbyid(nspowner)::text FROM pg_namespace WHERE nspname = 'orvano'");
        var tables = await TestDatabase.ScalarAsync<string[]>(database.Superuser,
            "SELECT array_agg(tablename::text ORDER BY tablename) FROM pg_tables WHERE schemaname = 'orvano'");
        Assert.Equal("orvano_admin", owner);
        Assert.Equal(["events", "jobs", "schema_migrations"], tables);
    }

    [Fact]
    public async Task Records_each_applied_migration_with_its_checksum()
    {
        await using var database = await postgres.NewDatabaseAsync();

        await database.MigrateAsync();

        var init = PlatformSchema.Migrations[0];
        var sha = await TestDatabase.ScalarAsync<string>(database.Superuser,
            "SELECT sha256 FROM orvano.schema_migrations WHERE version = @v AND name = @n", ("v", init.Version), ("n", init.Name));
        Assert.Equal(init.Sha256, sha);
    }

    [Fact]
    public async Task Applies_nothing_the_second_time_it_runs()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        var applied = await database.MigrateAsync();

        Assert.Equal(0, applied);
        Assert.Equal(PlatformSchema.Migrations.Count,
            await TestDatabase.ScalarAsync<long>(database.Superuser, "SELECT count(*) FROM orvano.schema_migrations"));
    }

    [Fact]
    public async Task Lets_only_one_of_two_concurrent_runs_apply_each_file()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await using var first = NpgsqlDataSource.Create(database.AdminUrl);
        await using var second = NpgsqlDataSource.Create(database.AdminUrl);

        var runs = await Task.WhenAll(
            new MigrationRunner(first, NullLogger<MigrationRunner>.Instance).RunAsync(PlatformSchema.Migrations, TestContext.Current.CancellationToken),
            new MigrationRunner(second, NullLogger<MigrationRunner>.Instance).RunAsync(PlatformSchema.Migrations, TestContext.Current.CancellationToken));

        Assert.Equal(PlatformSchema.Migrations.Count, runs.Sum());
        Assert.Contains(0, runs);
    }

    [Fact]
    public async Task Waits_for_a_run_that_holds_the_migration_lock()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await using var holder = await database.Admin.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_lock(1330796097, 1)", holder))
        {
            await take.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        var run = database.MigrateAsync();
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.False(run.IsCompleted);

        await using (var release = new NpgsqlCommand("SELECT pg_advisory_unlock(1330796097, 1)", holder))
        {
            await release.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(PlatformSchema.Migrations.Count, await run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Releases_the_migration_lock_when_it_finishes()
    {
        await using var database = await postgres.NewDatabaseAsync();

        await database.MigrateAsync();

        var held = await TestDatabase.ScalarAsync<long>(database.Superuser, """
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory' AND classid = 1330796097 AND objid = 1
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """);
        Assert.Equal(0, held);
    }

    [Fact]
    public async Task Refuses_to_run_when_an_applied_migration_changed_since()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await TestDatabase.ExecuteAsync(database.Admin, "UPDATE orvano.schema_migrations SET sha256 = 'edited' WHERE version = 1");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(database.MigrateAsync);

        Assert.StartsWith("Platform migration 0001_init changed after it was applied.", error.Message);
    }

    [Fact]
    public async Task Refuses_to_run_against_a_database_newer_than_this_build()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var future = PlatformSchema.ExpectedVersion + 1;
        await TestDatabase.ExecuteAsync(database.Admin,
            "INSERT INTO orvano.schema_migrations (version, name, sha256) VALUES (@v, 'future', 'x')", ("v", future));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(database.MigrateAsync);

        Assert.Contains($"platform migration {future:D4}, which this build does not know", error.Message);
    }

    [Fact]
    public async Task Leaves_no_trace_of_a_migration_that_fails_partway()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var broken = new PlatformMigration(PlatformSchema.ExpectedVersion + 1, "broken",
            "CREATE TABLE orvano.half_done (id int); SELECT * FROM orvano.no_such_table;", "sha");

        await Assert.ThrowsAsync<PostgresException>(() =>
            new MigrationRunner(database.Admin, NullLogger<MigrationRunner>.Instance)
                .RunAsync([.. PlatformSchema.Migrations, broken], TestContext.Current.CancellationToken));

        Assert.Equal(PlatformSchema.ExpectedVersion, await SchemaVersion.ReadAsync(database.Admin, TestContext.Current.CancellationToken));
        Assert.False(await TestDatabase.ScalarAsync<bool>(database.Superuser, "SELECT to_regclass('orvano.half_done') IS NOT NULL"));
    }

    [Fact]
    public async Task Reads_schema_version_0_on_an_empty_database()
    {
        await using var database = await postgres.NewDatabaseAsync();

        Assert.Equal(0, await SchemaVersion.ReadAsync(database.App, TestContext.Current.CancellationToken));
    }
}
