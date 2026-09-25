using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orvano.Core.Events;
using Orvano.Core.Jobs;

namespace Orvano.Core.Modules;

/// <summary>A schedule registered with <see cref="IWorkRegistry.AddInternalSchedule"/>.</summary>
/// <param name="Name">Names the schedule in logs.</param>
/// <param name="Interval">Time between runs.</param>
/// <param name="Task">The work to run.</param>
public sealed record InternalSchedule(string Name, TimeSpan Interval, ScheduledTask Task);

/// <summary>An event consumer with its stable name.</summary>
/// <param name="Name">The consumer's identity, <c>&lt;module&gt;.&lt;purpose&gt;</c>.</param>
/// <param name="Consumer">Maps the event to jobs.</param>
public sealed record NamedConsumer(string Name, EventConsumer Consumer);

/// <summary>The worker's registry of consumers, job handlers, queues, and schedules, filled by every module at startup.</summary>
public sealed partial class WorkRegistry : IWorkRegistry
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Dictionary<string, List<NamedConsumer>> _consumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JobHandler> _handlers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queues = new(StringComparer.Ordinal);
    private readonly List<InternalSchedule> _schedules = [];

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The consumer name is not 1 to 100 of <c>[a-z0-9_.]</c>.</exception>
    /// <exception cref="InvalidOperationException">The event type already has a consumer with that name.</exception>
    public void OnEvent(string eventType, string consumerName, EventConsumer consumer)
    {
        if (consumerName is null || !ConsumerName().IsMatch(consumerName))
            throw new ArgumentException(
                $"Consumer name '{consumerName}' must be 1 to 100 lowercase letters, digits, '_' or '.'.", nameof(consumerName));

        if (!_consumers.TryGetValue(eventType, out var list)) _consumers[eventType] = list = [];
        if (list.Exists(c => c.Name == consumerName))
            throw new InvalidOperationException($"Event type '{eventType}' already has a consumer named '{consumerName}'.");
        list.Add(new NamedConsumer(consumerName, consumer));
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The kind already has a handler.</exception>
    public void HandleJob(string kind, string queue, JobHandler handler)
    {
        if (!_handlers.TryAdd(kind, handler))
            throw new InvalidOperationException($"Job kind '{kind}' already has a handler.");
        _queues.Add(queue);
    }

    /// <inheritdoc />
    public void AddInternalSchedule(string name, TimeSpan interval, ScheduledTask task) =>
        _schedules.Add(new InternalSchedule(name, interval, task));

    /// <summary>The consumers of an event, in registration order.</summary>
    public IReadOnlyList<NamedConsumer> ConsumersFor(OutboxEvent e) =>
        _consumers.TryGetValue(e.Type, out var list) ? list : [];

    /// <summary>One consumer by event type and name, or <see langword="null"/> if it is no longer registered.</summary>
    public EventConsumer? ConsumerFor(string eventType, string name) =>
        _consumers.TryGetValue(eventType, out var list) ? list.Find(c => c.Name == name)?.Consumer : null;

    /// <summary>The handler for a job kind, or <see langword="null"/> if none is registered.</summary>
    public JobHandler? HandlerFor(string kind) => _handlers.GetValueOrDefault(kind);

    /// <summary>Every queue some handler registered; the job loop claims from these.</summary>
    public string[] Queues => [.. _queues];

    /// <summary>Every schedule, in registration order.</summary>
    public IReadOnlyList<InternalSchedule> Schedules => _schedules;

    /// <summary>
    /// Checks the jobs a consumer returned, in memory, before they reach the database. Returns why the first
    /// invalid job is invalid, or null when every job is valid. Catches the common mistakes with a clear error;
    /// it does not copy every Postgres rule (the dispatcher's savepoint catches the rest). The message never
    /// includes the payload.
    /// </summary>
    public string? ValidateJobs(IReadOnlyList<NewJob> jobs)
    {
        foreach (var job in jobs)
        {
            if (job is null) return "A consumer returned a null job.";
            if (job.Kind is null || !_handlers.ContainsKey(job.Kind))
                return $"Job kind '{job.Kind}' has no registered handler.";
            if (job.Queue is null || !_queues.Contains(job.Queue))
                return $"Job kind '{job.Kind}' uses queue '{job.Queue}', which no handler registered, so no job loop would claim it.";
            if (job.MaxAttempts < 1)
                return $"Job kind '{job.Kind}' has MaxAttempts {job.MaxAttempts}; it must be at least 1.";
            // Npgsql refuses to encode a lone surrogate before anything reaches Postgres, which would
            // otherwise look like a transient failure and stall the outbox.
            if (!IsValidUnicode(job.Kind) || !IsValidUnicode(job.Queue) || !IsValidUnicode(job.ProjectId) || !IsValidUnicode(job.PayloadJson))
                return $"Job kind '{job.Kind}' has text that is not valid Unicode.";
            if (!IsJson(job.PayloadJson))
                return $"The payload of job kind '{job.Kind}' is not valid JSON.";
        }

        return null;
    }

    private static bool IsJson(string? text)
    {
        if (text is null) return false;
        try
        {
            using var _ = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = int.MaxValue });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidUnicode(string? text)
    {
        if (text is null) return true;
        try
        {
            StrictUtf8.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^[a-z0-9_.]{1,100}\z")]
    private static partial Regex ConsumerName();
}

/// <summary>The realtime role's registry. Empty until the realtime protocol lands (scope row 22).</summary>
public sealed class RealtimeRegistry : IRealtimeRegistry;
