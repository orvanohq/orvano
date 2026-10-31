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

namespace Orvano.Modules.Platform.Console;

/// <summary>Projects (spec 0017 PJT-1, PJT-5). Rename, origins, and delete come with build plan steps 17 and 18.</summary>
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
            .WithDescription("Answers 201 once the project is active.")
            .WithErrors(ErrorCodes.ValidationFailed)
            .ProducesValidationProblem();

        console.MapGet("/projects/{projectId:guid}", GetAsync)
            .RequireConsolePermission(ConsolePermission.ProjectRead)
            .WithName("Platform_GetProject")
            .WithSummary("Read a project.")
            .WithDescription("Answers 404 for a project outside the operator's organizations.")
            .WithErrors();
        return console;
    }

    internal static async Task<Results<Ok<Page<ProjectResponse>>, ErrorProblemResult>> ListAsync(
        Guid organizationId,
        [AsParameters] PageQuery page,
        [AsParameters] ProjectFilterQuery filter,
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
            string pattern = "%" + query.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal) + "%";
            rows = rows.Where(x => EF.Functions.ILike(x.Name, pattern, @"\"));
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
