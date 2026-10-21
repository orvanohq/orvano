using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.Host.IntegrationTests.Jobs;
using Orvano.Host.IntegrationTests.Signals;
using Orvano.Infrastructure.Jobs.Data;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Outbox;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Outbox;

/// <summary>What a <see cref="SampleNoteAdded"/> subscriber does, by the event's <c>Text</c>.</summary>
internal static class NoteMode
{
    public const string Ok = "";
    public const string Throw = "throw";
    public const string Permanent = "permanent";

    /// <summary>Publishes a <see cref="SampleNoteChained"/> for the same note, in a transaction of its own.</summary>
    public const string Chain = "chain";
}

/// <summary>One delivery a subscriber ran: what it saw in its scope.</summary>
internal sealed record Delivery(
    string Subscriber,
    Guid NoteId,
    EventContext Context,
    CallerKind Caller,
    Guid? Project,
    string? ProjectError,
    ActivityContext[] Links);

/// <summary>Shared by a test and its hosts: every delivery, in the order the handlers ran.</summary>
internal sealed class OutboxProbes
{
    public const string Recorder = "sample.note_added.record";
    public const string Second = "sample.note_added.second";
    public const string Chained = "sample.note_chained.record";

    public ConcurrentQueue<Delivery> Deliveries { get; } = new();

    public IReadOnlyList<Delivery> Of(string subscriber) => [.. Deliveries.Where(delivery => delivery.Subscriber == subscriber)];

    public Task DeliveredAsync(string subscriber, Guid noteId, TimeSpan within, CancellationToken cancellationToken) =>
        Eventually.TrueAsync(
            () => Deliveries.Any(delivery => delivery.Subscriber == subscriber && delivery.NoteId == noteId),
            within,
            $"{subscriber} never received note {noteId}",
            cancellationToken);

    public void Record(string subscriber, Guid noteId, EventContext context, ICallerContext caller, IProjectContext project)
    {
        Guid? projectId = null;
        string? projectError = null;
        try
        {
            projectId = project.Id;
        }
        catch (InvalidOperationException ex)
        {
            projectError = ex.Message;
        }

        ActivityContext[] links = [.. Activity.Current?.Links.Select(link => link.Context) ?? []];
        Deliveries.Enqueue(new Delivery(subscriber, noteId, context, caller.Current.Kind, projectId, projectError, links));
    }
}

/// <summary>The first subscriber of <see cref="SampleNoteAdded"/>; it follows <see cref="NoteMode"/>.</summary>
internal sealed class NoteRecorder(OutboxProbes probes, ICallerContext caller, IProjectContext project, IOutbox outbox, IServiceProvider services)
    : IEventHandler<SampleNoteAdded>
{
    public async Task HandleAsync(SampleNoteAdded @event, EventContext context, CancellationToken cancellationToken)
    {
        probes.Record(OutboxProbes.Recorder, @event.NoteId, context, caller, project);
        switch (@event.Text)
        {
            case NoteMode.Throw:
                throw new InvalidOperationException("boom");
            case NoteMode.Permanent:
                throw new PermanentJobFailureException("retrying will not help");
            case NoteMode.Chain:
                await using (var db = SampleOutboxDbContext.On(services))
                {
                    await db.InTransactionAsync(
                        async token =>
                        {
                            outbox.Publish(db, new SampleNoteChained(@event.NoteId), context.ProjectId);
                            await db.SaveChangesAsync(token);
                        },
                        cancellationToken);
                }

                break;
            default:
                break;
        }
    }
}

/// <summary>A second subscriber of <see cref="SampleNoteAdded"/>, which always succeeds.</summary>
internal sealed class SecondNoteRecorder(OutboxProbes probes, ICallerContext caller, IProjectContext project) : IEventHandler<SampleNoteAdded>
{
    public Task HandleAsync(SampleNoteAdded @event, EventContext context, CancellationToken cancellationToken)
    {
        probes.Record(OutboxProbes.Second, @event.NoteId, context, caller, project);
        return Task.CompletedTask;
    }
}

/// <summary>The subscriber of <see cref="SampleNoteChained"/>.</summary>
internal sealed class ChainedRecorder(OutboxProbes probes, ICallerContext caller, IProjectContext project) : IEventHandler<SampleNoteChained>
{
    public Task HandleAsync(SampleNoteChained @event, EventContext context, CancellationToken cancellationToken)
    {
        probes.Record(OutboxProbes.Chained, @event.NoteId, context, caller, project);
        return Task.CompletedTask;
    }
}

/// <summary>Builds hosts with the sample outbox, its three subscribers, and the job probe kinds.</summary>
internal static class OutboxHosts
{
    /// <summary>Two attempts and a one minute lease, so a throwing delivery dies after its second attempt.</summary>
    public static void RecorderOptions(JobKindOptions options)
    {
        options.MaxAttempts = 2;
        options.Lease = TimeSpan.FromMinutes(1);
    }

    public static void AddSampleSubscribers(this IServiceCollection services, OutboxProbes probes)
    {
        services.AddSingleton(probes);
        services.AddSampleOutbox();
        services.AddEventHandler<SampleNoteAdded, NoteRecorder>(OutboxProbes.Recorder, RecorderOptions);
        services.AddEventHandler<SampleNoteAdded, SecondNoteRecorder>(OutboxProbes.Second);
        services.AddEventHandler<SampleNoteChained, ChainedRecorder>(OutboxProbes.Chained);
    }

    /// <summary>A host with the sample outbox and subscribers, <paramref name="clock"/> as its clock, and the given roles.</summary>
    public static OrvanoApiFactory Create(
        Dictionary<string, string> settings,
        OutboxProbes probes,
        TimeProvider? clock,
        string roles,
        Action<IServiceCollection>? configure = null) =>
        JobHosts.Create(settings, new JobProbes(), clock, roles, services =>
        {
            services.AddSampleSubscribers(probes);
            configure?.Invoke(services);
        });

    /// <summary>Publishes one <see cref="SampleNoteAdded"/> with a note, committed, and returns the event's id.</summary>
    public static async Task<Guid> PublishAsync(
        IServiceProvider host, SampleNoteAdded @event, Guid? projectId, CancellationToken cancellationToken)
    {
        IOutbox outbox = host.GetRequiredService<IOutbox>();
        await using var db = SampleOutboxDbContext.On(host);
        return await db.InTransactionAsync(
            async token =>
            {
                db.Notes.Add(new SampleNote { Id = @event.NoteId, Text = @event.Text });
                Guid id = outbox.Publish(db, @event, projectId);
                await db.SaveChangesAsync(token);
                return id;
            },
            cancellationToken);
    }

    /// <summary>Publishes <paramref name="count"/> events in one transaction, in order, and returns their ids.</summary>
    public static async Task<Guid[]> PublishManyAsync(
        IServiceProvider host, int count, Func<int, SampleNoteAdded> make, Guid? projectId, CancellationToken cancellationToken)
    {
        IOutbox outbox = host.GetRequiredService<IOutbox>();
        await using var db = SampleOutboxDbContext.On(host);
        return await db.InTransactionAsync(
            async token =>
            {
                Guid[] ids = [.. Enumerable.Range(0, count).Select(i => outbox.Publish(db, make(i), projectId))];
                await db.SaveChangesAsync(token);
                return ids;
            },
            cancellationToken);
    }
}

/// <summary>Reads and writes <c>sample.outbox</c> rows directly, as an operator or another process would.</summary>
internal sealed class OutboxRows(NpgsqlDataSource dataSource)
{
    public sealed record Row(Guid Id, string Type, string Payload, Guid? ProjectId, DateTimeOffset OccurredAt, DateTimeOffset? DispatchedAt, string? TraceParent);

    public async Task<IReadOnlyList<Row>> WhereAsync(string where, CancellationToken cancellationToken, params object[] values)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            $"SELECT id, type, payload::text, project_id, occurred_at, dispatched_at, trace_parent FROM sample.outbox WHERE {where} ORDER BY id");
        foreach (object value in values)
        {
            command.Parameters.AddWithValue(value);
        }

        var rows = new List<Row>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Row(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rows;
    }

    public async Task<long> CountAsync(string where, CancellationToken cancellationToken, params object[] values)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand($"SELECT count(*) FROM sample.outbox WHERE {where}");
        foreach (object value in values)
        {
            command.Parameters.AddWithValue(value);
        }

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Inserts a waiting row as written, for types the outbox would refuse.</summary>
    public async Task<Guid> InsertAsync(string type, string payload, DateTimeOffset occurredAt, CancellationToken cancellationToken, DateTimeOffset? dispatchedAt = null)
    {
        var id = Guid.CreateVersion7(occurredAt);
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "INSERT INTO sample.outbox (id, type, payload, occurred_at, dispatched_at) VALUES ($1, $2, $3::jsonb, $4, $5)");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(type);
        command.Parameters.AddWithValue(payload);
        command.Parameters.AddWithValue(occurredAt);
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset?> { TypedValue = dispatchedAt });
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }
}

/// <summary>
/// Watches the dispatcher's commands on <c>JobsDbContext</c>: records each outbox select's batch size, and can block the
/// select, or throw on the sample outbox's mark or on the delivery insert of one event, as a failing database would.
/// </summary>
internal sealed class DispatchInterceptor : DbCommandInterceptor
{
    private readonly Lock _lock = new();
    private volatile bool _failMark;
    private volatile bool _failAsideMark;
    private Guid? _failEvent;

    /// <summary>Each outbox select's <c>LIMIT</c>, in the order the selects ran.</summary>
    public ConcurrentQueue<int> Sizes { get; } = new();

    /// <summary>The UTF-8 size of each delivery job's payload as the dispatcher sent it, before <c>jsonb</c> stores it.</summary>
    public ConcurrentQueue<int> DeliveryBytes { get; } = new();

    /// <summary>Set by the first outbox select while <see cref="Hold"/> is set, which then waits for <see cref="Release"/>.</summary>
    public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Hold { get; set; }

    /// <summary>With <see cref="Hold"/>, the select waits for <see cref="Release"/> whatever its token says, as a stalled database would.</summary>
    public bool IgnoreCancel { get; set; }

    public bool FailMark
    {
        get => _failMark;
        set => _failMark = value;
    }

    /// <summary>Throws on the <c>aside</c> outbox's mark, as <see cref="FailMark"/> does on the sample one's.</summary>
    public bool FailAsideMark
    {
        get => _failAsideMark;
        set => _failAsideMark = value;
    }

    /// <summary>The event whose delivery insert throws, or none.</summary>
    public Guid? FailEvent
    {
        get
        {
            lock (_lock)
            {
                return _failEvent;
            }
        }

        set
        {
            lock (_lock)
            {
                _failEvent = value;
            }
        }
    }

    public static void AddTo(IServiceCollection services, DispatchInterceptor interceptor) =>
        services.ConfigureDbContext<JobsDbContext>(options => options.AddInterceptors(interceptor));

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("\"sample\".outbox", StringComparison.Ordinal)
            && command.CommandText.Contains("FOR UPDATE SKIP LOCKED", StringComparison.Ordinal))
        {
            Sizes.Enqueue(Convert.ToInt32(command.Parameters[0].Value, System.Globalization.CultureInfo.InvariantCulture));
            if (Hold)
            {
                Holding.TrySetResult();
                await (IgnoreCancel ? Release.Task : Release.Task.WaitAsync(cancellationToken));
            }
        }

        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("INSERT INTO jobs.job", StringComparison.Ordinal))
        {
            foreach (DbParameter parameter in command.Parameters)
            {
                if (parameter.Value is string envelope && envelope.StartsWith("{\"eventId\":", StringComparison.Ordinal))
                {
                    DeliveryBytes.Enqueue(System.Text.Encoding.UTF8.GetByteCount(envelope));
                }
            }
        }

        if (FailMark && command.CommandText.Contains("UPDATE \"sample\".outbox SET dispatched_at", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("the mark fails");
        }

        if (FailAsideMark && command.CommandText.Contains("UPDATE \"aside\".outbox SET dispatched_at", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("the aside mark fails");
        }

        if (FailEvent is { } failing && command.CommandText.Contains("INSERT INTO jobs.job", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is string key && key.StartsWith(failing.ToString("D"), StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("the delivery insert fails");
        }

        return ValueTask.FromResult(result);
    }
}

internal static class OutboxTestDatabase
{
    public static OutboxRows OutboxRows(this TestDatabase database) => new(database.DataSource);
}
