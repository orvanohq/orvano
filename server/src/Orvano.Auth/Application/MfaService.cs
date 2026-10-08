using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;
using Orvano.Platform.Contracts;

namespace Orvano.Auth.Application;

/// <summary>The signed in user's MFA state (the contract's <c>MfaStatus</c>).</summary>
internal sealed record MfaStatusView(
    bool MfaEnabled, bool TotpConfirmed, DateTimeOffset? TotpConfirmedAt, int RecoveryCodesRemaining, int PasskeyCount, IReadOnlyList<string> FactorsAvailable);

/// <summary>A new authenticator app secret waiting for its first code (the contract's <c>TotpSetup</c>). Never log it.</summary>
internal sealed record TotpSetupView(string Secret, string Uri, DateTimeOffset ExpiresAt);

/// <summary>MFA turned on (the contract's <c>TotpConfirmation</c>): the recovery codes, shown once, and the stronger session.</summary>
internal sealed record TotpConfirmationView(IReadOnlyList<string> RecoveryCodes, SessionTokensView Session);

/// <summary>The second factor a step two or step up answers with: exactly one is set.</summary>
internal sealed record FactorAnswer(string? TotpCode, string? RecoveryCode)
{
    /// <summary>The factor answered, or null when the body sent none or more than one.</summary>
    public string? Factor => (TotpCode, RecoveryCode) switch
    {
        ({ }, null) => MfaFactors.Totp,
        (null, { }) => MfaFactors.RecoveryCode,
        _ => null,
    };
}

/// <summary>
/// MFA for app users and console accounts (spec 0013): TOTP enrollment, the user's MFA state, and the second step of
/// sign in. Use cases know no HTTP; they return an <see cref="Outcome{T}"/>.
/// </summary>
internal sealed class MfaService(
    AuthStore store,
    Sessions sessions,
    SessionChecks checks,
    AccessTokens tokens,
    SigningKeys keys,
    AccountService accounts,
    MfaFactorStore factors,
    IProjectDirectory projects,
    RateLimits limits)
{
    /// <summary>The user's MFA state (AC-16).</summary>
    public async Task<Outcome<MfaStatusView>> GetAsync(string projectId, Guid userId, CancellationToken ct)
    {
        var state = await store.ReadAsync((db, token) =>
            MfaFactorState.ReadAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, userId, token), ct);
        var available = new List<string>();
        if (state.Policy.TotpEnabled) available.Add(MfaFactors.Totp);
        if (state.Policy.PasskeysEnabled) available.Add(MfaFactors.Passkey);
        return new MfaStatusView(state.MfaEnabled, state.TotpConfirmedAt is not null, state.TotpConfirmedAt, state.RecoveryCodesRemaining,
            state.ActivePasskeys, available);
    }

    /// <summary>
    /// Starts TOTP enrollment (AC-12): after the enrollment check (AC-17), a new 20 byte secret replaces any pending
    /// one of the user, under the user lock. Refused while TOTP is confirmed, while the project has TOTP off, and while
    /// the user's email is not verified (a user without an email may enroll).
    /// </summary>
    public async Task<Outcome<TotpSetupView>> CreateTotpAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        if (await EnrollmentCheckAsync(projectId, userId, sessionId, ct) is { } refused) return refused;
        var issuer = projectId == ConsoleProject.Id ? "Orvano" : await ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<TotpSetupView>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            var state = await MfaFactorState.ReadAsync(uow.Tx.Connection!, uow.Tx, projectId, userId, token);
            if (state.TotpConfirmedAt is not null) return Failure.MfaAlreadyEnabled;
            if (!state.Policy.TotpEnabled) return Failure.FactorNotEnabled;
            if (user.Email is not null && user.EmailVerifiedAt is null) return Failure.EmailNotVerified;

            var secret = Totp.NewSecret();
            await using var upsert = new NpgsqlCommand(
                """
                INSERT INTO orvano.auth_totp_factors (user_id, project_id, secret_ciphertext)
                VALUES (@user, @project, @secret)
                ON CONFLICT (user_id) DO UPDATE
                SET secret_ciphertext = excluded.secret_ciphertext, last_used_step = NULL, created_at = now(), updated_at = now()
                WHERE auth_totp_factors.confirmed_at IS NULL
                RETURNING created_at
                """, uow.Tx.Connection, uow.Tx);
            upsert.Parameters.AddWithValue("user", userId);
            upsert.Parameters.AddWithValue("project", projectId);
            upsert.Parameters.AddWithValue("secret", factors.SealSecret(userId, secret));
            var createdAt = (DateTime)(await upsert.ExecuteScalarAsync(token))!;

            var encoded = Base32.Encode(secret);
            var uri = Totp.Uri(issuer, user.Email ?? userId.ToString(), encoded);
            return new TotpSetupView(encoded, uri, new DateTimeOffset(createdAt, TimeSpan.Zero) + AuthTimings.PendingTotp);
        }, ct);
    }

    /// <summary>
    /// Turns MFA on with the pending factor's first code (AC-13). In one transaction: confirms the factor, replaces the
    /// recovery codes, ends every other session (<c>mfa_enabled</c>), and raises the caller's session to level 2.
    /// Answers a new access token and the current refresh token, unrotated.
    /// </summary>
    public async Task<Outcome<TotpConfirmationView>> ConfirmTotpAsync(string projectId, Guid userId, Guid sessionId, string? code, CancellationToken ct)
    {
        if (await EnrollmentCheckAsync(projectId, userId, sessionId, ct) is { } refused) return refused;
        var userKey = userId.ToString();
        var limit = limits.Check(RateLimitPolicies.FailedMfaPerUser, userKey);
        if (!limit.Allowed) return Failure.RateLimited(limit.RetryAfter);

        var outcome = await store.WriteAsync<(IReadOnlyList<string> Codes, Guid[] Ended, SessionStrength Strength, string Refresh, DateTimeOffset EndsAt, bool EmailVerified)>(
            async (uow, token) =>
            {
                if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;

                byte[]? ciphertext = null;
                await using (var read = new NpgsqlCommand(
                    """
                    SELECT secret_ciphertext FROM orvano.auth_totp_factors
                    WHERE user_id = @user AND confirmed_at IS NULL AND created_at > now() - @pending
                    """, uow.Tx.Connection, uow.Tx))
                {
                    read.Parameters.AddWithValue("user", userId);
                    read.Parameters.AddWithValue("pending", AuthTimings.PendingTotp);
                    ciphertext = (byte[]?)await read.ExecuteScalarAsync(token);
                }

                if (ciphertext is null) return Failure.TotpNotPending;
                if (!await factors.ConfirmTotpAsync(uow, userId, ciphertext, code, token))
                {
                    limits.Acquire(RateLimitPolicies.FailedMfaPerUser, userKey);
                    return Failure.InvalidMfaCode;
                }

                var codes = await factors.ReplaceRecoveryCodesAsync(uow, projectId, userId, token);
                var actor = Actor.User(userId);
                var ended = await sessions.EndAllAsync(uow, projectId, userId, SessionEndReason.MfaEnabled, actor, sessionId, token);
                var (strength, refresh, endsAt) = await StrengthenAsync(uow, sessionId, SessionStrength.ForFactor(MfaFactors.Totp), aal2: true, token);
                var ids = new Dictionary<string, string> { ["userId"] = userKey };
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.MfaEnabled, projectId, actor, userKey, ids, fields: Factor(MfaFactors.Totp), ct: token);
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.RecoveryCodesCreated, projectId, actor, userKey, ids, ct: token);
                return (codes, ended.ToArray(), strength, refresh, endsAt, user.EmailVerifiedAt is not null);
            }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        var done = outcome.Value;
        foreach (var id in done.Ended) await checks.EvictAsync(id, ct);
        var access = await tokens.IssueAsync(projectId, userId, sessionId, done.EmailVerified, done.Strength, ct);
        return new TotpConfirmationView(done.Codes, new SessionTokensView(access.Token, access.ExpiresAt, done.Refresh, done.EndsAt, sessionId));
    }

    /// <summary>
    /// The second step of sign in (AC-8), in the order of the spec's <i>Step two decision</i>: the IP limit, the body,
    /// the ticket (read without a lock), the user's limit, then under the user lock and the ticket's row lock, the
    /// factor. A wrong factor commits its attempt with the refusal (the second permitted use of
    /// <see cref="AuthStore.WriteDecidingAsync"/>); the fifth deletes the ticket. A right one deletes the ticket and
    /// creates the session at level 2 in one transaction.
    /// </summary>
    public async Task<Outcome<SignedIn>> CompleteAsync(string projectId, string? ticketValue, FactorAnswer answer, string ipKey, CancellationToken ct)
    {
        var ipLimit = limits.Check(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
        if (!ipLimit.Allowed) return Failure.RateLimited(ipLimit.RetryAfter);

        if (answer.Factor is not { } factor) return Failure.Invalid("Send exactly one of totpCode and recoveryCode.");
        if (factor == MfaFactors.Totp && !Totp.IsWellFormed(answer.TotpCode)) return Failure.Invalid("totpCode must be 6 digits.");
        if (!MfaTicket.TryParse(ticketValue, out var ticket)) return Failure.Invalid("The ticket is not an MFA ticket.");

        var found = await store.ReadAsync((db, token) =>
            MfaFactorStore.ReadTicketAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, ticket.Hash, lockRow: false, token), ct);
        if (found is null)
        {
            limits.Acquire(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
            return Failure.InvalidMfaTicket;
        }

        var userKey = found.UserId.ToString();
        var userLimit = limits.Check(RateLimitPolicies.FailedMfaPerUser, userKey);
        if (!userLimit.Allowed) return Failure.RateLimited(userLimit.RetryAfter);
        await keys.GetActiveAsync(projectId, ct);

        var wrong = false;
        var outcome = await store.WriteDecidingAsync<(Data.UserRow User, SessionGrant Grant)>(async (uow, token) =>
        {
            var locked = await UserLocks.ByIdAsync(uow, projectId, found.UserId, token);
            if (locked is null || await MfaFactorStore.ReadTicketAsync(uow.Tx.Connection!, uow.Tx, projectId, ticket.Hash, lockRow: true, token) is not { } row)
                return (Failure.InvalidMfaTicket, false);

            var state = await MfaFactorState.ReadAsync(uow.Tx.Connection!, uow.Tx, projectId, row.UserId, token);
            if (!state.Factors.Contains(factor)) return (Failure.FactorNotEnabled, false);

            var right = factor == MfaFactors.Totp
                ? await factors.UseTotpAsync(uow, row.UserId, answer.TotpCode, token)
                : await factors.UseRecoveryCodeAsync(uow, row.UserId, answer.RecoveryCode, token);
            if (!right)
            {
                wrong = true;
                await CountWrongAsync(uow, row, token);
                return (Failure.InvalidMfaCode, true);
            }

            await DeleteTicketAsync(uow, row.Id, token);
            if (locked.Status == UserStatuses.Blocked) return (Failure.UserBlocked, false);

            var actor = Actor.User(row.UserId);
            if (factor == MfaFactors.RecoveryCode)
            {
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.RecoveryCodeUsed, projectId, actor, userKey,
                    new Dictionary<string, string> { ["userId"] = userKey }, ct: token);
            }

            var strength = SessionStrength.StepOne(row.Method).With(SessionStrength.ForFactor(factor), aal2: true);
            var grant = await sessions.CreateAsync(uow, projectId, row.UserId, row.Client, actor, row.Method, token, row.Provider, strength);
            var user = await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == row.UserId, token);
            return ((user, grant), true);
        }, ct);

        if (wrong) limits.Acquire(RateLimitPolicies.FailedMfaPerUser, userKey);
        if (!outcome.Succeeded) return outcome.Failure!;
        return await accounts.SignedInAsync(projectId, outcome.Value.User, outcome.Value.Grant, ct);
    }

    /// <summary>
    /// The enrollment check (AC-17): a user with MFA on needs a strong check on this session within 10 minutes; a user
    /// without MFA needs a session created, or strongly checked, within 10 minutes.
    /// </summary>
    private async Task<Failure?> EnrollmentCheckAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        var (mfaEnabled, fresh, strong) = await store.ReadAsync(async (db, token) =>
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
            return await reader.ReadAsync(token) ? (state.MfaEnabled, reader.GetBoolean(0), reader.GetBoolean(1)) : (state.MfaEnabled, false, false);
        }, ct);

        if (mfaEnabled) return strong ? null : Failure.MfaVerificationRequired;
        return fresh || strong ? null : Failure.ReauthenticationRequired;
    }

    /// <summary>
    /// Raises the caller's session after a verified factor: adds its <c>amr</c> values, level 2 when asked, and
    /// <c>strong_auth_at = now()</c>. Answers the new strength and the current refresh token, decrypted, unrotated.
    /// </summary>
    private async Task<(SessionStrength Strength, string Refresh, DateTimeOffset EndsAt)> StrengthenAsync(
        AuthUnitOfWork uow, Guid sessionId, IEnumerable<string> added, bool aal2, CancellationToken ct)
    {
        SessionStrength current;
        await using (var read = new NpgsqlCommand("SELECT aal, amr FROM orvano.auth_sessions WHERE id = @id FOR UPDATE", uow.Tx.Connection, uow.Tx))
        {
            read.Parameters.AddWithValue("id", sessionId);
            await using var reader = await read.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            current = new SessionStrength(reader.GetInt16(0), reader.GetFieldValue<string[]>(1));
        }

        var next = current.With(added, aal2);
        await using var update = new NpgsqlCommand(
            """
            UPDATE orvano.auth_sessions SET aal = @aal, amr = @amr, strong_auth_at = now()
            WHERE id = @id
            RETURNING refresh_ciphertext, least(idle_expires_at, expires_at)
            """, uow.Tx.Connection, uow.Tx);
        update.Parameters.AddWithValue("aal", NpgsqlDbType.Smallint, next.Aal);
        update.Parameters.AddWithValue("amr", NpgsqlDbType.Array | NpgsqlDbType.Text, next.Amr.ToArray());
        update.Parameters.AddWithValue("id", sessionId);
        await using var updated = await update.ExecuteReaderAsync(ct);
        await updated.ReadAsync(ct);
        var refresh = sessions.Open(updated.GetFieldValue<byte[]>(0), sessionId);
        return (next, refresh, new DateTimeOffset(updated.GetFieldValue<DateTime>(1), TimeSpan.Zero));
    }

    private static async Task CountWrongAsync(AuthUnitOfWork uow, TicketRow row, CancellationToken ct)
    {
        if (row.Attempts + 1 >= AuthTimings.MfaTicketAttempts)
        {
            await DeleteTicketAsync(uow, row.Id, ct);
            return;
        }

        await using var count = new NpgsqlCommand("UPDATE orvano.auth_mfa_tickets SET attempts = attempts + 1 WHERE id = @id", uow.Tx.Connection, uow.Tx);
        count.Parameters.AddWithValue("id", row.Id);
        await count.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteTicketAsync(AuthUnitOfWork uow, Guid ticketId, CancellationToken ct)
    {
        await using var delete = new NpgsqlCommand("DELETE FROM orvano.auth_mfa_tickets WHERE id = @id", uow.Tx.Connection, uow.Tx);
        delete.Parameters.AddWithValue("id", ticketId);
        await delete.ExecuteNonQueryAsync(ct);
    }

    private static Dictionary<string, string?> Factor(string factor) => new() { ["factor"] = factor };

    private async Task<string> ProjectNameAsync(string projectId, CancellationToken ct) =>
        (await projects.GetAsync(projectId, ct))?.Name ?? throw new InvalidOperationException($"Project {projectId} vanished during MFA enrollment.");
}
