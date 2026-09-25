using Npgsql;
using NpgsqlTypes;

namespace Orvano.Core.Jobs;

public static class JobQueues
{
    public const string Default = "default";
    public const string Internal = "internal";
}

/// <summary>
/// A job to enqueue. Lower <see cref="Priority"/> runs first; ties run in <see cref="RunAt"/> order.
/// </summary>
public sealed record NewJob(
    string Kind,
    string PayloadJson = "{}",
    string Queue = JobQueues.Default,
    string? ProjectId = null,
    int Priority = 0,
    DateTimeOffset? RunAt = null,
    int MaxAttempts = NewJob.DefaultMaxAttempts)
{
    public const int DefaultMaxAttempts = 10;
}

public sealed record ClaimedJob(long Id, string Queue, string Kind, string? ProjectId, string Payload, int Attempts, int MaxAttempts);

public sealed record JobContext(ClaimedJob Job, IServiceProvider Services);

public static class JobQueue
{
    public const string Channel = "orvano_jobs";

    /// <summary>Enqueues in the caller's transaction, so a job exists only if the change that caused it commits.</summary>
    public static async Task<long> EnqueueAsync(NpgsqlTransaction tx, NewJob job, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            WITH inserted AS (
                INSERT INTO orvano.jobs (queue, kind, project_id, payload, priority, run_at, max_attempts)
                VALUES (@queue, @kind, @project_id, @payload, @priority, coalesce(@run_at, now()), @max_attempts)
                RETURNING id, queue)
            SELECT id, pg_notify('orvano_jobs', queue) FROM inserted
            """, tx.Connection, tx);
        cmd.Parameters.AddWithValue("queue", job.Queue);
        cmd.Parameters.AddWithValue("kind", job.Kind);
        cmd.Parameters.AddWithValue("project_id", (object?)job.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, job.PayloadJson);
        cmd.Parameters.AddWithValue("priority", job.Priority);
        cmd.Parameters.AddWithValue("run_at", NpgsqlDbType.TimestampTz, (object?)job.RunAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("max_attempts", job.MaxAttempts);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
