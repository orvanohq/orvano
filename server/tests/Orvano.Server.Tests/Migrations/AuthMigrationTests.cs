using Npgsql;
using Orvano.Auth.Application;
using Orvano.Auth.Contracts;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Migrations;

// Spec 0004 data model and spec 0003 task 7: the Auth tables are live, their rules hold in the database itself, and
// orvano_app can use them.
public class AuthMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Creates_the_four_auth_tables_with_dml_for_orvano_app()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        foreach (var table in new[] { "auth_users", "auth_passwords", "auth_sessions", "auth_signing_keys" })
        {
            Assert.True(await TestDatabase.ScalarAsync<bool>(database.Superuser,
                "SELECT has_table_privilege('orvano_app', @t, 'SELECT, INSERT, UPDATE, DELETE')", ("t", $"orvano.{table}")), table);
        }
    }

    [Fact]
    public async Task Allows_one_email_per_project_ignoring_case_and_the_same_email_in_another_project()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await InsertUserAsync(database, "shop", "Ada@x.com");
        await InsertUserAsync(database, "blog", "ada@x.com");
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertUserAsync(database, "shop", "ada@X.COM"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("auth_users_email_key", error.ConstraintName);
    }

    // Spec 0003 AC-16: the database itself holds the phone rules, whatever code writes the row.
    [Theory]
    [InlineData("4155550100")] // no plus
    [InlineData("+04155550100")] // country code starting with 0
    [InlineData("+1")] // too short
    [InlineData("+1415555010012345")] // more than 15 digits
    [InlineData("+1 415 555 0100")] // spaces
    public async Task Refuses_a_phone_that_is_not_E164(string phone)
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertPhoneAsync(database, "shop", phone));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task Allows_one_phone_per_project_and_the_same_phone_in_another_project()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await InsertPhoneAsync(database, "shop", "+14155550100");
        await InsertPhoneAsync(database, "blog", "+14155550100");
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertPhoneAsync(database, "shop", "+14155550100"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [Fact]
    public async Task A_new_user_is_active_with_empty_metadata_and_status_and_metadata_hold_their_rules()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var id = await InsertUserAsync(database, "shop", "ada@x.com");

        var defaults = await TestDatabase.ScalarAsync<string>(database.App,
            "SELECT status || ' ' || metadata::text FROM orvano.auth_users WHERE id = @id", ("id", id));
        var badStatus = await Assert.ThrowsAsync<PostgresException>(() => TestDatabase.ExecuteAsync(database.App,
            "UPDATE orvano.auth_users SET status = 'deleted' WHERE id = @id", ("id", id)));
        var arrayMetadata = await Assert.ThrowsAsync<PostgresException>(() => TestDatabase.ExecuteAsync(database.App,
            "UPDATE orvano.auth_users SET metadata = '[1]' WHERE id = @id", ("id", id)));

        Assert.Equal("active {}", defaults);
        Assert.Equal(PostgresErrorCodes.CheckViolation, badStatus.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, arrayMetadata.SqlState);
    }

    [Fact]
    public async Task Keeps_at_most_one_active_signing_key_per_project()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        const string insert = """
            INSERT INTO orvano.auth_signing_keys (id, project_id, alg, public_jwk, private_key_ciphertext, status, retire_after)
            VALUES (@id, 'shop', 'ES256', '{}', '\x00', @status, @retire)
            ON CONFLICT (project_id) WHERE status = 'active' DO NOTHING
            """;

        var first = await TestDatabase.ExecuteAsync(database.App, insert, ("id", new string('a', 22)), ("status", "active"), ("retire", DBNull.Value));
        var second = await TestDatabase.ExecuteAsync(database.App, insert, ("id", new string('b', 22)), ("status", "active"), ("retire", DBNull.Value));
        var retiring = await TestDatabase.ExecuteAsync(database.App, insert, ("id", new string('c', 22)), ("status", "retiring"), ("retire", DateTimeOffset.UtcNow));

        Assert.Equal((1, 0, 1), (first, second, retiring));
    }

    [Fact]
    public async Task Sets_an_end_time_and_reason_together_and_cascades_a_user_delete()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var userId = await InsertUserAsync(database, "shop", "ada@x.com");
        const string session = """
            INSERT INTO orvano.auth_sessions (project_id, user_id, refresh_hash, refresh_ciphertext, idle_expires_at, expires_at, ended_at, end_reason)
            VALUES ('shop', @user, @hash, '\x00', now() + interval '30 days', now() + interval '365 days', @ended, @reason)
            """;
        var hash = new byte[32];

        await TestDatabase.ExecuteAsync(database.App, session, ("user", userId), ("hash", hash), ("ended", DBNull.Value), ("reason", DBNull.Value));
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            TestDatabase.ExecuteAsync(database.App, session, ("user", userId), ("hash", hash), ("ended", DateTimeOffset.UtcNow), ("reason", DBNull.Value)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);

        await TestDatabase.ExecuteAsync(database.App, "INSERT INTO orvano.auth_passwords (user_id, project_id, hash) VALUES (@user, 'shop', '$argon2id$x')", ("user", userId));
        await TestDatabase.ExecuteAsync(database.App, "DELETE FROM orvano.auth_users WHERE id = @user", ("user", userId));

        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(database.App, "SELECT count(*) FROM orvano.auth_sessions"));
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(database.App, "SELECT count(*) FROM orvano.auth_passwords"));
    }

    [Fact]
    public async Task The_user_directory_finds_users_by_id_within_one_project_only()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var ada = await InsertUserAsync(database, "console", "ada@x.com");
        var bob = await InsertUserAsync(database, "shop", "bob@x.com");
        await TestDatabase.ExecuteAsync(database.App, "UPDATE orvano.auth_users SET status = 'blocked', name = 'Ada' WHERE id = @id", ("id", ada));
        var directory = new UserDirectory(new AuthStore(database.App));

        var found = await directory.GetManyAsync("console", [ada, bob, Guid.NewGuid()], TestContext.Current.CancellationToken);

        var user = Assert.Single(found);
        Assert.Equal(new UserSummary(ada, "ada@x.com", "Ada", AuthUserStatus.Blocked), user);
    }

    private static async Task<Guid> InsertUserAsync(TestDatabase database, string projectId, string email) =>
        await TestDatabase.ScalarAsync<Guid>(database.App,
            "INSERT INTO orvano.auth_users (project_id, email) VALUES (@p, @e) RETURNING id", ("p", projectId), ("e", email));

    private static Task InsertPhoneAsync(TestDatabase database, string projectId, string phone) =>
        TestDatabase.ExecuteAsync(database.App,
            "INSERT INTO orvano.auth_users (project_id, phone) VALUES (@p, @phone)", ("p", projectId), ("phone", phone));
}
