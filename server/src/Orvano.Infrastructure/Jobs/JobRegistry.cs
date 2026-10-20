using System.Collections.Frozen;
using System.Text;

using Cronos;

using Orvano.SharedKernel.Jobs;

namespace Orvano.Infrastructure.Jobs;

/// <summary>
/// Every registered job kind and schedule, checked once (spec 0013 SCH-1): duplicate kinds and schedule names, crons
/// <c>Cronos</c> cannot parse or that never fire, schedules whose kind has no handler, and schedule payloads over the
/// limit. <c>StartupChecks</c> turns <see cref="Problems"/> into a startup failure in every <c>serve</c> role, so the
/// queue and the runner only ever see what passed.
/// </summary>
internal sealed class JobRegistry
{
    /// <summary>The largest payload, in UTF-8 bytes as sent (ENQ-3).</summary>
    public const int MaxPayloadBytes = 65_536;

    /// <summary><c>ck_schedule_cron</c>'s upper bound (JDB-4).</summary>
    private const int MaxCronLength = 100;

    private readonly FrozenDictionary<string, JobRegistration> _kinds;
    private readonly FrozenDictionary<string, ScheduledJob> _schedules;

    public JobRegistry(IEnumerable<JobRegistration> kinds, IEnumerable<JobScheduleRegistration> schedules, TimeProvider time)
    {
        var problems = new List<string>();

        var byKind = new Dictionary<string, JobRegistration>(StringComparer.Ordinal);
        foreach (JobRegistration kind in kinds)
        {
            if (!byKind.TryAdd(kind.Kind, kind))
            {
                problems.Add($"The job kind {kind.Kind} is registered more than once (spec 0013 SCH-1).");
            }
        }

        DateTimeOffset now = time.GetUtcNow();
        var byName = new Dictionary<string, ScheduledJob>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JobScheduleRegistration schedule in schedules)
        {
            if (!names.Add(schedule.Name))
            {
                problems.Add($"The job schedule {schedule.Name} is registered more than once (spec 0013 SCH-1).");
                continue;
            }

            if (Check(schedule, byKind, now, out ScheduledJob? scheduled) is { } problem)
            {
                problems.Add(problem);
                continue;
            }

            byName[schedule.Name] = scheduled!;
        }

        _kinds = byKind.ToFrozenDictionary(StringComparer.Ordinal);
        _schedules = byName.ToFrozenDictionary(StringComparer.Ordinal);
        Schedules = [.. byName.Values.OrderBy(schedule => schedule.Name, StringComparer.Ordinal)];
        Kinds = [.. _kinds.Keys.Order(StringComparer.Ordinal)];
        Leases = [.. Kinds.Select(kind => _kinds[kind].Options.Lease)];
        Problems = problems;
    }

    /// <summary>What a startup check reports; empty when every registration is sound.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The valid schedules, in name order (the lock order of the sync, SCH-2).</summary>
    public IReadOnlyList<ScheduledJob> Schedules { get; }

    /// <summary>The registered kinds, for the claim's per kind lease (CLM-1).</summary>
    public string[] Kinds { get; }

    /// <summary>Each of <see cref="Kinds"/>' lease, in the same order.</summary>
    public TimeSpan[] Leases { get; }

    public JobRegistration? Find(string kind) => _kinds.GetValueOrDefault(kind);

    public ScheduledJob? FindSchedule(string name) => _schedules.GetValueOrDefault(name);

    /// <summary>The kind's registration when it is for <typeparamref name="TJob"/> (ENQ-3).</summary>
    public JobRegistration For<TJob>()
        where TJob : IJob =>
        Find(TJob.Kind) is { } registration && registration.JobType == typeof(TJob)
            ? registration
            : throw new InvalidOperationException($"No job handler is registered for kind '{TJob.Kind}' (spec 0013 JQ-5).");

    /// <summary>The next occurrence of <paramref name="cron"/> strictly after <paramref name="after"/>, in UTC.</summary>
    public static DateTimeOffset? NextOccurrence(CronExpression cron, DateTimeOffset after) =>
        cron.GetNextOccurrence(after, TimeZoneInfo.Utc);

    private static string? Check(
        JobScheduleRegistration schedule, Dictionary<string, JobRegistration> kinds, DateTimeOffset now, out ScheduledJob? scheduled)
    {
        scheduled = null;
        if (schedule.Cron.Length > MaxCronLength)
        {
            return $"The job schedule {schedule.Name} has a cron expression over {MaxCronLength} characters (spec 0013 JDB-4).";
        }

        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(schedule.Cron, CronFormat.Standard);
        }
        catch (CronFormatException ex)
        {
            return $"The job schedule {schedule.Name} has a cron expression that is not valid: {ex.Message} (spec 0013 SCH-1).";
        }

        if (NextOccurrence(cron, now) is null)
        {
            return $"The job schedule {schedule.Name} has the cron expression '{schedule.Cron}', which never fires (spec 0013 SCH-1).";
        }

        if (!kinds.TryGetValue(schedule.Kind, out JobRegistration? registration) || registration.JobType != schedule.JobType)
        {
            return $"The job schedule {schedule.Name} runs kind {schedule.Kind}, which has no registered handler (spec 0013 SCH-1).";
        }

        string payload = registration.Serialize(schedule.Job);
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
        {
            return $"The job schedule {schedule.Name} has a payload over {MaxPayloadBytes} bytes (spec 0013 SCH-1).";
        }

        scheduled = new ScheduledJob(schedule.Name, schedule.Cron, cron, registration, payload);
        return null;
    }
}

/// <summary>A schedule that passed the checks, with its payload serialized once (SCH-1, SCH-4).</summary>
internal sealed record ScheduledJob(string Name, string CronText, CronExpression Cron, JobRegistration Registration, string Payload);
