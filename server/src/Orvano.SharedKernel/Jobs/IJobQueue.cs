using Microsoft.EntityFrameworkCore;

namespace Orvano.SharedKernel.Jobs;

/// <summary>Queues work that must outlive the request (spec 0013 JQ-3, spec 0007 JOB-1).</summary>
#pragma warning disable CA1711 // JQ-3: spec 0013 names this interface IJobQueue; it is a job queue, not a collection.
public interface IJobQueue
#pragma warning restore CA1711
{
    /// <summary>
    /// Inserts the job in <paramref name="context"/>'s open transaction, normally inside <c>InTransactionAsync</c>, so it
    /// exists only if that transaction commits; a due job wakes the workers on commit (ENQ-1 to ENQ-5). Returns the new
    /// id, or <see langword="null"/> when a job with the same <see cref="EnqueueOptions.DedupeKey"/> exists in any status.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The context has no open transaction, is not a platform database context, or joined its transaction with
    /// <c>UseTransaction</c>; or no handler is registered for the kind.
    /// </exception>
    /// <exception cref="ArgumentException">The payload, dedupe key, or project id is out of range (ENQ-3).</exception>
    Task<Guid?> EnqueueAsync<TJob>(DbContext context, TJob job, EnqueueOptions? options, CancellationToken cancellationToken)
        where TJob : IJob;

    /// <summary>
    /// Inserts the job in a transaction of its own and commits it (ENQ-6). Use it only where no platform transaction is
    /// open: a handler queuing a follow up, startup work, or a request that writes nothing else. A caller with an open
    /// transaction uses the overload that takes its context, so the job cannot outlive a rolled back change; this
    /// overload cannot see another context's transaction, so that rule is reviewed, not enforced.
    /// </summary>
    /// <exception cref="InvalidOperationException">No handler is registered for the kind.</exception>
    /// <exception cref="ArgumentException">The payload, dedupe key, or project id is out of range (ENQ-3).</exception>
    Task<Guid?> EnqueueAsync<TJob>(TJob job, EnqueueOptions? options, CancellationToken cancellationToken)
        where TJob : IJob;

    /// <summary>
    /// Marks every queued and running job of the project dead with <c>cancelled: project deleted</c>, except
    /// <paramref name="exceptJobId"/>, and returns how many (ENQ-7). A running handler of such a job loses its lease at
    /// its next renewal. It does not stop a job a transaction that commits later enqueues, so the caller marks the
    /// project deleted first and handlers of project jobs refuse a deleted project.
    /// </summary>
    Task<int> CancelProjectJobsAsync(Guid projectId, Guid? exceptJobId, CancellationToken cancellationToken);
}

/// <summary>How to enqueue one job (spec 0013 JQ-3).</summary>
public sealed record EnqueueOptions
{
    /// <summary>When the job is due; <see langword="null"/> means now.</summary>
    public DateTimeOffset? RunAt { get; init; }

    /// <summary>
    /// 1 to 200 characters. While a job with this key exists, in any status and of any kind, another enqueue with it
    /// inserts nothing. The key is one space across every kind and the schedules' <c>&lt;name&gt;:&lt;time&gt;</c> keys, so
    /// start it with your kind (<c>project.teardown:&lt;id&gt;</c>) or a dedupe hit may come from another module's job.
    /// Cleanup frees the key 7 days after the job succeeded or 30 days after it died.
    /// </summary>
    public string? DedupeKey { get; init; }

    /// <summary>The project the job runs for; read it from <c>IProjectContext</c> when the caller has one.</summary>
    public Guid? ProjectId { get; init; }
}
