using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orvano.SharedKernel.Configuration;
using Orvano.SharedKernel.Jobs;

namespace Orvano.SharedKernel.Outbox;

/// <summary>
/// One module's outbox (spec 0013 OBX-3): its schema and the event types it publishes. Made by <c>AddOutbox</c>; read
/// by <c>Orvano.Infrastructure</c> and, later, by EVT-4's readers.
/// </summary>
public sealed class OutboxRegistration
{
    internal OutboxRegistration(string schema, IReadOnlyList<OutboxEventRegistration> events)
    {
        Schema = schema;
        Events = events;
    }

    /// <summary>The module schema, which holds the <c>outbox</c> table.</summary>
    public string Schema { get; }

    /// <summary>The event types the module publishes, in the order they were added.</summary>
    public IReadOnlyList<OutboxEventRegistration> Events { get; }
}

/// <summary>One event type an outbox publishes (spec 0013 OBX-3).</summary>
public sealed class OutboxEventRegistration
{
    internal OutboxEventRegistration(string type, Type eventType, JsonTypeInfo jsonTypeInfo)
    {
        Type = type;
        EventType = eventType;
        JsonTypeInfo = jsonTypeInfo;
    }

    /// <summary>The wire name (<see cref="IOutboxEvent.Type"/>).</summary>
    public string Type { get; }

    /// <summary>The event record's CLR type.</summary>
    public Type EventType { get; }

    /// <summary>The JSON type info from the module's source generated <c>JsonSerializerContext</c> (spec 0002 JSN-1).</summary>
    public JsonTypeInfo JsonTypeInfo { get; }
}

/// <summary>One subscriber of an event type (spec 0013 OBX-5). Made by <c>AddEventHandler</c>.</summary>
public sealed class EventSubscription
{
    internal EventSubscription(string name, string type, Type eventType)
    {
        Name = name;
        Type = type;
        EventType = eventType;
    }

    /// <summary>The subscriber's stable name: its delivery jobs' kind, part of each delivery's dedupe key, and the metric tag.</summary>
    public string Name { get; }

    /// <summary>The event type it subscribes to (<see cref="IOutboxEvent.Type"/>).</summary>
    public string Type { get; }

    /// <summary>The event record's CLR type.</summary>
    public Type EventType { get; }
}

/// <summary>The read only view of every outbox and subscription (spec 0013 OBX-6). Nothing writes it after startup.</summary>
public interface IOutboxRegistry
{
    /// <summary>Every registered outbox, in schema order (ordinal).</summary>
    IReadOnlyList<OutboxRegistration> Outboxes { get; }

    /// <summary>The registration of <paramref name="type"/>, or <see langword="null"/> when no outbox registers it.</summary>
    OutboxEventRegistration? FindEvent(string type);

    /// <summary>The subscribers of <paramref name="type"/>, in name order (ordinal); empty when there are none.</summary>
    IReadOnlyList<EventSubscription> SubscribersOf(string type);
}

/// <summary>Adds the event types of one outbox (spec 0013 OBX-3).</summary>
public sealed class OutboxBuilder
{
    private readonly List<OutboxEventRegistration> _events = [];

    internal OutboxBuilder(string schema) => Schema = schema;

    /// <summary>The module schema the outbox belongs to.</summary>
    public string Schema { get; }

    internal IReadOnlyList<OutboxEventRegistration> Events => _events;

    /// <summary>Adds <typeparamref name="TEvent"/>, with the JSON type info from the module's source generated context.</summary>
    /// <exception cref="ConfigurationException">The type breaks OBX-1's pattern, is not in this schema, or was added already.</exception>
    public OutboxBuilder Event<TEvent>(JsonTypeInfo<TEvent> jsonTypeInfo)
        where TEvent : IOutboxEvent
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        string type = TEvent.Type;
        if (!OutboxServiceCollectionExtensions.IsValidType(type))
        {
            throw new ConfigurationException(
                $"The event type '{type}' is not valid: use 3 to 100 characters of dotted lowercase segments ending in a version, "
                + "such as auth.user.created.v1 (spec 0013 OBX-1).");
        }

        if (!type.StartsWith(Schema + ".", StringComparison.Ordinal))
        {
            throw new ConfigurationException(
                $"The event type '{type}' must start with its outbox's schema, '{Schema}.' (spec 0013 OBX-1, OBX-3).");
        }

        if (_events.Any(registration => registration.Type == type))
        {
            throw new ConfigurationException($"The event type '{type}' is added to the {Schema} outbox more than once (spec 0013 OBX-3).");
        }

        _events.Add(new OutboxEventRegistration(type, typeof(TEvent), jsonTypeInfo));
        return this;
    }
}

/// <summary>Registers outboxes and event subscribers (spec 0013 OBX-3, OBX-5). Modules call both in <c>Add&lt;Module&gt;Module</c>, for every command.</summary>
public static partial class OutboxServiceCollectionExtensions
{
    private const int MinTypeLength = 3;
    private const int MaxTypeLength = 100;

    /// <summary>
    /// Registers the module's outbox in <paramref name="schema"/> and the event types <paramref name="configure"/> adds.
    /// The module's context calls <c>modelBuilder.HasOutbox()</c> and its migration creates the table.
    /// </summary>
    /// <exception cref="ConfigurationException">The schema or an event type is not valid (OBX-1, OBX-3).</exception>
    public static IServiceCollection AddOutbox(this IServiceCollection services, string schema, Action<OutboxBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (!IsValidSchema(schema))
        {
            throw new ConfigurationException(
                $"The outbox schema '{schema}' is not valid: use 2 to 31 lowercase letters, digits, or underscores, starting with a letter (spec 0013 OBX-3).");
        }

        var builder = new OutboxBuilder(schema);
        configure(builder);
        services.AddSingleton(new OutboxRegistration(schema, [.. builder.Events]));
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as a subscriber of <typeparamref name="TEvent"/> named
    /// <paramref name="name"/>: a scoped service of its own type, and a job kind of that name whose jobs deliver each
    /// event (DLV-1). Name it after the subscribing module and what it does, such as
    /// <c>platform.user_created.count_users</c>; renaming it is a new subscriber.
    /// </summary>
    /// <exception cref="ConfigurationException">
    /// The name is not valid, an option is out of range, or <see cref="JobKindOptions.RequiresProject"/> is set: a
    /// subscriber reads its project from <see cref="EventContext.ProjectId"/>.
    /// </exception>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(
        this IServiceCollection services,
        string name,
        Action<JobKindOptions>? configure = null)
        where TEvent : IOutboxEvent
        where THandler : class, IEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        JobServiceCollectionExtensions.EnsureValidName(name, "event subscriber name");
        var options = new JobKindOptions();
        configure?.Invoke(options);
        JobServiceCollectionExtensions.EnsureValidOptions(name, options);
        if (options.RequiresProject)
        {
            // OUT-4 would kill an instance wide event's delivery at once.
            throw new ConfigurationException(
                $"The event subscriber {name} sets RequiresProject; read the project from EventContext.ProjectId instead (spec 0013 OBX-5).");
        }

        string type = TEvent.Type;

        // OBX-5: by its own type, so two subscribers of one event type each keep their own handler.
        services.TryAddScoped<THandler>();
        services.AddSingleton(new EventSubscription(name, type, typeof(TEvent)));
        services.AddSingleton(new JobRegistration(
            name,
            typeof(OutboxDelivery),
            options,
            payload => JsonSerializer.Deserialize(payload, OutboxJsonContext.Default.OutboxDelivery),
            delivery => JsonSerializer.Serialize((OutboxDelivery)delivery, OutboxJsonContext.Default.OutboxDelivery),
            (scope, delivery, context, cancellationToken) =>
                DeliverAsync<TEvent, THandler>(scope, (OutboxDelivery)delivery, context, cancellationToken)));
        return services;
    }

    /// <summary>OBX-1: 3 to 100 characters, dotted lowercase segments, at least one between the schema and the version.</summary>
    public static bool IsValidType(string? type) =>
        type is { Length: >= MinTypeLength and <= MaxTypeLength } && TypePattern().IsMatch(type);

    /// <summary>OBX-3: a module schema the dispatcher's SQL can name (DSP-8).</summary>
    public static bool IsValidSchema(string? schema) => schema is not null && SchemaPattern().IsMatch(schema);

    /// <summary>
    /// DLV-5: reads the event from the envelope with its registered JSON type info and runs the subscriber's own
    /// handler. A type no longer registered, or data that does not read, throws, which is a failed attempt (OUT-2).
    /// </summary>
    private static Task DeliverAsync<TEvent, THandler>(
        IServiceProvider scope, OutboxDelivery delivery, JobContext context, CancellationToken cancellationToken)
        where TEvent : IOutboxEvent
        where THandler : class, IEventHandler<TEvent>
    {
        if (delivery.Type != TEvent.Type
            || scope.GetRequiredService<IOutboxRegistry>().FindEvent(delivery.Type) is not { } registration
            || registration.EventType != typeof(TEvent))
        {
            throw new InvalidOperationException(
                $"The delivery's event type '{delivery.Type}' is not registered as {typeof(TEvent).Name} (spec 0013 DLV-5).");
        }

        TEvent @event = (TEvent?)delivery.Data.Deserialize(registration.JsonTypeInfo)
            ?? throw new JsonException($"The data of a {delivery.Type} delivery is null.");
        var eventContext = new EventContext(delivery.EventId, delivery.Type, delivery.OccurredAt, context.ProjectId, context);
        return scope.GetRequiredService<THandler>().HandleAsync(@event, eventContext, cancellationToken);
    }

    // OBX-1's pattern, with \z for $ so a trailing newline does not match.
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+\.v[1-9][0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TypePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_]{1,30}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaPattern();
}

/// <summary>
/// A delivery job's payload (spec 0013 DLV-2): a copy of the event, so the delivery never reads the outbox row and
/// outlives its retention. The subscriber's kind reads it; the dispatcher in <c>Orvano.Infrastructure</c> writes the
/// same JSON with a writer of its own, since these internals are shown only to test projects (spec 0012 RSV-1).
/// </summary>
/// <param name="EventId">The outbox row's id.</param>
/// <param name="Type">The event's type.</param>
/// <param name="OccurredAt">The row's <c>occurred_at</c>.</param>
/// <param name="Data">The row's payload.</param>
internal sealed record OutboxDelivery(Guid EventId, string Type, DateTimeOffset OccurredAt, JsonElement Data);

/// <summary>The envelope's source generated JSON (spec 0002 JSN-1), with camelCase names (DLV-2).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OutboxDelivery))]
internal sealed partial class OutboxJsonContext : JsonSerializerContext;
