using Microsoft.Extensions.Logging;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Signals;

namespace Orvano.Infrastructure;

// Spec 0013 LSN-7. Payload values hold user ids, so they appear only at Debug (security model).
internal static partial class InfrastructureLog
{
    [LoggerMessage(EventId = LogEventIds.Infrastructure + 1, Level = LogLevel.Information,
        Message = "Signal listener connected to channel {Channel}")]
    public static partial void SignalsConnected(this ILogger logger, string channel);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 2, Level = LogLevel.Warning,
        Message = "Signal listener disconnected ({Reason}); the cache is bypassed until it reconnects")]
    public static partial void SignalsDisconnected(this ILogger logger, string reason, Exception? exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 3, Level = LogLevel.Warning,
        Message = "Signal listener could not connect (attempt {Attempt}); next attempt in {Delay}")]
    public static partial void SignalsConnectFailed(this ILogger logger, int attempt, TimeSpan delay, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 4, Level = LogLevel.Warning,
        Message = "Ignored a malformed signal payload")]
    public static partial void SignalMalformed(this ILogger logger);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 5, Level = LogLevel.Warning,
        Message = "Ignored a signal of unknown kind {Kind}")]
    public static partial void SignalKindUnknown(this ILogger logger, string kind);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 6, Level = LogLevel.Debug,
        Message = "Ignored a {Kind} signal with {Count} values")]
    public static partial void SignalIgnored(this ILogger logger, SignalKind kind, int count);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 7, Level = LogLevel.Debug,
        Message = "Received a {Kind} signal: {Values}")]
    public static partial void SignalReceived(this ILogger logger, SignalKind kind, string values);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 8, Level = LogLevel.Error,
        Message = "Signal handler {Handler} failed on a {Kind} signal")]
    public static partial void SignalHandlerFailed(this ILogger logger, string handler, SignalKind kind, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 9, Level = LogLevel.Error,
        Message = "Signal connection observer {Observer} failed in {Callback}")]
    public static partial void SignalObserverFailed(this ILogger logger, string observer, string callback, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 10, Level = LogLevel.Warning,
        Message = "Signal dispatch queue holds {Count} items; a handler or observer is slow")]
    public static partial void SignalQueueLong(this ILogger logger, int count);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 11, Level = LogLevel.Error,
        Message = "Removing cache tags after a commit failed; the entries expire on their own")]
    public static partial void CacheRemovalFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 12, Level = LogLevel.Error,
        Message = "Signal handler {Handler} did not finish a {Kind} signal within {Limit}; it was cancelled and the dispatch loop moved on")]
    public static partial void SignalHandlerTimedOut(this ILogger logger, string handler, SignalKind kind, TimeSpan limit);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 13, Level = LogLevel.Error,
        Message = "Signal connection observer {Observer} did not finish {Callback} within {Limit}; it was cancelled and the dispatch loop moved on")]
    public static partial void SignalObserverTimedOut(this ILogger logger, string observer, string callback, TimeSpan limit);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 14, Level = LogLevel.Error,
        Message = "Signal dispatch failed on one item; the loop continues")]
    public static partial void SignalDispatchFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 15, Level = LogLevel.Debug,
        Message = "A {Call} given up at its deadline failed later")]
    public static partial void SignalCallAbandonedFailed(this ILogger logger, string call, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 16, Level = LogLevel.Warning,
        Message = "Signal dispatch is {Lag} behind, past the heartbeat timeout {Limit}; the listener counts as disconnected")]
    public static partial void SignalDispatchLagging(this ILogger logger, TimeSpan lag, TimeSpan limit);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 17, Level = LogLevel.Warning,
        Message = "Signal listener connection dropped within {Stable} of connecting (failed attempt {Attempt}); next attempt in {Delay}")]
    public static partial void SignalsConnectionShortLived(this ILogger logger, TimeSpan stable, int attempt, TimeSpan delay);

    // Spec 0013 JTL-2: the job runner, LogEventIds.Infrastructure + 100 to + 199. Payloads and dedupe keys may hold user
    // ids (spec 0006 LOG-2), so they appear only at Debug.

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 100, Level = LogLevel.Information,
        Message = "Job runner started: concurrency {Concurrency}, poll interval {PollInterval}, worker {Worker}")]
    public static partial void JobRunnerStarted(this ILogger logger, int concurrency, TimeSpan pollInterval, string worker);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 101, Level = LogLevel.Error,
        Message = "Claiming jobs failed; the runner tries again at the next wake")]
    public static partial void JobClaimFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 102, Level = LogLevel.Debug,
        Message = "Job {Kind} {Id} attempt {Attempt} started")]
    public static partial void JobAttemptStarted(this ILogger logger, string kind, Guid id, int attempt);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 103, Level = LogLevel.Debug,
        Message = "Job {Kind} {Id} attempt {Attempt} succeeded")]
    public static partial void JobAttemptSucceeded(this ILogger logger, string kind, Guid id, int attempt);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 104, Level = LogLevel.Warning,
        Message = "Job {Kind} {Id} attempt {Attempt} of {MaxAttempts} failed; it runs again at {NextRunAt:O}")]
    public static partial void JobAttemptFailed(
        this ILogger logger, string kind, Guid id, int attempt, int maxAttempts, DateTimeOffset nextRunAt, Exception? exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 105, Level = LogLevel.Error,
        Message = "Job {Kind} {Id} is dead after attempt {Attempt}: {Reason}")]
    public static partial void JobDead(this ILogger logger, string kind, Guid id, int attempt, string reason, Exception? exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 106, Level = LogLevel.Warning,
        Message = "Job {Kind} {Id} attempt {Attempt} lost its lease; its handler was cancelled and no outcome was written")]
    public static partial void JobLeaseLost(this ILogger logger, string kind, Guid id, int attempt);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 107, Level = LogLevel.Warning,
        Message = "Job {Kind} {Id} attempt {Attempt} timed out after {Timeout}; its handler was cancelled and keeps its slot until it returns")]
    public static partial void JobTimedOut(this ILogger logger, string kind, Guid id, int attempt, TimeSpan timeout);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 108, Level = LogLevel.Warning,
        Message = "The handler of job {Kind} {Id}, given up {Elapsed} ago, has still not returned and holds a slot")]
    public static partial void JobAbandonedStillRunning(this ILogger logger, string kind, Guid id, TimeSpan elapsed);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 109, Level = LogLevel.Debug,
        Message = "The handler of job {Kind} {Id}, given up earlier, failed later")]
    public static partial void JobAbandonedFailed(this ILogger logger, string kind, Guid id, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 110, Level = LogLevel.Information,
        Message = "Released {Count} running jobs at shutdown; they run again at once on any worker")]
    public static partial void JobsReleased(this ILogger logger, int count);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 111, Level = LogLevel.Error,
        Message = "Writing the outcome of job {Kind} {Id} failed; its lease expires and another claim recovers it")]
    public static partial void JobOutcomeWriteFailed(this ILogger logger, string kind, Guid id, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 112, Level = LogLevel.Information,
        Message = "Job schedules synced: {Added} added, {Updated} updated, {Removed} removed")]
    public static partial void JobSchedulesSynced(this ILogger logger, int added, int updated, int removed);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 113, Level = LogLevel.Error,
        Message = "Syncing job schedules failed; no schedule fires until a later poll syncs them")]
    public static partial void JobScheduleSyncFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 114, Level = LogLevel.Debug,
        Message = "Job schedule {Name} fired for {FireTime:O}")]
    public static partial void JobScheduleFired(this ILogger logger, string name, DateTimeOffset fireTime);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 115, Level = LogLevel.Error,
        Message = "Firing due job schedules failed; the claim runs anyway and the next poll tries again")]
    public static partial void JobScheduleTickFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 116, Level = LogLevel.Information,
        Message = "Job cleanup deleted {Succeeded} succeeded and {Dead} dead jobs")]
    public static partial void JobsCleanedUp(this ILogger logger, int succeeded, int dead);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 117, Level = LogLevel.Error,
        Message = "Job schedule {Name} has no next occurrence; it is disabled")]
    public static partial void JobScheduleNeverFires(this ILogger logger, string name);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 118, Level = LogLevel.Debug,
        Message = "Enqueued job {Kind} {Id} due at {RunAt:O} with dedupe key {DedupeKey}: {Payload}")]
    public static partial void JobEnqueued(this ILogger logger, string kind, Guid id, DateTimeOffset runAt, string? dedupeKey, string payload);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 119, Level = LogLevel.Warning,
        Message = "Renewing the lease of job {Kind} {Id} failed; the next renewal tries again")]
    public static partial void JobRenewalFailed(this ILogger logger, string kind, Guid id, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 120, Level = LogLevel.Warning,
        Message = "A cancellation callback of job {Kind} {Id} threw; the job's outcome is unaffected")]
    public static partial void JobCancelCallbackFailed(this ILogger logger, string kind, Guid id, Exception exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 121, Level = LogLevel.Error,
        Message = "Marking expired jobs dead failed; the runner tries again at the next poll")]
    public static partial void JobMarkDeadFailed(this ILogger logger, Exception exception);

    // Spec 0013 OTL-2: the outbox block, + 200 to + 299. Payloads only at Debug (spec 0006 LOG-2).
    [LoggerMessage(EventId = LogEventIds.Infrastructure + 200, Level = LogLevel.Information,
        Message = "Outbox dispatcher started with {Outboxes} outboxes and {Subscribers} subscribers")]
    public static partial void OutboxDispatcherStarted(this ILogger logger, int outboxes, int subscribers);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 201, Level = LogLevel.Debug,
        Message = "Dispatched {Events} events of the {Outbox} outbox into {Jobs} jobs")]
    public static partial void OutboxBatchDispatched(this ILogger logger, string outbox, int events, int jobs);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 202, Level = LogLevel.Error,
        Message = "Dispatching the {Outbox} outbox failed; nothing was enqueued or marked, and the next wake tries again")]
    public static partial void OutboxBatchFailed(this ILogger logger, string outbox, Exception? exception);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 203, Level = LogLevel.Warning,
        Message = "The {Outbox} outbox holds an event of type {Type}, which no outbox registers; it was marked dispatched with no deliveries")]
    public static partial void OutboxEventTypeUnknown(this ILogger logger, string outbox, string type);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 204, Level = LogLevel.Debug,
        Message = "Published event {Type} {Id}: {Payload}")]
    public static partial void OutboxEventPublished(this ILogger logger, string type, Guid id, string payload);

    [LoggerMessage(EventId = LogEventIds.Infrastructure + 205, Level = LogLevel.Information,
        Message = "Outbox cleanup deleted {Deleted} dispatched events from the {Outbox} outbox")]
    public static partial void OutboxCleanedUp(this ILogger logger, string outbox, int deleted);
}
