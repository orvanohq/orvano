using DotNet.Testcontainers.Configurations;
using Npgsql;
using Orvano.Server.Tests.Infrastructure;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Orvano.Server.Tests.Infrastructure;

/// <summary>
/// One Postgres 18 container for the whole run, bootstrapped by the real deploy/postgres/initdb
/// script, so orvano_admin and orvano_app exist exactly as they do in production. Tests never share
/// a database: each calls <see cref="NewDatabaseAsync"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string AdminPassword = "test-admin";
    public const string AppPassword = "test-app";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6")
        .WithEnvironment("ORVANO_ADMIN_PASSWORD", AdminPassword)
        .WithEnvironment("ORVANO_APP_PASSWORD", AppPassword)
        .WithBindMount(RepoPaths.Combine("deploy", "postgres", "initdb"), "/docker-entrypoint-initdb.d", AccessMode.ReadOnly)
        .WithCommand("-c", "max_connections=300")
        .Build();

    private NpgsqlDataSource? _superuser;

    public NpgsqlDataSource Superuser => _superuser ?? throw new InvalidOperationException("The container is not started.");

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _superuser = NpgsqlDataSource.Create(Url("postgres", "postgres", PostgreSqlBuilder.DefaultPassword));
    }

    public async ValueTask DisposeAsync()
    {
        if (_superuser is not null) await _superuser.DisposeAsync();
        await _container.DisposeAsync();
    }

    public string Url(string database, string username, string password) =>
        $"Host={Host};Port={Port};Database={database};Username={username};Password={password};Gss Encryption Mode=Disable";

    /// <summary>
    /// An empty database set up the way the initdb script sets up <c>orvano</c>: owned by orvano_admin,
    /// closed to PUBLIC, and open to orvano_app for CONNECT and TEMPORARY only.
    /// </summary>
    public async Task<TestDatabase> NewDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N")[..16];
        await using (var cmd = Superuser.CreateCommand($"CREATE DATABASE {name} OWNER orvano_admin"))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = Superuser.CreateCommand(
            $"REVOKE ALL ON DATABASE {name} FROM PUBLIC; GRANT CONNECT, TEMPORARY ON DATABASE {name} TO orvano_app"))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        return new TestDatabase(this, name);
    }
}
