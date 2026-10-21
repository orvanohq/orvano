using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Outbox;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Outbox;

/// <summary>
/// Adds published events to the caller's own context, so its <c>SaveChangesAsync</c> writes them in its transaction,
/// and wakes the dispatcher on commit (spec 0013 PUB-1 to PUB-3, spec 0007 EVT-1).
/// </summary>
internal sealed class Outbox(
    IOutboxRegistry registry,
    SignalBus signals,
    TimeProvider time,
    InfrastructureTelemetry telemetry,
    ILogger<Outbox> logger) : IOutbox
{
    /// <summary>The largest payload, in UTF-8 bytes, so the delivery envelope stays under ENQ-3's 65,536 (PUB-2, DLV-2).</summary>
    public const int MaxPayloadBytes = 64_000;

    public Guid Publish<TEvent>(DbContext context, TEvent @event, Guid? projectId)
        where TEvent : IOutboxEvent
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(@event);

        // PUB-2: every check, in this order, before anything changes.
        string type = TEvent.Type;
        if (registry.FindEvent(type) is not { } registration || registration.EventType != typeof(TEvent))
        {
            throw new InvalidOperationException($"No outbox registers the event type '{type}'. Add it with services.AddOutbox (spec 0013 OBX-3).");
        }

        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("An event's project id is not Guid.Empty (spec 0013 PUB-2).", nameof(projectId));
        }

        string payload = JsonSerializer.Serialize(@event, registration.JsonTypeInfo);
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
        {
            throw new ArgumentException($"An event payload is at most {MaxPayloadBytes} bytes as JSON (spec 0013 PUB-2).", nameof(@event));
        }

        TransactionSignals.EnsureEnlistable(context); // the SND-5 refusals
        string schema = OrvanoConventions.SchemaOf(context.GetService<IDbContextOptions>())!;
        if (!type.StartsWith(schema + ".", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Publish {type} on the {type[..type.IndexOf('.', StringComparison.Ordinal)]} module's own DbContext (spec 0013 PUB-2).");
        }

        if (context.Model.FindEntityType(typeof(OutboxEvent)) is null)
        {
            throw new InvalidOperationException(
                $"The {schema} context has no outbox table. Call modelBuilder.HasOutbox() in OnModelCreating (spec 0013 OTB-2).");
        }

        // PUB-1: the id is a uuid v7 of the publish time, which the cleanup's boundary relies on (OCL-2).
        DateTimeOffset now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        context.Add(new OutboxEvent
        {
            Id = id,
            Type = type,
            Payload = payload,
            ProjectId = projectId,
            OccurredAt = now,
            TraceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null,
        });
        signals.Add(context, SignalKind.Job, []); // EVT-3: the dispatcher wakes on the job signal; one per transaction (SND-4)
        TransactionSignals.AddOnCommitted(context, _ =>
        {
            telemetry.EventPublished(type);
            return ValueTask.CompletedTask;
        });
        TransactionSignals.AddBeforeCommit(context, typeof(Outbox), EnsureSaved);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.OutboxEventPublished(type, id, payload);
        }

        return id;
    }

    /// <summary>PUB-3: an event added but never saved fails the commit, so its job signal cannot go out without it.</summary>
    private static void EnsureSaved(DbContext context)
    {
        if (context.ChangeTracker.Entries<OutboxEvent>().Any(entry => entry.State == EntityState.Added))
        {
            throw new InvalidOperationException(
                "An event published through IOutbox was not saved. Call SaveChangesAsync before the commit (spec 0013 PUB-3).");
        }
    }
}
