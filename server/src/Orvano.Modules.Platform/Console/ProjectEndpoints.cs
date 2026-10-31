using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Projects;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;

namespace Orvano.Modules.Platform.Console;

/// <summary>Projects (spec 0017 PJT-1, PJT-5, PJT-7).</summary>
internal static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjects(this RouteGroupBuilder console)
    {
        console.MapGet("/organizations/{organizationId:guid}/projects", ListAsync)
            .RequireConsolePermission(ConsolePermission.ProjectRead)
            .WithName("Platform_ListProjects")
            .WithSummary("List an organization's projects.")
            .WithDescription("Lists every status, oldest first. `query` matches the name, ignoring case.")
            .WithErrors(ErrorCodes.ValidationFailed, ErrorCodes.InvalidCursor)
            .WithPaging();

        console.MapPost("/organizations/{organizationId:guid}/projects", CreateAsync)
            .RequireConsolePermission(ConsolePermission.ProjectsCreate)
            .WithName("Platform_CreateProject")
            .WithSummary("Create a project.")
            .WithDescription("Answers 201 once the project is active. If a step fails, the project goes to teardown and the "
                + "answer is an error.")
            .WithErrors(ErrorCodes.ValidationFailed)
            .ProducesValidationProblem();

        console.MapGet("/projects/{projectId:guid}", GetAsync)
            .RequireConsolePermission(ConsolePermission.ProjectRead)
            .WithName("Platform_GetProject")
            .WithSummary("Read a project.")
            .WithDescription("Answers 404 for a project outside the operator's organizations.")
            .WithErrors();

        console.MapPatch("/projects/{projectId:guid}", UpdateAsync)
            .RequireConsolePermission(ConsolePermission.ProjectWrite)
            .WithConcurrency()
            .WithName("Platform_UpdateProject")
            .WithSummary("Change a project's name or allowed origins.")
            .WithDescription("A change to the origins applies within 60 seconds.")
            .WithErrors(ErrorCodes.ValidationFailed, ErrorCodes.VersionConflict, PlatformErrorCodes.ProjectNotActive)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        console.MapDelete("/projects/{projectId:guid}", DeleteAsync)
            .RequireConsolePermission(ConsolePermission.ProjectDelete)
            .RequireRecentReauth()
            .WithConcurrency()
            .WithName("Platform_DeleteProject")
            .WithSummary("Delete a project.")
            .WithDescription("Answers 202 once the project is `deleting`: its keys stop on their next request, and a job "
                + "removes it. A project already `deleting` answers 202 too.")
            .WithErrors(ErrorCodes.VersionConflict, PlatformErrorCodes.ProjectNotActive, PlatformErrorCodes.ReauthRequired)
            .Produces<ProjectResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        console.MapGet("/projects/{projectId:guid}/dependents", DependentsAsync)
            .RequireConsolePermission(ConsolePermission.ProjectRead)
            .WithName("Platform_GetProjectDependents")
            .WithSummary("Count what goes with a project.")
            .WithDescription("Counts what deleting the project removes with it.")
            .WithErrors();
        return console;
    }

    internal static async Task<Results<Ok<Page<ProjectResponse>>, ErrorProblemResult>> ListAsync(
        Guid organizationId,
        [AsParameters] PageQuery page,
        [AsParameters] NameFilterQuery filter,
        PlatformDbContext db,
        IMembershipLookup memberships,
        ICallerContext caller,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        string cursorQuery = CursorCodec.Query(http.Request);
        IQueryable<Project> rows = db.Projects.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (page.Cursor is { } cursor)
        {
            Result<CursorKeys> keys = CursorCodec.Decode(cursor, cursorQuery, CursorKey.Uuid);
            if (!keys.IsSuccess)
            {
                return keys.Error.ToProblem();
            }

            Guid after = keys.Value.GetUuid(0);
            rows = rows.Where(x => x.Id.CompareTo(after) > 0);
        }

        if (filter.Query is { Length: > 0 } query)
        {
            string pattern = ConsoleSearch.Contains(query);
            rows = rows.Where(x => EF.Functions.ILike(x.Name, pattern, ConsoleSearch.Escape));
        }

        int limit = page.Size;

        // Deviation DAT-2a: FLT-1's control plane read of an organization's projects; the membership was checked instead.
        var found = await rows.OrderBy(x => x.Id).Take(limit + 1)
            .GroupJoin(
                db.ProjectSettings.IgnoreQueryFilters([ProjectFilter.Name]).AsNoTracking(),
                project => project.Id,
                settings => settings.ProjectId,
                (project, settings) => new { Project = project, Settings = settings.FirstOrDefault() })
            .OrderBy(x => x.Project.Id)
            .ToListAsync(cancellationToken);
        string? role = await memberships.FindRoleAsync(caller.Current.OperatorId!.Value, organizationId, cancellationToken);
        return TypedResults.Ok(Page.From(
            found,
            limit,
            cursorQuery,
            row => new JsonArray(row.Project.Id.ToString("D")),
            row => ToResponse(row.Project, row.Settings?.AllowedOrigins ?? [], role)));
    }

    internal static async Task<Results<Created<ProjectResponse>, ErrorProblemResult>> CreateAsync(
        Guid organizationId,
        CreateProjectRequest request,
        ProjectCreation creation,
        IMembershipLookup memberships,
        ConsoleAudit audit,
        ICallerContext caller,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        Result<string[]> origins = AllowedOrigins.Normalize(request.AllowedOrigins);
        if (!origins.IsSuccess)
        {
            return origins.Error.ToProblem();
        }

        Project project = await creation.CreateAsync(organizationId, request.Name.Trim(), origins.Value, cancellationToken);
        Guid operatorId = caller.Current.OperatorId!.Value;
        await audit.RecordAsync(http, AuditActions.ProjectCreated, operatorId, "project", project.Id, organizationId, project.Id);
        string? role = await memberships.FindRoleAsync(operatorId, organizationId, cancellationToken);
        return TypedResults.Created((string?)null, ToResponse(project, origins.Value, role));
    }

    internal static async Task<Results<Ok<ProjectResponse>, ErrorProblemResult>> GetAsync(
        Guid projectId, PlatformDbContext db, IMembershipLookup memberships, ICallerContext caller, HttpContext http, CancellationToken cancellationToken)
    {
        Project? project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);
        string[]? origins = await db.ProjectSettings.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.AllowedOrigins)
            .SingleOrDefaultAsync(cancellationToken);
        if (project is null || origins is null)
        {
            return AccessErrors.NotFound.ToProblem();
        }

        string? role = await memberships.FindRoleAsync(caller.Current.OperatorId!.Value, project.OrganizationId, cancellationToken);
        ResourceVersion.SetETag(http.Response, project.Version);
        return TypedResults.Ok(ToResponse(project, origins, role));
    }

    internal static async Task<Results<Ok<ProjectResponse>, ErrorProblemResult>> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        IfMatch ifMatch,
        PlatformDbContext db,
        ICacheInvalidator invalidator,
        IMembershipLookup memberships,
        ConsoleAudit audit,
        ICallerContext caller,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        string? name = null;
        if (request.Name.HasValue)
        {
            name = request.Name.Value.Trim();
            if (name.Length == 0)
            {
                return ConsoleFields.Invalid("name", FieldCodes.Required).ToProblem();
            }
        }

        string[]? origins = null;
        if (request.AllowedOrigins.HasValue)
        {
            Result<string[]> normalized = AllowedOrigins.Normalize(request.AllowedOrigins.Value);
            if (!normalized.IsSuccess)
            {
                return normalized.Error.ToProblem();
            }

            origins = normalized.Value;
        }

        Result<(Project Project, string[] Origins)> updated = await db.InTransactionAsync<Result<(Project, string[])>>(
            async token =>
            {
                await LockAsync(db, projectId, token);
                Project project = await db.Projects.SingleAsync(x => x.Id == projectId, token);
                if (project.Status != ProjectStatus.Active)
                {
                    return PlatformErrors.ProjectNotActive;
                }

                Result fresh = ifMatch.Check(project.Version);
                if (!fresh.IsSuccess)
                {
                    return fresh.Error;
                }

                ProjectSettings settings = await db.ProjectSettings.SingleAsync(x => x.ProjectId == projectId, token);
                project.Name = name ?? project.Name;
                settings.AllowedOrigins = origins ?? settings.AllowedOrigins;

                // PJT-5: the project's version moves even when only the origins change.
                db.Entry(project).Property(x => x.UpdatedAt).IsModified = true;
                await db.SaveChangesAsync(token);
                if (origins is not null)
                {
                    invalidator.Invalidate(db, ProjectDirectory.Tag(projectId)); // PJT-4
                }

                return (project, settings.AllowedOrigins);
            },
            cancellationToken);
        if (!updated.IsSuccess)
        {
            return updated.Error.ToProblem();
        }

        Project saved = updated.Value.Project;
        Guid operatorId = caller.Current.OperatorId!.Value;
        await audit.RecordAsync(http, AuditActions.ProjectUpdated, operatorId, "project", projectId, saved.OrganizationId, projectId);
        string? role = await memberships.FindRoleAsync(operatorId, saved.OrganizationId, cancellationToken);
        ResourceVersion.SetETag(http.Response, saved.Version);
        return TypedResults.Ok(ToResponse(saved, updated.Value.Origins, role));
    }

    internal static async Task<Results<Accepted<ProjectResponse>, ErrorProblemResult>> DeleteAsync(
        Guid projectId,
        IfMatch ifMatch,
        PlatformDbContext db,
        ProjectDeletion deletion,
        IMembershipLookup memberships,
        ConsoleAudit audit,
        ICallerContext caller,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        bool requested = false;
        Result<Project> marked = await db.InTransactionAsync<Result<Project>>(
            async token =>
            {
                requested = false;
                await LockAsync(db, projectId, token);
                Project project = await db.Projects.SingleAsync(x => x.Id == projectId, token);
                if (project.Status == ProjectStatus.Deleting)
                {
                    return project;
                }

                if (project.Status != ProjectStatus.Active)
                {
                    return PlatformErrors.ProjectNotActive;
                }

                Result fresh = ifMatch.Check(project.Version);
                if (!fresh.IsSuccess)
                {
                    return fresh.Error;
                }

                await deletion.MarkAsync(project, token);
                requested = true;
                return project;
            },
            cancellationToken);
        if (!marked.IsSuccess)
        {
            return marked.Error.ToProblem();
        }

        Project saved = marked.Value;
        Guid operatorId = caller.Current.OperatorId!.Value;
        if (requested)
        {
            await audit.RecordAsync(http, AuditActions.ProjectDeleteRequested, operatorId, "project", projectId, saved.OrganizationId, projectId);
        }

        string[] origins = await db.ProjectSettings.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.AllowedOrigins)
            .SingleAsync(cancellationToken);
        string? role = await memberships.FindRoleAsync(operatorId, saved.OrganizationId, cancellationToken);
        ResourceVersion.SetETag(http.Response, saved.Version);
        return TypedResults.Accepted((string?)null, ToResponse(saved, origins, role));
    }

    internal static async Task<Ok<DependentsResponse>> DependentsAsync(Guid projectId, PlatformDbContext db, CancellationToken cancellationToken)
    {
        int keys = await db.ApiKeys.AsNoTracking().CountAsync(x => x.ProjectId == projectId, cancellationToken);
        return TypedResults.Ok(new DependentsResponse([new DependentResponse("api_key", keys, Estimated: false)]));
    }

    // A status flip and a write queue here, so If-Match and the status are read as they are at the write.
    private static async Task LockAsync(PlatformDbContext db, Guid projectId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM platform.project WHERE id = {projectId} FOR UPDATE", cancellationToken);

    private static ProjectResponse ToResponse(Project project, IReadOnlyList<string> allowedOrigins, string? role) => new(
        project.Id,
        project.OrganizationId,
        project.Name,
        project.Status,
        allowedOrigins,
        ProjectServices.All,
        ConsolePermissions.Allowed(role, ConsolePermissions.OnProject),
        ResourceVersion.Format(project.Version),
        project.CreatedAt,
        project.UpdatedAt);
}
