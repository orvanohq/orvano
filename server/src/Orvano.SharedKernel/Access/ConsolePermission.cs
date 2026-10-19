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
