namespace Orvano.SharedKernel.Projects;

/// <summary>An <c>active</c> project, as the data plane sees it (spec 0011 LIF-4).</summary>
public sealed record ActiveProject(Guid Id, Guid OrganizationId, IReadOnlyList<string> AllowedOrigins);

/// <summary>
/// Whether a project may reach the data plane (spec 0017 PJT-3). It lives here so Auth never references Platform (PSW-3).
/// The answer is cached for up to 60 seconds, so never use it to decide whether a key still works (AKY-9).
/// </summary>
public interface IProjectDirectory
{
    /// <summary>The project when it's <c>active</c>, else null.</summary>
    ValueTask<ActiveProject?> FindActiveAsync(Guid projectId, CancellationToken cancellationToken);
}
