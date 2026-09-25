using Orvano.Core.Events;

namespace Orvano.Core.Modules;

public sealed record InternalSchedule(string Name, TimeSpan Interval, ScheduledTask Task);

public sealed class WorkRegistry : IWorkRegistry
{
    private readonly Dictionary<string, List<EventConsumer>> _consumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JobHandler> _handlers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queues = new(StringComparer.Ordinal);
    private readonly List<InternalSchedule> _schedules = [];

    public void OnEvent(string eventType, EventConsumer consumer)
    {
        if (!_consumers.TryGetValue(eventType, out var list)) _consumers[eventType] = list = [];
        list.Add(consumer);
    }

    public void HandleJob(string kind, string queue, JobHandler handler)
    {
        if (!_handlers.TryAdd(kind, handler))
            throw new InvalidOperationException($"Job kind '{kind}' already has a handler.");
        _queues.Add(queue);
    }

    public void AddInternalSchedule(string name, TimeSpan interval, ScheduledTask task) =>
        _schedules.Add(new InternalSchedule(name, interval, task));

    public IReadOnlyList<EventConsumer> ConsumersFor(OutboxEvent e) =>
        _consumers.TryGetValue(e.Type, out var list) ? list : [];

    public JobHandler? HandlerFor(string kind) => _handlers.GetValueOrDefault(kind);

    public string[] Queues => [.. _queues];

    public IReadOnlyList<InternalSchedule> Schedules => _schedules;
}

public sealed class RealtimeRegistry : IRealtimeRegistry;
