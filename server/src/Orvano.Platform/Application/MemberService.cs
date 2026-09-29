using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Core.Paging;
using Orvano.Platform.Contracts;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;

namespace Orvano.Platform.Application;

/// <summary>A membership and the console account behind it.</summary>
internal sealed record MemberView(MembershipRow Membership, ConsoleUserSummary User);

/// <summary>
/// Member use cases (spec 0008, AC-8 to AC-10): list, change a role, and remove (leaving included). Changes lock the
/// org row <c>FOR UPDATE</c>, so an org never loses its last owner, even when two owners demote each other at once.
/// Every change takes effect on the member's next request, since <see cref="IConsoleAccess"/> reads memberships on
/// every call.
/// </summary>
internal sealed class MemberService(PlatformStore store, IConsoleUserDirectory directory, ILogger<MemberService> logger)
{
    /// <summary>
    /// AC-8: any member. A membership whose console account no longer exists is dropped after paging, so a page can
    /// hold fewer items than the limit while <c>nextCursor</c> is still set.
    /// </summary>
    public async Task<Outcome<Page<MemberView>>> ListAsync(Guid userId, Guid orgId, string? cursor, int? limit, CancellationToken ct)
    {
        var outcome = await store.ReadAsync<Outcome<Page<MembershipRow>>>(async (db, ct) =>
        {
            if (PageCursor.Limit(limit) is not { } size) return Failure.Invalid($"limit must be 1 to {PageCursor.MaxLimit}.");
            if (await db.RoleInAsync(userId, orgId, ct) is null) return Failure.OrgNotFound;

            var query = db.Memberships.Where(m => m.OrgId == orgId);
            if (cursor is not null)
            {
                if (!PageCursor.TryDecode(cursor, out var after) || !Guid.TryParse(after.Id, out var afterId)) return Failure.InvalidCursor;
                query = query.Where(m => m.CreatedAt > after.CreatedAt || m.CreatedAt == after.CreatedAt && m.Id.CompareTo(afterId) > 0);
            }

            var rows = await query.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Take(size + 1).AsNoTracking().ToListAsync(ct);
            var items = rows.Take(size).ToList();
            var next = rows.Count > size ? PageCursor.Encode(new PagePosition(items[^1].CreatedAt, items[^1].Id.ToString())) : null;
            return new Page<MembershipRow>(items, next);
        }, ct);
        if (!outcome.Succeeded) return outcome.Failure!;

        var page = outcome.Value!;
        var users = (await directory.GetManyAsync([.. page.Items.Select(m => m.UserId)], ct)).ToDictionary(u => u.Id);
        return new Page<MemberView>(
            [.. page.Items.Where(m => users.ContainsKey(m.UserId)).Select(m => new MemberView(m, users[m.UserId]))],
            page.NextCursor);
    }

    /// <summary>
    /// AC-9: owners only. Errors in order: non member 404, not an owner 403 (before the target is looked up), deleting
    /// org 409, unknown member 404, demoting the last owner 409. The same role changes nothing and writes no event.
    /// </summary>
    public async Task<Outcome<MemberView>> ChangeRoleAsync(Guid userId, Guid orgId, Guid? targetId, OrgRole role, CancellationToken ct)
    {
        var target = targetId is { } id ? (await directory.GetManyAsync([id], ct)).SingleOrDefault() : null;

        return await store.WriteAsync<MemberView>(async (uow, ct) =>
        {
            var (org, failure) = await OrgService.LockAsOwnerAsync(uow, userId, orgId, ct);
            if (failure is not null) return failure;
            if (Statuses.Org(org!.Status) != OrgStatus.Active) return Failure.OrgNotActive;

            var membership = targetId is null ? null : await uow.Db.Memberships.SingleOrDefaultAsync(m => m.OrgId == orgId && m.UserId == targetId, ct);
            if (membership is null || target is null) return Failure.MemberNotFound;

            var from = Roles.Parse(membership.Role);
            if (from == role) return new MemberView(membership, target);
            if (from == OrgRole.Owner && !OwnerRule.CanLoseOwner(await OwnerCountAsync(uow, orgId, ct))) return Failure.LastOwner;

            membership.Role = Roles.Wire(role);
            membership.UpdatedAt = await uow.NowAsync(ct);
            await uow.Db.SaveChangesAsync(ct);
            await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.MemberRoleChanged, Actor.User(userId), membership.Id.ToString(),
                new Dictionary<string, string>
                {
                    ["orgId"] = orgId.ToString(),
                    ["membershipId"] = membership.Id.ToString(),
                    ["userId"] = membership.UserId.ToString(),
                    ["from"] = Roles.Wire(from),
                    ["to"] = membership.Role,
                }, changed: ["role"], ct: ct);
            logger.LogInformation("Console user {UserId} changed the role of member {MemberId} in org {OrgId}", userId, membership.UserId, orgId);
            return new MemberView(membership, target);
        }, ct);
    }

    /// <summary>
    /// AC-10: owners remove anyone; every member may remove themselves (leave). Errors in order: non member 404, a
    /// developer or viewer removing someone else 403 (before the target is looked up), deleting org 409, unknown
    /// member 404, removing the last owner 409. API keys the member created keep working.
    /// </summary>
    public Task<Outcome<Done>> RemoveAsync(Guid userId, Guid orgId, Guid? targetId, CancellationToken ct) =>
        store.WriteAsync<Done>(async (uow, ct) =>
        {
            var org = await uow.LockOrgForUpdateAsync(orgId, ct);
            if (org is null || await uow.Db.RoleInAsync(userId, orgId, ct) is not { } callerRole) return Failure.OrgNotFound;

            var leaving = targetId == userId;
            if (!leaving && !ConsolePermissions.Allows(Roles.Parse(callerRole), ConsoleAction.ManageMembers)) return Failure.Forbidden;
            if (Statuses.Org(org.Status) != OrgStatus.Active) return Failure.OrgNotActive;

            var membership = targetId is null
                ? null
                : await uow.Db.Memberships.AsNoTracking().SingleOrDefaultAsync(m => m.OrgId == orgId && m.UserId == targetId, ct);
            if (membership is null) return Failure.MemberNotFound;
            if (Roles.Parse(membership.Role) == OrgRole.Owner && !OwnerRule.CanLoseOwner(await OwnerCountAsync(uow, orgId, ct)))
                return Failure.LastOwner;

            await uow.Db.Memberships.Where(m => m.Id == membership.Id).ExecuteDeleteAsync(ct);
            await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.MemberRemoved, Actor.User(userId), membership.Id.ToString(),
                new Dictionary<string, string>
                {
                    ["orgId"] = orgId.ToString(),
                    ["membershipId"] = membership.Id.ToString(),
                    ["userId"] = membership.UserId.ToString(),
                    ["role"] = membership.Role,
                    ["reason"] = leaving ? "left" : "removed",
                }, ct: ct);
            if (leaving) logger.LogInformation("Console user {UserId} left org {OrgId}", userId, orgId);
            else logger.LogInformation("Console user {UserId} removed member {MemberId} from org {OrgId}", userId, membership.UserId, orgId);
            return new Done();
        }, ct);

    /// <summary>Owners by membership row, blocked owners included (spec 0003).</summary>
    private static Task<int> OwnerCountAsync(UnitOfWork uow, Guid orgId, CancellationToken ct)
    {
        var owner = Roles.Wire(OrgRole.Owner);
        return uow.Db.Memberships.CountAsync(m => m.OrgId == orgId && m.Role == owner, ct);
    }
}
