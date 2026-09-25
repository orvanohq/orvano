using Npgsql;
using Orvano.Core.Data;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Data;

// Spec 0002, Postgres layout and isolation: each project's data lives in schema p_<id>, owned by the
// NOLOGIN role p_<id>, reached only through ProjectScope.
public class ProjectScopeTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("abc123", "p_abc123")]
    [InlineData("a", "p_a")]
    [InlineData("0", "p_0")]
    public void Prefixes_a_valid_project_id_with_p_(string projectId, string expected)
    {
        Assert.Equal(expected, ProjectScope.RoleName(projectId));
    }

    [Fact]
    public void Accepts_a_60_character_id_so_the_role_fits_Postgres_63_byte_limit()
    {
        var role = ProjectScope.RoleName(new string('a', 60));

        Assert.Equal(62, role.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bad-ID")]
    [InlineData("ABC")]
    [InlineData("abc_123")]
    [InlineData("abc 123")]
    [InlineData("abc\"; DROP ROLE orvano_app; --")]
    [InlineData("été")]
    [InlineData("abc\n")]
    public void Rejects_an_id_outside_lowercase_letters_and_digits(string projectId)
    {
        var error = Assert.Throws<ArgumentException>(() => ProjectScope.RoleName(projectId));

        Assert.Equal("projectId", error.ParamName);
    }

    [Fact]
    public void Rejects_a_61_character_id()
    {
        Assert.Throws<ArgumentException>(() => ProjectScope.RoleName(new string('a', 61)));
    }

    [Fact]
    public async Task Runs_the_work_as_the_project_role_with_the_project_schema_as_search_path()
    {
        await using var database = await postgres.NewDatabaseAsync();
        var projectId = await ProvisionProjectAsync(database);

        var (user, searchPath) = await ProjectScope.RunAsync(database.App, projectId, async (conn, tx, ct) =>
        {
            await using var cmd = new NpgsqlCommand("SELECT current_user::text, current_setting('search_path')", conn, tx);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return (reader.GetString(0), reader.GetString(1));
        }, TestContext.Current.CancellationToken);

        Assert.Equal($"p_{projectId}", user);
        Assert.Equal($"p_{projectId}", searchPath.Trim('"'));
    }

    [Fact]
    public async Task Resolves_unqualified_table_names_in_the_project_schema()
    {
        await using var database = await postgres.NewDatabaseAsync();
        var projectId = await ProvisionProjectAsync(database);

        var count = await ProjectScope.RunAsync(database.App, projectId, async (conn, tx, ct) =>
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM items", conn, tx);
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Drops_the_project_role_when_the_transaction_ends_so_a_pooled_connection_is_clean()
    {
        await using var database = await postgres.NewDatabaseAsync();
        var projectId = await ProvisionProjectAsync(database);
        await using var db = database.Track(NpgsqlDataSource.Create(database.AppUrl + ";Maximum Pool Size=1"));

        await ProjectScope.RunAsync(db, projectId, (_, _, _) => Task.FromResult(0), TestContext.Current.CancellationToken);
        var userAfter = await TestDatabase.ScalarAsync<string>(db, "SELECT current_user::text");

        Assert.Equal("orvano_app", userAfter);
    }

    [Fact]
    public async Task Fails_closed_orvano_app_cannot_read_project_data_without_the_helper()
    {
        await using var database = await postgres.NewDatabaseAsync();
        var projectId = await ProvisionProjectAsync(database);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            TestDatabase.ScalarAsync<long>(database.App, $"SELECT count(*) FROM p_{projectId}.items"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Rolls_back_the_work_when_it_throws()
    {
        await using var database = await postgres.NewDatabaseAsync();
        var projectId = await ProvisionProjectAsync(database);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProjectScope.RunAsync<int>(database.App, projectId, async (conn, tx, ct) =>
        {
            await using var cmd = new NpgsqlCommand("INSERT INTO items (name) VALUES ('lost')", conn, tx);
            await cmd.ExecuteNonQueryAsync(ct);
            throw new InvalidOperationException("boom");
        }, TestContext.Current.CancellationToken));

        var count = await TestDatabase.ScalarAsync<long>(database.Superuser, $"SELECT count(*) FROM p_{projectId}.items");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Fails_for_a_project_whose_role_does_not_exist()
    {
        await using var database = await postgres.NewDatabaseAsync();

        await Assert.ThrowsAsync<PostgresException>(() =>
            ProjectScope.RunAsync(database.App, "nosuchproject" + Guid.NewGuid().ToString("N")[..8], (_, _, _) => Task.FromResult(0),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Validates_the_id_before_opening_a_connection()
    {
        await using var unreachable = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=x;Password=x;Timeout=1");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ProjectScope.RunAsync(unreachable, "Bad-ID", (_, _, _) => Task.FromResult(0), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// What project provisioning will do (rows 3 and 7): a NOLOGIN role owning its schema, granted to
    /// orvano_app without inheritance so the app must SET ROLE to use it. Roles are cluster wide, so
    /// every test gets its own project ID.
    /// </summary>
    private static async Task<string> ProvisionProjectAsync(TestDatabase database)
    {
        var projectId = Guid.NewGuid().ToString("N")[..12];
        var role = $"p_{projectId}";
        await TestDatabase.ExecuteAsync(database.Superuser, $"""
            CREATE ROLE {role} NOLOGIN;
            GRANT {role} TO orvano_app WITH INHERIT FALSE, SET TRUE;
            CREATE SCHEMA {role} AUTHORIZATION {role};
            CREATE TABLE {role}.items (name text NOT NULL);
            ALTER TABLE {role}.items OWNER TO {role};
            INSERT INTO {role}.items (name) VALUES ('first');
            """);
        return projectId;
    }
}
