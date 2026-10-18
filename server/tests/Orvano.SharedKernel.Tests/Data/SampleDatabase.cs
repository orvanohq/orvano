using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

using Npgsql;

using Orvano.Testing;

using Xunit;

namespace Orvano.SharedKernel.Tests.Data;

/// <summary>
/// A database of its own with the stub anchor tables the test creates (<c>platform.organization</c>,
/// <c>platform.project</c>, <c>platform.operator</c>, <c>auth.user</c>) and the sample context's generated migration applied (spec 0011 build plan step 3).
/// </summary>
public sealed class SampleDatabase(PostgresFixture postgres) : IAsyncLifetime
{
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero));

    internal SampleContext CreateContext(Action<DbContextOptionsBuilder>? before = null) =>
        SampleContext.Create(DataSource, Clock, before);

    public async Task ExecuteAsync(string sql)
    {
        await using NpgsqlCommand command = DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<Guid> AddProjectAsync()
    {
        var id = Guid.CreateVersion7();
        await using NpgsqlCommand command = DataSource.CreateCommand("INSERT INTO platform.project (id) VALUES ($1)");
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>Applies the migration EF generates for the sample model, up (from nothing) or down (to nothing).</summary>
    public async Task MigrateAsync(bool up)
    {
        await using SampleContext context = CreateContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IRelationalModel relational = model.GetRelationalModel();
        IReadOnlyList<MigrationOperation> operations = up
            ? context.GetService<IMigrationsModelDiffer>().GetDifferences(null, relational)
            : context.GetService<IMigrationsModelDiffer>().GetDifferences(relational, null);
        foreach (MigrationCommand command in context.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
        {
            await ExecuteAsync(command.CommandText);
        }
    }

    public async ValueTask InitializeAsync()
    {
        string database = await postgres.CreateDatabaseAsync("sample", TestContext.Current.CancellationToken);
        DataSource = NpgsqlDataSource.Create(postgres.ConnectionString(database));
        await ExecuteAsync("""
            CREATE SCHEMA platform;
            CREATE TABLE platform.organization (id uuid PRIMARY KEY);
            CREATE TABLE platform.project (id uuid PRIMARY KEY);
            CREATE TABLE platform.operator (id uuid PRIMARY KEY);
            CREATE SCHEMA auth;
            CREATE TABLE auth."user" (id uuid PRIMARY KEY);
            """);
        await MigrateAsync(up: true);
    }

    public async ValueTask DisposeAsync() => await DataSource.DisposeAsync();
}
