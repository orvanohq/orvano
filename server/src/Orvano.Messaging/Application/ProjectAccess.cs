using Orvano.Platform.Contracts;

namespace Orvano.Messaging.Application;

/// <summary>A console user who passed the project checks, and their role in the project's org.</summary>
internal sealed record ProjectCaller(Guid UserId, string ProjectId, OrgRole Role)
{
    /// <summary>True for an owner or a developer, who may change email settings and templates.</summary>
    public bool CanChange => Role is OrgRole.Owner or OrgRole.Developer;
}

/// <summary>
/// The first two checks of every project email operation (spec 0009, AC-24): someone who is not a member of the
/// project's org gets 404 <c>project_not_found</c>, so existence never leaks, and a viewer gets 403 on a change.
/// </summary>
internal sealed class ProjectAccess(IConsoleAccess access)
{
    /// <param name="userId">The signed in console user.</param>
    /// <param name="projectId">The <c>X-Orvano-Project</c> value, or null when it was not sent.</param>
    /// <param name="change">True for an operation only owners and developers may call.</param>
    /// <param name="ct">Cancels the lookup.</param>
    public async Task<Outcome<ProjectCaller>> ForAsync(Guid userId, string? projectId, bool change, CancellationToken ct)
    {
        if (projectId is null) return Failure.ProjectHeaderMissing;
        if (await access.GetProjectRoleAsync(userId, projectId, ct) is not { } role) return Failure.ProjectNotFound;
        var caller = new ProjectCaller(userId, projectId, role);
        if (change && !caller.CanChange) return Failure.Forbidden;
        return caller;
    }
}
