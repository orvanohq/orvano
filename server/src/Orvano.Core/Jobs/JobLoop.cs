using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Modules;
using Orvano.Core.Notifications;

namespace Orvano.Core.Jobs;

/// <summary>
/// Worker role. Claims queued jobs with SKIP LOCKED and a 60 second lease, runs their handlers,
/// renews the lease every 20 seconds, and records success, retry, or dead.
/// </summary>
public sealed class JobLoop(
    [FromKeyedServices(OrvanoDb.App)] NpgsqlDataSource db,
    WorkRegistry work,
    WakeSignals signals,
    IServiceProvider services,
    ILogger<JobLoop> logger) : BackgroundService
{
    private const int Concurrency = 4;
    private readonly JobStore _store = new(db, $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid().ToString("N")[..8]}");

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queues = work.Queues;
        var running = new HashSet<Task>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                running.RemoveWhere(t => t.IsCompleted);
                var free = Concurrency - running.Count;

                if (free > 0 && queues.Length > 0)
                {
                    try
                    {
                        var jobs = await _store.ClaimAsync(queues, free, stoppingToken);
                        foreach (var job in jobs) running.Add(RunAsync(job, stoppingToken));
                        if (jobs.Count == free) continue; // likely more waiting
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(ex, "Claiming jobs failed; retrying on the next wake up");
                    }
                }

                await signals.Jobs.WaitAsync(Timings.PollInterval, stoppingToken);
            }
        }
        finally
        {
            await Task.WhenAll(running);
        }
    }

    private async Task RunAsync(ClaimedJob job, CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = HeartbeatAsync(job, jobCts);

        try
        {
            var handler = work.HandlerFor(job.Kind)
                ?? throw new InvalidOperationException($"No handler is registered for job kind '{job.Kind}'.");

            await using var scope = services.CreateAsyncScope();
            await handler(new JobContext(job, scope.ServiceProvider), jobCts.Token);
            await _store.CompleteAsync(job.Id, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await TryAsync(() => _store.ReleaseAsync(job.Id, CancellationToken.None), job);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested)
        {
            logger.LogWarning("Job {JobId} ({Kind}) lost its lease and was abandoned", job.Id, job.Kind);
        }
        catch (PermanentJobFailureException ex)
        {
            logger.LogError(ex, "Job {JobId} ({Kind}) failed permanently on attempt {Attempt}; marking it dead", job.Id, job.Kind, job.Attempts);
            await TryAsync(() => _store.FailPermanentlyAsync(job, ex, CancellationToken.None), job);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Job {JobId} ({Kind}) failed on attempt {Attempt} of {Max}", job.Id, job.Kind, job.Attempts, job.MaxAttempts);
            await TryAsync(() => _store.FailAsync(job, ex, CancellationToken.None), job);
        }
        finally
        {
            await jobCts.CancelAsync();
            await heartbeat;
            signals.Jobs.Set(); // a slot is free
        }
    }

    private async Task HeartbeatAsync(ClaimedJob job, CancellationTokenSource jobCts)
    {
        using var timer = new PeriodicTimer(Timings.JobHeartbeat);
        try
        {
            while (await timer.WaitForNextTickAsync(jobCts.Token))
            {
                try
                {
                    if (!await _store.ExtendLeaseAsync(job.Id, jobCts.Token))
                    {
                        await jobCts.CancelAsync();
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Keep trying; the lease has 40 seconds of slack before the reaper takes the job.
                    logger.LogWarning(ex, "Renewing the lease of job {JobId} failed", job.Id);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task TryAsync(Func<Task> update, ClaimedJob job)
    {
        try { await update(); }
        catch (Exception ex)
        {
            // The lease expires and the reaper requeues the job.
            logger.LogError(ex, "Recording the outcome of job {JobId} failed", job.Id);
        }
    }
}
