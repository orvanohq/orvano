using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;
using Orvano.Messaging.Contracts;

namespace Orvano.Auth.Application;

/// <summary>A user's MFA state (the contract's <c>MfaStatus</c>).</summary>
internal sealed record MfaStatusView(
    bool MfaEnabled, bool TotpConfirmed, DateTimeOffset? TotpConfirmedAt, int RecoveryCodesRemaining, int PasskeyCount, IReadOnlyList<string> FactorsAvailable);

/// <summary>A new authenticator app secret waiting for its first code (the contract's <c>TotpSetup</c>). Never log it.</summary>
internal sealed record TotpSetupView(string Secret, string Uri, DateTimeOffset ExpiresAt);

/// <summary>
/// A session after a second factor (the contract's <c>RaisedSession</c>): a new access token. The refresh token is never
/// sent back, so proving an access token can't yield one; <paramref name="RefreshTokenExpiresAt"/> is for the console's
/// cookie lifetime only.
/// </summary>
internal sealed record RaisedSessionView(string AccessToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt, Guid SessionId);

/// <summary>MFA turned on (the contract's <c>TotpConfirmation</c>): the recovery codes, shown once, and the stronger session.</summary>
internal sealed record TotpConfirmationView(IReadOnlyList<string> RecoveryCodes, RaisedSessionView Session);

/// <summary>The second factor a step two or step up answers with: exactly one is set.</summary>
internal sealed record FactorAnswer(string? TotpCode, string? RecoveryCode, PasskeyAnswerInput? Passkey = null)
{
    /// <summary>The factor answered, or null when the body sent none or more than one.</summary>
    public string? Factor => (TotpCode, RecoveryCode, Passkey) switch
    {
        ({ }, null, null) => MfaFactors.Totp,
        (null, { }, null) => MfaFactors.RecoveryCode,
        (null, null, { }) => MfaFactors.Passkey,
        _ => null,
    };

    /// <summary>The body's refusal (400 <c>invalid_request</c>), or null when it is well formed.</summary>
    public Failure? Malformed()
    {
        if (Factor is not { } factor) return Failure.Invalid("Send exactly one of totpCode, recoveryCode, and passkey.");
        if (factor == MfaFactors.Totp && !Totp.IsWellFormed(TotpCode)) return Failure.Invalid("totpCode must be 6 digits.");
        if (factor == MfaFactors.Passkey && (!Guid.TryParse(Passkey!.ChallengeId, out _) || Passkey.Credential is null))
            return Failure.Invalid("Send passkey with a challengeId and the credential the browser or the platform made.");
        return null;
    }
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
    RateLimits limits,
    StepUp stepUp,
    MethodPolicies policies,
    PasskeyService passkeys,
    SecurityAlerts alerts)
{
    /// <summary>
    /// A user's MFA state (AC-16), the one read behind <c>account.getMfa</c>, <c>users.getMfa</c>, and
    /// <c>consoleUsers.getMfa</c> (AC-27). A user not in the project is 404 <c>user_not_found</c>.
    /// </summary>
    public async Task<Outcome<MfaStatusView>> GetAsync(string projectId, Guid userId, CancellationToken ct)
    {
        var state = await store.ReadAsync(async (db, token) =>
            await db.Users.AnyAsync(u => u.Id == userId && u.ProjectId == projectId, token)
                ? await MfaFactorState.ReadAsync(policies, (NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, userId, token)
                : null, ct);
        if (state is null) return Failure.UserNotFound;
        var available = new List<string>();
        if (state.Policy.TotpEnabled) available.Add(MfaFactors.Totp);
        if (state.Policy.PasskeysEnabled) available.Add(MfaFactors.Passkey);
        return new MfaStatusView(state.MfaEnabled, state.TotpConfirmedAt is not null, state.TotpConfirmedAt, state.RecoveryCodesRemaining,
            state.ActivePasskeys, available);
    }

    /// <summary>
    /// Starts TOTP enrollment (AC-12): after the enrollment check (AC-17, with the user's current
    /// <paramref name="password"/>), a new 20 byte secret replaces any pending one of the user, under the user lock.
    /// Refused while TOTP is confirmed, while the project has TOTP off, and while the user's email is not verified (a
    /// user without an email may enroll).
    /// </summary>
    public async Task<Outcome<TotpSetupView>> CreateTotpAsync(string projectId, Guid userId, Guid sessionId, string? password, CancellationToken ct)
    {
        if ((await stepUp.EnrollmentAsync(projectId, userId, sessionId, password, ct) ?? stepUp.TakeEnrollLimit(userId)) is { } refused) return refused;
        var issuer = await alerts.ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<TotpSetupView>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            var state = await MfaFactorState.ReadAsync(policies, uow.Tx.Connection!, uow.Tx, projectId, userId, token);
            if (state.TotpConfirmedAt is not null) return Failure.MfaAlreadyEnabled;
            if (!state.Policy.TotpEnabled) return Failure.FactorNotEnabled;
            if (VerifiedEmailRule.Blocks(projectId, user.Email, user.EmailVerifiedAt)) return Failure.EmailNotVerified;

            return await factors.PendTotpAsync(uow, projectId, userId, issuer, user.Email, token);
        }, ct);
    }

    /// <summary>
    /// Starts TOTP enrollment with an enrollment ticket (spec 0014, AC-27), in place of a session: the ticket must be a
    /// live <c>enroll</c> ticket (401 <c>invalid_mfa_ticket</c>) and TOTP on (409 <c>factor_not_enabled</c>). Takes
    /// <c>auth.mfa_enroll.user</c>; otherwise <see cref="CreateTotpAsync"/>'s rules, under the user lock.
    /// </summary>
    public async Task<Outcome<TotpSetupView>> CreateEnrollmentTotpAsync(string projectId, string? ticketValue, string ipKey, CancellationToken ct)
    {
        var found = await MfaTickets.FindAsync(store, limits, projectId, ticketValue, MfaTicketPurposes.Enroll, ipKey, ct);
        if (!found.Succeeded) return found.Failure!;
        var userId = found.Value!.UserId;
        if (stepUp.TakeEnrollLimit(userId) is { } limited) return limited;
        var issuer = await alerts.ProjectNameAsync(projectId, ct);

        var vanished = false;
        var outcome = await store.WriteAsync<TotpSetupView>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user
                || await MfaTickets.RereadAsync(uow, projectId, ticketValue!, MfaTicketPurposes.Enroll, lockRow: false, token) is null)
            {
                vanished = true;
                return Failure.InvalidMfaTicket;
            }

            var state = await MfaFactorState.ReadAsync(policies, uow.Tx.Connection!, uow.Tx, projectId, userId, token);
            if (!state.Policy.TotpEnabled) return Failure.FactorNotEnabled;
            if (state.TotpConfirmedAt is not null) return Failure.MfaAlreadyEnabled;
            if (VerifiedEmailRule.Blocks(projectId, user.Email, user.EmailVerifiedAt)) return Failure.EmailNotVerified;
            return await factors.PendTotpAsync(uow, projectId, userId, issuer, user.Email, token);
        }, ct);

        // A ticket gone between the two reads counts against the IP like an unknown one at the first read.
        if (vanished) limits.Acquire(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
        return outcome;
    }

    /// <summary>
    /// Turns MFA on with the pending factor's first code (AC-13). The pending secret is the proof: only the caller of
    /// <see cref="CreateTotpAsync"/>, which passed the enrollment check, has it, so the session's age is not checked
    /// again. In one transaction: confirms the factor, replaces the recovery codes, ends every other session
    /// (<c>mfa_enabled</c>), and raises the caller's session to level 2. Answers a new access token, never the refresh
    /// token.
    /// </summary>
    public async Task<Outcome<TotpConfirmationView>> ConfirmTotpAsync(
        string projectId, Guid userId, Guid sessionId, string? code, string ipKey, CancellationToken ct)
    {
        using var failures = MfaTickets.ReserveFactor(limits, userId, ipKey, MfaFactors.Totp);
        if (!failures.Allowed) return Failure.RateLimited(failures.Decision.RetryAfter);
        var projectName = await alerts.ProjectNameAsync(projectId, ct);

        var outcome = await store.WriteAsync<(IReadOnlyList<string> Codes, Guid[] Ended, SessionStrength Strength, DateTimeOffset EndsAt, bool EmailVerified)>(
            async (uow, token) =>
            {
                if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;

                if (await MfaFactorStore.PendingTotpAsync(uow, userId, token) is not { } ciphertext) return Failure.TotpNotPending;
                if (!await factors.ConfirmTotpAsync(uow, userId, ciphertext, code, token))
                {
                    failures.Fail();
                    return Failure.InvalidMfaCode;
                }

                var (codes, ended) = await TotpConfirmedAsync(uow, projectId, projectName, user, sessionId, token);
                if (await StrengthenAsync(uow, projectId, userId, sessionId, SessionStrength.ForFactor(MfaFactors.Totp), aal2: true, token) is not { } raised)
                    return Failure.SessionNotFound;
                return (codes, ended, raised.Strength, raised.EndsAt, user.EmailVerifiedAt is not null);
            }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        var done = outcome.Value;
        foreach (var id in done.Ended) await checks.EvictAsync(id, ct);
        var access = await tokens.IssueAsync(projectId, userId, sessionId, done.EmailVerified, done.Strength, ct);
        return new TotpConfirmationView(done.Codes, new RaisedSessionView(access.Token, access.ExpiresAt, done.EndsAt, sessionId));
    }

    /// <summary>
    /// Finishes TOTP enrollment with an enrollment ticket (spec 0014, AC-27), in the order of step two: the IP limit, the
    /// ticket, the user's factor limits (AC-18), then under the user lock and the ticket's row lock, the first code. A
    /// wrong code commits its attempt with the refusal (as <see cref="CompleteAsync"/> does) and the fifth deletes the
    /// ticket. A right one turns MFA on as <see cref="ConfirmTotpAsync"/> does (recovery codes, every other session
    /// ended), applies a recovery's pending password, and creates the session at level 2 with <c>otp</c> added to the
    /// step one method's <c>amr</c>, all in one transaction.
    /// </summary>
    public async Task<Outcome<MfaEnrollmentView>> CompleteEnrollmentTotpAsync(
        string projectId, string? ticketValue, string? code, string ipKey, CancellationToken ct)
    {
        if (!Totp.IsWellFormed(code)) return Failure.Invalid("code must be 6 digits.");
        var found = await MfaTickets.FindAsync(store, limits, projectId, ticketValue, MfaTicketPurposes.Enroll, ipKey, ct);
        if (!found.Succeeded) return found.Failure!;
        var userId = found.Value!.UserId;
        using var failures = MfaTickets.ReserveFactor(limits, userId, ipKey, MfaFactors.Totp);
        if (!failures.Allowed) return Failure.RateLimited(failures.Decision.RetryAfter);
        await keys.GetActiveAsync(projectId, ct);
        var projectName = await alerts.ProjectNameAsync(projectId, ct);

        var wrong = false;
        var vanished = false;
        var outcome = await store.WriteDecidingAsync<(Data.UserRow User, SessionGrant Grant, Guid[] Ended, IReadOnlyList<string> Codes)>(async (uow, token) =>
        {
            var locked = await UserLocks.ByIdAsync(uow, projectId, userId, token);
            if (locked is null || await MfaTickets.RereadAsync(uow, projectId, ticketValue!, MfaTicketPurposes.Enroll, lockRow: true, token) is not { } row)
            {
                vanished = true;
                return (Failure.InvalidMfaTicket, false);
            }

            var policy = await policies.ReadAsync(uow.Tx.Connection!, uow.Tx, projectId, token);
            if (!policy.TotpEnabled) return (Failure.FactorNotEnabled, false);
            if (await MfaFactorStore.PendingTotpAsync(uow, userId, token) is not { } ciphertext) return (Failure.TotpNotPending, false);
            if (!await factors.ConfirmTotpAsync(uow, userId, ciphertext, code, token))
            {
                wrong = true;
                await MfaTickets.CountWrongAsync(uow, row, token);
                return (Failure.InvalidMfaCode, true);
            }

            if (locked.Status == UserStatuses.Blocked) return (Failure.UserBlocked, false);
            var (codes, ended) = await TotpConfirmedAsync(uow, projectId, projectName, locked, keep: null, token);
            var (user, grant, reset) = await MfaTickets.FinishEnrollmentAsync(uow, sessions, projectId, row, SessionStrength.ForFactor(MfaFactors.Totp), token);
            return ((user, grant, [.. ended, .. reset], codes), true);
        }, ct);

        if (wrong) failures.Fail();
        // A ticket gone between the two reads counts against the IP like an unknown one at the first read.
        if (vanished) limits.Acquire(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
        if (!outcome.Succeeded) return outcome.Failure!;

        var done = outcome.Value;
        foreach (var id in done.Ended) await checks.EvictAsync(id, ct);
        return new MfaEnrollmentView(await accounts.SignedInAsync(projectId, done.User, done.Grant, ct), done.Codes);
    }

    /// <summary>
    /// What turning MFA on with a confirmed TOTP factor does (AC-13), under the user lock: 10 new recovery codes, every
    /// session of the user but <paramref name="keep"/> ended (<c>mfa_enabled</c>), <c>auth.mfa.enabled</c>,
    /// <c>auth.recovery_codes.created</c>, and the <c>mfa_enabled</c> alert. Answers the codes and the ended sessions.
    /// </summary>
    private async Task<(IReadOnlyList<string> Codes, Guid[] Ended)> TotpConfirmedAsync(
        AuthUnitOfWork uow, string projectId, string projectName, LockedUser user, Guid? keep, CancellationToken ct)
    {
        var userKey = user.Id.ToString();
        var codes = await factors.ReplaceRecoveryCodesAsync(uow, projectId, user.Id, ct);
        var actor = Actor.User(user.Id);
        var ended = await sessions.EndAllAsync(uow, projectId, user.Id, SessionEndReason.MfaEnabled, actor, keep, ct);
        var ids = new Dictionary<string, string> { ["userId"] = userKey };
        await AuthEvents.WriteAsync(uow.Tx, AuthEvents.MfaEnabled, projectId, actor, userKey, ids, fields: Factor(MfaFactors.Totp), ct: ct);
        await AuthEvents.WriteAsync(uow.Tx, AuthEvents.RecoveryCodesCreated, projectId, actor, userKey, ids, ct: ct);
        await alerts.QueueAsync(uow, projectId, projectName, user.Email, SecurityAlertKind.MfaEnabled, ct);
        return (codes, [.. ended]);
    }

    /// <summary>
    /// Turns MFA off (AC-14) after the step up check (AC-18): deletes the TOTP factor, a pending one too, and every
    /// recovery code, under the user lock. Sessions stay.
    /// </summary>
    public async Task<Outcome<Done>> DeleteTotpAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        if (await stepUp.MfaChangeAsync(projectId, userId, sessionId, ct) is { } refused) return refused;
        var projectName = await alerts.ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<Done>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            if (!(await MfaFactorState.ReadAsync(policies, uow.Tx.Connection!, uow.Tx, projectId, userId, token)).MfaEnabled) return Failure.MfaNotEnabled;

            await MfaFactorStore.DeleteFactorsAsync(uow, userId, token);
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.MfaDisabled, projectId, Actor.User(userId), userId.ToString(),
                new Dictionary<string, string> { ["userId"] = userId.ToString() }, fields: Reason(AuthEvents.MfaDisabledByUser), ct: token);
            await alerts.QueueAsync(uow, projectId, projectName, user.Email, SecurityAlertKind.MfaDisabled, token);
            return default(Done);
        }, ct);
    }

    /// <summary>
    /// Replaces the user's recovery codes with 10 new ones (AC-15), after the step up check (AC-18), under the user
    /// lock. Answers the codes in display form, shown this once.
    /// </summary>
    public async Task<Outcome<string[]>> CreateRecoveryCodesAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        if ((await stepUp.MfaChangeAsync(projectId, userId, sessionId, ct) ?? stepUp.TakeEnrollLimit(userId)) is { } refused) return refused;
        var projectName = await alerts.ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<string[]>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            if (!(await MfaFactorState.ReadAsync(policies, uow.Tx.Connection!, uow.Tx, projectId, userId, token)).MfaEnabled) return Failure.MfaNotEnabled;

            var codes = await factors.ReplaceRecoveryCodesAsync(uow, projectId, userId, token);
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.RecoveryCodesCreated, projectId, Actor.User(userId), userId.ToString(),
                new Dictionary<string, string> { ["userId"] = userId.ToString() }, ct: token);
            await alerts.QueueAsync(uow, projectId, projectName, user.Email, SecurityAlertKind.RecoveryCodesCreated, token);
            return codes.ToArray();
        }, ct);
    }

    /// <summary>
    /// Step up (AC-19) with a TOTP code or a recovery code: the user's limit, the body, that the factor is usable now
    /// (both need MFA on), then the factor. A right one raises the caller's session to level 2, adds the factor to
    /// <c>amr</c>, sets <c>strong_auth_at</c>, and answers a new access token, never the refresh token.
    /// </summary>
    public async Task<Outcome<RaisedSessionView>> VerifyAsync(
        string projectId, Guid userId, Guid sessionId, FactorAnswer answer, string ipKey, CancellationToken ct)
    {
        var userKey = userId.ToString();
        if (answer.Malformed() is { } malformed) return malformed;
        var factor = answer.Factor!;
        using var failures = MfaTickets.ReserveFactor(limits, userId, ipKey, factor);
        if (!failures.Allowed) return Failure.RateLimited(failures.Decision.RetryAfter);
        var projectName = await alerts.ProjectNameAsync(projectId, ct);

        // A passkey's challenge is spent and its answer verified before the transaction (AC-19): only a step_up
        // challenge of this user, and only a passkey of this user, can raise this session.
        AssertionCheck? check = null;
        if (factor == MfaFactors.Passkey)
        {
            var policy = await passkeys.ReadPolicyAsync(projectId, ct);
            var challenge = await passkeys.ConsumeAsync(projectId, Guid.Parse(answer.Passkey!.ChallengeId!), ChallengePurposes.StepUp, userId, null, ct);
            check = challenge is null || !policy.PasskeysEnabled
                ? AssertionCheck.Failed
                : await passkeys.VerifyAsync(policy, projectId, challenge, answer.Passkey.Credential!, ct);
        }

        var wrong = false;
        var outcome = await store.WriteAsync<(SessionStrength Strength, DateTimeOffset EndsAt, bool EmailVerified)>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            var state = await MfaFactorState.ReadAsync(policies, uow.Tx.Connection!, uow.Tx, projectId, userId, token);
            if (!Usable(state, factor)) return Failure.FactorNotEnabled;

            if (!await UseFactorAsync(uow, userId, answer, check, state.Policy, token))
            {
                wrong = true;
                return factor == MfaFactors.Passkey ? Failure.InvalidPasskey : Failure.InvalidMfaCode;
            }

            if (factor == MfaFactors.RecoveryCode)
            {
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.RecoveryCodeUsed, projectId, Actor.User(userId), userKey,
                    new Dictionary<string, string> { ["userId"] = userKey }, ct: token);
                await alerts.QueueAsync(uow, projectId, projectName, user.Email, SecurityAlertKind.RecoveryCodeUsed, token);
            }

            if (await StrengthenAsync(uow, projectId, userId, sessionId, Amr(factor, check), aal2: true, token) is not { } raised)
                return Failure.SessionNotFound;
            return (raised.Strength, raised.EndsAt, user.EmailVerifiedAt is not null);
        }, ct);

        if (wrong) failures.Fail();
        if (!outcome.Succeeded) return outcome.Failure!;
        var done = outcome.Value;
        var access = await tokens.IssueAsync(projectId, userId, sessionId, done.EmailVerified, done.Strength, ct);
        return new RaisedSessionView(access.Token, access.ExpiresAt, done.EndsAt, sessionId);
    }

    /// <summary>
    /// The second step of sign in (AC-8), in the order of the spec's <i>Step two decision</i>: the IP limit, the body,
    /// the ticket (read without a lock), the user's limit, then under the user lock and the ticket's row lock, the
    /// factor. A wrong factor commits its attempt with the refusal (the second permitted use of
    /// <see cref="AuthStore.WriteDecidingAsync"/>); the fifth deletes the ticket. A right one deletes the ticket, applies
    /// a recovery's pending password, and creates the session at level 2 in one transaction. <paramref name="ticketEnded"/>
    /// runs when a refusal also ended the ticket (the fifth wrong factor), so the console can clear its ticket cookie
    /// (AC-41).
    /// </summary>
    public async Task<Outcome<SignedIn>> CompleteAsync(
        string projectId, string? ticketValue, FactorAnswer answer, string ipKey, CancellationToken ct, Action? ticketEnded = null)
    {
        var ipLimit = limits.Check(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
        if (!ipLimit.Allowed) return Failure.RateLimited(ipLimit.RetryAfter);

        if (answer.Malformed() is { } malformed) return malformed;
        var factor = answer.Factor!;
        if (!MfaTicket.TryParse(ticketValue, out var ticket)) return Failure.Invalid("The ticket is not an MFA ticket.");

        var found = await store.ReadAsync((db, token) =>
            MfaFactorStore.ReadTicketAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, ticket.Hash, lockRow: false, token), ct);
        if (found is null)
        {
            limits.Acquire(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
            return Failure.InvalidMfaTicket;
        }

        var userKey = found.UserId.ToString();
        using var failures = MfaTickets.ReserveFactor(limits, found.UserId, ipKey, factor);
        if (!failures.Allowed) return Failure.RateLimited(failures.Decision.RetryAfter);
        await keys.GetActiveAsync(projectId, ct);
        var projectName = await alerts.ProjectNameAsync(projectId, ct);

        // A passkey's mfa challenge is spent and its answer verified before the transaction (AC-11): only a challenge
        // made for this ticket counts, and the passkey must be the ticket's user's, checked below.
        AssertionCheck? check = null;
        if (factor == MfaFactors.Passkey)
        {
            var policy = await passkeys.ReadPolicyAsync(projectId, ct);
            var challenge = await passkeys.ConsumeAsync(projectId, Guid.Parse(answer.Passkey!.ChallengeId!), ChallengePurposes.Mfa, null, found.Id, ct);
            check = challenge is null || challenge.UserId != found.UserId || !policy.PasskeysEnabled
                ? AssertionCheck.Failed
                : await passkeys.VerifyAsync(policy, projectId, challenge, answer.Passkey.Credential!, ct);
        }

        var wrong = false;
        var ticketGone = false;
        var vanished = false;
        var outcome = await store.WriteDecidingAsync<(Data.UserRow User, SessionGrant Grant, Guid[] Ended)>(async (uow, token) =>
        {
            var locked = await UserLocks.ByIdAsync(uow, projectId, found.UserId, token);
            if (locked is null || await MfaFactorStore.ReadTicketAsync(uow.Tx.Connection!, uow.Tx, projectId, ticket.Hash, lockRow: true, token) is not { } row)
            {
                vanished = true;
                return (Failure.InvalidMfaTicket, false);
            }

            var state = await MfaFactorState.ReadAsync(policies, uow.Tx.Connection!, uow.Tx, projectId, row.UserId, token);
            if (!state.Factors.Contains(factor)) return (Failure.FactorNotEnabled, false);

            if (row.Id != found.Id || !await UseFactorAsync(uow, row.UserId, answer, check, state.Policy, token))
            {
                wrong = true;
                ticketGone = await MfaTickets.CountWrongAsync(uow, row, token);
                return (factor == MfaFactors.Passkey ? Failure.InvalidPasskey : Failure.InvalidMfaCode, true);
            }

            await MfaTickets.DeleteAsync(uow, row.Id, token);
            if (locked.Status == UserStatuses.Blocked) return (Failure.UserBlocked, false);

            var actor = Actor.User(row.UserId);
            Guid[] ended = [];
            if (row.PendingPasswordHash is { } pending)
            {
                // A recovery that waited for this step (AC-6): what spec 0010, AC-10 does at once for a user without MFA.
                ended = await RecoveryService.ResetPasswordAsync(uow, sessions, projectId, row.UserId, pending, actor, token);
            }

            if (factor == MfaFactors.RecoveryCode)
            {
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.RecoveryCodeUsed, projectId, actor, userKey,
                    new Dictionary<string, string> { ["userId"] = userKey }, ct: token);
                await alerts.QueueAsync(uow, projectId, projectName, locked.Email, SecurityAlertKind.RecoveryCodeUsed, token);
            }

            var strength = SessionStrength.StepOne(row.Method).With(Amr(factor, check), aal2: true);
            var grant = await sessions.CreateAsync(uow, projectId, row.UserId, row.Client, actor, row.Method, token, row.Provider, strength);
            var user = await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == row.UserId, token);
            return ((user, grant, ended), true);
        }, ct);

        if (wrong) failures.Fail();
        // A ticket gone between the two reads counts against the IP like an unknown one at the first read.
        if (vanished) limits.Acquire(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
        if (!outcome.Succeeded)
        {
            if (ticketGone) ticketEnded?.Invoke();
            return outcome.Failure!;
        }

        foreach (var id in outcome.Value.Ended) await checks.EvictAsync(id, ct);
        return await accounts.SignedInAsync(projectId, outcome.Value.User, outcome.Value.Grant, ct);
    }

    /// <summary>
    /// Raises the caller's session after a verified factor: adds its <c>amr</c> values, level 2 when asked, and
    /// <c>strong_auth_at = now()</c>. Answers the new strength and when the session ends; null when the session is not
    /// the user's open session anymore.
    /// </summary>
    private static async Task<(SessionStrength Strength, DateTimeOffset EndsAt)?> StrengthenAsync(
        AuthUnitOfWork uow, string projectId, Guid userId, Guid sessionId, IEnumerable<string> added, bool aal2, CancellationToken ct)
    {
        SessionStrength current;
        await using (var read = new NpgsqlCommand(
            """
            SELECT aal, amr FROM orvano.auth_sessions
            WHERE id = @id AND user_id = @user AND project_id = @project AND ended_at IS NULL
            FOR UPDATE
            """, uow.Tx.Connection, uow.Tx))
        {
            read.Parameters.AddWithValue("id", sessionId);
            read.Parameters.AddWithValue("user", userId);
            read.Parameters.AddWithValue("project", projectId);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            current = new SessionStrength(reader.GetInt16(0), reader.GetFieldValue<string[]>(1));
        }

        var next = current.With(added, aal2);
        await using var update = new NpgsqlCommand(
            """
            UPDATE orvano.auth_sessions SET aal = @aal, amr = @amr, strong_auth_at = now()
            WHERE id = @id
            RETURNING least(idle_expires_at, expires_at)
            """, uow.Tx.Connection, uow.Tx);
        update.Parameters.AddWithValue("aal", NpgsqlDbType.Smallint, next.Aal);
        update.Parameters.AddWithValue("amr", NpgsqlDbType.Array | NpgsqlDbType.Text, next.Amr.ToArray());
        update.Parameters.AddWithValue("id", sessionId);
        var endsAt = (DateTime)(await update.ExecuteScalarAsync(ct))!;
        return (next, new DateTimeOffset(endsAt, TimeSpan.Zero));
    }

    /// <summary>
    /// Whether a step up may use <paramref name="factor"/> now (AC-19): codes need MFA on; a passkey needs passkeys on and
    /// an active passkey of the user.
    /// </summary>
    private static bool Usable(MfaFactorState state, string factor) =>
        factor == MfaFactors.Passkey ? state.Policy.PasskeysEnabled && state.ActivePasskeys > 0 : state.Factors.Contains(factor);

    /// <summary>
    /// Uses the answered factor under the user lock: a TOTP step or a recovery code once, or for a passkey, records the
    /// verified assertion when the passkey is the user's and still active. False for a wrong answer.
    /// </summary>
    private async Task<bool> UseFactorAsync(AuthUnitOfWork uow, Guid userId, FactorAnswer answer, AssertionCheck? check, MethodPolicy policy, CancellationToken ct) =>
        answer.Factor switch
        {
            MfaFactors.Totp => await factors.UseTotpAsync(uow, userId, answer.TotpCode, ct),
            MfaFactors.RecoveryCode => await factors.UseRecoveryCodeAsync(uow, userId, answer.RecoveryCode, ct),
            _ => check is { Valid: true, Passkey: { } passkey } && passkey.UserId == userId && policy.RpId is { } rpId
                && await PasskeyRows.UseAsync(uow, passkey.Id, rpId, check.SignCount, check.BackedUp, ct),
        };

    /// <summary>The <c>amr</c> values a verified factor adds (AC-25): a passkey's from its backup state after this assertion.</summary>
    private static string[] Amr(string factor, AssertionCheck? check) =>
        factor == MfaFactors.Passkey ? SessionStrength.ForPasskey(check!.BackedUp) : SessionStrength.ForFactor(factor);

    private static Dictionary<string, string?> Factor(string factor) => new() { ["factor"] = factor };

    private static Dictionary<string, string?> Reason(string reason) => new() { ["reason"] = reason };
}
