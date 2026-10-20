using Npgsql;

using Testcontainers.PostgreSql;

using Xunit;

namespace Orvano.Testing;

/// <summary>
/// One PostgreSQL per test run (spec 0002 TST-5), pinned to the same tag as the compose files (spec 0007 PG-1). The
/// default database is migrated with the real <c>orvano migrate</c> once at start, and a copy of it is the template for
/// <see cref="CreateMigratedDatabaseAsync"/>, so a server on either passes its pending migrations check.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18.6";
    public const string Username = "orvano";
    public const string Password = "orvano-test-only";
    public const string Database = "orvano";

    /// <summary>A copy of <see cref="Database"/> right after its migrations, never connected to, so it can be cloned.</summary>
    private const string MigratedTemplate = "orvano_migrated";

    /// <summary>The maintenance database the fixture runs <c>CREATE DATABASE</c> from, so no template has a session.</summary>
    private const string Maintenance = "postgres";

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
    public Task<string> CreateDatabaseAsync(string prefix, CancellationToken cancellationToken) =>
        CreateAsync(prefix, template: null, cancellationToken);

    /// <summary>
    /// Creates a database with every migration applied (a clone of the migrated template) and returns its name, for
    /// servers of a test of its own.
    /// </summary>
    public Task<string> CreateMigratedDatabaseAsync(string prefix, CancellationToken cancellationToken) =>
        CreateAsync(prefix, MigratedTemplate, cancellationToken);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        OrvanoCli.Result migrate = await OrvanoCli.RunAsync(["migrate"], DatabaseEnvironment, CancellationToken.None);
        if (migrate.ExitCode != 0)
        {
            throw new InvalidOperationException($"orvano migrate failed on the test database: {migrate.Error}");
        }

        await using var server = NpgsqlDataSource.Create(ConnectionString(Maintenance));
        await using NpgsqlCommand command = server.CreateCommand($"CREATE DATABASE \"{MigratedTemplate}\" TEMPLATE \"{Database}\"");
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> CreateAsync(string prefix, string? template, CancellationToken cancellationToken)
    {
        string name = $"{prefix}_{Guid.CreateVersion7():N}";
        await using var server = NpgsqlDataSource.Create(ConnectionString(Maintenance));
        await using NpgsqlCommand command = server.CreateCommand(
            template is null ? $"CREATE DATABASE \"{name}\"" : $"CREATE DATABASE \"{name}\" TEMPLATE \"{template}\"");
        await command.ExecuteNonQueryAsync(cancellationToken);
        return name;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
