using System.Diagnostics;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Orvano.Infrastructure.Jobs.Data;
using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Jobs;

/// <summary>
/// Writes job rows in the caller's transaction, or in one of its own (spec 0013 ENQ-1 to ENQ-7). A due job enlists the
/// <c>job</c> signal on the same context, so workers wake only when the job's transaction commits.
/// </summary>
internal sealed class JobQueue(
    JobRegistry registry,
    SignalBus signals,
    IServiceScopeFactory scopes,
    TimeProvider time,
    InfrastructureTelemetry telemetry,
    ILogger<JobQueue> logger) : IJobQueue
{
    private const int MaxDedupeKeyLength = 200;
    private const string DeletedProject = "cancelled: project deleted";

    public async Task<Guid?> EnqueueAsync<TJob>(DbContext context, TJob job, EnqueueOptions? options, CancellationToken cancellationToken)
        where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(context);
        (JobRegistration registration, string payload) = Check(job, options);
        TransactionSignals.EnsureEnlistable(context); // ENQ-2: the SND-5 refusals, before any I/O
        return await InsertAsync(context, registration, payload, options, cancellationToken);
    }

    public async Task<Guid?> EnqueueAsync<TJob>(TJob job, EnqueueOptions? options, CancellationToken cancellationToken)
        where TJob : IJob
    {
        (JobRegistration registration, string payload) = Check(job, options);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        JobsDbContext db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        return await db.InTransactionAsync(token => InsertAsync(db, registration, payload, options, token), cancellationToken);
    }

    public async Task<int> CancelProjectJobsAsync(Guid projectId, Guid? exceptJobId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        JobsDbContext db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        return await db.InTransactionAsync(
            token =>
            {
                DateTimeOffset now = time.GetUtcNow();
                return db.Database.ExecuteSqlAsync(
                    $"""
                    UPDATE jobs.job SET status = 'dead', finished_at = {now}, locked_until = NULL, locked_by = NULL,
                        last_error = {DeletedProject}
                    WHERE project_id = {projectId} AND status IN ('queued', 'running') AND id IS DISTINCT FROM {exceptJobId}
                    """,
                    token);
            },
            cancellationToken);
    }

    /// <summary>
    /// ENQ-1, ENQ-4, ENQ-5: one insert on <paramref name="context"/>'s connection and transaction, which the caller has
    /// open. Returns the new id, or <see langword="null"/> on a dedupe hit. The schedule tick calls it with a payload
    /// <c>JobRegistry</c> already checked.
    /// </summary>
    public async Task<Guid?> InsertAsync(
        DbContext context, JobRegistration registration, string payload, EnqueueOptions? options, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset runAt = options?.RunAt?.ToUniversalTime() ?? now;
        var id = Guid.CreateVersion7(now);
        string kind = registration.Kind;
        string? dedupeKey = options?.DedupeKey;
        Guid? projectId = options?.ProjectId;
        int maxAttempts = registration.Options.MaxAttempts;
        string? traceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

        int inserted = await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO jobs.job (id, kind, payload, project_id, status, attempt, max_attempts, run_at, dedupe_key, trace_parent, created_at)
            VALUES ({id}, {kind}, {payload}::jsonb, {projectId}, 'queued', 0, {maxAttempts}, {runAt}, {dedupeKey}, {traceParent}, {now})
            ON CONFLICT (kind, project_id, dedupe_key) WHERE dedupe_key IS NOT NULL DO NOTHING
            """,
            cancellationToken);
        if (inserted == 0)
        {
            return null;
        }

        if (runAt <= now)
        {
            signals.Add(context, SignalKind.Job, []); // ENQ-4: one job signal per transaction, however many it enqueues
        }

        TransactionSignals.AddOnCommitted(context, _ =>
        {
            telemetry.JobEnqueued(kind);
            return ValueTask.CompletedTask;
        });
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.JobEnqueued(kind, id, runAt, dedupeKey, payload);
        }

        return id;
    }

    /// <summary>ENQ-3: every argument check, before any I/O.</summary>
    private (JobRegistration Registration, string Payload) Check<TJob>(TJob job, EnqueueOptions? options)
        where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(job);
        JobRegistration registration = registry.For<TJob>();
        string payload = registration.Serialize(job);
        if (Encoding.UTF8.GetByteCount(payload) > JobRegistry.MaxPayloadBytes)
        {
            throw new ArgumentException($"A job payload is at most {JobRegistry.MaxPayloadBytes} bytes as JSON (spec 0013 ENQ-3).", nameof(job));
        }

        if (options?.DedupeKey is { Length: < 1 or > MaxDedupeKeyLength })
        {
            throw new ArgumentException($"A dedupe key is 1 to {MaxDedupeKeyLength} characters (spec 0013 ENQ-3).", nameof(options));
        }

        if (options?.ProjectId == Guid.Empty)
        {
            throw new ArgumentException("A job's project id is not Guid.Empty (spec 0013 ENQ-3).", nameof(options));
        }

        if (registration.Options.RequiresProject && options?.ProjectId is null)
        {
            throw new ArgumentException($"Jobs of kind {registration.Kind} run for a project; set EnqueueOptions.ProjectId (spec 0013 ENQ-3).", nameof(options));
        }

        return (registration, payload);
    }
}
