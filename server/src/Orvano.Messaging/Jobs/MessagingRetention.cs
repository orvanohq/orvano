using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Messaging.Application;
using Orvano.Messaging.Domain;

namespace Orvano.Messaging.Jobs;

/// <summary>
/// The hourly cleanup on the leader worker (spec 0009, AC-17 and AC-20): it fails every <c>queued</c> email older
/// than 30 minutes with <c>email_expired</c>, so a job the lease reaper marked <c>dead</c> without running the
/// handler never leaves a row queued with sealed content, and deletes rows older than 30 days. Both go in batches,
/// so one run never holds a long lock.
/// </summary>
internal static class MessagingRetention
{
    public const string Name = "messaging.retention";
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public const int BatchSize = 1000;

    /// <summary>Runs one cleanup; <c>Expired</c> and <c>Deleted</c> are how many rows it failed and deleted.</summary>
    public static async Task<(int Expired, int Deleted)> RunAsync(NpgsqlDataSource db, CancellationToken ct)
    {
        var expired = 0;
        int batch;
        do
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var failed = new List<(Guid Id, string ProjectId)>();
            await using (var cmd = new NpgsqlCommand(
                """
                UPDATE orvano.messaging_emails
                SET status = 'failed', error_code = @code, completed_at = now(), content_ciphertext = NULL
                WHERE id IN (
                    SELECT id FROM orvano.messaging_emails
                    WHERE status = 'queued' AND created_at < now() - @stale
                    LIMIT @batch
                    FOR UPDATE SKIP LOCKED)
                RETURNING id, project_id
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("code", EmailFailures.Expired);
                cmd.Parameters.AddWithValue("stale", EmailDelivery.StaleAfter);
                cmd.Parameters.AddWithValue("batch", BatchSize);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) failed.Add((reader.GetGuid(0), reader.GetString(1)));
            }

            foreach (var (id, projectId) in failed)
                await MessagingEvents.WriteEmailAsync(tx, MessagingEvents.EmailFailed, projectId, id, EmailFailures.Expired, ct);
            await tx.CommitAsync(ct);
            batch = failed.Count;
            expired += batch;
        }
        while (batch == BatchSize);

        var deleted = 0;
        do
        {
            await using var cmd = db.CreateCommand(
                """
                DELETE FROM orvano.messaging_emails
                WHERE id IN (SELECT id FROM orvano.messaging_emails WHERE created_at < now() - @retention LIMIT @batch)
                """);
            cmd.Parameters.AddWithValue("retention", EmailDelivery.Retention);
            cmd.Parameters.AddWithValue("batch", BatchSize);
            batch = await cmd.ExecuteNonQueryAsync(ct);
            deleted += batch;
        }
        while (batch == BatchSize);

        return (expired, deleted);
    }

    /// <summary>The schedule's body: resolves the app data source and logs the counts.</summary>
    public static async Task RunScheduledAsync(IServiceProvider services, CancellationToken ct)
    {
        var (expired, deleted) = await RunAsync(services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App), ct);
        if (expired + deleted > 0)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MessagingRetention))
                .LogInformation("Failed {Expired} email(s) stuck in the queue and deleted {Deleted} old email(s)", expired, deleted);
        }
    }
}
