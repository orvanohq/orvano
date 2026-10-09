using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;

namespace Orvano.Auth.Application;

/// <summary>
/// How recently the caller's session proved who it is (spec 0013, AC-17, AC-18): whether the user has MFA on, whether
/// the session was created within <see cref="AuthTimings.StrongAuthWindow"/>, and whether it passed a second factor or
/// a passkey within it. Read without locks; the write that follows checks its own rows again under the user lock.
/// </summary>
internal sealed record SessionRecency(bool MfaEnabled, bool Fresh, bool Strong);

/// <summary>The enrollment check (AC-17) and the step up check (AC-18), shared by every operation they guard.</summary>
internal sealed class StepUp(AuthStore store, MethodPolicies policies, PasswordHasher hasher, RateLimits limits)
{
    /// <summary>The caller's <see cref="SessionRecency"/>; an unknown session reads as neither fresh nor strong.</summary>
    public Task<SessionRecency> ReadAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct) =>
        store.ReadAsync(async (db, token) =>
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            var state = await MfaFactorState.ReadAsync(policies, conn, null, projectId, userId, token);
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
    /// The enrollment check (AC-17) of the operations that add a way in (<c>createTotp</c>,
    /// <c>createPasskeyRegistration</c>, and the two link operations): a user with MFA on needs a strong check on this
    /// session within 10 minutes (403 <c>mfa_verification_required</c>). A user without MFA passes with a strong check;
    /// otherwise a user with a password must send it (missing or wrong: 401 <c>invalid_credentials</c>, one Argon2id
    /// run), and a user without one needs a session created within 10 minutes (403 <c>reauthentication_required</c>).
    /// An access token alone, however fresh, never adds a factor to an account that has a password.
    /// <c>auth.password_check.user</c> is taken only when a sent password is actually checked (spec 0014, AC-24): a
    /// password the check ignores (MFA on, a strong check already, no password on the account) counts nothing.
    /// </summary>
    public async Task<Failure?> EnrollmentAsync(string projectId, Guid userId, Guid sessionId, string? password, CancellationToken ct)
    {
        var recency = await ReadAsync(projectId, userId, sessionId, ct);
        if (recency.MfaEnabled) return recency.Strong ? null : Failure.MfaVerificationRequired;
        if (recency.Strong) return null;

        var hash = await store.ReadAsync((db, token) =>
            db.Passwords.AsNoTracking().Where(p => p.UserId == userId && p.ProjectId == projectId).Select(p => p.Hash).SingleOrDefaultAsync(token), ct);
        if (hash is null) return recency.Fresh ? null : Failure.ReauthenticationRequired;

        if (password is null) return Failure.InvalidCredentials;
        var limit = limits.Acquire(RateLimitPolicies.PasswordCheckPerUser, userId.ToString());
        if (!limit.Allowed) return Failure.RateLimited(limit.RetryAfter);

        var wellFormed = PasswordPolicy.TryNormalize(password, out var normalized);
        var check = await hasher.TryVerifyAsync(normalized, wellFormed ? hash : null, ct);
        if (check is null) return Failure.Busy;
        return check.Value.Matches ? null : Failure.InvalidCredentials;
    }

    /// <summary>
    /// <c>auth.mfa_enroll.user</c>, taken only once the enrollment or step up check passed (spec 0014, AC-24), so
    /// refused checks never spend the user's enrollments.
    /// </summary>
    public Failure? TakeEnrollLimit(Guid userId)
    {
        var limit = limits.Acquire(RateLimitPolicies.MfaEnrollPerUser, userId.ToString());
        return limit.Allowed ? null : Failure.RateLimited(limit.RetryAfter);
    }

    /// <summary>
    /// The recency rule of <c>deletePasskey</c> (AC-18): a user with MFA on needs a strong check on this session within
    /// 10 minutes; a user without MFA needs a session created, or strongly checked, within 10 minutes.
    /// </summary>
    public async Task<Failure?> RecentAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
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
