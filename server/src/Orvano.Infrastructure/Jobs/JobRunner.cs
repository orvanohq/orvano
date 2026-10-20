using System.Collections.Concurrent;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orvano.Infrastructure.Jobs;

/// <summary>
/// The worker's job loop (spec 0013 WAK-1 to WAK-4, SCH-2, SCH-3, OUT-6): syncs the schedules, then claims on every
/// wake and starts each claimed job on its own task. Only <c>serve</c> with the <c>worker</c> role runs it (JOP-3).
/// </summary>
internal sealed class JobRunner : BackgroundService
{
    private readonly JobExecutor _executor;
    private readonly JobScheduler _scheduler;
    private readonly WorkerWake _wake;
    private readonly WorkerWake.Subscription _wakes;
    private readonly JobOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<JobRunner> _logger;
    private readonly CancellationTokenSource _handlersStop = new();
    private readonly ConcurrentDictionary<Task, byte> _attempts = new();
    private int _released;

    // WAK-2 (4): the most recent claim returned as many rows as it asked for, so more may be due.
    private volatile bool _lastClaimFull;

    public JobRunner(
        JobExecutor executor,
        JobScheduler scheduler,
        WorkerWake wake,
        IOptions<JobOptions> options,
        TimeProvider time,
        ILogger<JobRunner> logger)
    {
        _executor = executor;
        _scheduler = scheduler;
        _wake = wake;
        _wakes = wake.Subscribe();
        _options = options.Value;
        _time = time;
        _logger = logger;
        executor.SlotFreed += OnSlotFreed;
    }

    public override void Dispose()
    {
        _executor.SlotFreed -= OnSlotFreed;
        _handlersStop.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Host's PostConfigure (JOP-2) always fills the default; 1 only if a test builds the runner without it.
        int concurrency = _options.Concurrency ?? 1;
        _logger.JobRunnerStarted(concurrency, _options.PollInterval, _executor.Worker);

        // SCH-2: the sync comes before the first claim; until one succeeds, no schedule fires (SCH-3).
        bool synced = await TrySyncAsync(stoppingToken);
        Task poll = PollAsync(stoppingToken);
        _wake.Wake();
        try
        {
            while (await WaitAsync(stoppingToken))
            {
                if (_wakes.Take())
                {
                    synced = synced || await TrySyncAsync(stoppingToken);
                    if (synced)
                    {
                        await TryTickAsync(stoppingToken);
                    }
                }

                await TryClaimAsync(concurrency);
            }
        }
        finally
        {
            // OUT-6: the loop has stopped and the claim in flight finished, so no row is claimed without a task. Now the
            // handlers are cancelled; each one the shutdown stops is released, and one that finishes writes its outcome.
            await _handlersStop.CancelAsync();
            await poll;
            await Task.WhenAll(_attempts.Keys);
            if (_released > 0)
            {
                _logger.JobsReleased(_released);
            }
        }
    }

    private async Task<bool> WaitAsync(CancellationToken stoppingToken)
    {
        try
        {
            return await _wakes.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>WAK-2 (2): always on, connected or not; the only path for delayed jobs, retries, and schedules.</summary>
    private async Task PollAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _wake.Poll();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>WAK-4: a claim that throws is logged, and the next wake (at most a poll away) tries again.</summary>
    private async Task TryClaimAsync(int concurrency)
    {
        int free = concurrency - _executor.RunningCount;
        if (free <= 0)
        {
            return;
        }

        IReadOnlyList<ClaimedJob> claimed;
        try
        {
            // Not the stopping token: a claim in flight at shutdown finishes, so its rows get tasks (OUT-6).
            claimed = await _executor.ClaimAsync(free, CancellationToken.None);
        }
        catch (Exception ex) // spec 0013 WAK-4: the loop never ends while the host runs
        {
            _logger.JobClaimFailed(ex);
            return;
        }

        _lastClaimFull = claimed.Count == free;
        foreach (ClaimedJob job in claimed)
        {
            Task attempt = RunAsync(job);
            _attempts[attempt] = 0;
            _ = attempt.ContinueWith(
                (finished, attempts) => ((ConcurrentDictionary<Task, byte>)attempts!).TryRemove(finished, out _),
                _attempts,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    /// <summary>Each claimed job on its own task, so at most <c>Concurrency</c> handlers run (CLM-2).</summary>
    private async Task RunAsync(ClaimedJob job)
    {
        try
        {
            JobAttemptResult result = await _executor.RunAsync(job, _handlersStop.Token);
            if (result.Outcome == JobOutcome.Released)
            {
                Interlocked.Increment(ref _released);
            }
        }
        catch (Exception ex) // an attempt that failed past its own handling is left to its lease (CLM-6)
        {
            _logger.JobOutcomeWriteFailed(job.Kind, job.Id, ex);
        }
    }

    private async Task<bool> TrySyncAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _scheduler.SyncAsync(stoppingToken);
            return true;
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested) // spec 0013 SCH-3: retried on the next poll
        {
            _logger.JobScheduleSyncFailed(ex);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task TryTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _scheduler.TickAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested) // spec 0013 SCH-3: the claim after it still runs
        {
            _logger.JobScheduleTickFailed(ex);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnSlotFreed()
    {
        if (_lastClaimFull)
        {
            _wake.Wake();
        }
    }
}
