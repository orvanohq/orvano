using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orvano.SharedKernel.Outbox;

namespace Orvano.SharedKernel.Api;

/// <summary>An event the public contract publishes (spec 0014 EVS-1, EVS-2), from <c>AddPublicEvent</c>.</summary>
/// <param name="Type">The event's type, such as <c>auth.user.created.v1</c> (spec 0007 EVT-2).</param>
/// <param name="Payload">The event record, an <see cref="IOutboxEvent"/>.</param>
/// <param name="Description">What the event means, for app developers (CLT-3).</param>
public sealed record PublicEvent(string Type, Type Payload, string Description)
{
    /// <summary>
    /// The payload's schema name in the contract: each dot separated segment PascalCased, then <c>Event</c>
    /// (<c>auth.user.created.v1</c> gives <c>AuthUserCreatedV1Event</c>, EVS-1).
    /// </summary>
    public string SchemaName => string.Concat(Type.Split('.').Select(Pascal)) + "Event";

    private static string Pascal(string segment) =>
        string.Concat(segment.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}

/// <summary>
/// The events the public contract publishes (spec 0014 EVS-2), in the document's <c>webhooks</c>. Empty until feature
/// 33 registers events; an outbox event never appears unless it is registered here.
/// </summary>
public interface IPublicEventCatalog
{
    /// <summary>Every public event, ordered by type.</summary>
    IReadOnlyList<PublicEvent> Events { get; }
}

/// <summary>Registers public events (spec 0014 EVS-2).</summary>
public static partial class PublicEventServiceCollectionExtensions
{
    /// <summary>
    /// Publishes <typeparamref name="TEvent"/> in the public contract, so every SDK gets its payload model (EVS-3).
    /// Only an event meant for apps belongs here; feature 33 decides which those are.
    /// </summary>
    public static IServiceCollection AddPublicEvent<TEvent>(this IServiceCollection services, string description)
        where TEvent : IOutboxEvent
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (!TypePattern().IsMatch(TEvent.Type))
        {
            throw new ArgumentException($"'{TEvent.Type}' is not an event type such as auth.user.created.v1 (spec 0007 EVT-2).", nameof(TEvent));
        }

        services.TryAddSingleton<IPublicEventCatalog, PublicEventCatalog>();
        services.AddSingleton(new PublicEvent(TEvent.Type, typeof(TEvent), description));
        return services;
    }

    /// <summary>Adds the catalog without events, so the contract can always ask for it.</summary>
    public static IServiceCollection AddPublicEventCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPublicEventCatalog, PublicEventCatalog>();
        return services;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+\.v[1-9][0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TypePattern();
}

internal sealed class PublicEventCatalog : IPublicEventCatalog
{
    public PublicEventCatalog(IEnumerable<PublicEvent> events)
    {
        PublicEvent[] all = [.. events.OrderBy(entry => entry.Type, StringComparer.Ordinal)];
        foreach (IGrouping<string, PublicEvent> duplicate in all.GroupBy(entry => entry.Type, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            throw new InvalidOperationException($"The public event {duplicate.Key} is registered more than once.");
        }

        Events = all;
    }

    public IReadOnlyList<PublicEvent> Events { get; }
}
