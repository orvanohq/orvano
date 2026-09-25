using Npgsql;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Migrations;

// Spec 0002 S-2 and the security model: orvano_admin provisions, orvano_app only reads and writes platform rows.
public class PlatformPrivilegesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.NewDatabaseAsync();
        await _database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Makes_orvano_admin_a_login_that_can_create_roles_but_is_not_superuser()
    {
        var (login, createRole, super) = await RoleFlagsAsync("orvano_admin");

        Assert.True(login);
        Assert.True(createRole);
        Assert.False(super);
    }

    [Fact]
    public async Task Makes_orvano_app_a_plain_login_with_no_role_or_superuser_powers()
    {
        var (login, createRole, super) = await RoleFlagsAsync("orvano_app");

        Assert.True(login);
        Assert.False(createRole);
        Assert.False(super);
    }

    [Theory]
    [InlineData("events")]
    [InlineData("jobs")]
    public async Task Gives_orvano_app_select_insert_update_and_delete_on(string table)
    {
        var privileges = await TestDatabase.ScalarAsync<string[]>(_database.Superuser, """
            SELECT array_agg(privilege_type::text ORDER BY privilege_type) FROM information_schema.role_table_grants
            WHERE table_schema = 'orvano' AND table_name = @table AND grantee = 'orvano_app'
            """, ("table", table));

        Assert.Equal(["DELETE", "INSERT", "SELECT", "UPDATE"], privileges);
    }

    [Fact]
    public async Task Lets_orvano_app_write_and_delete_events()
    {
        var id = await TestDatabase.ScalarAsync<long>(_database.App,
            "INSERT INTO orvano.events (type, payload) VALUES ('t', '{}') RETURNING id");

        var deleted = await TestDatabase.ExecuteAsync(_database.App, "DELETE FROM orvano.events WHERE id = @id", ("id", id));

        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task Lets_orvano_app_read_the_schema_version()
    {
        var version = await TestDatabase.ScalarAsync<int>(_database.App, "SELECT max(version) FROM orvano.schema_migrations");

        Assert.Equal(1, version);
    }

    [Theory]
    [InlineData("INSERT INTO orvano.schema_migrations (version, name, sha256) VALUES (99, 'x', 'x')")]
    [InlineData("UPDATE orvano.schema_migrations SET sha256 = 'x'")]
    [InlineData("DELETE FROM orvano.schema_migrations")]
    public async Task Denies_orvano_app_any_write_to_the_migration_ledger(string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => TestDatabase.ExecuteAsync(_database.App, sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Theory]
    [InlineData("CREATE TABLE orvano.sneaky (id int)")]
    [InlineData("CREATE TABLE public.sneaky (id int)")]
    [InlineData("CREATE SCHEMA sneaky")]
    [InlineData("DROP TABLE orvano.events")]
    [InlineData("TRUNCATE orvano.jobs")]
    [InlineData("CREATE ROLE sneaky")]
    public async Task Denies_orvano_app_any_schema_or_role_change(string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => TestDatabase.ExecuteAsync(_database.App, sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Rejects_a_job_with_an_unknown_status()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => TestDatabase.ExecuteAsync(_database.App,
            "INSERT INTO orvano.jobs (queue, kind, payload, max_attempts, status) VALUES ('q', 'k', '{}', 1, 'paused')"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task Rejects_a_job_with_zero_max_attempts()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => TestDatabase.ExecuteAsync(_database.App,
            "INSERT INTO orvano.jobs (queue, kind, payload, max_attempts) VALUES ('q', 'k', '{}', 0)"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    private async Task<(bool Login, bool CreateRole, bool Super)> RoleFlagsAsync(string role)
    {
        await using var cmd = _database.Superuser.CreateCommand("SELECT rolcanlogin, rolcreaterole, rolsuper FROM pg_roles WHERE rolname = @r");
        cmd.Parameters.AddWithValue("r", role);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), $"role {role} is missing");
        return (reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }
}
