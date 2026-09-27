using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Auth.Domain;
using Orvano.Core.Data;

namespace Orvano.Auth.Jobs;

/// <summary>
/// The hourly cleanup on the leader worker: session rows 30 days after they end or expire (AC-32), and retiring
/// signing keys once their 24 hour overlap has passed (AC-22). Rows go in batches, so one run never holds a long lock.
/// </summary>
internal static class AuthRetention
{
    public const string Name = "auth.retention";
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public const int BatchSize = 1000;

    /// <summary>Runs one cleanup; <c>Sessions</c> and <c>Keys</c> are how many rows it deleted.</summary>
    public static async Task<(int Sessions, int Keys)> RunAsync(NpgsqlDataSource db, CancellationToken ct)
    {
        var sessions = 0;
        int batch;
        do
        {
            // The expression matches auth_sessions_retention_idx; idle_expires_at never passes expires_at.
            await using var cmd = db.CreateCommand(
                """
                DELETE FROM orvano.auth_sessions
                WHERE id IN (
                    SELECT id FROM orvano.auth_sessions
                    WHERE least(coalesce(ended_at, 'infinity'), idle_expires_at) < now() - @retention
                    LIMIT @batch)
                """);
            cmd.Parameters.AddWithValue("retention", AuthTimings.Retention);
            cmd.Parameters.AddWithValue("batch", BatchSize);
            batch = await cmd.ExecuteNonQueryAsync(ct);
            sessions += batch;
        }
        while (batch == BatchSize);

        await using var keys = db.CreateCommand("DELETE FROM orvano.auth_signing_keys WHERE status = 'retiring' AND retire_after <= now()");
        return (sessions, await keys.ExecuteNonQueryAsync(ct));
    }

    /// <summary>The schedule's body: resolves the app data source and logs the counts.</summary>
    public static async Task RunScheduledAsync(IServiceProvider services, CancellationToken ct)
    {
        var (sessions, keys) = await RunAsync(services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App), ct);
        if (sessions + keys > 0)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthRetention))
                .LogInformation("Deleted {Sessions} old session(s) and {Keys} retired signing key(s)", sessions, keys);
        }
    }
}
