using Microsoft.EntityFrameworkCore;
using Orvano.Platform.Data;

namespace Orvano.Platform.Application;

/// <summary>Row locks and the database clock for a unit of work.</summary>
internal static class UnitOfWorkExtensions
{
    /// <summary>The transaction's start time from Postgres, so every timestamp agrees with the jobs' <c>now()</c>.</summary>
    public static Task<DateTimeOffset> NowAsync(this UnitOfWork uow, CancellationToken ct) => uow.Db.NowAsync(ct);

    /// <summary>The database clock, for reads that compare against it (an invitation's expiry).</summary>
    public static Task<DateTimeOffset> NowAsync(this PlatformDbContext db, CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset>($"SELECT now() AS \"Value\"").SingleAsync(ct);

    /// <summary>The org, locked <c>FOR UPDATE</c> (org changes, deletes, and membership changes).</summary>
    public static async Task<OrgRow?> LockOrgForUpdateAsync(this UnitOfWork uow, Guid orgId, CancellationToken ct) =>
        // Not composed (no LIMIT around it), so the lock clause runs exactly as written.
        (await uow.Db.Orgs.FromSql($"SELECT * FROM orvano.platform_orgs WHERE id = {orgId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();

    /// <summary>The org, locked <c>FOR SHARE</c> (project create and restore), so it can't start deleting underneath.</summary>
    public static async Task<OrgRow?> LockOrgForShareAsync(this UnitOfWork uow, Guid orgId, CancellationToken ct) =>
        (await uow.Db.Orgs.FromSql($"SELECT * FROM orvano.platform_orgs WHERE id = {orgId} FOR SHARE").ToListAsync(ct)).SingleOrDefault();

    /// <summary>The caller's membership role in an org, as stored, or null.</summary>
    public static Task<string?> RoleInAsync(this PlatformDbContext db, Guid userId, Guid orgId, CancellationToken ct) =>
        db.Memberships.Where(m => m.OrgId == orgId && m.UserId == userId).Select(m => m.Role).SingleOrDefaultAsync(ct);

    public static Task<bool> IsInstallAdminAsync(this PlatformDbContext db, Guid userId, CancellationToken ct) =>
        db.InstallAdmins.AnyAsync(a => a.UserId == userId, ct);
}
