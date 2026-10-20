using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.Host.IntegrationTests.Signals;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Jobs;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Jobs;

/// <summary>What a probe job does when it runs, by <see cref="IProbeJob.Mode"/>.</summary>
internal static class ProbeMode
{
    public const string Ok = "ok";
    public const string Throw = "throw";
    public const string Permanent = "permanent";
    public const string OwnCancel = "own-cancel";

    /// <summary>Waits for <see cref="JobProbes.Release"/>, and stops when its token cancels.</summary>
    public const string Block = "block";

    /// <summary>Waits for <see cref="JobProbes.Release"/> whatever its token says.</summary>
    public const string BlockIgnoringCancel = "block-ignore";

    /// <summary>Waits until its token cancels, then returns normally a moment later: a handler that finishes during shutdown.</summary>
    public const string FinishOnCancel = "finish-on-cancel";

    /// <summary>
    /// Registers a cancellation callback that throws, then waits for <see cref="JobProbes.Release"/> whatever its token
    /// says: a handler whose own callback misbehaves.
    /// </summary>
    public const string ThrowOnCancel = "throw-on-cancel";

    /// <summary>Takes 30 ms, for the concurrency count.</summary>
    public const string Slow = "slow";

    /// <summary>
    /// Opens a platform pool connection, then waits for <see cref="JobProbes.Release"/> holding it, as a handler mid query
    /// would (spec 0013 JOP-4).
    /// </summary>
    public const string HoldConnection = "hold-connection";
}

internal interface IProbeJob
{
    string Mode { get; }

    string Marker { get; }
}

/// <summary>A job with default kind options.</summary>
internal sealed record ProbeJob(string Mode, string Marker = "") : IJob, IProbeJob
{
    public static string Kind => "test.probe";
}

/// <summary>A one minute lease, a one second timeout, and two attempts.</summary>
internal sealed record ShortJob(string Mode, string Marker = "") : IJob, IProbeJob
{
    public static string Kind => "test.short";

    public static void Options(JobKindOptions options)
    {
        options.Lease = TimeSpan.FromMinutes(1);
        options.Timeout = TimeSpan.FromSeconds(1);
        options.MaxAttempts = 2;
    }
}

/// <summary>A one minute lease (renewed every 20 seconds) and two attempts.</summary>
internal sealed record LeaseJob(string Mode, string Marker = "") : IJob, IProbeJob
{
    public static string Kind => "test.lease";

    public static void Options(JobKindOptions options)
    {
        options.Lease = TimeSpan.FromMinutes(1);
        options.MaxAttempts = 2;
    }
}

/// <summary>A kind whose jobs all run for a project (RequiresProject).</summary>
internal sealed record ProjectJob(string Mode, string Marker = "") : IJob, IProbeJob
{
    public static string Kind => "test.project";
}

/// <summary>A <c>Timeout</c> of <see cref="TimeSpan.MaxValue"/>, past what a timer can hold: no deadline.</summary>
internal sealed record UntimedJob(string Mode, string Marker = "") : IJob, IProbeJob
{
    public static string Kind => "test.untimed";

    public static void Options(JobKindOptions options) => options.Timeout = TimeSpan.MaxValue;
}

/// <summary>A kind whose payload reader throws something other than a <see cref="JsonException"/>.</summary>
internal sealed record UnreadableJob([property: JsonConverter(typeof(RefusingConverter))] string Mode, string Marker = "") : IJob, IProbeJob
{
    public static string Kind => "test.unreadable";
}

/// <summary>Writes a string, and refuses to read one back.</summary>
internal sealed class RefusingConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("this reader refuses");

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

[JsonSerializable(typeof(ProbeJob))]
[JsonSerializable(typeof(UntimedJob))]
[JsonSerializable(typeof(UnreadableJob))]
[JsonSerializable(typeof(ShortJob))]
[JsonSerializable(typeof(ProjectJob))]
[JsonSerializable(typeof(LeaseJob))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;

/// <summary>One handler run: what the handler saw in its scope.</summary>
internal sealed record ProbeRun(string Kind, Guid Id, int Attempt, string Mode, CallerKind Caller, Guid? Project, string? ProjectError);

/// <summary>Shared by a test and its hosts: what ran, how many ran at once, and the gate blocking handlers wait on.</summary>
internal sealed class JobProbes
{
    private int _running;
    private int _maxRunning;

    public ConcurrentQueue<ProbeRun> Runs { get; } = new();

    /// <summary>Ids of handlers that started, in order; a blocking handler is here before it waits.</summary>
    public ConcurrentQueue<Guid> Started { get; } = new();

    /// <summary>Ids of <see cref="ProbeMode.HoldConnection"/> handlers holding their platform connection open.</summary>
    public ConcurrentQueue<Guid> Holding { get; } = new();

    /// <summary>Handlers whose token was cancelled while they waited.</summary>
    public ConcurrentQueue<Guid> Cancelled { get; } = new();

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int MaxRunning => Volatile.Read(ref _maxRunning);

    public int Count(string mode) => Runs.Count(run => run.Mode == mode);

    public Task StartedAsync(Guid id, CancellationToken cancellationToken) =>
        Eventually.TrueAsync(() => Started.Contains(id), TimeSpan.FromSeconds(10), $"job {id} never started", cancellationToken);

    public void Enter()
    {
        int now = Interlocked.Increment(ref _running);
        int max;
        while (now > (max = Volatile.Read(ref _maxRunning)) && Interlocked.CompareExchange(ref _maxRunning, now, max) != max)
        {
        }
    }

    public void Leave() => Interlocked.Decrement(ref _running);
}

internal sealed class ProbeHandler<TJob>(JobProbes probes, ICallerContext caller, IProjectContext project, NpgsqlDataSource platform)
    : IJobHandler<TJob>
    where TJob : IJob, IProbeJob
{
    public async Task HandleAsync(TJob job, JobContext context, CancellationToken cancellationToken)
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

        // Registered before the handler counts as started, so a test that waits for the start finds it in place.
        using CancellationTokenRegistration throwing = job.Mode == ProbeMode.ThrowOnCancel
            ? cancellationToken.Register(() => throw new InvalidOperationException("callback boom"))
            : default;
        probes.Runs.Enqueue(new ProbeRun(context.Kind, context.Id, context.Attempt, job.Mode, caller.Current.Kind, projectId, projectError));
        probes.Started.Enqueue(context.Id);
        probes.Enter();
        try
        {
            switch (job.Mode)
            {
                case ProbeMode.Throw:
                    throw new InvalidOperationException("boom");
                case ProbeMode.Permanent:
                    throw new PermanentJobFailureException("retrying will not help");
                case ProbeMode.OwnCancel:
                    throw new OperationCanceledException("the handler's own timeout");
                case ProbeMode.Block:
                    try
                    {
                        await probes.Release.Task.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        probes.Cancelled.Enqueue(context.Id);
                        throw;
                    }

                    break;
                case ProbeMode.BlockIgnoringCancel:
                    await probes.Release.Task;
                    break;
                case ProbeMode.ThrowOnCancel:
                    await probes.Release.Task;
                    break;
                case ProbeMode.FinishOnCancel:
                    await Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => { }, TaskScheduler.Default);
                    probes.Cancelled.Enqueue(context.Id);
                    await Task.Delay(100, CancellationToken.None);
                    break;
                case ProbeMode.Slow:
                    await Task.Delay(30, CancellationToken.None);
                    break;
                case ProbeMode.HoldConnection:
                    await using (NpgsqlConnection held = await platform.OpenConnectionAsync(cancellationToken))
                    {
                        probes.Holding.Enqueue(context.Id);
                        await probes.Release.Task.WaitAsync(cancellationToken);
                    }

                    break;
                default:
                    break;
            }
        }
        finally
        {
            probes.Leave();
        }
    }
}

/// <summary>Builds hosts that register the probe kinds, on a test's own migrated database.</summary>
internal static class JobHosts
{
    public static void AddProbeJobs(this IServiceCollection services, JobProbes probes)
    {
        services.AddSingleton(probes);
        services.AddJobHandler<ProbeJob, ProbeHandler<ProbeJob>>(ProbeJsonContext.Default.ProbeJob);
        services.AddJobHandler<ShortJob, ProbeHandler<ShortJob>>(ProbeJsonContext.Default.ShortJob, ShortJob.Options);
        services.AddJobHandler<LeaseJob, ProbeHandler<LeaseJob>>(ProbeJsonContext.Default.LeaseJob, LeaseJob.Options);
        services.AddJobHandler<UntimedJob, ProbeHandler<UntimedJob>>(ProbeJsonContext.Default.UntimedJob, UntimedJob.Options);
        services.AddJobHandler<UnreadableJob, ProbeHandler<UnreadableJob>>(ProbeJsonContext.Default.UnreadableJob);
        services.AddJobHandler<ProjectJob, ProbeHandler<ProjectJob>>(
            ProbeJsonContext.Default.ProjectJob, options => options.RequiresProject = true);
    }

    /// <summary>A host with the probe kinds, <paramref name="clock"/> as its clock, and the given roles.</summary>
    public static OrvanoApiFactory Create(
        Dictionary<string, string> settings,
        JobProbes probes,
        TimeProvider? clock,
        string roles,
        Action<IServiceCollection>? configure = null)
    {
        var hostSettings = new Dictionary<string, string>(settings) { ["ROLES"] = roles };
        return new OrvanoApiFactory(hostSettings, services: services =>
        {
            services.AddProbeJobs(probes);
            if (clock is not null)
            {
                services.AddSingleton(clock);
            }

            configure?.Invoke(services);
        });
    }
}

/// <summary>Reads and writes <c>jobs.job</c> rows directly, as an operator or another process would.</summary>
internal sealed class JobRows(NpgsqlDataSource dataSource)
{
    public sealed record Row(
        Guid Id,
        string Kind,
        string Status,
        int Attempt,
        int MaxAttempts,
        DateTimeOffset RunAt,
        DateTimeOffset? LockedUntil,
        string? LockedBy,
        string? LastError,
        string? DedupeKey,
        string? TraceParent,
        DateTimeOffset? FinishedAt,
        Guid? ProjectId);

    public async Task<Row?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await WhereAsync("id = $1", cancellationToken, id)).SingleOrDefault();

    public async Task<IReadOnlyList<Row>> WhereAsync(string where, CancellationToken cancellationToken, params object[] values)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "SELECT id, kind, status, attempt, max_attempts, run_at, locked_until, locked_by, last_error, dedupe_key, trace_parent, "
            + $"finished_at, project_id FROM jobs.job WHERE {where} ORDER BY id");
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
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12)));
        }

        return rows;
    }

    public async Task<long> CountAsync(string where, CancellationToken cancellationToken, params object[] values)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand($"SELECT count(*) FROM jobs.job WHERE {where}");
        foreach (object value in values)
        {
            command.Parameters.AddWithValue(value);
        }

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Inserts a queued row as written, for kinds or payloads the queue would refuse.</summary>
    public async Task<Guid> InsertQueuedAsync(
        string kind, string payload, DateTimeOffset runAt, CancellationToken cancellationToken, Guid? projectId = null, int maxAttempts = 10)
    {
        var id = Guid.CreateVersion7();
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "INSERT INTO jobs.job (id, kind, payload, project_id, status, attempt, max_attempts, run_at, created_at) "
            + "VALUES ($1, $2, $3::jsonb, $4, 'queued', 0, $5, $6, $6)");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(kind);
        command.Parameters.AddWithValue(payload);
        command.Parameters.Add(new NpgsqlParameter<Guid?> { TypedValue = projectId });
        command.Parameters.AddWithValue(maxAttempts);
        command.Parameters.AddWithValue(runAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }
}

internal static class JobTestDatabase
{
    public static JobRows Rows(this TestDatabase database) => new(database.DataSource);
}

/// <summary>
/// Counts the runner's dead marking and claim statements on one test database, from Npgsql's command activities. Each
/// runs in a transaction of its own (spec 0013 CLM-1), so the counts are transactions too.
/// </summary>
internal sealed class JobStatements : IDisposable
{
    private readonly ActivityListener _listener;
    private int _claims;
    private int _deadMarkings;

    public JobStatements(string database)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                string[] values = [.. activity.TagObjects.Select(tag => tag.Value as string ?? string.Empty)];
                if (!values.Contains(database, StringComparer.Ordinal))
                {
                    return;
                }

                if (values.Any(value => value.Contains("lease expired on the last attempt", StringComparison.Ordinal)))
                {
                    Interlocked.Increment(ref _deadMarkings);
                }
                else if (values.Any(value => value.Contains("UPDATE jobs.job AS j", StringComparison.Ordinal)))
                {
                    Interlocked.Increment(ref _claims);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public int Claims => Volatile.Read(ref _claims);

    public int DeadMarkings => Volatile.Read(ref _deadMarkings);

    public void Dispose() => _listener.Dispose();
}
