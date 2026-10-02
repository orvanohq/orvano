using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orvano.Auth.Data;
using Orvano.Auth.Domain;
using Orvano.Core.Paging;

namespace Orvano.Auth.Application;

/// <summary>Filters for listing a project's users (AC-17, spec 0010 AC-21). Every one is optional.</summary>
internal sealed record UserFilter(string? EmailPrefix, string? Status, DateTimeOffset? CreatedAfter, DateTimeOffset? CreatedBefore, bool? EmailVerified = null);

/// <summary>
/// A project's users as a server (API key) or the console manages them (spec 0004, <c>users</c> service, AC-17,
/// AC-18). The caller names the actor, so the same use cases serve <c>consoleUsers</c>.
/// </summary>
internal sealed class UsersService(
    AuthStore store, AccountService accounts, SessionService sessionService, Sessions sessions, SessionChecks checks,
    VerificationService verification, RecoveryService recovery)
{
    /// <summary>The project's users, newest first, cursor paged, optionally filtered.</summary>
    public async Task<Outcome<Page<UserRow>>> ListAsync(string projectId, UserFilter filter, string? cursor, int? limit, CancellationToken ct)
    {
        if (PageCursor.Limit(limit) is not { } size) return Failure.Invalid($"limit must be 1 to {PageCursor.MaxLimit}.");
        if (filter.Status is not (null or UserStatuses.Active or UserStatuses.Blocked)) return Failure.Invalid("status must be active or blocked.");
        PagePosition? after = null;
        Guid afterId = default;
        if (cursor is not null)
        {
            if (!PageCursor.TryDecode(cursor, out var position) || !Guid.TryParse(position.Id, out afterId)) return Failure.InvalidCursor;
            after = position;
        }

        var rows = await store.ReadAsync(async (db, token) =>
        {
            var query = db.Users.AsNoTracking().Where(u => u.ProjectId == projectId);
            if (!string.IsNullOrEmpty(filter.EmailPrefix))
            {
                // Matches auth_users_email_prefix_idx: lower(email) with text_pattern_ops.
                var pattern = EscapeLike(filter.EmailPrefix.ToLowerInvariant()) + "%";
                query = query.Where(u => EF.Functions.Like(u.Email!.ToLower(), pattern, "\\"));
            }

            if (filter.Status is { } status) query = query.Where(u => u.Status == status);
            if (filter.CreatedAfter is { } createdAfter) query = query.Where(u => u.CreatedAt > createdAfter);
            if (filter.CreatedBefore is { } createdBefore) query = query.Where(u => u.CreatedAt < createdBefore);
            if (filter.EmailVerified is { } verified) query = verified ? query.Where(u => u.EmailVerifiedAt != null) : query.Where(u => u.EmailVerifiedAt == null);
            if (after is not null)
                query = query.Where(u => u.CreatedAt < after.CreatedAt || u.CreatedAt == after.CreatedAt && u.Id.CompareTo(afterId) < 0);

            return await query.OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id).Take(size + 1).ToListAsync(token);
        }, ct);

        var items = rows.Take(size).ToList();
        var next = rows.Count > size ? PageCursor.Encode(new PagePosition(items[^1].CreatedAt, items[^1].Id.ToString())) : null;
        return new Page<UserRow>(items, next);
    }

    /// <summary>One user of the project.</summary>
    public async Task<Outcome<UserRow>> GetAsync(string projectId, string userId, CancellationToken ct) =>
        Guid.TryParse(userId, out var id) && await FindAsync(projectId, id, ct) is { } user ? user : Failure.UserNotFound;

    /// <summary>Creates a user without a session (AC-17), already verified when <paramref name="emailVerified"/> (spec 0010, AC-21).</summary>
    public Task<Outcome<UserRow>> CreateAsync(
        string projectId, string? email, string? password, string? name, Actor actor, CancellationToken ct, bool emailVerified = false) =>
        accounts.CreateUserAsync(projectId, email, password, name, actor, ct, emailVerified);

    /// <summary>
    /// Marks the email verified or not (spec 0010, AC-21). True keeps an earlier date (else now) and deletes live
    /// verification links; false clears it. A real change writes <c>auth.user.updated</c>.
    /// </summary>
    public async Task<Outcome<UserRow>> UpdateEmailVerificationAsync(string projectId, string userId, bool verified, Actor actor, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var id)) return Failure.UserNotFound;
        return await store.WriteAsync<UserRow>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, id, token) is not { } user) return Failure.UserNotFound;
            if ((user.EmailVerifiedAt is not null) != verified)
            {
                await using (var update = new NpgsqlCommand(
                    "UPDATE orvano.auth_users SET email_verified_at = CASE WHEN @verified THEN now() END, updated_at = now() WHERE id = @id",
                    uow.Tx.Connection, uow.Tx))
                {
                    update.Parameters.AddWithValue("verified", verified);
                    update.Parameters.AddWithValue("id", id);
                    await update.ExecuteNonQueryAsync(token);
                }

                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpdated, projectId, actor, id.ToString(),
                    new Dictionary<string, string> { ["userId"] = id.ToString() }, ["emailVerified"], ct: token);
            }

            if (verified) await EmailTokens.DeleteForUserAsync(uow, projectId, id, EmailTokenKind.Verification, token);
            return await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == id, token);
        }, ct);
    }

    /// <summary>Emails the user a verification link (spec 0010, AC-21): 409 <c>email_already_verified</c> for a verified user.</summary>
    public async Task<Outcome<Done>> CreateVerificationAsync(string projectId, string userId, string? redirectUrl, Actor actor, CancellationToken ct) =>
        Guid.TryParse(userId, out var id) && await FindAsync(projectId, id, ct) is not null
            ? await verification.RequestAsync(projectId, id, redirectUrl, actor, ct)
            : Failure.UserNotFound;

    /// <summary>Emails the user a password reset link (spec 0010, AC-21): 403 <c>user_blocked</c> for a blocked user.</summary>
    public async Task<Outcome<Done>> CreateRecoveryAsync(string projectId, string userId, string? redirectUrl, Actor actor, CancellationToken ct) =>
        Guid.TryParse(userId, out var id) && await FindAsync(projectId, id, ct) is not null
            ? await recovery.SendForUserAsync(projectId, id, redirectUrl, actor, ct)
            : Failure.UserNotFound;

    /// <summary>
    /// Changes the user's email at once (spec 0010, AC-21): verified only when <paramref name="emailVerified"/>. 409
    /// <c>email_already_in_use</c> for a taken address; deletes the user's live tokens and keeps their sessions.
    /// </summary>
    public async Task<Outcome<UserRow>> UpdateEmailAsync(string projectId, string userId, string? email, bool emailVerified, Actor actor, CancellationToken ct)
    {
        if (!EmailRule.TryNormalize(email, out var trimmed)) return Failure.Invalid("The email must be an address of at most 320 characters.");
        if (!Guid.TryParse(userId, out var id)) return Failure.UserNotFound;

        return await store.WriteAsync<UserRow>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, id, token) is not { } user) return Failure.UserNotFound;
            if (await EmailChangeService.EmailTakenAsync(uow, projectId, trimmed, id, token)) return Failure.EmailAlreadyInUse;
            if (!await EmailChangeService.SetEmailAsync(uow, id, trimmed, emailVerified, token)) return Failure.EmailAlreadyInUse;

            await EmailTokens.DeleteForUserAsync(uow, projectId, id, kind: null, token);
            var changed = new List<string>();
            if (!string.Equals(user.Email, trimmed, StringComparison.Ordinal)) changed.Add("email");
            // A new verified address gets a new date, so it changes even when it was verified before.
            if (emailVerified || user.EmailVerifiedAt is not null) changed.Add("emailVerified");
            if (changed.Count > 0)
            {
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpdated, projectId, actor, id.ToString(),
                    new Dictionary<string, string> { ["userId"] = id.ToString() }, changed, ct: token);
            }

            return await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == id, token);
        }, ct);
    }

    /// <summary>
    /// Blocks a user (AC-18): <c>status = blocked</c>, every session ended (<c>user_blocked</c>), and
    /// <c>auth.user.blocked</c>, in one transaction. Blocking a blocked user changes nothing.
    /// </summary>
    public Task<Outcome<UserRow>> BlockAsync(string projectId, string userId, Actor actor, CancellationToken ct) =>
        SetStatusAsync(projectId, userId, UserStatuses.Blocked, actor, ct);

    /// <summary>Unblocks a user (AC-18) and writes <c>auth.user.unblocked</c>. Old sessions stay ended.</summary>
    public Task<Outcome<UserRow>> UnblockAsync(string projectId, string userId, Actor actor, CancellationToken ct) =>
        SetStatusAsync(projectId, userId, UserStatuses.Active, actor, ct);

    /// <summary>Deletes a user with their password and sessions (AC-17).</summary>
    public async Task<Outcome<Done>> DeleteAsync(string projectId, string userId, Actor actor, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var id)) return Failure.UserNotFound;
        var outcome = await store.WriteAsync<Guid[]>(async (uow, token) =>
            await UserRecords.DeleteAsync(uow, projectId, id, actor, token) is { } ended ? ended : Failure.UserNotFound, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        foreach (var session in outcome.Value!) await checks.EvictAsync(session, ct);
        return default(Done);
    }

    /// <summary>A user's active sessions, newest first; none is <c>current</c>.</summary>
    public async Task<Outcome<Page<SessionView>>> ListSessionsAsync(string projectId, string userId, string? cursor, int? limit, CancellationToken ct) =>
        Guid.TryParse(userId, out var id) && await FindAsync(projectId, id, ct) is not null
            ? await sessionService.ListAsync(projectId, id, current: null, cursor, limit, ct)
            : Failure.UserNotFound;

    /// <summary>Ends every session of a user (<c>revoked</c>).</summary>
    public async Task<Outcome<Done>> DeleteSessionsAsync(string projectId, string userId, Actor actor, CancellationToken ct) =>
        Guid.TryParse(userId, out var id) && await FindAsync(projectId, id, ct) is not null
            ? await sessionService.EndAllAsync(projectId, id, keep: null, actor, ct)
            : Failure.UserNotFound;

    /// <summary>Ends one session of a user (<c>revoked</c>); a session that is not theirs is 404 <c>session_not_found</c>.</summary>
    public async Task<Outcome<Done>> DeleteSessionAsync(string projectId, string userId, string sessionId, Actor actor, CancellationToken ct) =>
        Guid.TryParse(userId, out var id) && await FindAsync(projectId, id, ct) is not null
            ? await sessionService.EndAsync(projectId, id, sessionId, actor, ct)
            : Failure.UserNotFound;

    private async Task<Outcome<UserRow>> SetStatusAsync(string projectId, string userId, string status, Actor actor, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var id)) return Failure.UserNotFound;

        var outcome = await store.WriteAsync<(UserRow User, Guid[] Ended)>(async (uow, token) =>
        {
            await using var update = new NpgsqlCommand(
                "UPDATE orvano.auth_users SET status = @status, updated_at = now() WHERE id = @id AND project_id = @project AND status <> @status",
                uow.Tx.Connection, uow.Tx);
            update.Parameters.AddWithValue("status", status);
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("project", projectId);
            var changed = await update.ExecuteNonQueryAsync(token) == 1;

            Guid[] ended = [];
            if (changed)
            {
                var blocked = status == UserStatuses.Blocked;
                if (blocked)
                {
                    ended = [.. await sessions.EndAllAsync(uow, projectId, id, SessionEndReason.UserBlocked, actor, keep: null, token)];
                    // Spec 0010 AC-29: a blocked user's live email links and codes go with their sessions.
                    await EmailTokens.DeleteForUserAsync(uow, projectId, id, kind: null, token);
                }

                await AuthEvents.WriteAsync(uow.Tx, blocked ? AuthEvents.UserBlocked : AuthEvents.UserUnblocked, projectId, actor, id.ToString(),
                    new Dictionary<string, string> { ["userId"] = id.ToString() }, ct: token);
            }

            var user = await uow.Db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.ProjectId == projectId, token);
            if (user is null) return Failure.UserNotFound;
            return (user, ended);
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        foreach (var session in outcome.Value.Ended) await checks.EvictAsync(session, ct);
        return outcome.Value.User;
    }

    private Task<UserRow?> FindAsync(string projectId, Guid id, CancellationToken ct) =>
        store.ReadAsync((db, token) => db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.ProjectId == projectId, token), ct);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
