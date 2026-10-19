using Npgsql;

using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Signals;

/// <summary>Sends the listener's heartbeat on its own connection (spec 0013 LSN-3). Tests replace it with one that sends nothing.</summary>
internal interface IHeartbeatSender
{
    Task SendAsync(NpgsqlConnection connection, CancellationToken cancellationToken);
}

internal sealed class NotifyHeartbeatSender : IHeartbeatSender
{
    private static readonly string Heartbeat = SignalPayload.EncodeHeartbeat();

    public async Task SendAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_notify('" + SignalPayload.Channel + "', $1)", connection);
        command.Parameters.AddWithValue(Heartbeat);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
