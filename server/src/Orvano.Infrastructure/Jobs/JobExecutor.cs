using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Infrastructure.Jobs.Data;
using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure.Jobs;

/// <summary>
/// Claims due jobs and runs each attempt to its outcome (spec 0013 CLM-1 to CLM-7, OUT-1 to OUT-8). The runner calls it
/// from its wake loop; <c>JobsDriver</c> calls it inline in tests. It holds this process's running set: a job's slot is
/// taken at the claim and freed only when its handler returns, timed out and lease lost handlers included (CLM-2).
/// </summary>
internal sealed class JobExecutor
{
    /// <summary>The kind lease of a row whose kind is not registered (CLM-1).</summary>
    public static readonly TimeSpan UnknownKindLease = TimeSpan.FromMinutes(5);

    /// <summary>The bound of an outcome or release write, which never uses the stopping token (OUT-8).</summary>
    public static readonly TimeSpan WriteBound = TimeSpan.FromSeconds(10);

    private const int MaxLastErrorLength = 2000;

    private const string MarkExpiredDeadSql = """
        UPDATE jobs.job SET status = 'dead', finished_at = @now, locked_until = NULL, locked_by = NULL,
            last_error = 'lease expired on the last attempt'
        WHERE id IN (
            SELECT id FROM jobs.job WHERE status = 'running' AND locked_until < @now AND attempt >= max_attempts
            FOR UPDATE SKIP LOCKED)
        RETURNING id, kind, attempt
        """;

    // CLM-1: due rows first, then expired leases while slots remain, each through its own partial index. PostgreSQL
    // refuses FOR UPDATE in a UNION branch, so each branch locks inside a derived table of its own; the outer LIMIT
    // stops the append before the second branch runs when the first fills it.
    internal const string ClaimSql = """
        UPDATE jobs.job AS j
        SET status = 'running', attempt = j.attempt + 1, locked_by = @worker,
            locked_until = @now + COALESCE(
              (SELECT k.lease FROM unnest(@kinds::text[], @leases::interval[]) AS k(kind, lease) WHERE k.kind = j.kind),
              interval '5 minutes')
        FROM (
          SELECT id FROM (
            SELECT id FROM (
              SELECT id FROM jobs.job WHERE status = 'queued' AND run_at <= @now AND id <> ALL(@mine)
              ORDER BY run_at, id LIMIT @free FOR UPDATE SKIP LOCKED) AS due
            UNION ALL
            SELECT id FROM (
              SELECT id FROM jobs.job WHERE status = 'running' AND locked_until < @now AND attempt < max_attempts AND id <> ALL(@mine)
              ORDER BY locked_until, id LIMIT @free FOR UPDATE SKIP LOCKED) AS expired
          ) AS both_sets LIMIT @free
        ) AS pick
        WHERE j.id = pick.id
        RETURNING j.id, j.kind, j.payload::text, j.project_id, j.attempt, j.max_attempts, j.run_at, j.created_at, j.trace_parent
        """;

    private readonly IServiceProvider _services;
    private readonly IServiceScopeFactory _scopes;
    private readonly JobRegistry _registry;
    private readonly SignalBus _signals;
    private readonly TimeProvider _time;
    private readonly InfrastructureTelemetry _telemetry;
    private readonly ILogger<JobExecutor> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _running = new();
    private readonly ConcurrentDictionary<int, DbContextOptions<JobsDbContext>> _renewalOptions = new();

    public JobExecutor(
        IServiceProvider services,
        JobRegistry registry,
        SignalBus signals,
        OrvanoInstance instance,
        TimeProvider time,
        InfrastructureTelemetry telemetry,
        ILogger<JobExecutor> logger)
    {
        _services = services;
        _scopes = services.GetRequiredService<IServiceScopeFactory>();
        _registry = registry;
        _signals = signals;
        Worker = instance.WorkerId;
        _time = time;
        _telemetry = telemetry;
        _logger = logger;
        telemetry.Meter.CreateObservableGauge(
            "orvano.infrastructure.jobs.running", () => _running.Count, "{job}", "Job handlers this process has not seen return.");
    }

    /// <summary>Runs when a slot frees, so the runner can claim again (WAK-2 (4)).</summary>
    public event Action? SlotFreed;

    /// <summary>This process's <c>locked_by</c> (CLM-2).</summary>
    public string Worker { get; }

    /// <summary>Handlers this process has not seen return, timed out and lease lost ones included (CLM-2).</summary>
    public int RunningCount => _running.Count;

    /// <summary>
    /// CLM-1: marks expired last attempts dead, then claims at most <paramref name="free"/> due jobs, never one this process
    /// is still running. Each statement runs under the execution strategy in its own short transaction. The claimed jobs
    /// hold slots until <see cref="RunAsync"/> frees them.
    /// </summary>
    public async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(int free, CancellationToken cancellationToken)
    {
        if (free <= 0)
        {
            return [];
        }

        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        JobsDbContext db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();

        await db.InTransactionAsync(async token =>
        {
            DateTimeOffset now = _time.GetUtcNow();
            await using NpgsqlCommand command = JobSql.Command(db, MarkExpiredDeadSql);
            command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
            var dead = new List<(Guid Id, string Kind, int Attempt)>();
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    dead.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2)));
                }
            }

            TransactionSignals.AddOnCommitted(db, _ =>
            {
                foreach ((Guid id, string kind, int attempt) in dead)
                {
                    _telemetry.JobAttempted(kind, JobOutcome.Dead, null);
                    _logger.JobDead(kind, id, attempt, "lease expired on the last attempt", null);
                }

                return ValueTask.CompletedTask;
            });
        }, cancellationToken);

        Guid[] mine = [.. _running.Keys];
        List<ClaimedJob> claimed = await db.InTransactionAsync(async token =>
        {
            DateTimeOffset now = _time.GetUtcNow();
            await using NpgsqlCommand command = JobSql.Command(db, ClaimSql);
            command.Parameters.Add(new NpgsqlParameter<string>("worker", Worker));
            command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
            command.Parameters.Add(new NpgsqlParameter<string[]>("kinds", _registry.Kinds));
            command.Parameters.Add(new NpgsqlParameter<TimeSpan[]>("leases", _registry.Leases));
            command.Parameters.Add(new NpgsqlParameter<Guid[]>("mine", mine));
            command.Parameters.Add(new NpgsqlParameter<int>("free", free));
            var rows = new List<ClaimedJob>();
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                string kind = reader.GetString(1);
                TimeSpan lease = _registry.Find(kind)?.Options.Lease ?? UnknownKindLease;
                rows.Add(new ClaimedJob(
                    reader.GetGuid(0),
                    kind,
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    now,
                    now + lease));
            }

            return rows;
        }, cancellationToken);

        // Only the attempt that committed reaches here, so only its rows take slots.
        foreach (ClaimedJob job in claimed)
        {
            _running[job.Id] = 0;
            _telemetry.JobClaimed(job.Kind, job.ClaimedAt - job.RunAt);
        }

        return claimed;
    }

    /// <summary>
    /// Runs one claimed job to its outcome and returns it. The returned task ends once the outcome is written; a timed
    /// out handler may still be running then, and keeps its slot until it returns (OUT-2). <paramref name="stopping"/> is
    /// the host's shutdown: a handler it cancels is released, not failed (OUT-6, OUT-8).
    /// </summary>
    public async Task<JobAttemptResult> RunAsync(ClaimedJob job, CancellationToken stopping)
    {
        using Activity? activity = StartActivity(job);
        JobRegistration? registration = _registry.Find(job.Kind);
        string? refusal = null;
        string? deadReason = null;
        object? payload = null;
        Exception? refusalError = null;
        if (registration is null)
        {
            refusal = "no handler registered for kind"; // OUT-2: a rollback to the older version can still drain these rows
        }
        else if (registration.Options.RequiresProject && job.ProjectId is null)
        {
            deadReason = "project id missing"; // OUT-4
        }
        else
        {
            try
            {
                payload = registration.Deserialize(job.Payload);
            }
            catch (JsonException ex)
            {
                refusal = "payload could not be read: " + ex.Message;
                refusalError = ex;
            }
        }

        if (payload is null)
        {
            try
            {
                string outcome = deadReason is not null
                    ? await DieAsync(job, deadReason, null)
                    : await FailAsync(job, refusal!, refusalError, null);
                activity?.SetStatus(ActivityStatusCode.Error, deadReason ?? refusal);
                return new JobAttemptResult(job.Kind, job.Id, outcome);
            }
            finally
            {
                FreeSlot(job.Id);
            }
        }

        using var attempt = new Attempt(this, job, registration!, stopping);
        return await attempt.RunAsync(payload, activity);
    }

    /// <summary>
    /// The fenced statement's WHERE clause (CLM-4): only the holder of this attempt's lease changes the row. Callers add
    /// <c>@id</c>, <c>@worker</c>, and <c>@attempt</c>.
    /// </summary>
    private const string Fence = "id = @id AND status = 'running' AND locked_by = @worker AND attempt = @attempt";

    private static void AddFence(NpgsqlCommand command, ClaimedJob job, string worker)
    {
        command.Parameters.Add(new NpgsqlParameter<Guid>("id", job.Id));
        command.Parameters.Add(new NpgsqlParameter<string>("worker", worker));
        command.Parameters.Add(new NpgsqlParameter<int>("attempt", job.Attempt));
    }

    /// <summary>OUT-5: the exception's type name and message, cut to 2000 characters.</summary>
    internal static string LastError(Exception exception) => Cut($"{exception.GetType().Name}: {exception.Message}");

    private static string Cut(string text) => text.Length > MaxLastErrorLength ? text[..MaxLastErrorLength] : text;

    private static Activity? StartActivity(ClaimedJob job)
    {
        // JTL-3: linked to the enqueuing trace, not a child of it, since the job runs long after the request ended.
        ActivityLink[]? links = job.TraceParent is { } parent && ActivityContext.TryParse(parent, null, out ActivityContext context)
            ? [new ActivityLink(context)]
            : null;
        Activity? activity = InfrastructureTelemetry.ActivitySource.StartActivity(
            ActivityKind.Consumer, parentContext: default, tags: null, links: links, name: "job " + job.Kind);
        activity?.SetTag("orvano.job.kind", job.Kind);
        activity?.SetTag("orvano.job.id", job.Id);
        activity?.SetTag("orvano.job.attempt", job.Attempt);
        return activity;
    }

    private void FreeSlot(Guid id)
    {
        if (_running.TryRemove(id, out _))
        {
            SlotFreed?.Invoke();
        }
    }

    /// <summary>OUT-1: the handler returned.</summary>
    private Task<string> SucceedAsync(ClaimedJob job, TimeSpan duration) =>
        WriteAsync(job, JobOutcome.Succeeded, duration, null, async (db, now, token) =>
        {
            await using NpgsqlCommand command = JobSql.Command(db, $"""
                UPDATE jobs.job SET status = 'succeeded', finished_at = @now, locked_until = NULL, locked_by = NULL
                WHERE {Fence}
                """);
            command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
            AddFence(command, job, Worker);
            return await command.ExecuteNonQueryAsync(token);
        });

    /// <summary>OUT-2, OUT-3: a failed attempt goes back to the queue with backoff, or dies on its last attempt.</summary>
    private Task<string> FailAsync(ClaimedJob job, string lastError, Exception? exception, TimeSpan? duration)
    {
        if (job.Attempt >= job.MaxAttempts)
        {
            return DieAsync(job, lastError, exception, duration);
        }

        TimeSpan delay = JobBackoff.Delay(job.Attempt, JobBackoff.NextRandom());
        DateTimeOffset nextRunAt = default;
        return WriteAsync(job, JobOutcome.Failed, duration, () =>
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.JobAttemptFailed(job.Kind, job.Id, job.Attempt, job.MaxAttempts, nextRunAt, exception);
                }
            },
            async (db, now, token) =>
            {
                nextRunAt = now + delay;
                await using NpgsqlCommand command = JobSql.Command(db, $"""
                    UPDATE jobs.job SET status = 'queued', run_at = @run_at, locked_until = NULL, locked_by = NULL, last_error = @error
                    WHERE {Fence}
                    """);
                command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("run_at", nextRunAt));
                command.Parameters.Add(new NpgsqlParameter<string>("error", Cut(lastError)));
                AddFence(command, job, Worker);
                return await command.ExecuteNonQueryAsync(token);
            });
    }

    /// <summary>OUT-4: dead, with its reason in <c>last_error</c>.</summary>
    private Task<string> DieAsync(ClaimedJob job, string reason, Exception? exception, TimeSpan? duration = null) =>
        WriteAsync(job, JobOutcome.Dead, duration, () =>
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.JobDead(job.Kind, job.Id, job.Attempt, Cut(reason), exception);
                }
            },
            async (db, now, token) =>
            {
                await using NpgsqlCommand command = JobSql.Command(db, $"""
                    UPDATE jobs.job SET status = 'dead', finished_at = @now, locked_until = NULL, locked_by = NULL, last_error = @error
                    WHERE {Fence}
                    """);
                command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
                command.Parameters.Add(new NpgsqlParameter<string>("error", Cut(reason)));
                AddFence(command, job, Worker);
                return await command.ExecuteNonQueryAsync(token);
            });

    /// <summary>
    /// OUT-6: a handler the shutdown cancelled goes back to the queue, due now, with its attempt restored, and wakes the
    /// other workers; the release runs in a transaction so its <c>job</c> signal joins it.
    /// </summary>
    private Task<string> ReleaseAsync(ClaimedJob job, TimeSpan duration) =>
        WriteAsync(job, JobOutcome.Released, duration, null, async (db, now, token) =>
        {
            await using NpgsqlCommand command = JobSql.Command(db, $"""
                UPDATE jobs.job SET status = 'queued', attempt = attempt - 1, run_at = @now, locked_until = NULL, locked_by = NULL
                WHERE {Fence}
                """);
            command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
            AddFence(command, job, Worker);
            int rows = await command.ExecuteNonQueryAsync(token);
            if (rows == 1)
            {
                _signals.Add(db, SignalKind.Job, []);
            }

            return rows;
        });

    /// <summary>
    /// Runs one fenced outcome write in its own transaction under the execution strategy, bounded by
    /// <see cref="WriteBound"/> and never by the stopping token (OUT-7, OUT-8). A fence that matches no row means the lease
    /// was lost; a write that fails is logged, and the lease recovers the job.
    /// </summary>
    private async Task<string> WriteAsync(
        ClaimedJob job,
        string outcome,
        TimeSpan? duration,
        Action? onWritten,
        Func<JobsDbContext, DateTimeOffset, CancellationToken, Task<int>> write)
    {
        using var bound = new CancellationTokenSource(WriteBound, _time);
        try
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            JobsDbContext db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
            int rows = await db.InTransactionAsync(token => write(db, _time.GetUtcNow(), token), bound.Token);
            if (rows == 0)
            {
                _logger.JobLeaseLost(job.Kind, job.Id, job.Attempt);
                _telemetry.JobAttempted(job.Kind, JobOutcome.LeaseLost, duration);
                return JobOutcome.LeaseLost;
            }

            onWritten?.Invoke();
        }
        catch (Exception ex) // spec 0013 OUT-7: any failed outcome write is logged; the lease recovers the job
        {
            _logger.JobOutcomeWriteFailed(job.Kind, job.Id, ex);
        }

        _telemetry.JobAttempted(job.Kind, outcome, duration);
        return outcome;
    }

    private DbContextOptions<JobsDbContext> RenewalOptions(TimeSpan bound)
    {
        int seconds = Math.Max(1, (int)Math.Ceiling(bound.TotalSeconds));
        return _renewalOptions.GetOrAdd(seconds, timeout => new DbContextOptionsBuilder<JobsDbContext>()
            .UseOrvanoConventions(JobsDbContext.Schema, _services, npgsql =>
            {
                // CLM-5: a single attempt, so a retrying renewal never outlives the lease it renews.
                npgsql.EnableRetryOnFailure(0);
                npgsql.CommandTimeout(timeout);
            })
            .Options);
    }

    /// <summary>One attempt of one claimed job: the handler, its renewal timer, and its outcome (CLM-5, CLM-7, OUT-1 to OUT-8).</summary>
    private sealed class Attempt : IDisposable
    {
        private readonly JobExecutor _executor;
        private readonly ClaimedJob _job;
        private readonly JobRegistration _registration;
        private readonly CancellationToken _stopping;
        private readonly TimeSpan _lease;
        private readonly CancellationTokenSource _leaseLost = new();
        private readonly Lock _renewLock = new();
        private DateTimeOffset _lockedUntil;
        private Task _renewal = Task.CompletedTask;
        private volatile bool _handlerReturned;

        public Attempt(JobExecutor executor, ClaimedJob job, JobRegistration registration, CancellationToken stopping)
        {
            _executor = executor;
            _job = job;
            _registration = registration;
            _stopping = stopping;
            _lease = registration.Options.Lease;
            _lockedUntil = job.LockedUntil;
        }

        private TimeSpan RenewEvery => _lease / 3;

        public void Dispose() => _leaseLost.Dispose();

        public async Task<JobAttemptResult> RunAsync(object payload, Activity? activity)
        {
            TimeProvider time = _executor._time;
            long started = time.GetTimestamp();
            _executor._logger.JobAttemptStarted(_job.Kind, _job.Id, _job.Attempt);

            // Disposed only once the handler returns: disposing a linked source unlinks it, and a handler still running
            // must keep seeing its cancellation.
            var timeout = new CancellationTokenSource(_registration.Options.Timeout, time);
            var handlerToken = CancellationTokenSource.CreateLinkedTokenSource(_stopping, _leaseLost.Token, timeout.Token);
            using var abandon = CancellationTokenSource.CreateLinkedTokenSource(_leaseLost.Token, timeout.Token);
            ITimer renewTimer = time.CreateTimer(_ => OnRenewTick(), null, RenewEvery, RenewEvery);

            // The handler runs on its own task and never throws here: its exception is its result.
            Task<Exception?> handler = Task.Run(() => InvokeAsync(payload, handlerToken.Token), CancellationToken.None);
            Exception? error = null;
            bool returned;
            try
            {
                error = await handler.WaitAsync(abandon.Token);
                returned = true;
            }
            catch (OperationCanceledException) when (abandon.IsCancellationRequested)
            {
                returned = handler.IsCompleted;
                if (returned)
                {
                    error = await handler;
                }
            }

            _handlerReturned = returned;
            if (!returned)
            {
                // This code can resume inside the cancel that abandoned the handler, before the cancel reached the
                // handler's own linked token, so cancel that token here too.
                await handlerToken.CancelAsync();
            }

            // CLM-5: the renewal stops, and one in flight finishes, before the outcome is written, so a job that
            // succeeded is never counted as a lost lease.
            await renewTimer.DisposeAsync();
            Task renewal;
            lock (_renewLock)
            {
                renewal = _renewal;
            }

            await renewal;

            TimeSpan duration = time.GetElapsedTime(started);
            string outcome;
            try
            {
                outcome = await OutcomeAsync(returned, error, timeout.IsCancellationRequested, duration);
            }
            finally
            {
                if (returned)
                {
                    _executor.FreeSlot(_job.Id);
                    handlerToken.Dispose();
                    timeout.Dispose();
                }
                else
                {
                    KeepSlotUntilReturn(handler, handlerToken, timeout);
                }
            }

            if (outcome != JobOutcome.Succeeded)
            {
                activity?.SetStatus(ActivityStatusCode.Error, outcome);
            }

            return new JobAttemptResult(_job.Kind, _job.Id, outcome);
        }

        /// <summary>OUT-8: the cause of a cancellation is judged lease lost, then timed out, then shutdown, else the handler's own.</summary>
        private async Task<string> OutcomeAsync(bool returned, Exception? error, bool timedOut, TimeSpan duration)
        {
            if (_leaseLost.IsCancellationRequested)
            {
                _executor._logger.JobLeaseLost(_job.Kind, _job.Id, _job.Attempt);
                _executor._telemetry.JobAttempted(_job.Kind, JobOutcome.LeaseLost, duration);
                return JobOutcome.LeaseLost;
            }

            string timedOutText = "timed out after " + _registration.Options.Timeout.ToString("c", CultureInfo.InvariantCulture);
            if (!returned)
            {
                // Only the timeout abandons a handler besides a lost lease; the retry schedule starts now.
                _executor._logger.JobTimedOut(_job.Kind, _job.Id, _job.Attempt, _registration.Options.Timeout);
                return await _executor.FailAsync(_job, timedOutText, null, duration);
            }

            switch (error)
            {
                case null:
                    _executor._logger.JobAttemptSucceeded(_job.Kind, _job.Id, _job.Attempt);
                    return await _executor.SucceedAsync(_job, duration);
                case PermanentJobFailureException permanent:
                    return await _executor.DieAsync(_job, LastError(permanent), permanent, duration);
                case OperationCanceledException when timedOut:
                    return await _executor.FailAsync(_job, timedOutText, error, duration);
                case OperationCanceledException when _stopping.IsCancellationRequested:
                    return await _executor.ReleaseAsync(_job, duration);
                default:
                    return await _executor.FailAsync(_job, LastError(error), error, duration);
            }
        }

        /// <summary>CLM-7: a new scope with the system caller and the job's project, then the kind's handler.</summary>
        private async Task<Exception?> InvokeAsync(object payload, CancellationToken cancellationToken)
        {
            try
            {
                await using AsyncServiceScope scope = _executor._scopes.CreateAsyncScope();
                JobAccessScope.Establish(scope.ServiceProvider, _job.ProjectId);
                var context = new JobContext(_job.Id, _job.Kind, _job.Attempt, _job.MaxAttempts, _job.ProjectId, _job.RunAt, _job.CreatedAt);
                await _registration.RunAsync(scope.ServiceProvider, payload, context, cancellationToken);
                return null;
            }
            catch (Exception ex) // spec 0013 OUT-2: every exception a handler throws is its attempt's outcome
            {
                return ex;
            }
        }

        /// <summary>
        /// OUT-2: a timed out handler keeps its slot and its place in the running set until it returns, with a Warning
        /// each lease period it has not, and its late exception at Debug.
        /// </summary>
        private void KeepSlotUntilReturn(Task<Exception?> handler, CancellationTokenSource handlerToken, CancellationTokenSource timeout)
        {
            TimeProvider time = _executor._time;
            long abandoned = time.GetTimestamp();
            ITimer reminder = time.CreateTimer(
                _ =>
                {
                    if (_executor._logger.IsEnabled(LogLevel.Warning))
                    {
                        _executor._logger.JobAbandonedStillRunning(_job.Kind, _job.Id, time.GetElapsedTime(abandoned));
                    }
                },
                null,
                _lease,
                _lease);
            _ = FreeOnReturnAsync();

            async Task FreeOnReturnAsync()
            {
                Exception? late = await handler;
                await reminder.DisposeAsync();
                handlerToken.Dispose();
                timeout.Dispose();
                if (late is not null && _executor._logger.IsEnabled(LogLevel.Debug))
                {
                    _executor._logger.JobAbandonedFailed(_job.Kind, _job.Id, late);
                }

                _executor.FreeSlot(_job.Id);
            }
        }

        /// <summary>CLM-5: at most one renewal in flight; a tick that finds one still running is skipped.</summary>
        private void OnRenewTick()
        {
            if (_handlerReturned || _leaseLost.IsCancellationRequested)
            {
                return;
            }

            lock (_renewLock)
            {
                if (!_renewal.IsCompleted)
                {
                    return;
                }

                // The clock passed the last lease this worker set without a renewal that succeeded.
                if (_executor._time.GetUtcNow() >= _lockedUntil)
                {
                    LoseLease();
                    return;
                }

                _renewal = RenewAsync();
            }
        }

        private async Task RenewAsync()
        {
            await Task.Yield();
            try
            {
                TimeProvider time = _executor._time;
                DateTimeOffset until = time.GetUtcNow() + _lease;
                using var bound = new CancellationTokenSource(RenewEvery, time);
                await using var db = new JobsDbContext(_executor.RenewalOptions(RenewEvery));
                string worker = _executor.Worker;
                int rows = await db.Database.ExecuteSqlAsync(
                    $"""
                    UPDATE jobs.job SET locked_until = {until}
                    WHERE id = {_job.Id} AND status = 'running' AND locked_by = {worker} AND attempt = {_job.Attempt}
                    """,
                    bound.Token);
                if (rows == 1)
                {
                    _lockedUntil = until;
                }
                else if (!_handlerReturned)
                {
                    LoseLease(); // another worker took the job after this lease expired, or the project was cancelled
                }
            }
            catch (Exception ex) // spec 0013 CLM-5: a renewal that throws is retried at the next tick
            {
                _executor._logger.JobRenewalFailed(_job.Kind, _job.Id, ex);
            }
        }

        private void LoseLease()
        {
            if (!_handlerReturned)
            {
                _leaseLost.Cancel();
            }
        }
    }
}

/// <summary>A row the claim took (CLM-1), with the claim's time and the lease it set.</summary>
internal sealed record ClaimedJob(
    Guid Id,
    string Kind,
    string Payload,
    Guid? ProjectId,
    int Attempt,
    int MaxAttempts,
    DateTimeOffset RunAt,
    DateTimeOffset CreatedAt,
    string? TraceParent,
    DateTimeOffset ClaimedAt,
    DateTimeOffset LockedUntil);

/// <summary>One attempt's outcome, as the <c>outcome</c> metric tag names it (JTL-1).</summary>
internal sealed record JobAttemptResult(string Kind, Guid Id, string Outcome);

internal static class JobOutcome
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Dead = "dead";
    public const string Released = "released";
    public const string LeaseLost = "lease_lost";
}
