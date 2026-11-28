using Testcontainers.PostgreSql;

namespace Orvano.IntegrationTests;

/// <summary>A real Postgres 17, the same tag as Compose and the AppHost. One per test class that uses it.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => Container.GetConnectionString();

    public async ValueTask InitializeAsync() => await Container.StartAsync();

    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
