using Microsoft.EntityFrameworkCore;

using Orvano.SharedKernel.Jobs;

namespace Orvano.SharedKernel.Outbox;

/// <summary>
/// An event type: a public sealed record in the publishing module's <c>Contracts/</c> whose properties are the event
/// (spec 0013 OBX-1). It holds ids and codes only, never personal values or secrets (spec 0011 CLS-2).
/// </summary>
public interface IOutboxEvent
{
    /// <summary>
    /// The event's wire name, 3 to 100 characters matching <c>^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+\.v[1-9][0-9]*\z</c>,
    /// whose first segment is the publishing module's schema, for example <c>auth.user.created.v1</c>. A change old rows
    /// or old subscribers cannot read takes a new version.
    /// </summary>
    static abstract string Type { get; }
}

/// <summary>Announces events in the caller's own transaction (spec 0013 OBX-2, spec 0007 EVT-1).</summary>
public interface IOutbox
{
    /// <summary>
    /// Adds <paramref name="event"/> to <paramref name="context"/>'s change tracker, so the caller's next
    /// <c>SaveChangesAsync</c> writes it into the module's outbox with the change, and the commit wakes the dispatcher.
    /// Synchronous, no I/O. Call it inside <c>InTransactionAsync</c>, on the publishing module's own context (PUB-4).
    /// Returns the event's id.
    /// </summary>
    /// <param name="context">The publishing module's context, with a transaction it began open.</param>
    /// <param name="event">The event.</param>
    /// <param name="projectId">The project the event is about, or <see langword="null"/> for an instance wide event; its subscribers run in that project's scope.</param>
    /// <exception cref="InvalidOperationException">
    /// No outbox registers the type; the context has no open transaction, is not a platform database context, or joined
    /// its transaction; the context belongs to another module; or the context maps no outbox (PUB-2).
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="projectId"/> is <see cref="Guid.Empty"/>, or the payload is over 64,000 bytes.</exception>
#pragma warning disable CA1716 // OBX-2: spec 0013 names this parameter event; C# callers write @event, and modules are C#.
    Guid Publish<TEvent>(DbContext context, TEvent @event, Guid? projectId)
        where TEvent : IOutboxEvent;
#pragma warning restore CA1716
}

/// <summary>
/// Reacts to one event type (spec 0013 OBX-4). Register it with <c>services.AddEventHandler&lt;TEvent, THandler&gt;(name)</c>;
/// it runs as a job of its own kind, scoped, one instance per attempt. Handlers are idempotent: an event can reach a
/// subscriber more than once, and in no set order (DLV-6). The token cancels on timeout, lease loss, and host shutdown.
/// </summary>
#pragma warning disable CA1711 // OBX-4: spec 0013 names this interface IEventHandler; it handles outbox events, not a .NET event.
public interface IEventHandler<in TEvent>
#pragma warning restore CA1711
    where TEvent : IOutboxEvent
{
    /// <summary>
    /// Handles the event. Returning completes the delivery; throwing is a failed attempt, retried with backoff, except
    /// <see cref="PermanentJobFailureException"/>, which makes the delivery dead at once.
    /// </summary>
#pragma warning disable CA1716 // OBX-4: spec 0013 names this parameter event; C# callers write @event, and modules are C#.
    Task HandleAsync(TEvent @event, EventContext context, CancellationToken cancellationToken);
#pragma warning restore CA1716
}

/// <summary>The event a handler runs for (spec 0013 OBX-4).</summary>
/// <param name="EventId">The event's id, a uuid v7, so it orders by publish time.</param>
/// <param name="Type">The event's type (<see cref="IOutboxEvent.Type"/>).</param>
/// <param name="OccurredAt">When the event was published.</param>
/// <param name="ProjectId">The project the event is about, if any; the same as <c>IProjectContext.Id</c> in its scope.</param>
/// <param name="Job">The delivery job's row: its attempt and ids.</param>
public sealed record EventContext(
    Guid EventId,
    string Type,
    DateTimeOffset OccurredAt,
    Guid? ProjectId,
    JobContext Job);
