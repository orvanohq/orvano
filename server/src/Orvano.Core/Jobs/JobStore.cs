using Npgsql;

namespace Orvano.Core.Jobs;

/// <summary>State changes for claimed jobs. Every update checks <c>locked_by</c>, so a worker that lost its lease cannot overwrite the new owner.</summary>
internal sealed class JobStore(NpgsqlDataSource db, string workerId)
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    public async Task<List<ClaimedJob>> ClaimAsync(string[] queues, int limit, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            """
            UPDATE orvano.jobs
            SET status = 'running', attempts = attempts + 1, lease_until = now() + @lease, locked_by = @worker
            WHERE id IN (
                SELECT id FROM orvano.jobs
                WHERE status = 'queued' AND queue = ANY(@queues) AND run_at <= now()
                ORDER BY priority, run_at
                LIMIT @limit
                FOR UPDATE SKIP LOCKED)
            RETURNING id, queue, kind, project_id, payload::text, attempts, max_attempts
            """);
        cmd.Parameters.AddWithValue("lease", Timings.JobLease);
        cmd.Parameters.AddWithValue("worker", workerId);
        cmd.Parameters.AddWithValue("queues", queues);
        cmd.Parameters.AddWithValue("limit", limit);

        var jobs = new List<ClaimedJob>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            jobs.Add(new ClaimedJob(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6)));
        }

        return jobs;
    }

    /// <summary>Returns false when this worker no longer holds the job.</summary>
    public async Task<bool> ExtendLeaseAsync(long id, CancellationToken ct) =>
        await ExecuteAsync(
            "UPDATE orvano.jobs SET lease_until = now() + @lease WHERE id = @id AND locked_by = @worker AND status = 'running'",
            id, ct, ("lease", Timings.JobLease)) == 1;

    public Task CompleteAsync(long id, CancellationToken ct) => ExecuteAsync(
        """
        UPDATE orvano.jobs SET status = 'succeeded', finished_at = now(), lease_until = NULL, locked_by = NULL
        WHERE id = @id AND locked_by = @worker AND status = 'running'
        """, id, ct);

    /// <summary>Retries with exponential backoff and jitter; after max_attempts the job is dead.</summary>
    public Task FailAsync(ClaimedJob job, Exception error, CancellationToken ct) => ExecuteAsync(
        """
        UPDATE orvano.jobs SET
            status = CASE WHEN attempts >= max_attempts THEN 'dead' ELSE 'queued' END,
            run_at = CASE WHEN attempts >= max_attempts THEN run_at ELSE now() + @delay END,
            finished_at = CASE WHEN attempts >= max_attempts THEN now() END,
            last_error = @error, lease_until = NULL, locked_by = NULL
        WHERE id = @id AND locked_by = @worker AND status = 'running'
        """, job.Id, ct,
        ("delay", Backoff(job.Attempts)),
        ("error", Describe(error)));

    /// <summary>Marks the job dead at once, whatever attempts it has left. <c>attempts</c> already counts this one.</summary>
    public Task FailPermanentlyAsync(ClaimedJob job, Exception error, CancellationToken ct) => ExecuteAsync(
        """
        UPDATE orvano.jobs SET status = 'dead', finished_at = now(), last_error = @error, lease_until = NULL, locked_by = NULL
        WHERE id = @id AND locked_by = @worker AND status = 'running'
        """, job.Id, ct,
        ("error", Describe(error)));

    /// <summary>Hands a job back on shutdown without counting the interrupted attempt.</summary>
    public Task ReleaseAsync(long id, CancellationToken ct) => ExecuteAsync(
        """
        UPDATE orvano.jobs SET status = 'queued', attempts = attempts - 1, lease_until = NULL, locked_by = NULL
        WHERE id = @id AND locked_by = @worker AND status = 'running'
        """, id, ct);

    /// <summary>Requeues jobs whose worker stopped renewing the lease. Run by the leader.</summary>
    public static async Task<int> ReapExpiredLeasesAsync(NpgsqlDataSource db, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            """
            UPDATE orvano.jobs SET
                status = CASE WHEN attempts >= max_attempts THEN 'dead' ELSE 'queued' END,
                finished_at = CASE WHEN attempts >= max_attempts THEN now() END,
                last_error = 'lease expired', lease_until = NULL, locked_by = NULL
            WHERE status = 'running' AND lease_until < now()
            """);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    internal static TimeSpan Backoff(int attempt)
    {
        var ceiling = Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(attempt, 30)));
        return TimeSpan.FromSeconds(ceiling / 2 + Random.Shared.NextDouble() * ceiling / 2);
    }

    private async Task<int> ExecuteAsync(string sql, long id, CancellationToken ct, params (string Name, object Value)[] extra)
    {
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("worker", workerId);
        foreach (var (name, value) in extra) cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Describe(Exception error) => Truncate($"{error.GetType().Name}: {error.Message}", 2000);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
