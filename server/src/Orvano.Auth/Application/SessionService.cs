using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Core.Paging;

namespace Orvano.Auth.Application;

/// <summary>One page of a list, and the cursor that continues it.</summary>
internal sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>An active session as the list shows it (the contract's <c>Session</c>).</summary>
internal sealed record SessionView(
    Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastRefreshedAt, string? UserAgent, string? Sdk, IPAddress? IpAddress, bool Current, string Method,
    string? Provider = null);

/// <summary>
/// The signed in user's sessions (spec 0004, <c>account</c> service): refresh with rotation, grace, and reuse
/// detection (AC-8), sign out (AC-10), and listing and ending sessions (AC-16).
/// </summary>
internal sealed class SessionService(AuthStore store, Sessions sessions, SessionChecks checks, AccessTokens tokens)
{
    /// <summary>What the locked refresh decided; a reuse is committed (the session ended) and still answers 401.</summary>
    private sealed record Refreshed(RefreshAction Action, Guid UserId, string? RefreshToken, DateTimeOffset EndsAt, bool EmailVerified = false);

    /// <summary>
    /// Trades a refresh token (AC-8, AC-9), deciding on the session row locked <c>FOR UPDATE</c>, so a refresh racing a
    /// sign out or another refresh sees the other's result. Times come from the database clock.
    /// </summary>
    public async Task<Outcome<SessionTokensView>> RefreshAsync(string projectId, string? refreshToken, ClientInfo client, CancellationToken ct)
    {
        if (!RefreshToken.TryParse(refreshToken, out var presented)) return Failure.InvalidRefreshToken;

        var outcome = await store.WriteAsync<Refreshed>(async (uow, token) =>
        {
            await using var read = new NpgsqlCommand(
                """
                SELECT s.user_id, s.ended_at IS NOT NULL OR u.status <> 'active', s.idle_expires_at, s.expires_at,
                       s.refresh_hash, s.previous_refresh_hash, s.rotated_at, s.refresh_ciphertext, now(), u.email_verified_at IS NOT NULL
                FROM orvano.auth_sessions s
                JOIN orvano.auth_users u ON u.id = s.user_id
                WHERE s.id = @id AND s.project_id = @project
                FOR UPDATE OF s
                """, uow.Tx.Connection, uow.Tx);
            read.Parameters.AddWithValue("id", presented.SessionId);
            read.Parameters.AddWithValue("project", projectId);

            Guid userId;
            RefreshState state;
            byte[] sealedToken;
            DateTimeOffset now;
            bool emailVerified;
            await using (var reader = await read.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token)) return new Refreshed(RefreshAction.Refuse, Guid.Empty, null, default);
                userId = reader.GetGuid(0);
                state = new RefreshState(
                    reader.GetBoolean(1),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetFieldValue<byte[]>(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<byte[]>(5),
                    reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6));
                sealedToken = reader.GetFieldValue<byte[]>(7);
                now = reader.GetFieldValue<DateTimeOffset>(8);
                emailVerified = reader.GetBoolean(9);
            }

            var action = RefreshDecision.Decide(state, presented.SecretHash, now);
            switch (action)
            {
                case RefreshAction.Rotate:
                    var next = RefreshToken.New(presented.SessionId);
                    await using (var rotate = new NpgsqlCommand(
                        """
                        UPDATE orvano.auth_sessions
                        SET previous_refresh_hash = refresh_hash, rotated_at = now(), refresh_hash = @hash, refresh_ciphertext = @ciphertext,
                            last_refreshed_at = now(), idle_expires_at = least(now() + @idle, expires_at), ip_last = @ip
                        WHERE id = @id
                        RETURNING least(idle_expires_at, expires_at)
                        """, uow.Tx.Connection, uow.Tx))
                    {
                        rotate.Parameters.AddWithValue("hash", next.SecretHash);
                        rotate.Parameters.AddWithValue("ciphertext", sessions.Seal(next, presented.SessionId));
                        rotate.Parameters.AddWithValue("idle", AuthTimings.IdleExpiry);
                        rotate.Parameters.AddWithValue("ip", NpgsqlDbType.Inet, (object?)client.Ip ?? DBNull.Value);
                        rotate.Parameters.AddWithValue("id", presented.SessionId);
                        var endsAt = (DateTime)(await rotate.ExecuteScalarAsync(token))!;
                        return new Refreshed(action, userId, next.Value, new DateTimeOffset(endsAt, TimeSpan.Zero), emailVerified);
                    }

                case RefreshAction.Replay:
                    // The pair the winning refresh got: the current token, decrypted, with a fresh access token.
                    return new Refreshed(action, userId, sessions.Open(sealedToken, presented.SessionId),
                        SessionLifetime.EndsAt(state.IdleExpiresAt, state.ExpiresAt), emailVerified);

                case RefreshAction.Reuse:
                    await sessions.EndAsync(uow, projectId, userId, presented.SessionId, SessionEndReason.ReuseDetected, Actor.System, token);
                    return new Refreshed(action, userId, null, default);

                default:
                    return new Refreshed(RefreshAction.Refuse, userId, null, default);
            }
        }, ct);

        var refreshed = outcome.Value!;
        if (refreshed.Action == RefreshAction.Reuse) await checks.EvictAsync(presented.SessionId, ct);
        if (refreshed.RefreshToken is not { } current) return Failure.InvalidRefreshToken;

        var access = await tokens.IssueAsync(projectId, refreshed.UserId, presented.SessionId, refreshed.EmailVerified, ct);
        return new SessionTokensView(access.Token, access.ExpiresAt, current, refreshed.EndsAt, presented.SessionId);
    }

    /// <summary>The user's active sessions, newest first (AC-16); <paramref name="current"/> marks the caller's.</summary>
    public async Task<Outcome<Page<SessionView>>> ListAsync(string projectId, Guid userId, Guid? current, string? cursor, int? limit, CancellationToken ct)
    {
        if (PageCursor.Limit(limit) is not { } size) return Failure.Invalid($"limit must be 1 to {PageCursor.MaxLimit}.");
        PagePosition? after = null;
        if (cursor is not null)
        {
            if (!PageCursor.TryDecode(cursor, out var position) || !Guid.TryParse(position.Id, out _)) return Failure.InvalidCursor;
            after = position;
        }

        var rows = await store.ReadAsync(async (db, token) =>
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = new NpgsqlCommand(
                $"""
                SELECT id, created_at, last_refreshed_at, user_agent, sdk, ip_last, method, provider
                FROM orvano.auth_sessions
                WHERE user_id = @user AND project_id = @project AND ended_at IS NULL AND now() < least(idle_expires_at, expires_at)
                {(after is null ? "" : "AND (created_at, id) < (@afterCreated, @afterId)")}
                ORDER BY created_at DESC, id DESC
                LIMIT @take
                """, conn);
            cmd.Parameters.AddWithValue("user", userId);
            cmd.Parameters.AddWithValue("project", projectId);
            cmd.Parameters.AddWithValue("take", size + 1);
            if (after is not null)
            {
                cmd.Parameters.AddWithValue("afterCreated", after.CreatedAt);
                cmd.Parameters.AddWithValue("afterId", Guid.Parse(after.Id));
            }

            var found = new List<SessionView>();
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var id = reader.GetGuid(0);
                found.Add(new SessionView(
                    id,
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<IPAddress>(5),
                    id == current,
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }

            return found;
        }, ct);

        var items = rows.Take(size).ToList();
        var next = rows.Count > size ? PageCursor.Encode(new PagePosition(items[^1].CreatedAt, items[^1].Id.ToString())) : null;
        return new Page<SessionView>(items, next);
    }

    /// <summary>Signs out (AC-10): ends the caller's session, and the api refuses its access token at once.</summary>
    public async Task<Outcome<Done>> SignOutAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        await store.WriteAsync<bool>(async (uow, token) =>
            await sessions.EndAsync(uow, projectId, userId, sessionId, SessionEndReason.SignOut, Actor.User(userId), token), ct);
        await checks.EvictAsync(sessionId, ct);
        return default(Done);
    }

    /// <summary>
    /// Signs out with the refresh token instead of an access token (the console's sign out once its access cookie has
    /// expired). Only the session's current secret ends it, checked on the locked row, so a session ID alone never
    /// can. Anything else changes nothing.
    /// </summary>
    public async Task SignOutWithRefreshTokenAsync(string projectId, string? refreshToken, CancellationToken ct)
    {
        if (!RefreshToken.TryParse(refreshToken, out var presented)) return;

        var outcome = await store.WriteAsync<bool>(async (uow, token) =>
        {
            Guid userId;
            byte[] currentHash;
            await using (var read = new NpgsqlCommand(
                "SELECT user_id, refresh_hash FROM orvano.auth_sessions WHERE id = @id AND project_id = @project AND ended_at IS NULL FOR UPDATE",
                uow.Tx.Connection, uow.Tx))
            {
                read.Parameters.AddWithValue("id", presented.SessionId);
                read.Parameters.AddWithValue("project", projectId);
                await using var reader = await read.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token)) return false;
                userId = reader.GetGuid(0);
                currentHash = reader.GetFieldValue<byte[]>(1);
            }

            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(presented.SecretHash, currentHash)) return false;
            return await sessions.EndAsync(uow, projectId, userId, presented.SessionId, SessionEndReason.SignOut, Actor.User(userId), token);
        }, ct);

        if (outcome.Value) await checks.EvictAsync(presented.SessionId, ct);
    }

    /// <summary>
    /// Ends one session of the user (AC-16, AC-17, <c>revoked</c>). A session of another user, or none at all, is
    /// 404 <c>session_not_found</c>; one of the user's that has already ended answers as done.
    /// </summary>
    public async Task<Outcome<Done>> EndAsync(string projectId, Guid userId, string sessionId, Actor actor, CancellationToken ct)
    {
        if (!Guid.TryParse(sessionId, out var id)) return Failure.SessionNotFound;

        var outcome = await store.WriteAsync<Done>(async (uow, token) =>
        {
            if (await sessions.EndAsync(uow, projectId, userId, id, SessionEndReason.Revoked, actor, token)) return default(Done);
            if (!await uow.Db.Sessions.AnyAsync(s => s.Id == id && s.UserId == userId && s.ProjectId == projectId, token)) return Failure.SessionNotFound;
            return default(Done);
        }, ct);

        if (outcome.Succeeded) await checks.EvictAsync(id, ct);
        return outcome;
    }

    /// <summary>
    /// Ends every session of the user (AC-17), or all but <paramref name="keep"/>, the caller's (AC-16), as
    /// <c>revoked</c>.
    /// </summary>
    public async Task<Outcome<Done>> EndAllAsync(string projectId, Guid userId, Guid? keep, Actor actor, CancellationToken ct)
    {
        var outcome = await store.WriteAsync<Guid[]>(async (uow, token) =>
            (await sessions.EndAllAsync(uow, projectId, userId, SessionEndReason.Revoked, actor, keep, token)).ToArray(), ct);
        foreach (var id in outcome.Value!) await checks.EvictAsync(id, ct);
        return default(Done);
    }
}
