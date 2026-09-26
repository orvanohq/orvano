namespace Orvano.Platform.Contracts;

/// <summary>An org member's one role (AC-9).</summary>
public enum OrgRole
{
    /// <summary>Everything, including deleting projects, managing members, and the org itself.</summary>
    Owner,

    /// <summary>Builds: projects, keys, and platforms, but deletes nothing but their own keys.</summary>
    Developer,

    /// <summary>Reads only.</summary>
    Viewer,
}

/// <summary>Every console endpoint's permission check (spec 0003, module contracts).</summary>
public interface IConsoleAccess
{
    /// <summary>The console user's role in the org, or <see langword="null"/> when they are not a member.</summary>
    /// <param name="userId">The console user.</param>
    /// <param name="orgId">The org.</param>
    /// <param name="ct">Cancels the lookup.</param>
    Task<OrgRole?> GetOrgRoleAsync(Guid userId, Guid orgId, CancellationToken ct);

    /// <summary>The console user's role in the org that owns the project, or <see langword="null"/> when none.</summary>
    /// <param name="userId">The console user.</param>
    /// <param name="projectId">The project.</param>
    /// <param name="ct">Cancels the lookup.</param>
    Task<OrgRole?> GetProjectRoleAsync(Guid userId, string projectId, CancellationToken ct);
}
