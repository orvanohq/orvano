using Microsoft.Extensions.DependencyInjection;

using Orvano.Infrastructure.Jobs;

namespace Orvano.Testing;

/// <summary>One attempt <see cref="JobsDriver.RunDueAsync"/> ran: the job's kind, id, and outcome (spec 0013 JTL-1).</summary>
public sealed record JobRun(string Kind, Guid Id, string Outcome);

/// <summary>
/// Runs due jobs inline, with no background loop and on any role (spec 0013 JTS-2). Module tests host with
/// <c>ORVANO_ROLES=api</c>, advance their <see cref="ManualClock"/>, and call <see cref="RunDueAsync"/>.
/// </summary>
public static class JobsDriver
{
    /// <summary>The most attempts one call runs before it gives up, so a handler that keeps enqueuing fails the test.</summary>
    public const int MaxAttempts = 1000;

    /// <summary>
    /// Syncs the registered schedules, marks expired last attempts dead, fires the due schedules, then claims and runs every due job on the current clock to
    /// its outcome, one at a time, until a claim returns nothing. Returns each attempt in the order it ran.
    /// </summary>
    /// <exception cref="InvalidOperationException">More than <see cref="MaxAttempts"/> attempts ran; the message names their kinds.</exception>
    public static async Task<IReadOnlyList<JobRun>> RunDueAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        JobScheduler scheduler = services.GetRequiredService<JobScheduler>();
        JobExecutor executor = services.GetRequiredService<JobExecutor>();

        await scheduler.SyncAsync(cancellationToken);
        await executor.MarkExpiredDeadAsync(cancellationToken);
        await scheduler.TickAsync(cancellationToken);

        var runs = new List<JobRun>();
        while (true)
        {
            IReadOnlyList<ClaimedJob> claimed = await executor.ClaimAsync(1, cancellationToken);
            if (claimed.Count == 0)
            {
                return runs;
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
}
