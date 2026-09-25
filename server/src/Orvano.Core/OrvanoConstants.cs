namespace Orvano.Core;

/// <summary>Two integer advisory lock keys (spec 0002). New locks take the next object number.</summary>
public static class AdvisoryLocks
{
    /// <summary>"ORVA"</summary>
    public const int Class = 0x4F525641;
    /// <summary>Held while platform migrations run, so concurrent runs wait for each other.</summary>
    public const int Migrations = 1;

    /// <summary>Held by the one worker that runs internal schedules.</summary>
    public const int SchedulerLeader = 2;
}

/// <summary>Schedule timing from spec 0002's value sourcing table.</summary>
public static class Timings
{
    /// <summary>How often the leader deletes dispatched events past their retention.</summary>
    public static readonly TimeSpan EventPruneInterval = TimeSpan.FromHours(1);

    /// <summary>How often the leader requeues running jobs whose lease lapsed.</summary>
    public static readonly TimeSpan LeaseReaperInterval = TimeSpan.FromSeconds(30);

    /// <summary>How long a claimed job's lease lasts without a heartbeat.</summary>
    public static readonly TimeSpan JobLease = TimeSpan.FromSeconds(60);

    /// <summary>How often a running job renews its lease.</summary>
    public static readonly TimeSpan JobHeartbeat = TimeSpan.FromSeconds(20);

    /// <summary>Fallback poll, because NOTIFY is not durable across a dropped connection.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>After failed dispatch passes the delay doubles from <see cref="PollInterval"/> up to this.</summary>
    public static readonly TimeSpan EventDispatchMaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>How often a worker that is not the leader tries to take the leader lock.</summary>
    public static readonly TimeSpan LeaderRetry = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Maximum pool size per role and data source (spec 0002, connection budget). Set in code, never
/// from the connection string, so a self hoster cannot drift past Postgres' max_connections.
/// </summary>
public static class ConnectionBudget
{
    /// <summary><c>api</c> role, as <c>orvano_app</c>.</summary>
    public const int ApiApp = 40;

    /// <summary><c>worker</c> role, as <c>orvano_app</c>.</summary>
    public const int WorkerApp = 15;

    /// <summary><c>worker</c> role, as <c>orvano_admin</c>.</summary>
    public const int WorkerAdmin = 3;

    /// <summary><c>realtime</c> role, as <c>orvano_app</c>.</summary>
    public const int RealtimeApp = 5;

    /// <summary><c>migrate</c> role, as <c>orvano_admin</c>.</summary>
    public const int MigrateAdmin = 2;
}
