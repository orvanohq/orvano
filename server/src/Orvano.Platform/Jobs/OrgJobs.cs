using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orvano.Core.Jobs;
using Orvano.Platform.Application;
using Orvano.Platform.Contracts;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;

namespace Orvano.Platform.Jobs;

/// <summary>The org purge job (AC-15) and the membership cleanup after a console account is deleted (AC-10).</summary>
internal static class OrgJobs
{
    /// <summary>The Auth event whose console user deletions clean up memberships.</summary>
    public const string UserDeletedEvent = "auth.user.deleted";

    /// <summary>
    /// Locks the org, and does nothing unless it is still <c>deleting</c> and due. While project rows of the org remain,
    /// it enqueues itself again at their latest <c>purge_after</c> plus a minute; otherwise it deletes the invitations,
    /// the memberships, and the org.
    /// </summary>
    public static async Task PurgeOrgAsync(JobContext job, CancellationToken ct)
    {
        var orgId = Guid.Parse(PlatformJobs.Read(job.Job.Payload, "orgId"));
        var store = job.Services.GetRequiredService<PlatformStore>();
        var logger = Log(job);

        await store.WriteAsync<Done>(async (uow, ct) =>
        {
            var org = await uow.LockOrgForUpdateAsync(orgId, ct);
            var now = await uow.NowAsync(ct);
            if (org is null || Statuses.Org(org.Status) != OrgStatus.Deleting || org.PurgeAfter > now)
            {
                logger.LogInformation("Purging org {OrgId} skipped: it is gone, restored, or not due yet", orgId);
                return new Done();
            }

            var waitFor = await uow.Db.Projects.Where(p => p.OrgId == orgId).MaxAsync(p => p.PurgeAfter, ct);
            if (await uow.Db.Projects.AnyAsync(p => p.OrgId == orgId, ct))
            {
                var next = (waitFor is { } latest && latest > now ? latest : now).AddMinutes(1);
                await JobQueue.EnqueueAsync(uow.Tx, PlatformJobs.PurgeOrgAt(orgId, next), ct);
                logger.LogInformation("Purging org {OrgId} waits for its projects until {RunAt}", orgId, next);
                return new Done();
            }

            // Invitations reference the org, so they go first (spec 0008, retention); no revoked events, the org purge says it.
            await uow.Db.Invitations.Where(i => i.OrgId == orgId).ExecuteDeleteAsync(ct);
            await uow.Db.Memberships.Where(m => m.OrgId == orgId).ExecuteDeleteAsync(ct);
            await uow.Db.Orgs.Where(o => o.Id == orgId).ExecuteDeleteAsync(ct);
            await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.OrgPurged, Actor.System, orgId.ToString(), OrgService.Ids(orgId), ct: ct);
            logger.LogInformation("Purged org {OrgId}", orgId);
            return new Done();
        }, ct);
    }

    /// <summary>
    /// After a console account is deleted: orgs where it was the only member with no live project start deleting
    /// (AC-10), then its memberships and install admin row go. Running it again finds nothing to do.
    /// </summary>
    public static async Task RemoveMembershipsAsync(JobContext job, CancellationToken ct)
    {
        var userId = Guid.Parse(PlatformJobs.Read(job.Job.Payload, "userId"));
        var store = job.Services.GetRequiredService<PlatformStore>();
        var grace = job.Services.GetRequiredService<DeleteGrace>();
        var logger = Log(job);

        await store.WriteAsync<Done>(async (uow, ct) =>
        {
            var memberships = await uow.Db.Memberships.Where(m => m.UserId == userId).AsNoTracking().ToListAsync(ct);
            foreach (var membership in memberships.OrderBy(m => m.OrgId))
            {
                var org = await uow.LockOrgForUpdateAsync(membership.OrgId, ct);
                if (org is null) continue;

                var members = await uow.Db.Memberships.CountAsync(m => m.OrgId == org.Id, ct);
                var deleting = Statuses.Wire(ProjectStatus.Deleting);
                var liveProjects = await uow.Db.Projects.CountAsync(p => p.OrgId == org.Id && p.Status != deleting, ct);
                if (members == 1 && liveProjects == 0 && Statuses.Org(org.Status) == OrgStatus.Active)
                    await StartDeletingAsync(uow, org, grace, ct);

                await uow.Db.Memberships.Where(m => m.Id == membership.Id).ExecuteDeleteAsync(ct);
                await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.MemberRemoved, Actor.System, membership.Id.ToString(),
                    new Dictionary<string, string>
                    {
                        ["orgId"] = membership.OrgId.ToString(),
                        ["membershipId"] = membership.Id.ToString(),
                        ["userId"] = userId.ToString(),
                    }, ct: ct);
            }

            await uow.Db.InstallAdmins.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
            logger.LogInformation("Removed {Count} membership(s) of deleted console user {UserId}", memberships.Count, userId);
            return new Done();
        }, ct);
    }

    private static async Task StartDeletingAsync(UnitOfWork uow, OrgRow org, DeleteGrace grace, CancellationToken ct)
    {
        var now = await uow.NowAsync(ct);
        org.Status = Statuses.Wire(OrgStatus.Deleting);
        org.DeletedAt = now;
        org.PurgeAfter = ProjectLifecycle.PurgeAfter(now, grace);
        org.UpdatedAt = now;
        await uow.Db.SaveChangesAsync(ct);
        await JobQueue.EnqueueAsync(uow.Tx, PlatformJobs.PurgeOrgAt(org.Id, org.PurgeAfter.Value), ct);
        await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.OrgDeleting, Actor.System, org.Id.ToString(), OrgService.Ids(org.Id), ct: ct);
    }

    private static ILogger Log(JobContext job) =>
        job.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OrgJobs));
}
