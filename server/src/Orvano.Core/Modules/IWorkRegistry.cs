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

public interface IWorkRegistry
{
    /// <summary>
    /// <paramref name="consumerName"/> is <c>&lt;module&gt;.&lt;purpose&gt;</c> and is the consumer's stable identity:
    /// a redispatch job finds its consumer again by event type plus name, so renaming one is a breaking change.
    /// </summary>
    void OnEvent(string eventType, string consumerName, EventConsumer consumer);
    void HandleJob(string kind, string queue, JobHandler handler);
    void AddInternalSchedule(string name, TimeSpan interval, ScheduledTask task);
}

/// <summary>Members arrive with the realtime protocol (scope row 22).</summary>
public interface IRealtimeRegistry;
