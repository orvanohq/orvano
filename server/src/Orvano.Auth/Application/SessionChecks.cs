using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Npgsql;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>What the api remembers about a session for the bearer check: who and where, and whether it is active.</summary>
/// <param name="Active">Not ended, not expired, and its user is <c>active</c>.</param>
/// <param name="UserId">The session's user, or empty when there is no such session.</param>
/// <param name="ProjectId">The session's project, or empty when there is no such session.</param>
internal sealed record SessionState(bool Active, Guid UserId, string ProjectId);

/// <summary>
/// The Orvano API's "session still active" check (AC-7): the session row by <c>sid</c>, not ended, not expired, of an
/// <c>active</c> user, cached for at most 30 seconds. The instance that ends a session evicts it at once, so there the
/// token stops working immediately.
/// </summary>
internal sealed class SessionChecks(AuthStore store, HybridCache cache)
{
    private static readonly HybridCacheEntryOptions Entry = new()
    {
        Expiration = AuthTimings.SessionCache,
        LocalCacheExpiration = AuthTimings.SessionCache,
    };

    public async Task<bool> IsActiveAsync(Guid sessionId, Guid userId, string projectId, CancellationToken ct)
    {
        var state = await cache.GetOrCreateAsync(
            Key(sessionId),
            (store, sessionId),
            static async (state, token) => await ReadAsync(state.store, state.sessionId, token),
            Entry,
            cancellationToken: ct);
        return state.Active && state.UserId == userId && state.ProjectId == projectId;
    }

    /// <summary>Forgets a session's cached state; call it after ending the session.</summary>
    public ValueTask EvictAsync(Guid sessionId, CancellationToken ct = default) => cache.RemoveAsync(Key(sessionId), ct);

    private static string Key(Guid sessionId) => $"auth:session:{sessionId}";

    private static Task<SessionState> ReadAsync(AuthStore store, Guid sessionId, CancellationToken ct) =>
        store.ReadAsync(async (db, token) =>
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT s.user_id, s.project_id,
                       s.ended_at IS NULL AND u.status = 'active' AND now() < least(s.idle_expires_at, s.expires_at)
                FROM orvano.auth_sessions s
                JOIN orvano.auth_users u ON u.id = s.user_id
                WHERE s.id = @id
                """, conn);
            cmd.Parameters.AddWithValue("id", sessionId);
            await using var reader = await cmd.ExecuteReaderAsync(token);
            return await reader.ReadAsync(token)
                ? new SessionState(reader.GetBoolean(2), reader.GetGuid(0), reader.GetString(1))
                : new SessionState(false, Guid.Empty, "");
        }, ct);
}
