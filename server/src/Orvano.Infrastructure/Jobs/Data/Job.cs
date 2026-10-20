using Orvano.SharedKernel.Data;

namespace Orvano.Infrastructure.Jobs.Data;

/// <summary>
/// A queue row, <c>jobs.job</c> (spec 0013 JDB-2). Only the queue's and runner's SQL change it, so it is neither
/// <see cref="ITimestamped"/> nor <see cref="IVersioned"/>: the attempt fence (CLM-4) replaces <c>xmin</c>.
/// </summary>
internal sealed class Job : Entity
{
    [DataClass(DataClass.Public)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>The job record as JSON (ids and codes only, spec 0011 CLS-2), at most 65,536 bytes as sent.</summary>
    [DataClass(DataClass.Personal)]
    public string Payload { get; set; } = "{}";

    /// <summary>A plain id with no foreign key (spec 0011 REF-4).</summary>
    public Guid? ProjectId { get; set; }

    public JobStatus Status { get; set; }

    /// <summary>0 at enqueue, plus 1 per claim, minus 1 on a shutdown release.</summary>
    public int Attempt { get; set; }

    public int MaxAttempts { get; set; }

    public DateTimeOffset RunAt { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary><c>&lt;machine&gt;:&lt;instance id&gt;</c> of the worker holding the lease (CLM-2).</summary>
    [DataClass(DataClass.Public)]
    public string? LockedBy { get; set; }

    /// <summary>The last failure's type and message (OUT-5); it may quote input.</summary>
    [DataClass(DataClass.Personal)]
    public string? LastError { get; set; }

    [DataClass(DataClass.Personal)]
    public string? DedupeKey { get; set; }

    /// <summary>The W3C <c>traceparent</c> of the enqueuing activity (JTL-3).</summary>
    [DataClass(DataClass.Public)]
    public string? TraceParent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>A job's state (spec 0013 _Job state transitions_), stored as text with <c>ck_job_status</c>.</summary>
internal enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Dead,
}

/// <summary>
/// A schedule's state, <c>jobs.schedule</c> (spec 0013 JDB-3). Its kind and payload live in code, in the registration
/// with the same name; <see cref="Cron"/> is kept only to notice a change (SCH-2).
/// </summary>
internal sealed class JobSchedule : Entity, ITimestamped, IVersioned
{
    [DataClass(DataClass.Public)]
    public string Name { get; set; } = string.Empty;

    [DataClass(DataClass.Public)]
    public string Cron { get; set; } = string.Empty;

    /// <summary>True on insert and kept across syncs; feature 46 toggles it.</summary>
    public bool Enabled { get; set; }

    public DateTimeOffset NextRunAt { get; set; }

    public DateTimeOffset? LastFiredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
