using Orvano.Platform.Contracts;

namespace Orvano.Platform.Domain;

/// <summary>Everything a console member may try in an org (spec 0003, security model).</summary>
internal enum ConsoleAction
{
    /// <summary>See the org, its projects, members, keys, and platforms.</summary>
    View,
    CreateProject,
    EditProject,
    RetryProvisioning,
    DeleteProject,
    RestoreProject,
    RetryPurge,
    CreateApiKey,
    DeleteAnyApiKey,
    DeleteOwnApiKey,
    ManagePlatforms,
    ManageMembers,
    ManageOrg,
    LeaveOrg,
}

/// <summary>The role permission matrix (AC-9). Anything not listed is denied.</summary>
internal static class ConsolePermissions
{
    public static bool Allows(OrgRole role, ConsoleAction action) => action switch
    {
        ConsoleAction.View or ConsoleAction.LeaveOrg => true,
        ConsoleAction.CreateProject or ConsoleAction.EditProject or ConsoleAction.RetryProvisioning
            or ConsoleAction.CreateApiKey or ConsoleAction.DeleteOwnApiKey or ConsoleAction.ManagePlatforms
            => role is OrgRole.Owner or OrgRole.Developer,
        ConsoleAction.DeleteProject or ConsoleAction.RestoreProject or ConsoleAction.RetryPurge
            or ConsoleAction.DeleteAnyApiKey or ConsoleAction.ManageMembers or ConsoleAction.ManageOrg
            => role is OrgRole.Owner,
        _ => false,
    };

    /// <summary>An owner deletes any key; a developer only the keys they created; a viewer none.</summary>
    public static bool CanDeleteApiKey(OrgRole role, bool createdByCaller) =>
        Allows(role, ConsoleAction.DeleteAnyApiKey) || createdByCaller && Allows(role, ConsoleAction.DeleteOwnApiKey);
}

/// <summary>Wire names of the roles, the values stored in <c>platform_memberships.role</c>.</summary>
internal static class Roles
{
    public static string Wire(OrgRole role) => role switch
    {
        OrgRole.Owner => "owner",
        OrgRole.Developer => "developer",
        OrgRole.Viewer => "viewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static OrgRole Parse(string wire) => wire switch
    {
        "owner" => OrgRole.Owner,
        "developer" => OrgRole.Developer,
        "viewer" => OrgRole.Viewer,
        _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, "Unknown role."),
    };
}
