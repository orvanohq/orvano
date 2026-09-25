using Npgsql;
using Orvano.Core;
using Orvano.Core.Data;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Data;

public class OrvanoDbTests(PostgresFixture postgres)
{
    private const string Url = "Host=db;Username=orvano_app;Password=secret;Database=orvano";

    [Fact]
    public void Caps_the_pool_at_the_role_budget_even_when_the_url_asks_for_more()
    {
        using var db = OrvanoDb.Create(Url + ";Maximum Pool Size=500", ConnectionBudget.ApiApp, "orvano-api");

        Assert.Equal(40, Settings(db).MaxPoolSize);
    }

    [Fact]
    public void Names_connections_after_the_service_so_pg_stat_activity_shows_the_role()
    {
        using var db = OrvanoDb.Create(Url, 5, "orvano-worker");

        Assert.Equal("orvano-worker", Settings(db).ApplicationName);
    }

    [Fact]
    public void Keeps_an_application_name_the_url_already_sets()
    {
        using var db = OrvanoDb.Create(Url + ";Application Name=custom", 5, "orvano-worker");

        Assert.Equal("custom", Settings(db).ApplicationName);
    }

    [Fact]
    public void Accepts_a_postgres_url()
    {
        using var db = OrvanoDb.Create("postgres://orvano_app:secret@db:6543/orvano", 5, "orvano-api");

        Assert.Equal(6543, Settings(db).Port);
        Assert.Equal(5, Settings(db).MaxPoolSize);
    }

    // Regression: Npgsql's default (Prefer) made .NET print a non JSON "cannot load libgssapi_krb5"
    // error on every role's first connection inside the chiseled image (/check verify, S-6).
    [Fact]
    public void Disables_GSS_encryption_on_pooled_data_sources()
    {
        using var db = OrvanoDb.Create(Url, 5, "orvano-api");

        Assert.Equal(GssEncryptionMode.Disable, Settings(db).GssEncryptionMode);
    }

    [Fact]
    public void Disables_GSS_encryption_even_when_the_url_asks_for_it()
    {
        using var db = OrvanoDb.Create(Url + ";Gss Encryption Mode=Prefer", 5, "orvano-api");

        Assert.Equal(GssEncryptionMode.Disable, Settings(db).GssEncryptionMode);
    }

    [Fact]
    public void Makes_dedicated_connections_unpooled_with_keepalive_and_no_GSS()
    {
        using var db = OrvanoDb.CreateDedicated(Url, "orvano-worker");
        var settings = Settings(db);

        Assert.False(settings.Pooling);
        Assert.Equal(30, settings.KeepAlive);
        Assert.Equal(GssEncryptionMode.Disable, settings.GssEncryptionMode);
        Assert.Equal("orvano-worker", settings.ApplicationName);
    }

    [Fact]
    public async Task Refuses_a_connection_beyond_the_budget_against_a_real_database()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await using var db = OrvanoDb.Create(database.AppUrl + ";Maximum Pool Size=500;Timeout=1", 2, "orvano-test");
        await using var first = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var second = await db.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<NpgsqlException>(() => db.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("pool has been exhausted", error.Message);
    }

    private static NpgsqlConnectionStringBuilder Settings(NpgsqlDataSource db) => new(db.ConnectionString);
}
