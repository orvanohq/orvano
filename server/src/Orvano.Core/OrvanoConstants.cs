namespace Orvano.Core;

/// <summary>Two integer advisory lock keys (spec 0002). New locks take the next object number.</summary>
public static class AdvisoryLocks
{
    /// <summary>"ORVA"</summary>
    public const int Class = 0x4F525641;
    public const int Migrations = 1;
    public const int SchedulerLeader = 2;
}

/// <summary>Schedule timing from spec 0002's value sourcing table.</summary>
public static class Timings
{
    public static readonly TimeSpan EventPruneInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan LeaseReaperInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan JobLease = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan JobHeartbeat = TimeSpan.FromSeconds(20);
    /// <summary>Fallback poll, because NOTIFY is not durable across a dropped connection.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    /// <summary>After failed dispatch passes the delay doubles from <see cref="PollInterval"/> up to this.</summary>
    public static readonly TimeSpan EventDispatchMaxRetryDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan LeaderRetry = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Maximum pool size per role and data source (spec 0002, connection budget). Set in code, never
/// from the connection string, so a self hoster cannot drift past Postgres' max_connections.
/// </summary>
public static class ConnectionBudget
{
    public const int ApiApp = 40;
    public const int WorkerApp = 15;
    public const int WorkerAdmin = 3;
    public const int RealtimeApp = 5;
    public const int MigrateAdmin = 2;
}
