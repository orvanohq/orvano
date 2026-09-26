using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Jobs;
using Orvano.Core.Paging;
using Orvano.Platform.Contracts;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;
using Orvano.Platform.Jobs;

namespace Orvano.Platform.Application;

/// <summary>
/// Project use cases (spec 0003, state transitions). Every transition is one conditional
/// <c>UPDATE ... WHERE id = @id AND status = @expected</c> that checks the row count, never a read then a write, and
/// enqueues its job and writes its event in the same transaction.
/// </summary>
internal sealed class ProjectService(PlatformStore store, DeleteGrace grace, ILogger<ProjectService> logger)
{
    private const int MaxIdAttempts = 3;

    public Task<Outcome<Page<ProjectRow>>> ListAsync(Guid userId, Guid orgId, string? cursor, int? limit, CancellationToken ct) =>
        store.ReadAsync<Outcome<Page<ProjectRow>>>(async (db, ct) =>
        {
            if (PageCursor.Limit(limit) is not { } size) return Failure.Invalid($"limit must be 1 to {PageCursor.MaxLimit}.");
            if (await db.RoleInAsync(userId, orgId, ct) is null) return Failure.OrgNotFound;

            var query = db.Projects.Where(p => p.OrgId == orgId);
            if (cursor is not null)
            {
                if (!PageCursor.TryDecode(cursor, out var after)) return Failure.InvalidCursor;
                query = query.Where(p => p.CreatedAt > after.CreatedAt || p.CreatedAt == after.CreatedAt && string.Compare(p.Id, after.Id) > 0);
            }

            var rows = await query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Take(size + 1).AsNoTracking().ToListAsync(ct);
            var items = rows.Take(size).ToList();
            var next = rows.Count > size ? PageCursor.Encode(new PagePosition(items[^1].CreatedAt, items[^1].Id)) : null;
            return new Page<ProjectRow>(items, next);
        }, ct);

    public Task<Outcome<ProjectRow>> GetAsync(Guid userId, string? projectId, CancellationToken ct) =>
        store.ReadAsync<Outcome<ProjectRow>>(async (db, ct) =>
        {
            var (project, _, failure) = await FindAsync(db, userId, projectId, ConsoleAction.View, ct);
            return failure is not null ? failure : project!;
        }, ct);

    /// <summary>AC-2, AC-3: a new ID, <c>provisioning</c>, and the provision job in the same transaction.</summary>
    public async Task<Outcome<ProjectRow>> CreateAsync(Guid userId, Guid orgId, string? name, CancellationToken ct)
    {
        if (!Names.TryNormalize(name, out var projectName)) return Failure.Invalid($"A project name is 1 to {Names.MaxLength} characters.");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await store.WriteAsync<ProjectRow>(async (uow, ct) =>
                {
                    var org = await uow.LockOrgForShareAsync(orgId, ct);
                    var role = org is null ? null : await uow.Db.RoleInAsync(userId, orgId, ct);
                    if (org is null || role is null) return Failure.OrgNotFound;
                    if (!ConsolePermissions.Allows(Roles.Parse(role), ConsoleAction.CreateProject)) return Failure.Forbidden;
                    if (Statuses.Org(org.Status) != OrgStatus.Active) return Failure.OrgNotActive;

                    var project = await AddProjectAsync(uow, ProjectIds.New(), orgId, projectName, userId, Actor.User(userId), ct);
                    logger.LogInformation("Console user {UserId} created project {ProjectId} in org {OrgId}", userId, project.Id, orgId);
                    return project;
                }, ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } && attempt < MaxIdAttempts)
            {
                // A 20 character random ID practically never collides, but a collision retries with a new one.
            }
        }
    }

    public Task<Outcome<ProjectRow>> RenameAsync(Guid userId, string? projectId, string? name, CancellationToken ct) =>
        store.WriteAsync<ProjectRow>(async (uow, ct) =>
        {
            if (!Names.TryNormalize(name, out var projectName)) return Failure.Invalid($"A project name is 1 to {Names.MaxLength} characters.");
            var (project, _, failure) = await FindAsync(uow.Db, userId, projectId, ConsoleAction.EditProject, ct);
            if (failure is not null) return failure;
            if (await ActiveOrgFailureAsync(uow, project!, ct) is { } inactive) return inactive;

            if (project!.Name != projectName)
            {
                var now = await uow.NowAsync(ct);
                await uow.Db.Projects.Where(p => p.Id == project.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, projectName).SetProperty(p => p.UpdatedAt, now), ct);
                await WriteEventAsync(uow, PlatformEvents.ProjectUpdated, userId, project, ["name"], ct);
                logger.LogInformation("Console user {UserId} renamed project {ProjectId}", userId, project.Id);
            }

            return await ReloadAsync(uow, project.Id, ct);
        }, ct);

    /// <summary>AC-14: stops serving at once and schedules the purge after the grace period.</summary>
    public Task<Outcome<ProjectRow>> DeleteAsync(Guid userId, string? projectId, CancellationToken ct) =>
        store.WriteAsync<ProjectRow>(async (uow, ct) =>
        {
            var (project, _, failure) = await FindAsync(uow.Db, userId, projectId, ConsoleAction.DeleteProject, ct);
            if (failure is not null) return failure;
            if (await ActiveOrgFailureAsync(uow, project!, ct) is { } inactive) return inactive;

            var now = await uow.NowAsync(ct);
            var purgeAfter = ProjectLifecycle.PurgeAfter(now, grace);
            var from = ProjectLifecycle.DeletableFrom.Select(Statuses.Wire).ToArray();
            var updated = await uow.Db.Projects.Where(p => p.Id == project!.Id && from.Contains(p.Status))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, Statuses.Wire(ProjectStatus.Deleting))
                    .SetProperty(p => p.DeletedAt, now)
                    .SetProperty(p => p.PurgeAfter, purgeAfter)
                    .SetProperty(p => p.PurgeFailedAt, (DateTimeOffset?)null)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated == 0) return Failure.ProjectNotReady("The project is already being deleted.");

            await JobQueue.EnqueueAsync(uow.Tx, PlatformJobs.Purge(project!.Id, purgeAfter), ct);
            await WriteEventAsync(uow, PlatformEvents.ProjectDeleting, userId, project, null, ct);
            logger.LogInformation("Console user {UserId} deleted project {ProjectId}", userId, project.Id);
            return await ReloadAsync(uow, project.Id, ct);
        }, ct);

    /// <summary>There is one path back, whatever the state before the delete: <c>provisioning</c> and the provision job.</summary>
    public Task<Outcome<ProjectRow>> RestoreAsync(Guid userId, string? projectId, CancellationToken ct) =>
        store.WriteAsync<ProjectRow>(async (uow, ct) =>
        {
            var (project, _, failure) = await FindAsync(uow.Db, userId, projectId, ConsoleAction.RestoreProject, ct);
            if (failure is not null) return failure;
            if (await ActiveOrgFailureAsync(uow, project!, ct) is { } inactive) return inactive;

            var now = await uow.NowAsync(ct);
            var updated = await uow.Db.Projects
                .Where(p => p.Id == project!.Id && p.Status == Statuses.Wire(ProjectLifecycle.RestoreFrom))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, Statuses.Wire(ProjectStatus.Provisioning))
                    .SetProperty(p => p.DeletedAt, (DateTimeOffset?)null)
                    .SetProperty(p => p.PurgeAfter, (DateTimeOffset?)null)
                    .SetProperty(p => p.PurgeFailedAt, (DateTimeOffset?)null)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated == 0) return Failure.ProjectNotReady("Only a deleted project can be restored.");

            await JobQueue.EnqueueAsync(uow.Tx, PlatformJobs.Provision(project!.Id), ct);
            await WriteEventAsync(uow, PlatformEvents.ProjectRestored, userId, project, null, ct);
            logger.LogInformation("Console user {UserId} restored project {ProjectId}", userId, project.Id);
            return await ReloadAsync(uow, project.Id, ct);
        }, ct);

    public Task<Outcome<ProjectRow>> RetryProvisioningAsync(Guid userId, string? projectId, CancellationToken ct) =>
        store.WriteAsync<ProjectRow>(async (uow, ct) =>
        {
            var (project, _, failure) = await FindAsync(uow.Db, userId, projectId, ConsoleAction.RetryProvisioning, ct);
            if (failure is not null) return failure;
            if (await ActiveOrgFailureAsync(uow, project!, ct) is { } inactive) return inactive;

            var now = await uow.NowAsync(ct);
            var updated = await uow.Db.Projects
                .Where(p => p.Id == project!.Id && p.Status == Statuses.Wire(ProjectLifecycle.RetryProvisioningFrom))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, Statuses.Wire(ProjectStatus.Provisioning))
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated == 0) return Failure.ProjectNotReady("Only a project whose provisioning failed can retry it.");

            await JobQueue.EnqueueAsync(uow.Tx, PlatformJobs.Provision(project!.Id), ct);
            await WriteEventAsync(uow, PlatformEvents.ProjectUpdated, userId, project, ["status"], ct);
            logger.LogInformation("Console user {UserId} retried provisioning project {ProjectId}", userId, project.Id);
            return await ReloadAsync(uow, project.Id, ct);
        }, ct);

    /// <summary>AC-14: owners, and install admins on any org, since a failed purge can hold personal data.</summary>
    public Task<Outcome<ProjectRow>> RetryPurgeAsync(Guid userId, string? projectId, CancellationToken ct) =>
        store.WriteAsync<ProjectRow>(async (uow, ct) =>
        {
            var (project, _, failure) = await FindAsync(uow.Db, userId, projectId, ConsoleAction.RetryPurge, ct);
            if (failure is not null)
            {
                // An install admin may act without a membership, but still only on a real app project.
                if (failure.Kind == FailureKind.Invalid || !await uow.Db.IsInstallAdminAsync(userId, ct)) return failure;
                project = await FindAppProjectAsync(uow.Db, projectId, ct);
                if (project is null) return Failure.ProjectNotFound;
            }

            var now = await uow.NowAsync(ct);
            var updated = await uow.Db.Projects
                .Where(p => p.Id == project!.Id && p.Status == Statuses.Wire(ProjectStatus.Deleting) && p.PurgeFailedAt != null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.PurgeFailedAt, (DateTimeOffset?)null)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated == 0) return Failure.ProjectNotReady("Only a deleted project whose purge failed can retry it.");

            await JobQueue.EnqueueAsync(uow.Tx, PlatformJobs.Purge(project!.Id, now), ct);
            await WriteEventAsync(uow, PlatformEvents.ProjectUpdated, userId, project, ["purgeFailedAt"], ct);
            logger.LogInformation("Console user {UserId} retried the purge of project {ProjectId}", userId, project.Id);
            return await ReloadAsync(uow, project.Id, ct);
        }, ct);

    /// <summary>Inserts a <c>provisioning</c> project, its provision job, and its event.</summary>
    internal static async Task<ProjectRow> AddProjectAsync(UnitOfWork uow, string id, Guid orgId, string name, Guid createdBy, Actor actor, CancellationToken ct)
    {
        var now = await uow.NowAsync(ct);
        var project = new ProjectRow
        {
            Id = id,
            OrgId = orgId,
            Kind = Statuses.Wire(ProjectKind.App),
            Name = name,
            Status = Statuses.Wire(ProjectStatus.Provisioning),
            CreatedByUserId = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
        uow.Db.Projects.Add(project);
        await uow.Db.SaveChangesAsync(ct);
        await JobQueue.EnqueueAsync(uow.Tx, PlatformJobs.Provision(project.Id), ct);
        await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.ProjectCreated, actor, project.Id,
            new Dictionary<string, string> { ["orgId"] = orgId.ToString(), ["projectId"] = project.Id }, project.Id, ct: ct);
        return project;
    }

    /// <summary>
    /// The project and the caller's role, checked against <paramref name="action"/>. Unknown, <c>console</c>, and projects
    /// of orgs the caller is not in are all 404 <c>project_not_found</c>, so a project's existence never leaks.
    /// </summary>
    internal static async Task<(ProjectRow? Project, OrgRole? Role, Failure? Failure)> FindAsync(
        PlatformDbContext db, Guid userId, string? projectId, ConsoleAction action, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectId)) return (null, null, Failure.ProjectHeaderMissing);
        var project = await FindAppProjectAsync(db, projectId, ct);
        if (project is null) return (null, null, Failure.ProjectNotFound);
        var role = await db.RoleInAsync(userId, project.OrgId!.Value, ct);
        if (role is null) return (null, null, Failure.ProjectNotFound);
        var parsed = Roles.Parse(role);
        return ConsolePermissions.Allows(parsed, action) ? (project, parsed, null) : (null, parsed, Failure.Forbidden);
    }

    private static async Task<ProjectRow?> FindAppProjectAsync(PlatformDbContext db, string? projectId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectId)) return null;
        var app = Statuses.Wire(ProjectKind.App);
        return await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId && p.Kind == app, ct);
    }

    /// <summary>AC-15: while its org is deleting, a project can't change. The org row is taken <c>FOR SHARE</c>.</summary>
    private static async Task<Failure?> ActiveOrgFailureAsync(UnitOfWork uow, ProjectRow project, CancellationToken ct)
    {
        var org = await uow.LockOrgForShareAsync(project.OrgId!.Value, ct);
        return org is not null && Statuses.Org(org.Status) == OrgStatus.Active ? null : Failure.OrgNotActive;
    }

    private static async Task<Outcome<ProjectRow>> ReloadAsync(UnitOfWork uow, string projectId, CancellationToken ct) =>
        await uow.Db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct) is { } row ? row : Failure.ProjectNotFound;

    private static Task WriteEventAsync(UnitOfWork uow, string type, Guid userId, ProjectRow project, IReadOnlyList<string>? changed, CancellationToken ct) =>
        PlatformEvents.WriteAsync(uow.Tx, type, Actor.User(userId), project.Id,
            new Dictionary<string, string> { ["orgId"] = project.OrgId!.Value.ToString(), ["projectId"] = project.Id },
            project.Id, changed, ct);
}
