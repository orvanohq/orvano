namespace Orvano.SharedKernel.Access;

/// <summary>
/// The console permission list (spec 0012 CPL-1). <c>Data*</c> covers both table rows and storage files;
/// <see cref="SchemaWrite"/> covers tables, buckets, and functions' grants and <c>run_scopes</c>.
/// </summary>
#pragma warning disable CA1711 // CPL-1: spec 0012 names this type ConsolePermission; it is not a CAS permission.
public enum ConsolePermission
#pragma warning restore CA1711
{
    OrgRead,
    OrgWrite,
    OrgDelete,
    MembersRead,
    MembersWrite,
    ProjectsCreate,
    ProjectRead,
    ProjectWrite,
    ProjectDelete,
    KeysRead,

    /// <summary>CPL-5: may mint a secret key with any scope in the ceiling, so it equals full data power.</summary>
    KeysWrite,
    UsersRead,
    UsersWrite,
    DataRead,
    DataWrite,
    SchemaWrite,
    FunctionsDeploy,
    MessagingWrite,
    SitesWrite,
    AuditRead,
    UsageRead,
}

/// <summary>The names of <see cref="ConsolePermission"/> on the wire (CPL-1, spec 0014 CPX-1).</summary>
public static class ConsolePermissions
{
    /// <summary>What an organization's payload can allow (spec 0017 SES-5).</summary>
    public static IReadOnlyList<ConsolePermission> OnOrganization { get; } =
    [
        ConsolePermission.OrgRead, ConsolePermission.OrgWrite, ConsolePermission.OrgDelete, ConsolePermission.MembersRead,
        ConsolePermission.MembersWrite, ConsolePermission.ProjectsCreate,
    ];

    /// <summary>What a project's payload can allow.</summary>
    public static IReadOnlyList<ConsolePermission> OnProject { get; } =
    [
        ConsolePermission.ProjectRead, ConsolePermission.ProjectWrite, ConsolePermission.ProjectDelete, ConsolePermission.KeysRead,
        ConsolePermission.KeysWrite, ConsolePermission.UsersRead, ConsolePermission.UsersWrite, ConsolePermission.DataRead,
        ConsolePermission.DataWrite, ConsolePermission.SchemaWrite, ConsolePermission.FunctionsDeploy, ConsolePermission.MessagingWrite,
        ConsolePermission.SitesWrite, ConsolePermission.AuditRead, ConsolePermission.UsageRead,
    ];

    /// <summary>What an API key's payload can allow.</summary>
    public static IReadOnlyList<ConsolePermission> OnKey { get; } = [ConsolePermission.KeysRead, ConsolePermission.KeysWrite];

    public static string ToToken(this ConsolePermission permission) => permission switch
    {
        ConsolePermission.OrgRead => "org.read",
        ConsolePermission.OrgWrite => "org.write",
        ConsolePermission.OrgDelete => "org.delete",
        ConsolePermission.MembersRead => "members.read",
        ConsolePermission.MembersWrite => "members.write",
        ConsolePermission.ProjectsCreate => "projects.create",
        ConsolePermission.ProjectRead => "project.read",
        ConsolePermission.ProjectWrite => "project.write",
        ConsolePermission.ProjectDelete => "project.delete",
        ConsolePermission.KeysRead => "keys.read",
        ConsolePermission.KeysWrite => "keys.write",
        ConsolePermission.UsersRead => "users.read",
        ConsolePermission.UsersWrite => "users.write",
        ConsolePermission.DataRead => "data.read",
        ConsolePermission.DataWrite => "data.write",
        ConsolePermission.SchemaWrite => "schema.write",
        ConsolePermission.FunctionsDeploy => "functions.deploy",
        ConsolePermission.MessagingWrite => "messaging.write",
        ConsolePermission.SitesWrite => "sites.write",
        ConsolePermission.AuditRead => "audit.read",
        ConsolePermission.UsageRead => "usage.read",
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null),
    };

    /// <summary>
    /// CPX-1: the names of <paramref name="onResource"/> that <paramref name="role"/> holds, the <c>allowedActions</c>
    /// of a payload.
    /// </summary>
    public static string[] Allowed(string? role, IReadOnlyList<ConsolePermission> onResource)
    {
        ArgumentNullException.ThrowIfNull(onResource);
        IReadOnlySet<ConsolePermission> held = RolePermissions.For(role);
        return [.. onResource.Where(held.Contains).Select(ToToken)];
    }
}

/// <summary>
/// CPL-2: what each <c>platform.membership.role</c> may do, in code. Feature 51 adds roles here and to the role
/// CHECK; endpoints do not change.
/// </summary>
public static class RolePermissions
{
    public const string Owner = "owner";

    private static readonly IReadOnlySet<ConsolePermission> None = new HashSet<ConsolePermission>();

    private static readonly IReadOnlySet<ConsolePermission> Everything = Enum.GetValues<ConsolePermission>().ToHashSet();

    /// <summary>The permissions of <paramref name="role"/>; an unknown role has none.</summary>
    public static IReadOnlySet<ConsolePermission> For(string? role) => role switch
    {
        Owner => Everything,
        _ => None,
    };
}
