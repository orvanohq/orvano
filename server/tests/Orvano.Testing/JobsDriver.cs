using Microsoft.Extensions.DependencyInjection;

using Orvano.Infrastructure.Jobs;
using Orvano.Infrastructure.Outbox;

namespace Orvano.Testing;

/// <summary>One attempt <see cref="JobsDriver.RunDueAsync(IServiceProvider, CancellationToken)"/> ran: the job's kind, id, and outcome (spec 0013 JTL-1).</summary>
public sealed record JobRun(string Kind, Guid Id, string Outcome);

/// <summary>
/// Runs due jobs and dispatches pending events inline, with no background loop and on any role (spec 0013 JTS-2, OTS-2).
/// Module tests host with <c>ORVANO_ROLES=api</c>, advance their <see cref="ManualClock"/>, and call
/// <see cref="RunDueAsync(IServiceProvider, CancellationToken)"/>.
/// </summary>
public static class JobsDriver
{
    /// <summary>The most attempts one call runs before it gives up, so a handler that keeps enqueuing fails the test.</summary>
    public const int MaxAttempts = 1000;

    /// <summary>
    /// Syncs the registered schedules, marks expired last attempts dead, and fires the due schedules. Then it dispatches
    /// every pending event (OTS-2), claims and runs every due job on the current clock to its outcome, one at a time,
    /// until a claim returns nothing, and dispatches again; it stops when that dispatch moves nothing, so a handler that
    /// publishes an event another subscriber handles completes in one call. Returns each attempt in the order it ran.
    /// </summary>
    /// <exception cref="InvalidOperationException">More than <see cref="MaxAttempts"/> attempts ran; the message names their kinds.</exception>
    /// <exception cref="AggregateException">A dispatch batch failed (DSP-5); each inner exception names its outbox. The
    /// worker would log it and try again, but here it would look like nothing was left to run.</exception>
    public static Task<IReadOnlyList<JobRun>> RunDueAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        RunDueAsync(services, allowDispatchFailures: false, cancellationToken);

    /// <summary>
    /// <see cref="RunDueAsync(IServiceProvider, CancellationToken)"/>, which with <paramref name="allowDispatchFailures"/>
    /// goes on past a failed dispatch batch as the worker does, for a test that breaks an outbox on purpose.
    /// </summary>
    public static async Task<IReadOnlyList<JobRun>> RunDueAsync(IServiceProvider services, bool allowDispatchFailures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        JobScheduler scheduler = services.GetRequiredService<JobScheduler>();
        JobExecutor executor = services.GetRequiredService<JobExecutor>();
        OutboxDispatch dispatch = services.GetRequiredService<OutboxDispatch>();

        await scheduler.SyncAsync(cancellationToken);
        await executor.MarkExpiredDeadAsync(cancellationToken);
        await scheduler.TickAsync(cancellationToken);
        await DispatchOrThrowAsync(dispatch, allowDispatchFailures, cancellationToken);

        var runs = new List<JobRun>();
        do
        {
            while (true)
            {
                IReadOnlyList<ClaimedJob> claimed = await executor.ClaimAsync(1, cancellationToken);
                if (claimed.Count == 0)
                {
                    break;
                }

                foreach (ClaimedJob job in claimed)
                {
                    JobAttemptResult result = await executor.RunAsync(job, cancellationToken);
                    runs.Add(new JobRun(result.Kind, result.Id, result.Outcome));
                }

                if (runs.Count >= MaxAttempts)
                {
                    throw new InvalidOperationException(
                        $"JobsDriver ran {runs.Count} attempts in one call; a handler keeps enqueuing due jobs. Kinds: "
                        + string.Join(", ", runs.Select(run => run.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) + ".");
                }
            }
        }
        while (await DispatchOrThrowAsync(dispatch, allowDispatchFailures, cancellationToken) > 0);

        return runs;
    }

    /// <summary>
    /// Runs one full dispatch pass alone (spec 0013 OTS-2), with the same per outbox batch sizes the worker's loop would
    /// use, and returns the events it marked dispatched. Runs no handler. A failed batch is logged and counts nothing, as
    /// in the worker, so a test of a failing batch can check what it left.
    /// </summary>
    public static Task<int> DispatchAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<OutboxDispatch>().DispatchAsync(cancellationToken);
    }

    private static async Task<int> DispatchOrThrowAsync(OutboxDispatch dispatch, bool allowFailures, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        int marked = await dispatch.DispatchAsync(cancellationToken, failures);
        if (failures.Count > 0 && !allowFailures)
        {
            throw new AggregateException("A dispatch batch failed, so JobsDriver.RunDueAsync stops (spec 0013 DSP-5).", failures);
        }

        return marked;
    }
}
