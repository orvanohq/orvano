using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orvano.Core.Migrations;
using Orvano.Server.Hosting;

namespace Orvano.Server.Tests.Infrastructure;

/// <summary>One throwaway database per test, with a data source for each of the three Postgres users.</summary>
public sealed class TestDatabase(PostgresFixture fixture, string name) : IAsyncDisposable
{
    private readonly List<NpgsqlDataSource> _sources = [];
    private NpgsqlDataSource? _admin;
    private NpgsqlDataSource? _app;
    private NpgsqlDataSource? _superuser;

    public string Name => name;

    public string AdminUrl => fixture.Url(name, "orvano_admin", PostgresFixture.AdminPassword);

    public string AppUrl => fixture.Url(name, "orvano_app", PostgresFixture.AppPassword);

    public string SuperuserUrl => fixture.Url(name, "postgres", "postgres");

    /// <summary>As orvano_admin, the role migrate runs as.</summary>
    public NpgsqlDataSource Admin => _admin ??= Track(NpgsqlDataSource.Create(AdminUrl));

    /// <summary>As orvano_app, the role api, worker, and realtime run as.</summary>
    public NpgsqlDataSource App => _app ??= Track(NpgsqlDataSource.Create(AppUrl));

    /// <summary>As the Postgres superuser, for setup and for inspecting state the app cannot see.</summary>
    public NpgsqlDataSource Superuser => _superuser ??= Track(NpgsqlDataSource.Create(SuperuserUrl));

    public NpgsqlDataSource Track(NpgsqlDataSource source)
    {
        _sources.Add(source);
        return source;
    }

    public Task<int> MigrateAsync() =>
        new MigrationRunner(Admin, NullLogger<MigrationRunner>.Instance)
            .RunAsync(PlatformSchema.Migrations, TestContext.Current.CancellationToken);

    public static async Task<T> ScalarAsync<T>(NpgsqlDataSource db, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = db.CreateCommand(sql);
        foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value);
        var result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result is null or DBNull ? default! : (T)result;
    }

    public static async Task<int> ExecuteAsync(NpgsqlDataSource db, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = db.CreateCommand(sql);
        foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value);
        return await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _sources) await source.DisposeAsync();
        await using var drop = fixture.Superuser.CreateCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
        await drop.ExecuteNonQueryAsync();
    }
}
