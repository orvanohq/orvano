using Microsoft.EntityFrameworkCore;
using Orvano.Auth.Contracts;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>The Auth side of <see cref="IUserDirectory"/>: users by ID, always filtered by project (spec 0003, AC-5).</summary>
internal sealed class UserDirectory(AuthStore store) : IUserDirectory
{
    public async Task<IReadOnlyList<UserSummary>> GetManyAsync(string projectId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var wanted = ids.Distinct().ToArray();
        return await store.ReadAsync(async (db, token) =>
            (await db.Users.AsNoTracking()
                .Where(u => u.ProjectId == projectId && wanted.Contains(u.Id))
                .Select(u => new { u.Id, u.Email, u.Name, u.Status })
                .ToListAsync(token))
            .Select(u => new UserSummary(u.Id, u.Email, u.Name, u.Status == UserStatuses.Blocked ? AuthUserStatus.Blocked : AuthUserStatus.Active))
            .ToList(), ct);
    }
}
