using System.Collections.Frozen;

using Orvano.SharedKernel.Outbox;

namespace Orvano.Infrastructure.Outbox;

/// <summary>
/// Every registered outbox and subscriber, checked once (spec 0013 OBX-6, OBX-7): a schema registered twice, an event
/// type registered by two outboxes, and a subscriber whose type no outbox registers, or registers with another CLR
/// type. <c>StartupChecks</c> turns <see cref="Problems"/> into a startup failure in every <c>serve</c> role, next to
/// <c>JobRegistry</c>'s, which already refuses a subscriber named like another job kind.
/// </summary>
internal sealed class OutboxRegistry : IOutboxRegistry
{
    private readonly FrozenDictionary<string, OutboxEventRegistration> _events;
    private readonly FrozenDictionary<string, EventSubscription[]> _subscribers;

    public OutboxRegistry(IEnumerable<OutboxRegistration> outboxes, IEnumerable<EventSubscription> subscriptions)
    {
        var problems = new List<string>();

        var bySchema = new Dictionary<string, OutboxRegistration>(StringComparer.Ordinal);
        var byType = new Dictionary<string, (OutboxEventRegistration Event, string Schema)>(StringComparer.Ordinal);
        foreach (OutboxRegistration outbox in outboxes)
        {
            if (!bySchema.TryAdd(outbox.Schema, outbox))
            {
                // Its types are still checked: within one schema, a type twice is only possible this way (OBX-3).
                problems.Add($"The outbox of schema {outbox.Schema} is registered more than once (spec 0013 OBX-7).");
            }

            foreach (OutboxEventRegistration registration in outbox.Events)
            {
                if (!byType.TryAdd(registration.Type, (registration, outbox.Schema)))
                {
                    problems.Add(
                        $"The event type {registration.Type} is registered by the {byType[registration.Type].Schema} and {outbox.Schema} outboxes (spec 0013 OBX-7).");
                }
            }
        }

        var bySubscribedType = new Dictionary<string, List<EventSubscription>>(StringComparer.Ordinal);
        foreach (EventSubscription subscription in subscriptions)
        {
            if (!byType.TryGetValue(subscription.Type, out (OutboxEventRegistration Event, string Schema) registered))
            {
                problems.Add(
                    $"The event subscriber {subscription.Name} subscribes to {subscription.Type}, which no outbox registers (spec 0013 OBX-7).");
                continue;
            }

            if (registered.Event.EventType != subscription.EventType)
            {
                problems.Add(
                    $"The event subscriber {subscription.Name} reads {subscription.Type} as {subscription.EventType.Name}, but its outbox "
                    + $"registers it as {registered.Event.EventType.Name} (spec 0013 OBX-7).");
                continue;
            }

            if (!bySubscribedType.TryGetValue(subscription.Type, out List<EventSubscription>? list))
            {
                list = [];
                bySubscribedType[subscription.Type] = list;
            }

            list.Add(subscription);
        }

        Outboxes = [.. bySchema.Values.OrderBy(outbox => outbox.Schema, StringComparer.Ordinal)];
        _events = byType.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Event, StringComparer.Ordinal);
        _subscribers = bySubscribedType.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderBy(subscription => subscription.Name, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        SubscriberCount = _subscribers.Values.Sum(list => list.Length);
        Problems = problems;
    }

    /// <summary>What a startup check reports; empty when every registration is sound.</summary>
    public IReadOnlyList<string> Problems { get; }

    public IReadOnlyList<OutboxRegistration> Outboxes { get; }

    /// <summary>Every valid subscription, for the dispatcher's start log (OTL-2).</summary>
    public int SubscriberCount { get; }

    public OutboxEventRegistration? FindEvent(string type) => _events.GetValueOrDefault(type);

    public IReadOnlyList<EventSubscription> SubscribersOf(string type) => _subscribers.GetValueOrDefault(type) ?? [];
}
