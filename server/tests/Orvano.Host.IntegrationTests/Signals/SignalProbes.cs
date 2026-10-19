using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Signals;

/// <summary>A platform context with no tables of its own, for writes made with SQL inside <c>InTransactionAsync</c>.</summary>
internal sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    public static ProbeContext On(IServiceProvider host) =>
        new(new DbContextOptionsBuilder<ProbeContext>().UseOrvanoConventions("probe", host).Options);
}

/// <summary>Records each connection event, in order.</summary>
internal sealed class RecordingObserver : ISignalConnectionObserver
{
    public ConcurrentQueue<string> Events { get; } = new();

    public int Count(string name) => Events.Count(e => e == name);

    public ValueTask OnConnectedAsync(CancellationToken cancellationToken) => Record("connected");

    public ValueTask OnDisconnectedAsync(CancellationToken cancellationToken) => Record("disconnected");

    public ValueTask OnFallbackPollAsync(CancellationToken cancellationToken) => Record("poll");

    private ValueTask Record(string name)
    {
        Events.Enqueue(name);
        return ValueTask.CompletedTask;
    }
}

/// <summary>A heartbeat sender that sends nothing, so the listener's timeout fires (spec 0013 LSN-3).</summary>
internal sealed class SilentHeartbeat : IHeartbeatSender
{
    public Task SendAsync(NpgsqlConnection connection, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Raw access to a test database: the platform owner's connection, for NOTIFY and backend control.</summary>
internal sealed class TestDatabase(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    public NpgsqlDataSource DataSource => dataSource;

    public static TestDatabase For(PostgresFixture postgres, IReadOnlyDictionary<string, string> settings) =>
        new(NpgsqlDataSource.Create(postgres.ConnectionString(settings["Database:Name"])));

    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params object[] values)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        foreach (object value in values)
        {
            command.Parameters.AddWithValue(value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task NotifyAsync(string payload, CancellationToken cancellationToken) =>
        ExecuteAsync($"SELECT pg_notify('{SignalPayload.Channel}', $1)", cancellationToken, payload);

    /// <summary>Ends the backends whose application name is <paramref name="applicationName"/>, waiting until they are gone.</summary>
    public async Task<int> TerminateAsync(string applicationName, CancellationToken cancellationToken)
    {
        // In the select list, so it runs only for the rows the WHERE keeps (a WHERE clause has no evaluation order).
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE application_name = $1");
        command.Parameters.AddWithValue(applicationName);
        int terminated = 0;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            terminated += reader.GetBoolean(0) ? 1 : 0;
        }

        return terminated;
    }

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
