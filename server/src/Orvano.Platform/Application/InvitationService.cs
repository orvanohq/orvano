using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Http;
using Orvano.Core.Paging;
using Orvano.Platform.Contracts;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;

namespace Orvano.Platform.Application;

/// <summary>An invitation with its state at the database clock and the inviter, if that account still exists.</summary>
internal sealed record InvitationView(InvitationRow Row, InvitationState State, ConsoleUserSummary? InvitedBy);

/// <summary>
/// A new invitation and its link, which leaves the server only in this one response and, when
/// <paramref name="Emailed"/>, in the invite email (spec 0009, AC-23).
/// </summary>
internal sealed record CreatedInvite(InvitationView Invitation, string Url, bool Emailed);

/// <summary>What an invite link is for (AC-5).</summary>
internal sealed record InvitePreview(OrgRow Org, InvitationRow Invitation, string? InvitedByName);

/// <summary>The org an invitation joined, and whether the caller was already in it (AC-6).</summary>
internal sealed record AcceptedInvite(OrgView Org, bool AlreadyMember);

/// <summary>An invitation the consuming delete took: its ID, org, and the role it grants.</summary>
internal sealed record ConsumedInvitation(Guid Id, Guid OrgId, OrgRole Role);

/// <summary>
/// Invitation use cases (spec 0008, AC-1 to AC-6): create (replacing the email's old one, cleaning up long expired
/// ones, under the cap), list, revoke, preview, and accept. Every change locks the org row <c>FOR UPDATE</c> first.
/// The token exists only in the create response and the invite email; the database holds its hash, and no log line,
/// event, or problem body carries it.
/// </summary>
internal sealed class InvitationService(
    PlatformStore store, IConsoleUserDirectory directory, PublicUrl publicUrl, IServiceProvider services, ILogger<InvitationService> logger)
{
    public async Task<Outcome<CreatedInvite>> CreateAsync(Guid userId, Guid orgId, string? email, OrgRole? role, CancellationToken ct)
    {
        if (!InviteEmail.TryNormalize(email, out var invited)) return Failure.Invalid("The email must be an address of at most 320 characters.");
        if (role is not { } invitedRole) return Failure.Invalid("role is owner, developer, or viewer.");

        // Read before the transaction, so no connection waits on another module's lookup.
        var existing = await directory.FindByEmailAsync(invited, ct);
        var inviter = (await directory.GetManyAsync([userId], ct)).SingleOrDefault();

        return await store.WriteAsync<CreatedInvite>(async (uow, ct) =>
        {
            var (org, failure) = await OrgService.LockAsOwnerAsync(uow, userId, orgId, ct);
            if (failure is not null) return failure;
            if (Statuses.Org(org!.Status) != OrgStatus.Active) return Failure.OrgNotActive;
            if (existing is not null && await uow.Db.Memberships.AnyAsync(m => m.OrgId == orgId && m.UserId == existing.Id, ct))
                return Failure.AlreadyMember;

            var actor = Actor.User(userId);
            var now = await uow.NowAsync(ct);
            foreach (var replaced in await DeleteReturningIdsAsync(uow,
                "DELETE FROM orvano.platform_invitations WHERE org_id = @org AND lower(email) = lower(@email) RETURNING id",
                ct, ("org", orgId), ("email", invited)))
            {
                await WriteRevokedAsync(uow, actor, orgId, replaced, "replaced", ct);
            }

            foreach (var expired in await DeleteReturningIdsAsync(uow,
                "DELETE FROM orvano.platform_invitations WHERE org_id = @org AND expires_at < @before RETURNING id",
                ct, ("org", orgId), ("before", InvitationRules.CleanupBefore(now))))
            {
                await WriteRevokedAsync(uow, Actor.System, orgId, expired, "expired", ct);
            }

            if (!InvitationRules.FitsCap(await uow.Db.Invitations.CountAsync(i => i.OrgId == orgId, ct))) return Failure.InvitationLimit;

            var token = InviteToken.New();
            var row = new InvitationRow
            {
                Id = Guid.CreateVersion7(),
                OrgId = orgId,
                Email = invited,
                Role = Roles.Wire(invitedRole),
                TokenHash = token.Hash,
                InvitedByUserId = userId,
                ExpiresAt = InvitationRules.ExpiresAt(now),
                CreatedAt = now,
            };
            uow.Db.Invitations.Add(row);
            await uow.Db.SaveChangesAsync(ct);
            await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.InvitationCreated, actor, row.Id.ToString(),
                new Dictionary<string, string> { ["orgId"] = orgId.ToString(), ["invitationId"] = row.Id.ToString(), ["role"] = row.Role }, ct: ct);
            logger.LogInformation("Console user {UserId} created invitation {InvitationId} in org {OrgId}", userId, row.Id, orgId);

            // Resolved per request, so an install without the Messaging module still works and simply never emails
            // (spec 0009, module seams). Any error here rolls the invitation back with it.
            var url = token.Url(publicUrl.Origin);
            var emailed = inviter is not null
                && services.GetService<IConsoleInvitationMailer>() is { } mailer
                && await mailer.QueueAsync(
                    uow.Tx, new InvitationEmail(invited, org.Name, inviter.Name, inviter.Email, invitedRole, url, row.ExpiresAt), ct);
            return new CreatedInvite(new InvitationView(row, InvitationState.Pending, inviter), url, emailed);
        }, ct);
    }

    public async Task<Outcome<Page<InvitationView>>> ListAsync(Guid userId, Guid orgId, string? cursor, int? limit, CancellationToken ct)
    {
        var outcome = await store.ReadAsync<Outcome<(Page<InvitationRow> Page, DateTimeOffset Now)>>(async (db, ct) =>
        {
            if (PageCursor.Limit(limit) is not { } size) return Failure.Invalid($"limit must be 1 to {PageCursor.MaxLimit}.");
            if (await db.RoleInAsync(userId, orgId, ct) is not { } role) return Failure.OrgNotFound;
            if (!ConsolePermissions.Allows(Roles.Parse(role), ConsoleAction.ManageMembers)) return Failure.Forbidden;

            var query = db.Invitations.Where(i => i.OrgId == orgId);
            if (cursor is not null)
            {
                if (!PageCursor.TryDecode(cursor, out var after) || !Guid.TryParse(after.Id, out var afterId)) return Failure.InvalidCursor;
                query = query.Where(i => i.CreatedAt > after.CreatedAt || i.CreatedAt == after.CreatedAt && i.Id.CompareTo(afterId) > 0);
            }

            var rows = await query.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Take(size + 1).AsNoTracking().ToListAsync(ct);
            var items = rows.Take(size).ToList();
            var next = rows.Count > size ? PageCursor.Encode(new PagePosition(items[^1].CreatedAt, items[^1].Id.ToString())) : null;
            return (new Page<InvitationRow>(items, next), await db.NowAsync(ct));
        }, ct);
        if (!outcome.Succeeded) return outcome.Failure!;

        var (page, now) = outcome.Value;
        var inviters = await UsersByIdAsync(page.Items.Select(i => i.InvitedByUserId), ct);
        return new Page<InvitationView>(
            [.. page.Items.Select(i => new InvitationView(i, InvitationRules.StateAt(i.ExpiresAt, now), inviters.GetValueOrDefault(i.InvitedByUserId)))],
            page.NextCursor);
    }

    public Task<Outcome<Done>> RevokeAsync(Guid userId, Guid orgId, string invitationId, CancellationToken ct) =>
        store.WriteAsync<Done>(async (uow, ct) =>
        {
            var (org, failure) = await OrgService.LockAsOwnerAsync(uow, userId, orgId, ct);
            if (failure is not null) return failure;
            if (Statuses.Org(org!.Status) != OrgStatus.Active) return Failure.OrgNotActive;
            if (!Guid.TryParse(invitationId, out var id)) return Failure.NotFound("invitation");

            if (await uow.Db.Invitations.Where(i => i.Id == id && i.OrgId == orgId).ExecuteDeleteAsync(ct) == 0)
                return Failure.NotFound("invitation");
            await WriteRevokedAsync(uow, Actor.User(userId), orgId, id, "revoked", ct);
            logger.LogInformation("Console user {UserId} revoked invitation {InvitationId} in org {OrgId}", userId, id, orgId);
            return new Done();
        }, ct);

    /// <summary>AC-5: needs no session. Unknown, used, revoked, or replaced gets 404; expired 410; a deleting org 409.</summary>
    public async Task<Outcome<InvitePreview>> PreviewAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token)) return Failure.Invalid("Send the invitation token.");
        if (!InviteToken.TryParse(token, out var parsed)) return Failure.InvitationNotFound;

        var hash = parsed.Hash;
        var found = await store.ReadAsync(async (db, ct) =>
        {
            var row = await (
                from i in db.Invitations
                join o in db.Orgs on i.OrgId equals o.Id
                where i.TokenHash == hash
                select new { Invitation = i, Org = o }).AsNoTracking().SingleOrDefaultAsync(ct);
            return row is null ? null : new { row.Invitation, row.Org, Now = await db.NowAsync(ct) };
        }, ct);

        if (found is null) return Failure.InvitationNotFound;
        if (InvitationRules.StateAt(found.Invitation.ExpiresAt, found.Now) == InvitationState.Expired) return Failure.InvitationExpired;
        if (Statuses.Org(found.Org.Status) != OrgStatus.Active) return Failure.OrgNotActive;

        var inviter = (await directory.GetManyAsync([found.Invitation.InvitedByUserId], ct)).SingleOrDefault();
        return new InvitePreview(found.Org, found.Invitation, inviter is null ? null : inviter.Name ?? inviter.Email);
    }

    /// <summary>
    /// AC-6: joins the org with the signed in account through the one consuming delete. A member already in the org
    /// keeps their role; the invitation is used up either way.
    /// </summary>
    public async Task<Outcome<AcceptedInvite>> AcceptAsync(Guid userId, string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token)) return Failure.Invalid("Send the invitation token.");
        if (!InviteToken.TryParse(token, out var parsed)) return Failure.InvitationNotFound;

        // An account with no email can match no invitation; the empty string never equals a stored email.
        var callerEmail = (await directory.GetManyAsync([userId], ct)).SingleOrDefault()?.Email ?? "";

        return await store.WriteAsync<AcceptedInvite>(async (uow, ct) =>
        {
            var consumed = await ConsumeAsync(uow, parsed, callerEmail, ct);
            if (!consumed.Succeeded) return consumed.Failure!;
            var invitation = consumed.Value!;
            var actor = Actor.User(userId);

            var org = await uow.Db.Orgs.AsNoTracking().SingleAsync(o => o.Id == invitation.OrgId, ct);
            var current = await uow.Db.RoleInAsync(userId, invitation.OrgId, ct);
            if (current is null)
                await OrgService.AddMemberAsync(uow, invitation.OrgId, userId, invitation.Role, actor, await uow.NowAsync(ct), ct, invitation.Id);
            await WriteAcceptedAsync(uow, actor, invitation, userId, ct);
            logger.LogInformation("Console user {UserId} accepted invitation {InvitationId} to org {OrgId}", userId, invitation.Id, invitation.OrgId);
            return new AcceptedInvite(new OrgView(org, current is null ? invitation.Role : Roles.Parse(current)), AlreadyMember: current is not null);
        }, ct);
    }

    /// <summary>
    /// The one path that uses an invitation up, for accept and invited sign up (spec 0008, key invariants): a read by
    /// hash finds the org, the org is locked <c>FOR UPDATE</c>, then a conditional <c>DELETE ... RETURNING</c> takes the
    /// invitation only while it is unexpired and for <paramref name="email"/> (ignoring case). When it takes nothing, a
    /// second read picks the error: gone 404, expired 410, org deleting 409, else a different email 403 (the
    /// invitation is kept).
    /// </summary>
    internal static async Task<Outcome<ConsumedInvitation>> ConsumeAsync(UnitOfWork uow, InviteToken token, string email, CancellationToken ct)
    {
        var hash = token.Hash;
        var orgId = await uow.Db.Invitations.Where(i => i.TokenHash == hash).Select(i => (Guid?)i.OrgId).SingleOrDefaultAsync(ct);
        if (orgId is null) return Failure.InvitationNotFound;
        var org = await uow.LockOrgForUpdateAsync(orgId.Value, ct);

        // A deleting org takes no one; skipping the delete lets the second read answer in the spec's order.
        if (org is not null && Statuses.Org(org.Status) == OrgStatus.Active)
        {
            await using var take = new NpgsqlCommand(
                """
                DELETE FROM orvano.platform_invitations
                WHERE token_hash = @hash AND expires_at > now() AND lower(email) = lower(@email)
                RETURNING id, org_id, role
                """, uow.Tx.Connection, uow.Tx);
            take.Parameters.AddWithValue("hash", hash);
            take.Parameters.AddWithValue("email", email);
            await using var reader = await take.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) return new ConsumedInvitation(reader.GetGuid(0), reader.GetGuid(1), Roles.Parse(reader.GetString(2)));
        }

        var left = await (
            from i in uow.Db.Invitations
            join o in uow.Db.Orgs on i.OrgId equals o.Id
            where i.TokenHash == hash
            select new { i.ExpiresAt, o.Status }).AsNoTracking().SingleOrDefaultAsync(ct);
        if (left is null) return Failure.InvitationNotFound;
        if (InvitationRules.StateAt(left.ExpiresAt, await uow.NowAsync(ct)) == InvitationState.Expired) return Failure.InvitationExpired;
        if (Statuses.Org(left.Status) != OrgStatus.Active) return Failure.OrgNotActive;
        return Failure.InvitationEmailMismatch;
    }

    /// <summary>Writes <c>platform.invitation.accepted</c> for a consumed invitation.</summary>
    internal static Task WriteAcceptedAsync(UnitOfWork uow, Actor actor, ConsumedInvitation invitation, Guid userId, CancellationToken ct) =>
        PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.InvitationAccepted, actor, invitation.Id.ToString(),
            new Dictionary<string, string>
            {
                ["orgId"] = invitation.OrgId.ToString(),
                ["invitationId"] = invitation.Id.ToString(),
                ["userId"] = userId.ToString(),
                ["role"] = Roles.Wire(invitation.Role),
            }, ct: ct);

    private static Task WriteRevokedAsync(UnitOfWork uow, Actor actor, Guid orgId, Guid invitationId, string reason, CancellationToken ct) =>
        PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.InvitationRevoked, actor, invitationId.ToString(),
            new Dictionary<string, string> { ["orgId"] = orgId.ToString(), ["invitationId"] = invitationId.ToString(), ["reason"] = reason }, ct: ct);

    private static async Task<List<Guid>> DeleteReturningIdsAsync(
        UnitOfWork uow, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, uow.Tx.Connection, uow.Tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private async Task<Dictionary<Guid, ConsoleUserSummary>> UsersByIdAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        (await directory.GetManyAsync([.. ids.Distinct()], ct)).ToDictionary(u => u.Id);
}
