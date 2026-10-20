namespace Orvano.SharedKernel.Jobs;

/// <summary>
/// A job type: a record whose properties are the job's payload (spec 0013 JQ-1). The payload holds ids and codes only,
/// never personal values or secrets (spec 0011 CLS-2).
/// </summary>
public interface IJob
{
    /// <summary>
    /// The job's wire name, 3 to 100 characters matching <c>^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$</c>, for example
    /// <c>jobs.cleanup</c>. A payload change old rows cannot read takes a new kind (<c>auth.session_cleanup.v2</c>).
    /// </summary>
    static abstract string Kind { get; }
}

/// <summary>
/// Runs one kind of job (spec 0013 JQ-2). Register it with <c>services.AddJobHandler&lt;TJob, THandler&gt;(...)</c>; it
/// is scoped, one instance per attempt. Handlers are idempotent: a job can run again after a crash, a lost lease, or an
/// outcome write that failed. The token cancels on timeout, lease loss, and host shutdown.
/// </summary>
public interface IJobHandler<in TJob>
    where TJob : IJob
{
    /// <summary>
    /// Runs the job. Returning marks it succeeded; throwing is a failed attempt, retried with backoff, except
    /// <see cref="PermanentJobFailureException"/>, which makes the job dead at once.
    /// </summary>
    Task HandleAsync(TJob job, JobContext context, CancellationToken cancellationToken);
}

/// <summary>The row a handler runs for (spec 0013 JQ-2).</summary>
/// <param name="Id">The job's id.</param>
/// <param name="Kind">The job's kind (<see cref="IJob.Kind"/>).</param>
/// <param name="Attempt">This attempt, 1 on the first run.</param>
/// <param name="MaxAttempts">The attempts the job has in all.</param>
/// <param name="ProjectId">The project the job runs for, if any; the same as <c>IProjectContext.Id</c> in its scope.</param>
/// <param name="RunAt">When the job was due.</param>
/// <param name="CreatedAt">When the job was enqueued.</param>
public sealed record JobContext(
    Guid Id,
    string Kind,
    int Attempt,
    int MaxAttempts,
    Guid? ProjectId,
    DateTimeOffset RunAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// Thrown by a handler when retrying will not help (spec 0013 JQ-4, OUT-3): the job becomes dead at once. Any other
/// exception is a failed attempt.
/// </summary>
public sealed class PermanentJobFailureException(string message, Exception? inner = null) : Exception(message, inner);
