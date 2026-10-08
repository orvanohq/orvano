using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// How recently the caller's session proved who it is (spec 0013, AC-17, AC-18): whether the user has MFA on, whether
/// the session was created within <see cref="AuthTimings.StrongAuthWindow"/>, and whether it passed a second factor or
/// a passkey within it. Read without locks; the write that follows checks its own rows again under the user lock.
/// </summary>
internal sealed record SessionRecency(bool MfaEnabled, bool Fresh, bool Strong);

/// <summary>The enrollment check (AC-17) and the step up check (AC-18), shared by every operation they guard.</summary>
internal sealed class StepUp(AuthStore store)
{
    /// <summary>The caller's <see cref="SessionRecency"/>; an unknown session reads as neither fresh nor strong.</summary>
    public Task<SessionRecency> ReadAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct) =>
        store.ReadAsync(async (db, token) =>
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            var state = await MfaFactorState.ReadAsync(conn, null, projectId, userId, token);
            await using var cmd = new NpgsqlCommand(
                """
                SELECT created_at > now() - @window, coalesce(strong_auth_at > now() - @window, false)
                FROM orvano.auth_sessions WHERE id = @id AND user_id = @user AND project_id = @project
                """, conn);
            cmd.Parameters.AddWithValue("window", AuthTimings.StrongAuthWindow);
            cmd.Parameters.AddWithValue("id", sessionId);
            cmd.Parameters.AddWithValue("user", userId);
            cmd.Parameters.AddWithValue("project", projectId);
            await using var reader = await cmd.ExecuteReaderAsync(token);
            return await reader.ReadAsync(token)
                ? new SessionRecency(state.MfaEnabled, reader.GetBoolean(0), reader.GetBoolean(1))
                : new SessionRecency(state.MfaEnabled, false, false);
        }, ct);

    /// <summary>
    /// The enrollment check (AC-17): a user with MFA on needs a strong check on this session within 10 minutes; a user
    /// without MFA needs a session created, or strongly checked, within 10 minutes.
    /// </summary>
    public async Task<Failure?> EnrollmentAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        var recency = await ReadAsync(projectId, userId, sessionId, ct);
        if (recency.MfaEnabled) return recency.Strong ? null : Failure.MfaVerificationRequired;
        return recency.Fresh || recency.Strong ? null : Failure.ReauthenticationRequired;
    }

    /// <summary>
    /// The step up check of a change that needs MFA on (AC-18: <c>deleteTotp</c>, <c>createRecoveryCodes</c>): 409
    /// <c>mfa_not_enabled</c> without it, else a strong check on this session within 10 minutes.
    /// </summary>
    public async Task<Failure?> MfaChangeAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        var recency = await ReadAsync(projectId, userId, sessionId, ct);
        if (!recency.MfaEnabled) return Failure.MfaNotEnabled;
        return recency.Strong ? null : Failure.MfaVerificationRequired;
    }

    /// <summary>
    /// The step up check of a sensitive change (AC-18: <c>updatePassword</c>, <c>updateEmail</c>, <c>delete</c>): for a
    /// user with MFA on, a strong check on this session within 10 minutes, before any password check. A user without
    /// MFA passes here and keeps the change's own rules.
    /// </summary>
    public async Task<Failure?> SensitiveChangeAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        var recency = await ReadAsync(projectId, userId, sessionId, ct);
        return recency.MfaEnabled && !recency.Strong ? Failure.MfaVerificationRequired : null;
    }
}
