using Npgsql;

using Testcontainers.PostgreSql;

using Xunit;

namespace Orvano.Testing;

/// <summary>
/// One PostgreSQL per test run (spec 0002 TST-5), pinned to the same tag as the compose files (spec 0007 PG-1).
/// Per class databases cloned from a migrated template, and Respawn, arrive with the first module schema.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18.6";
    public const string Username = "orvano";
    public const string Password = "orvano-test-only";
    public const string Database = "orvano";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithUsername(Username)
        .WithPassword(Password)
        .WithDatabase(Database)
        .WithCommand("-c", "max_connections=300")
        .Build();

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort);

    /// <summary>The platform database settings the server reads (spec 0002 CFG-5), as configuration keys.</summary>
    public IReadOnlyDictionary<string, string> DatabaseSettings => new Dictionary<string, string>
    {
        ["Database:Host"] = Host,
        ["Database:Port"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Database:Name"] = Database,
        ["Database:Username"] = Username,
        ["Database:Password"] = Password,
        ["Database:SslMode"] = "Disable",
    };

    /// <summary>The same settings as <c>ORVANO_</c> environment variables, for running the CLI.</summary>
    public IReadOnlyDictionary<string, string> DatabaseEnvironment => DatabaseSettings.ToDictionary(
        pair => "ORVANO_" + pair.Key.Replace(":", "__", StringComparison.Ordinal).ToUpperInvariant(),
        pair => pair.Value);

    /// <summary>A connection string for <paramref name="database"/> on this server, five connections at most (TST-5).</summary>
    public string ConnectionString(string database) => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = database,
        Username = Username,
        Password = Password,
        SslMode = SslMode.Disable,
        MaxPoolSize = 5,
    }.ConnectionString;

    /// <summary>Creates an empty database with a unique name and returns that name, for a test class of its own.</summary>
    public async Task<string> CreateDatabaseAsync(string prefix, CancellationToken cancellationToken)
    {
        string name = $"{prefix}_{Guid.CreateVersion7():N}";
        await using var server = NpgsqlDataSource.Create(ConnectionString(Database));
        await using NpgsqlCommand command = server.CreateCommand($"CREATE DATABASE \"{name}\"");
        await command.ExecuteNonQueryAsync(cancellationToken);
        return name;
    }

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
