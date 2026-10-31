using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Console;

/// <summary>Organizations (spec 0017 ORG-1 to ORG-3).</summary>
internal static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizations(this RouteGroupBuilder console)
    {
        console.MapGet("/organizations", ListAsync)
            .RequireOperator()
            .WithName("Platform_ListOrganizations")
            .WithSummary("List the operator's organizations.")
            .WithDescription("Lists only the organizations the operator belongs to, oldest first. `query` matches the name, ignoring case.")
            .WithErrors(ErrorCodes.ValidationFailed, ErrorCodes.InvalidCursor)
            .WithPaging();

        console.MapPost("/organizations", CreateAsync)
            .RequireOperator()
            .WithName("Platform_CreateOrganization")
            .WithSummary("Create an organization.")
            .WithDescription("The operator becomes its owner.")
            .WithErrors(ErrorCodes.ValidationFailed)
            .ProducesValidationProblem();

        console.MapGet("/organizations/{organizationId:guid}", GetAsync)
            .RequireConsolePermission(ConsolePermission.OrgRead)
            .WithName("Platform_GetOrganization")
            .WithSummary("Read an organization.")
            .WithDescription("Answers 404 for an organization the operator isn't a member of.")
            .WithErrors();

        console.MapPatch("/organizations/{organizationId:guid}", UpdateAsync)
            .RequireConsolePermission(ConsolePermission.OrgWrite)
            .WithConcurrency()
            .WithName("Platform_UpdateOrganization")
            .WithSummary("Rename an organization.")
            .WithDescription("Answers 404 for an organization the operator isn't a member of.")
            .WithErrors(ErrorCodes.ValidationFailed, ErrorCodes.VersionConflict)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
        return console;
    }

    internal static async Task<Results<Ok<Page<OrganizationResponse>>, ErrorProblemResult>> ListAsync(
        [AsParameters] PageQuery page,
        [AsParameters] NameFilterQuery filter,
        PlatformDbContext db,
        ICallerContext caller,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        Guid operatorId = caller.Current.OperatorId!.Value;
        string cursorQuery = CursorCodec.Query(http.Request);
        var rows =
            from membership in db.Memberships.AsNoTracking()
            join organization in db.Organizations.AsNoTracking() on membership.OrganizationId equals organization.Id
            where membership.OperatorId == operatorId
            select new { Organization = organization, membership.Role };
        if (page.Cursor is { } cursor)
        {
            Result<CursorKeys> keys = CursorCodec.Decode(cursor, cursorQuery, CursorKey.Uuid);
            if (!keys.IsSuccess)
            {
                return keys.Error.ToProblem();
            }

            Guid after = keys.Value.GetUuid(0);
            rows = rows.Where(x => x.Organization.Id.CompareTo(after) > 0);
        }

        if (filter.Query is { Length: > 0 } query)
        {
            string pattern = ConsoleSearch.Contains(query);
            rows = rows.Where(x => EF.Functions.ILike(x.Organization.Name, pattern, ConsoleSearch.Escape));
        }

        int limit = page.Size;
        var found = await rows.OrderBy(x => x.Organization.Id).Take(limit + 1).ToListAsync(cancellationToken);
        return TypedResults.Ok(Page.From(
            found,
            limit,
            cursorQuery,
            row => new JsonArray(row.Organization.Id.ToString("D")),
            row => ToResponse(row.Organization, row.Role)));
    }

    internal static async Task<Created<OrganizationResponse>> CreateAsync(
        CreateOrganizationRequest request,
        PlatformDbContext db,
        ConsoleAudit audit,
        ICallerContext caller,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        Guid operatorId = caller.Current.OperatorId!.Value;
        var organization = new Organization { Name = request.Name.Trim() };
        await db.InTransactionAsync(
            async token =>
            {
                db.Organizations.Add(organization);
                db.Memberships.Add(new Membership { OperatorId = operatorId, OrganizationId = organization.Id, Role = RolePermissions.Owner });
                await db.SaveChangesAsync(token);
            },
            cancellationToken);
        await audit.RecordAsync(http, AuditActions.OrganizationCreated, operatorId, "organization", organization.Id, organization.Id);
        return TypedResults.Created((string?)null, ToResponse(organization, RolePermissions.Owner));
    }

    internal static async Task<Results<Ok<OrganizationResponse>, ErrorProblemResult>> GetAsync(
        Guid organizationId, PlatformDbContext db, IMembershipLookup memberships, ICallerContext caller, HttpContext http, CancellationToken cancellationToken)
    {
        Organization? organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == organizationId, cancellationToken);
        if (organization is null)
        {
            return AccessErrors.NotFound.ToProblem();
        }

        string? role = await memberships.FindRoleAsync(caller.Current.OperatorId!.Value, organizationId, cancellationToken);
        ResourceVersion.SetETag(http.Response, organization.Version);
        return TypedResults.Ok(ToResponse(organization, role));
    }

    internal static async Task<Results<Ok<OrganizationResponse>, ErrorProblemResult>> UpdateAsync(
        Guid organizationId,
        UpdateOrganizationRequest request,
        IfMatch ifMatch,
        PlatformDbContext db,
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

        Result<Organization> updated = await db.InTransactionAsync<Result<Organization>>(
            async token =>
            {
                await db.Database.ExecuteSqlAsync($"SELECT 1 FROM platform.organization WHERE id = {organizationId} FOR UPDATE", token);
                Organization organization = await db.Organizations.SingleAsync(x => x.Id == organizationId, token);
                Result fresh = ifMatch.Check(organization.Version);
                if (!fresh.IsSuccess)
                {
                    return fresh.Error;
                }

                organization.Name = name ?? organization.Name;
                await db.SaveChangesAsync(token);
                return organization;
            },
            cancellationToken);
        if (!updated.IsSuccess)
        {
            return updated.Error.ToProblem();
        }

        Guid operatorId = caller.Current.OperatorId!.Value;
        await audit.RecordAsync(http, AuditActions.OrganizationUpdated, operatorId, "organization", organizationId, organizationId);
        string? role = await memberships.FindRoleAsync(operatorId, organizationId, cancellationToken);
        ResourceVersion.SetETag(http.Response, updated.Value.Version);
        return TypedResults.Ok(ToResponse(updated.Value, role));
    }

    private static OrganizationResponse ToResponse(Organization organization, string? role) => new(
        organization.Id,
        organization.Name,
        ConsolePermissions.Allowed(role, ConsolePermissions.OnOrganization),
        ResourceVersion.Format(organization.Version),
        organization.CreatedAt,
        organization.UpdatedAt);
}
