using Microsoft.Extensions.DependencyInjection;

using Orvano.Infrastructure.Jobs;
using Orvano.Infrastructure.Outbox;

namespace Orvano.Testing;

/// <summary>One attempt <see cref="JobsDriver.RunDueAsync"/> ran: the job's kind, id, and outcome (spec 0013 JTL-1).</summary>
public sealed record JobRun(string Kind, Guid Id, string Outcome);

/// <summary>
/// Runs due jobs and dispatches pending events inline, with no background loop and on any role (spec 0013 JTS-2, OTS-2).
/// Module tests host with <c>ORVANO_ROLES=api</c>, advance their <see cref="ManualClock"/>, and call
/// <see cref="RunDueAsync"/>.
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
    public static async Task<IReadOnlyList<JobRun>> RunDueAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        JobScheduler scheduler = services.GetRequiredService<JobScheduler>();
        JobExecutor executor = services.GetRequiredService<JobExecutor>();
        OutboxDispatch dispatch = services.GetRequiredService<OutboxDispatch>();

        await scheduler.SyncAsync(cancellationToken);
        await executor.MarkExpiredDeadAsync(cancellationToken);
        await scheduler.TickAsync(cancellationToken);
        await dispatch.DispatchAsync(cancellationToken);

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
        while (await dispatch.DispatchAsync(cancellationToken) > 0);

        return runs;
    }

    /// <summary>
    /// Runs one full dispatch pass alone (spec 0013 OTS-2), with the same per outbox batch sizes the worker's loop would
    /// use, and returns the events it marked dispatched. Runs no handler.
    /// </summary>
    public static Task<int> DispatchAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<OutboxDispatch>().DispatchAsync(cancellationToken);
    }
}
