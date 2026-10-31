using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Console;

/// <summary>Organizations (spec 0017 ORG-3). The list, create, and rename come with build plan step 19.</summary>
internal static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizations(this RouteGroupBuilder console)
    {
        console.MapGet("/organizations/{organizationId:guid}", GetAsync)
            .RequireConsolePermission(ConsolePermission.OrgRead)
            .WithName("Platform_GetOrganization")
            .WithSummary("Read an organization.")
            .WithDescription("Answers 404 for an organization the operator isn't a member of.")
            .WithErrors();
        return console;
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
        return TypedResults.Ok(new OrganizationResponse(
            organization.Id,
            organization.Name,
            ConsolePermissions.Allowed(role, ConsolePermissions.OnOrganization),
            ResourceVersion.Format(organization.Version),
            organization.CreatedAt,
            organization.UpdatedAt));
    }
}
