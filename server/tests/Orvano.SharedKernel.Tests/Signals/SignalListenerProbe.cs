using Npgsql;

using Orvano.SharedKernel.Signals;

namespace Orvano.SharedKernel.Tests.Signals;

/// <summary>A raw <c>LISTEN orvano_signal</c> connection that collects every payload it hears.</summary>
internal sealed class SignalListenerProbe : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly List<string> _payloads = [];

    private SignalListenerProbe(NpgsqlConnection connection)
    {
        _connection = connection;
        _connection.Notification += (_, e) => _payloads.Add(e.Payload);
    }

    public static async Task<SignalListenerProbe> StartAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (NpgsqlCommand command = new($"LISTEN {SignalPayload.Channel}", connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new SignalListenerProbe(connection);
    }

    /// <summary>Reads until no notification arrives for <paramref name="quietMilliseconds"/>, then returns and forgets what it heard.</summary>
    public async Task<List<string>> TakeAsync(CancellationToken cancellationToken, int quietMilliseconds = 300)
    {
        while (await _connection.WaitAsync(TimeSpan.FromMilliseconds(quietMilliseconds), cancellationToken))
        {
        }

        List<string> heard = [.. _payloads];
        _payloads.Clear();
        return heard;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
