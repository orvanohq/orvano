using Orvano.Platform.Contracts;

namespace Orvano.Platform.Domain;

/// <summary>The states of an org.</summary>
internal enum OrgStatus
{
    Active,
    Deleting,
}

/// <summary>The project state machine: which state each console transition starts from.</summary>
internal static class ProjectLifecycle
{
    /// <summary>Delete is allowed from every live state.</summary>
    public static readonly ProjectStatus[] DeletableFrom = [ProjectStatus.Provisioning, ProjectStatus.Active, ProjectStatus.Failed];

    /// <summary>Only a failed project can retry provisioning.</summary>
    public const ProjectStatus RetryProvisioningFrom = ProjectStatus.Failed;

    /// <summary>Only a deleting project can be restored, and restoring always goes through provisioning.</summary>
    public const ProjectStatus RestoreFrom = ProjectStatus.Deleting;

    /// <summary>A live project is one that is not deleting (AC-10, AC-15).</summary>
    public static bool IsLive(ProjectStatus status) => status != ProjectStatus.Deleting;

    /// <summary>A purge can be retried only while deleting with a recorded purge failure.</summary>
    public static bool CanRetryPurge(ProjectStatus status, DateTimeOffset? purgeFailedAt) =>
        status == ProjectStatus.Deleting && purgeFailedAt is not null;

    /// <summary>When a project or org deleted at <paramref name="now"/> is purged.</summary>
    public static DateTimeOffset PurgeAfter(DateTimeOffset now, DeleteGrace grace) => now.AddDays(grace.Days);
}

/// <summary>Wire names of the statuses, the values stored in the <c>status</c> columns.</summary>
internal static class Statuses
{
    public static string Wire(ProjectStatus status) => status switch
    {
        ProjectStatus.Provisioning => "provisioning",
        ProjectStatus.Active => "active",
        ProjectStatus.Failed => "failed",
        ProjectStatus.Deleting => "deleting",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static ProjectStatus Project(string wire) => wire switch
    {
        "provisioning" => ProjectStatus.Provisioning,
        "active" => ProjectStatus.Active,
        "failed" => ProjectStatus.Failed,
        "deleting" => ProjectStatus.Deleting,
        _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, "Unknown project status."),
    };

    public static string Wire(OrgStatus status) => status switch
    {
        OrgStatus.Active => "active",
        OrgStatus.Deleting => "deleting",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string Wire(ProjectKind kind) => kind switch
    {
        ProjectKind.App => "app",
        ProjectKind.System => "system",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static ProjectKind Kind(string wire) => wire switch
    {
        "app" => ProjectKind.App,
        "system" => ProjectKind.System,
        _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, "Unknown project kind."),
    };

    public static OrgStatus Org(string wire) => wire switch
    {
        "active" => OrgStatus.Active,
        "deleting" => OrgStatus.Deleting,
        _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, "Unknown org status."),
    };
}

/// <summary>Days between deleting a project or org and its purge (<c>ORVANO_DELETE_GRACE_DAYS</c>, 0 to 90, default 7).</summary>
internal sealed record DeleteGrace(int Days)
{
    public const string Setting = "ORVANO_DELETE_GRACE_DAYS";
    public const int Default = 7;
    public const int Max = 90;
}
