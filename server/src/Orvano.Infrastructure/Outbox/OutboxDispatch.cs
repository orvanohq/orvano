using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Orvano.Infrastructure.Jobs;
using Orvano.Infrastructure.Jobs.Data;
using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Outbox;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Outbox;

/// <summary>
/// Moves committed events into one delivery job per subscriber (spec 0013 DSP-1 to DSP-8, DLV-1 to DLV-4). The
/// <c>OutboxDispatcher</c> loop calls <see cref="DispatchAsync"/> on every worker wake; <c>JobsDriver</c> calls it inline
/// in tests, on any role. It holds each outbox's current batch size, in memory, per process (DSP-5).
/// </summary>
internal sealed class OutboxDispatch
{
    /// <summary>The batch size of an outbox whose last batch did not fail (spec 0007 EVT-3).</summary>
    public const int FullBatch = 100;

    /// <summary>
    /// The bound of one batch's work, so a stalled database cannot hold the loop, or the shutdown behind it (spec 0002
    /// ASY-7). A batch it cuts off rolls back whole; the rollback itself is bounded only by the command timeout (DSP-5).
    /// </summary>
    public static readonly TimeSpan BatchBound = TimeSpan.FromSeconds(10);

    private readonly IOutboxRegistry _registry;
    private readonly JobRegistry _jobs;
    private readonly JobQueue _queue;
    private readonly SignalBus _signals;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly InfrastructureTelemetry _telemetry;
    private readonly ILogger<OutboxDispatch> _logger;
    private readonly FrozenDictionary<string, OutboxSql> _sql;
    private readonly ConcurrentDictionary<string, int> _sizes = new(StringComparer.Ordinal);

    public OutboxDispatch(
        IOutboxRegistry registry,
        JobRegistry jobs,
        JobQueue queue,
        SignalBus signals,
        IServiceScopeFactory scopes,
        TimeProvider time,
        InfrastructureTelemetry telemetry,
        ILogger<OutboxDispatch> logger)
    {
        _registry = registry;
        _jobs = jobs;
        _queue = queue;
        _signals = signals;
        _scopes = scopes;
        _time = time;
        _telemetry = telemetry;
        _logger = logger;
        _sql = registry.Outboxes.ToFrozenDictionary(outbox => outbox.Schema, outbox => OutboxSql.For(outbox), StringComparer.Ordinal);
    }

    /// <summary>The size of <paramref name="schema"/>'s next batch: 100, or less after a failed or cut batch (DSP-5).</summary>
    public int BatchSizeOf(string schema) => _sizes.GetValueOrDefault(schema, FullBatch);

    /// <summary>
    /// DSP-2: one pass. A batch per outbox per round, in schema order, then another round with only the outboxes whose
    /// batch was full, until none is. An outbox whose batch fails sits out the rest of the pass. Returns the events
    /// marked dispatched. An outbox that registers no event type, or no outbox at all, sends no query (DSP-4). Each failed batch, already logged, is also added
    /// to <paramref name="failures"/> when given, naming its outbox, so <c>JobsDriver</c> can surface it to a test.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="stoppingToken"/> was cancelled; the batch in flight rolled back.</exception>
    public async Task<int> DispatchAsync(CancellationToken stoppingToken, ICollection<Exception>? failures = null)
    {
        int marked = 0;
        IReadOnlyList<OutboxRegistration> round = _registry.Outboxes;
        while (round.Count > 0)
        {
            var full = new List<OutboxRegistration>();
            foreach (OutboxRegistration outbox in round)
            {
                OutboxSql sql = _sql[outbox.Schema];
                if (!sql.HasTypes)
                {
                    continue; // DSP-4: it registers no type, so nothing in it is this process's to dispatch
                }

                int size = BatchSizeOf(outbox.Schema);
                if (await TryBatchAsync(sql, size, failures, stoppingToken) is not { } selected)
                {
                    continue;
                }

                marked += selected;
                if (selected == size)
                {
                    full.Add(outbox);
                }
            }

            round = full;
        }

        return marked;
    }

    /// <summary>
    /// DSP-5: one batch under <see cref="BatchBound"/>, linked to the stopping token. Returns the rows it selected and
    /// marked, or <see langword="null"/> when it failed or was cut off, which halves the outbox's next batch. A commit
    /// with an unknown outcome or a shutdown leaves the size as it was.
    /// </summary>
    private async Task<int?> TryBatchAsync(OutboxSql sql, int size, ICollection<Exception>? failures, CancellationToken stoppingToken)
    {
        var commit = new CommitWatch();
        try
        {
            using var bound = new CancellationTokenSource(BatchBound, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, bound.Token);
            int selected = await BatchAsync(sql, size, commit, linked.Token);
            _sizes[sql.Schema] = FullBatch;
            return selected;
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            // DSP-6: the batch rolled back, and nothing has to be released; not logged while the host stops.
            throw new OperationCanceledException(stoppingToken);
        }
        catch (Exception ex) // spec 0013 DSP-5: the loop never ends while the host runs, and no event is lost
        {
            // EF has already logged a failed command or commit at Error with its exception, so it is not repeated here.
            _logger.OutboxBatchFailed(sql.Schema, ex is DbException ? null : ex);
            failures?.Add(new InvalidOperationException($"Dispatching the {sql.Schema} outbox failed (spec 0013 DSP-5).", ex));
            if (!commit.Reached)
            {
                _sizes[sql.Schema] = Math.Max(1, size / 2);
            }

            return null;
        }
    }

    /// <summary>
    /// DSP-3: one transaction on a new <see cref="JobsDbContext"/>, so the row locks, the deliveries, and the mark commit
    /// or roll back together (spec 0007 EVT-3).
    /// </summary>
    private async Task<int> BatchAsync(OutboxSql sql, int size, CommitWatch commit, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        JobsDbContext db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        return await db.InTransactionAsync(
            async token =>
            {
                // A retry of the execution strategy starts over, commit included.
                commit.Reached = false;

                // Deviation DAT-6: identifier from a registered outbox schema (spec 0013 DSP-8)
                List<OutboxRow> rows = await db.Database.SqlQuery<OutboxRow>(sql.Select(size)).ToListAsync(token);
                if (rows.Count == 0)
                {
                    return 0;
                }

                using Activity? activity = InfrastructureTelemetry.ActivitySource.StartActivity("outbox dispatch " + sql.Schema, ActivityKind.Internal);
                activity?.SetTag("orvano.outbox.schema", sql.Schema);
                activity?.SetTag("orvano.outbox.events", rows.Count);

                DateTimeOffset now = _time.GetUtcNow();
                var dispatched = new List<(string Type, DateTimeOffset OccurredAt)>(rows.Count);
                int jobs = 0;

                // In the order the database returned them: Guid.CompareTo does not order uuids as PostgreSQL does.
                foreach (OutboxRow row in rows)
                {
                    // DSP-4: the select takes only registered types, so this holds; if it ever did not, the batch fails
                    // and marks nothing rather than drop the event.
                    OutboxEventRegistration registration = _registry.FindEvent(row.Type)
                        ?? throw new InvalidOperationException($"The {sql.Schema} outbox selected the unregistered event type {row.Type} (spec 0013 DSP-4).");

                    dispatched.Add((registration.Type, row.OccurredAt));
                    IReadOnlyList<EventSubscription> subscribers = _registry.SubscribersOf(registration.Type);
                    if (subscribers.Count == 0)
                    {
                        continue;
                    }

                    string envelope = Envelope(row); // throws past ENQ-3's limit, which fails the batch (DSP-5)
                    foreach (EventSubscription subscriber in subscribers)
                    {
                        // A subscriber with no kind cannot pass startup (OBX-7, SCH-1).
                        JobRegistration kind = _jobs.Find(subscriber.Name)
                            ?? throw new InvalidOperationException($"The event subscriber {subscriber.Name} has no job kind (spec 0013 DLV-1).");
                        var options = new EnqueueOptions
                        {
                            ProjectId = row.ProjectId,
                            DedupeKey = DedupeKey(row.Id, subscriber.Name),
                            RunAt = now,
                        };
                        await _queue.InsertAsync(db, kind, envelope, options, row.TraceParent, token);
                        jobs++;
                    }
                }

                // Deviation DAT-6: identifier from a registered outbox schema (spec 0013 DSP-8)
                await db.Database.ExecuteSqlAsync(sql.MarkDispatched(now, [.. rows.Select(row => row.Id)]), token);

                // DSP-7: the commit wakes every dispatcher also when it enqueued nothing (ENQ-4 enlists only for a
                // delivery), so a batch cut short by another dispatcher's locks is followed by a pass, not by the poll.
                _signals.Add(db, SignalKind.Job, []);

                TransactionSignals.AddAfterCommit(db, _ =>
                {
                    commit.Reached = true;
                    return ValueTask.CompletedTask;
                });
                TransactionSignals.AddOnCommitted(db, _ =>
                {
                    // OTL-1: confirmed commits only, the lag measured at the commit.
                    DateTimeOffset committed = _time.GetUtcNow();
                    foreach ((string type, DateTimeOffset occurredAt) in dispatched)
                    {
                        _telemetry.EventDispatched(type);
                        _telemetry.EventDispatchLag(sql.Schema, committed - occurredAt);
                    }

                    _logger.OutboxBatchDispatched(sql.Schema, rows.Count, jobs);
                    return ValueTask.CompletedTask;
                });
                return rows.Count;
            },
            cancellationToken);
    }

    /// <summary>DLV-3: the id in <c>D</c> format and the subscriber, at most 137 characters.</summary>
    internal static string DedupeKey(Guid eventId, string subscriber) => eventId.ToString("D") + ":" + subscriber;

    /// <summary>
    /// DLV-2: the delivery job's payload, a copy of the event with the row's payload as <c>data</c>:
    /// <c>{"eventId":…,"type":…,"occurredAt":…,"data":{…}}</c>, which the subscriber's kind reads with SharedKernel's
    /// <c>OutboxJsonContext</c>. Written here rather than through that internal context, because SharedKernel shows its
    /// internals only to test projects (spec 0012 RSV-1); a test pins that the kind reads what this writes. The payload
    /// is compacted first: <c>jsonb</c> prints a space after every <c>:</c> and <c>,</c>, which would take a payload
    /// within PUB-2's 64,000 bytes past ENQ-3's 65,536.
    /// </summary>
    /// <exception cref="InvalidOperationException">The envelope is over ENQ-3's limit, which only numbers that <c>jsonb</c>
    /// prints in full (such as <c>1E+300</c>) can cause.</exception>
    internal static string Envelope(OutboxRow row)
    {
        byte[] data = Encoding.UTF8.GetBytes(row.Payload);
        int length = Compact(data);
        var buffer = new ArrayBufferWriter<byte>(length + 256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("eventId", row.Id);
            writer.WriteString("type", row.Type);
            writer.WriteString("occurredAt", row.OccurredAt);
            writer.WritePropertyName("data");

            // Validated although jsonb already is JSON, so a mistake in Compact can never reach a subscriber.
            writer.WriteRawValue(data.AsSpan(0, length));
            writer.WriteEndObject();
        }

        if (buffer.WrittenCount > JobRegistry.MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"The delivery of event {row.Id:D} is {buffer.WrittenCount} bytes, over the job payload limit of {JobRegistry.MaxPayloadBytes} (spec 0013 ENQ-3).");
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Removes the whitespace outside strings from <paramref name="json"/> in place and returns the new length. Strings
    /// stay byte for byte; a UTF-8 continuation byte is never an ASCII quote, backslash, or space.
    /// </summary>
    internal static int Compact(Span<byte> json)
    {
        int length = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = 0; i < json.Length; i++)
        {
            byte b = json[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (b == (byte)'\\')
                {
                    escaped = true;
                }
                else if (b == (byte)'"')
                {
                    inString = false;
                }
            }
            else if (b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r')
            {
                continue;
            }
            else if (b == (byte)'"')
            {
                inString = true;
            }

            json[length++] = b;
        }

        return length;
    }

    /// <summary>
    /// Whether the batch reached its commit: set by an after commit action, which runs for a confirmed commit and for
    /// one whose outcome is unknown (SND-3), so a failure after it leaves the batch size alone (DSP-5, DSP-6).
    /// </summary>
    private sealed class CommitWatch
    {
        public bool Reached { get; set; }
    }
}
