namespace Orvano.Platform.Contracts;

/// <summary>What a project is for.</summary>
public enum ProjectKind
{
    /// <summary>A developer's project, owned by exactly one org.</summary>
    App,

    /// <summary>The seeded <c>console</c> project console accounts belong to. It has no org and no schema.</summary>
    System,
}

/// <summary>Where a project is in its lifecycle (spec 0003, state transitions).</summary>
public enum ProjectStatus
{
    /// <summary>Its schema and role are being created, or it was just restored.</summary>
    Provisioning,

    /// <summary>Served on public routes.</summary>
    Active,

    /// <summary>The last provisioning attempt failed; an owner or developer can retry.</summary>
    Failed,

    /// <summary>Deleted and not served; restorable until its purge.</summary>
    Deleting,
}

/// <summary>A project as other modules see it.</summary>
/// <param name="Id">The project ID, 1 to 60 characters of <c>[a-z0-9]</c>.</param>
/// <param name="OrgId">The owning org, or <see langword="null"/> for the <c>system</c> project.</param>
/// <param name="Name">Its display name, 1 to 100 characters (spec 0009: emails name the project).</param>
/// <param name="Kind">Whether it is an app or the system project.</param>
/// <param name="Status">Its lifecycle state.</param>
/// <param name="PurgeFailedAt">When its purge ran out of attempts, if it did.</param>
public sealed record ProjectInfo(string Id, Guid? OrgId, string Name, ProjectKind Kind, ProjectStatus Status, DateTimeOffset? PurgeFailedAt);

/// <summary>Whether a public route may serve a project (AC-4). Exactly one of the three cases.</summary>
public abstract record ProjectLookup
{
    private ProjectLookup() { }

    /// <summary>An <c>active</c> project of kind <c>app</c>: serve it.</summary>
    /// <param name="Project">The project.</param>
    public sealed record Servable(ProjectInfo Project) : ProjectLookup;

    /// <summary>The project is <c>provisioning</c> or <c>failed</c>: answer 409 <c>project_not_ready</c>.</summary>
    public sealed record NotReady : ProjectLookup;

    /// <summary>Unknown, purged, <c>deleting</c>, or <c>console</c>: answer 404 <c>project_not_found</c>.</summary>
    public sealed record NotFound : ProjectLookup;
}

/// <summary>The one place that decides whether a project exists and is served (spec 0003, module contracts).</summary>
public interface IProjectDirectory
{
    /// <summary>The only lookup a public route may use. Applies AC-4.</summary>
    /// <param name="projectId">The <c>X-Orvano-Project</c> value, unchecked.</param>
    /// <param name="ct">Cancels the lookup.</param>
    Task<ProjectLookup> GetServableAsync(string projectId, CancellationToken ct);

    /// <summary>A project in any state, for console routes and jobs only; <see langword="null"/> when unknown.</summary>
    /// <param name="projectId">The project ID, unchecked.</param>
    /// <param name="ct">Cancels the lookup.</param>
    Task<ProjectInfo?> GetAsync(string projectId, CancellationToken ct);
}
