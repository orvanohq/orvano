using System.Text.RegularExpressions;
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

public static partial class JobQueue
{
    public const string Channel = "orvano_jobs";

    private const string InsertSql =
        """
        WITH inserted AS (
            INSERT INTO orvano.jobs (queue, kind, project_id, payload, priority, run_at, max_attempts)
            VALUES (@queue, @kind, @project_id, @payload, @priority, coalesce(@run_at, now()), @max_attempts)
            RETURNING id, queue)
        SELECT id, pg_notify('orvano_jobs', queue) FROM inserted
        """;

    /// <summary>Enqueues in the caller's transaction, so a job exists only if the change that caused it commits.</summary>
    public static async Task<long> EnqueueAsync(NpgsqlTransaction tx, NewJob job, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(InsertSql, tx.Connection, tx);
        AddParameters(cmd.Parameters, job);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Enqueues several jobs in the caller's transaction in one round trip. With a <paramref name="savepoint"/>,
    /// the inserts are wrapped in <c>SAVEPOINT</c> and <c>RELEASE SAVEPOINT</c> in the same batch, so after a
    /// <see cref="PostgresException"/> the caller can roll back to it and keep using the transaction.
    /// </summary>
    public static async Task EnqueueManyAsync(NpgsqlTransaction tx, IReadOnlyList<NewJob> jobs, string? savepoint, CancellationToken ct)
    {
        if (jobs.Count == 0) return;
        if (savepoint is not null && !SavepointName().IsMatch(savepoint))
            throw new ArgumentException($"'{savepoint}' is not a plain savepoint name.", nameof(savepoint));

        await using var batch = new NpgsqlBatch(tx.Connection, tx);
        if (savepoint is not null) batch.BatchCommands.Add(new NpgsqlBatchCommand($"SAVEPOINT {savepoint}"));
        foreach (var job in jobs)
        {
            var insert = new NpgsqlBatchCommand(InsertSql);
            AddParameters(insert.Parameters, job);
            batch.BatchCommands.Add(insert);
        }
        if (savepoint is not null) batch.BatchCommands.Add(new NpgsqlBatchCommand($"RELEASE SAVEPOINT {savepoint}"));

        await batch.ExecuteNonQueryAsync(ct);
    }

    private static void AddParameters(NpgsqlParameterCollection p, NewJob job)
    {
        p.AddWithValue("queue", job.Queue);
        p.AddWithValue("kind", job.Kind);
        p.AddWithValue("project_id", NpgsqlDbType.Text, (object?)job.ProjectId ?? DBNull.Value);
        p.AddWithValue("payload", NpgsqlDbType.Jsonb, job.PayloadJson);
        p.AddWithValue("priority", job.Priority);
        // Npgsql only writes UTC offsets to timestamptz; the instant is the same.
        p.AddWithValue("run_at", NpgsqlDbType.TimestampTz, (object?)job.RunAt?.ToUniversalTime() ?? DBNull.Value);
        p.AddWithValue("max_attempts", job.MaxAttempts);
    }

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*\z")]
    private static partial Regex SavepointName();
}
