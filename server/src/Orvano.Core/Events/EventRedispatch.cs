using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;

namespace Orvano.Core.Events;

/// <summary>The payload of an <see cref="EventRedispatch"/> job.</summary>
/// <param name="Consumer">The name of the consumer to rerun.</param>
/// <param name="Event">A full copy of the event it failed on.</param>
public sealed record EventRedispatchPayload(string Consumer, RedispatchedEvent Event);

/// <summary>The copy of an <see cref="OutboxEvent"/> carried in a redispatch job, with the payload kept as JSON.</summary>
/// <param name="Id">The original outbox row ID.</param>
/// <param name="ProjectId">The project the event belongs to, or <see langword="null"/> for a platform event.</param>
/// <param name="Type">The event type name.</param>
/// <param name="Subject">The ID of the thing the event is about, if any.</param>
/// <param name="Payload">The event body. Never log it.</param>
/// <param name="CreatedAt">When the original event was written.</param>
public sealed record RedispatchedEvent(long Id, string? ProjectId, string Type, string? Subject, JsonElement Payload, DateTimeOffset CreatedAt)
{
    /// <summary>The event in the form consumers receive.</summary>
    public OutboxEvent ToOutboxEvent() => new(Id, ProjectId, Type, Subject, Payload.GetRawText(), CreatedAt);
}

/// <summary>
/// The job that reruns one consumer on one event after it failed in the dispatcher (spec 0002, poison events).
/// It carries a full copy of the event, so event pruning never breaks it. Delivery is at least once.
/// </summary>
public static class EventRedispatch
{
    /// <summary>The job kind.</summary>
    public const string Kind = "events.redispatch";

    /// <summary>About 10 hours of retries with the job backoff, so a fix deployed the same day heals on its own.</summary>
    public const int MaxAttempts = 25;

    // jsonb nests deeper than System.Text.Json's default of 64; reading is iterative, so no limit is needed.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = int.MaxValue };

    /// <summary>The only place this job is built. The dispatcher calls it for a failed consumer.</summary>
    public static NewJob For(OutboxEvent e, string consumer) =>
        new(Kind, Payload(e, consumer), JobQueues.Internal, e.ProjectId, MaxAttempts: MaxAttempts);

    // The event's payload is embedded as JSON, not a string. It came from jsonb, so it is valid JSON; writing it
    // raw (rather than parsing it into a JsonNode) means a deeply nested payload can never make this throw,
    // which would end the dispatch pass and stall the outbox.
    private static string Payload(OutboxEvent e, string consumer)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("consumer", consumer);
            w.WriteStartObject("event");
            w.WriteNumber("id", e.Id);
            w.WriteString("projectId", e.ProjectId);
            w.WriteString("type", e.Type);
            w.WriteString("subject", e.Subject);
            w.WritePropertyName("payload");
            w.WriteRawValue(e.Payload, skipInputValidation: true);
            w.WriteString("createdAt", e.CreatedAt);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Parses a redispatch job's payload.</summary>
    /// <exception cref="JsonException">The payload is not a redispatch payload.</exception>
    public static EventRedispatchPayload Read(string payloadJson) =>
        JsonSerializer.Deserialize<EventRedispatchPayload>(payloadJson, Json)
        ?? throw new JsonException("The redispatch payload is null.");

    /// <summary>Reruns the consumer and enqueues its jobs. A throw, an invalid job, or a database rejection is an ordinary job failure.</summary>
    public static async Task HandleAsync(JobContext job, CancellationToken ct)
    {
        var payload = Read(job.Job.Payload);
        var e = payload.Event.ToOutboxEvent();
        var work = job.Services.GetRequiredService<WorkRegistry>();

        var consumer = work.ConsumerFor(e.Type, payload.Consumer)
            ?? throw new PermanentJobFailureException($"Consumer '{payload.Consumer}' for event type '{e.Type}' is no longer registered");

        var jobs = consumer(e).ToList();
        if (work.ValidateJobs(jobs) is { } problem) throw new InvalidOperationException(problem);
        if (jobs.Count == 0) return;

        var db = job.Services.GetRequiredKeyedService<NpgsqlDataSource>(OrvanoDb.App);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await JobQueue.EnqueueManyAsync(tx, jobs, savepoint: null, ct);
        await tx.CommitAsync(ct);
    }
}
