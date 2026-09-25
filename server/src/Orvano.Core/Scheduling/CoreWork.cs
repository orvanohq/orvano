using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;

namespace Orvano.Core.Scheduling;

/// <summary>The shared kernel's own work: the event redispatch job, event pruning, and the job lease reaper.</summary>
public static class CoreWork
{
    public static void Register(IWorkRegistry work, int eventRetentionDays)
    {
        work.HandleJob(EventRedispatch.Kind, JobQueues.Internal, EventRedispatch.HandleAsync);

        work.AddInternalSchedule("events.prune", Timings.EventPruneInterval, async (services, ct) =>
        {
            var db = services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App);
            await using var cmd = db.CreateCommand(
                "DELETE FROM orvano.events WHERE dispatched_at < now() - make_interval(days => @days)");
            cmd.Parameters.AddWithValue("days", eventRetentionDays);
            var deleted = await cmd.ExecuteNonQueryAsync(ct);
            if (deleted > 0) Log(services).LogInformation("Pruned {Count} dispatched event(s)", deleted);
        });

        work.AddInternalSchedule("jobs.reap", Timings.LeaseReaperInterval, async (services, ct) =>
        {
            var db = services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App);
            var reaped = await JobStore.ReapExpiredLeasesAsync(db, ct);
            if (reaped > 0) Log(services).LogWarning("Requeued {Count} job(s) whose lease expired", reaped);
        });
    }

    private static ILogger Log(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CoreWork));
}
