using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace Orvano.Core.Jobs;

/// <summary>Queue names. A job loop claims every queue that has a registered handler.</summary>
public static class JobQueues
{
    /// <summary>The queue for module jobs unless a module picks another.</summary>
    public const string Default = "default";

    /// <summary>The platform's own jobs, such as <see cref="Events.EventRedispatch"/>.</summary>
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
    /// <summary>The attempts a job gets unless it asks for a different number.</summary>
    public const int DefaultMaxAttempts = 10;
}

/// <summary>A job a worker has claimed and holds the lease on.</summary>
/// <param name="Id">The job's row ID.</param>
/// <param name="Queue">The queue it was claimed from.</param>
/// <param name="Kind">Selects the handler.</param>
/// <param name="ProjectId">The project the job runs for, or <see langword="null"/> for platform work.</param>
/// <param name="Payload">The handler's input as JSON text. Never log it.</param>
/// <param name="Attempts">Claims so far, including this one.</param>
/// <param name="MaxAttempts">After this many attempts a failing job is dead.</param>
public sealed record ClaimedJob(long Id, string Queue, string Kind, string? ProjectId, string Payload, int Attempts, int MaxAttempts);

/// <summary>What a <see cref="Modules.JobHandler"/> receives.</summary>
/// <param name="Job">The claimed job.</param>
/// <param name="Services">A service scope for this one job run.</param>
public sealed record JobContext(ClaimedJob Job, IServiceProvider Services);

/// <summary>Enqueues jobs in <c>orvano.jobs</c> and wakes the job loops.</summary>
public static partial class JobQueue
{
    /// <summary>The NOTIFY channel; each notification's payload is the queue name.</summary>
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
