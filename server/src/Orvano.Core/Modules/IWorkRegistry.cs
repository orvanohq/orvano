using Orvano.Core.Events;
using Orvano.Core.Jobs;

namespace Orvano.Core.Modules;

/// <summary>
/// Maps an event to the durable jobs it should cause. Must only enqueue, never do the work, and must stay
/// small and free of IO: a consumer that hangs stalls the dispatcher.
/// </summary>
public delegate IEnumerable<NewJob> EventConsumer(OutboxEvent e);

/// <summary>Runs one job. Delivery is at least once, so a handler must be idempotent.</summary>
public delegate Task JobHandler(JobContext job, CancellationToken ct);

/// <summary>A task the leader worker runs on an interval.</summary>
public delegate Task ScheduledTask(IServiceProvider services, CancellationToken ct);

/// <summary>Where a module registers the work the <c>worker</c> role runs.</summary>
public interface IWorkRegistry
{
    /// <summary>
    /// <paramref name="consumerName"/> is <c>&lt;module&gt;.&lt;purpose&gt;</c> and is the consumer's stable identity:
    /// a redispatch job finds its consumer again by event type plus name, so renaming one is a breaking change.
    /// </summary>
    void OnEvent(string eventType, string consumerName, EventConsumer consumer);
    /// <summary>Registers the handler for a job kind, and makes the worker claim <paramref name="queue"/>. One handler per kind.</summary>
    void HandleJob(string kind, string queue, JobHandler handler);

    /// <summary>Runs <paramref name="task"/> every <paramref name="interval"/> on the leader worker only.</summary>
    void AddInternalSchedule(string name, TimeSpan interval, ScheduledTask task);
}

/// <summary>Members arrive with the realtime protocol (scope row 22).</summary>
public interface IRealtimeRegistry;
