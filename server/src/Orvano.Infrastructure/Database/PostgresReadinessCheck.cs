using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Orvano.Infrastructure.Database;

/// <summary>
/// Readiness: can <c>orvano</c> reach Postgres. Runs <c>SELECT 1</c> with a 2 second limit, reuses the last result
/// for 1 second (counted from when that probe finished), and lets callers that arrive together share one probe.
/// Worst case a change shows after 3 seconds. Register it as a DI singleton so its cache and state live as long
/// as the app. It logs on a change of state only, never the connection string.
/// </summary>
public sealed partial class PostgresReadinessCheck(NpgsqlDataSource dataSource, ILogger<PostgresReadinessCheck> logger) : IHealthCheck
{
    /// <summary>The name the host registers this check under.</summary>
    public const string Name = "postgres";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private Task<HealthCheckResult>? _inFlight;
    private HealthCheckResult _last;
    private long _lastFinished;

    // Start as ready, so the first failed probe after startup counts as a change and is logged.
    private bool _ready = true;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        Task<HealthCheckResult> probe;

        lock (_gate)
        {
            if (_lastFinished != 0 && Stopwatch.GetElapsedTime(_lastFinished) < CacheFor)
            {
                return Task.FromResult(_last);
            }

            // Task.Run keeps the probe off this lock, so it can clear _inFlight when it finishes.
            probe = _inFlight ??= Task.Run(ProbeAndRecordAsync, CancellationToken.None);
        }

        // One caller giving up must not cancel the probe the others share.
        return probe.WaitAsync(cancellationToken);
    }

    private async Task<HealthCheckResult> ProbeAndRecordAsync()
    {
        string? failure = null;
        using var timeout = new CancellationTokenSource(ProbeTimeout);
        var ping = PingAsync(timeout.Token);

        try
        {
            // Npgsql may wait on its own cancel request after the token fires, so the limit is enforced here too.
            await ping.WaitAsync(ProbeTimeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            failure = $"no answer within {ProbeTimeout.TotalSeconds:0} seconds";

            // The ping may still fault later. Observe it so it never surfaces as an unobserved task exception.
            _ = ping.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
#pragma warning disable CA1031 // Any failure to reach Postgres means "not ready". The probe must never throw.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failure = exception.Message;
        }

        var result = failure is null ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();

        lock (_gate)
        {
            var wasReady = _ready;
            _ready = failure is null;
            _last = result;
            _lastFinished = Stopwatch.GetTimestamp();
            _inFlight = null;

            if (wasReady && failure is not null)
            {
                LogNotReady(logger, failure);
            }
            else if (!wasReady && failure is null)
            {
                LogReady(logger);
            }
        }

        return result;
    }

    private async Task PingAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "orvano is not ready: Postgres did not answer ({Reason})")]
    private static partial void LogNotReady(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "orvano is ready again: Postgres answers")]
    private static partial void LogReady(ILogger logger);
}
