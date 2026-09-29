using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Core.Jobs;
using Orvano.Core.Paging;
using Orvano.Platform.Contracts;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;
using Orvano.Platform.Jobs;

namespace Orvano.Platform.Application;

/// <summary>A page of a list, and the cursor for the next one.</summary>
internal sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>An org with the caller's role in it.</summary>
internal sealed record OrgView(OrgRow Org, OrgRole Role);

/// <summary>
/// Org use cases (spec 0003): list, create, rename, delete, and restore. Every change locks the org row
/// <c>FOR UPDATE</c> and writes its event in the same transaction (AC-19).
/// </summary>
internal sealed class OrgService(PlatformStore store, DeleteGrace grace, ILogger<OrgService> logger)
{
    public Task<Outcome<Page<OrgView>>> ListAsync(Guid userId, string? cursor, int? limit, CancellationToken ct) =>
        store.ReadAsync<Outcome<Page<OrgView>>>(async (db, ct) =>
        {
            if (PageCursor.Limit(limit) is not { } size) return Failure.Invalid($"limit must be 1 to {PageCursor.MaxLimit}.");
            var query =
                from m in db.Memberships
                join o in db.Orgs on m.OrgId equals o.Id
                where m.UserId == userId
                select new { Org = o, m.Role };

            if (cursor is not null)
            {
                if (!PageCursor.TryDecode(cursor, out var after) || !Guid.TryParse(after.Id, out var afterId)) return Failure.InvalidCursor;
                query = query.Where(x => x.Org.CreatedAt > after.CreatedAt
                    || x.Org.CreatedAt == after.CreatedAt && x.Org.Id.CompareTo(afterId) > 0);
            }

            var rows = await query.OrderBy(x => x.Org.CreatedAt).ThenBy(x => x.Org.Id).Take(size + 1).AsNoTracking().ToListAsync(ct);
            var items = rows.Take(size).Select(r => new OrgView(r.Org, Roles.Parse(r.Role))).ToList();
            var next = rows.Count > size ? PageCursor.Encode(new PagePosition(items[^1].Org.CreatedAt, items[^1].Org.Id.ToString())) : null;
            return new Page<OrgView>(items, next);
        }, ct);

    public Task<Outcome<OrgView>> GetAsync(Guid userId, Guid orgId, CancellationToken ct) =>
        store.ReadAsync<Outcome<OrgView>>(async (db, ct) =>
        {
            var row = await (
                from m in db.Memberships
                join o in db.Orgs on m.OrgId equals o.Id
                where m.UserId == userId && m.OrgId == orgId
                select new { Org = o, m.Role }).AsNoTracking().SingleOrDefaultAsync(ct);
            return row is null ? Failure.OrgNotFound : new OrgView(row.Org, Roles.Parse(row.Role));
        }, ct);

    public Task<Outcome<OrgView>> CreateAsync(Guid userId, string? name, CancellationToken ct) =>
        store.WriteAsync<OrgView>(async (uow, ct) =>
        {
            if (!Names.TryNormalize(name, out var orgName)) return Failure.Invalid($"An org name is 1 to {Names.MaxLength} characters.");
            var org = await AddOrgAsync(uow, orgName, userId, Actor.User(userId), ct);
            logger.LogInformation("Console user {UserId} created org {OrgId}", userId, org.Id);
            return new OrgView(org, OrgRole.Owner);
        }, ct);

    public Task<Outcome<OrgView>> RenameAsync(Guid userId, Guid orgId, string? name, CancellationToken ct) =>
        store.WriteAsync<OrgView>(async (uow, ct) =>
        {
            if (!Names.TryNormalize(name, out var orgName)) return Failure.Invalid($"An org name is 1 to {Names.MaxLength} characters.");
            var (org, failure) = await LockAsOwnerAsync(uow, userId, orgId, ct);
            if (failure is not null) return failure;
            if (Statuses.Org(org!.Status) != OrgStatus.Active) return Failure.OrgNotActive;

            if (org.Name != orgName)
            {
                org.Name = orgName;
                org.UpdatedAt = await uow.NowAsync(ct);
                await uow.Db.SaveChangesAsync(ct);
                await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.OrgUpdated, Actor.User(userId), org.Id.ToString(),
                    Ids(org.Id), changed: ["name"], ct: ct);
                logger.LogInformation("Console user {UserId} renamed org {OrgId}", userId, org.Id);
            }

            return new OrgView(org, OrgRole.Owner);
        }, ct);

    /// <summary>AC-15: allowed only when every project of the org is deleting; the purge waits for them.</summary>
    public Task<Outcome<OrgView>> DeleteAsync(Guid userId, Guid orgId, CancellationToken ct) =>
        store.WriteAsync<OrgView>(async (uow, ct) =>
        {
            var (org, failure) = await LockAsOwnerAsync(uow, userId, orgId, ct);
            if (failure is not null) return failure;
            if (Statuses.Org(org!.Status) != OrgStatus.Active) return Failure.OrgNotActive;

            var deleting = Statuses.Wire(ProjectStatus.Deleting);
            if (await uow.Db.Projects.AnyAsync(p => p.OrgId == orgId && p.Status != deleting, ct)) return Failure.OrgNotEmpty;

            var now = await uow.NowAsync(ct);
            org.Status = Statuses.Wire(OrgStatus.Deleting);
            org.DeletedAt = now;
            org.PurgeAfter = ProjectLifecycle.PurgeAfter(now, grace);
            org.UpdatedAt = now;
            await uow.Db.SaveChangesAsync(ct);
            await JobQueue.EnqueueAsync(uow.Tx, PlatformJobs.PurgeOrgAt(org.Id, org.PurgeAfter.Value), ct);
            await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.OrgDeleting, Actor.User(userId), org.Id.ToString(), Ids(org.Id), ct: ct);
            logger.LogInformation("Console user {UserId} deleted org {OrgId}", userId, org.Id);
            return new OrgView(org, OrgRole.Owner);
        }, ct);

    /// <summary>Restoring an active org changes nothing. Restoring an org does not restore its projects.</summary>
    public Task<Outcome<OrgView>> RestoreAsync(Guid userId, Guid orgId, CancellationToken ct) =>
        store.WriteAsync<OrgView>(async (uow, ct) =>
        {
            var (org, failure) = await LockAsOwnerAsync(uow, userId, orgId, ct);
            if (failure is not null) return failure;
            if (Statuses.Org(org!.Status) == OrgStatus.Active) return new OrgView(org, OrgRole.Owner);

            org.Status = Statuses.Wire(OrgStatus.Active);
            org.DeletedAt = null;
            org.PurgeAfter = null;
            org.UpdatedAt = await uow.NowAsync(ct);
            await uow.Db.SaveChangesAsync(ct);
            await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.OrgRestored, Actor.User(userId), org.Id.ToString(), Ids(org.Id), ct: ct);
            logger.LogInformation("Console user {UserId} restored org {OrgId}", userId, org.Id);
            return new OrgView(org, OrgRole.Owner);
        }, ct);

    /// <summary>Inserts an active org with <paramref name="ownerId"/> as its owner, and both events.</summary>
    internal static async Task<OrgRow> AddOrgAsync(UnitOfWork uow, string name, Guid ownerId, Actor actor, CancellationToken ct)
    {
        var now = await uow.NowAsync(ct);
        var org = new OrgRow
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Status = Statuses.Wire(OrgStatus.Active),
            CreatedByUserId = ownerId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        uow.Db.Orgs.Add(org);
        await uow.Db.SaveChangesAsync(ct);
        await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.OrgCreated, actor, org.Id.ToString(), Ids(org.Id), ct: ct);
        await AddMemberAsync(uow, org.Id, ownerId, OrgRole.Owner, actor, now, ct);
        return org;
    }

    /// <summary>Inserts a membership and writes <c>platform.member.added</c> with its role, and the invitation it came from, if any.</summary>
    internal static async Task AddMemberAsync(
        UnitOfWork uow, Guid orgId, Guid userId, OrgRole role, Actor actor, DateTimeOffset now, CancellationToken ct, Guid? invitationId = null)
    {
        var membership = new MembershipRow
        {
            Id = Guid.CreateVersion7(),
            OrgId = orgId,
            UserId = userId,
            Role = Roles.Wire(role),
            CreatedAt = now,
            UpdatedAt = now,
        };
        uow.Db.Memberships.Add(membership);
        await uow.Db.SaveChangesAsync(ct);
        var fields = new Dictionary<string, string>
        {
            ["orgId"] = orgId.ToString(),
            ["membershipId"] = membership.Id.ToString(),
            ["userId"] = userId.ToString(),
            ["role"] = Roles.Wire(role),
        };
        if (invitationId is { } fromInvitation) fields["invitationId"] = fromInvitation.ToString();
        await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.MemberAdded, actor, membership.Id.ToString(), fields, ct: ct);
    }

    internal static Dictionary<string, string> Ids(Guid orgId) => new() { ["orgId"] = orgId.ToString() };

    /// <summary>Locks the org and checks the caller may manage it: 404 to non members, 403 to non owners.</summary>
    internal static async Task<(OrgRow? Org, Failure? Failure)> LockAsOwnerAsync(UnitOfWork uow, Guid userId, Guid orgId, CancellationToken ct)
    {
        var org = await uow.LockOrgForUpdateAsync(orgId, ct);
        if (org is null) return (null, Failure.OrgNotFound);
        var role = await uow.Db.RoleInAsync(userId, orgId, ct);
        if (role is null) return (null, Failure.OrgNotFound);
        return ConsolePermissions.Allows(Roles.Parse(role), ConsoleAction.ManageOrg) ? (org, null) : (null, Failure.Forbidden);
    }
}
