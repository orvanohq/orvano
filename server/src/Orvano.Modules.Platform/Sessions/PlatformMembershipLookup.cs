using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Access;

namespace Orvano.Modules.Platform.Sessions;

/// <summary>Spec 0017 ACX-5: plain indexed reads, no cache, so a change applies on the next request.</summary>
internal sealed class PlatformMembershipLookup(PlatformDbContext db) : IMembershipLookup
{
    public Task<string?> FindRoleAsync(Guid operatorId, Guid organizationId, CancellationToken cancellationToken) =>
        db.Memberships.AsNoTracking()
            .Where(x => x.OperatorId == operatorId && x.OrganizationId == organizationId)
            .Select(x => x.Role)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Guid?> FindProjectOrganizationAsync(Guid projectId, CancellationToken cancellationToken) =>
        db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => (Guid?)x.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken);
}
