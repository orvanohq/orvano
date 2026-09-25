using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Modules;

namespace Orvano.Core.Scheduling;

/// <summary>
/// Worker role. Runs internal schedules only while holding the session level advisory lock
/// (ORVA, 2) on a dedicated connection. If that connection drops, leader tasks stop at once and the
/// worker tries to take the lock again every 10 seconds.
/// </summary>
public sealed class LeaderScheduler(
    [FromKeyedServices(OrvanoDb.Dedicated)] NpgsqlDataSource dedicatedDb,
    WorkRegistry work,
    IServiceProvider services,
    ILogger<LeaderScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan ConnectionCheck = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = await dedicatedDb.OpenConnectionAsync(stoppingToken);
                if (await TryTakeLockAsync(conn, stoppingToken))
                {
                    logger.LogInformation("This worker is the scheduler leader");
                    await LeadAsync(conn, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scheduler leader connection lost; leader tasks stopped");
            }

            try { await Task.Delay(Timings.LeaderRetry, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task LeadAsync(NpgsqlConnection conn, CancellationToken stoppingToken)
    {
        using var leadership = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var tasks = work.Schedules.Select(s => RunScheduleAsync(s, leadership.Token)).ToList();
        try
        {
            // Returns only by throwing: on shutdown, or when the lock connection breaks.
            while (true)
            {
                await Task.Delay(ConnectionCheck, stoppingToken);
                await using var ping = new NpgsqlCommand("SELECT 1", conn);
                await ping.ExecuteScalarAsync(stoppingToken);
            }
        }
        finally
        {
            await leadership.CancelAsync();
            await Task.WhenAll(tasks);
        }
    }

    private async Task RunScheduleAsync(InternalSchedule schedule, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(schedule.Interval);
        try
        {
            do
            {
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    await schedule.Task(scope.ServiceProvider, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Internal schedule {Schedule} failed", schedule.Name);
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    private static async Task<bool> TryTakeLockAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@class, @object)", conn);
        cmd.Parameters.AddWithValue("class", AdvisoryLocks.Class);
        cmd.Parameters.AddWithValue("object", AdvisoryLocks.SchedulerLeader);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
