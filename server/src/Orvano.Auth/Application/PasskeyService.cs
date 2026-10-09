using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;
using Orvano.Messaging.Contracts;
using Orvano.Platform.Contracts;

namespace Orvano.Auth.Application;

/// <summary>
/// Passkeys for app users and console accounts (spec 0013, AC-20 to AC-24): registration, sign in with no email typed,
/// the challenges for step two and step up, and list, rename, and delete. A ceremony first takes its challenge out
/// with one conditional delete in its own transaction, so a challenge works once whatever the outcome, then verifies
/// outside any lock, then writes under the user lock.
/// </summary>
internal sealed class PasskeyService(
    AuthStore store,
    MethodPolicies policies,
    WebAuthnVerifier verifier,
    StepUp stepUp,
    Sessions sessions,
    SigningKeys keys,
    AccountService accounts,
    IProjectDirectory projects,
    RateLimits limits,
    SecurityAlerts alerts)
{
    /// <summary>
    /// Starts a passkey sign in (AC-23): a <c>sign_in</c> challenge with no user and an empty <c>allowCredentials</c>,
    /// so the browser or the platform offers every passkey of the RP ID, autofill included.
    /// </summary>
    public async Task<Outcome<PasskeyChallengeView>> CreateSignInChallengeAsync(string projectId, CancellationToken ct)
    {
        var policy = await ReadPolicyAsync(projectId, ct);
        if (!policy.PasskeysEnabled || policy.RpId is not { } rpId) return Failure.FactorNotEnabled;

        return await store.WriteAsync<PasskeyChallengeView>(async (uow, token) =>
        {
            var (id, challenge) = await PasskeyRows.CreateChallengeAsync(uow, projectId, ChallengePurposes.SignIn, null, null, token);
            return new PasskeyChallengeView(id, new RequestOptionsView(Base64Codec.Encode(challenge), rpId, []));
        }, ct);
    }

    /// <summary>
    /// Signs in with a passkey (AC-24): takes the <c>sign_in</c> challenge out, verifies the assertion (AC-22), and
    /// creates a session at level 2 with the passkey's <c>amr</c>. Every failure before the assertion is valid is the
    /// same 401 <c>invalid_passkey</c>, counted per IP, so it never says whether a credential exists. Never challenged.
    /// </summary>
    public async Task<Outcome<SignedIn>> SignInAsync(
        string projectId, string? challengeId, AssertionInput? credential, ClientInfo client, string ipKey, CancellationToken ct)
    {
        var failures = limits.Check(RateLimitPolicies.FailedPasskeyPerIp, ipKey);
        if (!failures.Allowed) return Failure.RateLimited(failures.RetryAfter);
        var policy = await ReadPolicyAsync(projectId, ct);
        if (!policy.PasskeysEnabled || policy.RpId is not { } rpId) return Failure.FactorNotEnabled;

        Outcome<SignedIn> Refuse()
        {
            limits.Acquire(RateLimitPolicies.FailedPasskeyPerIp, ipKey);
            return Failure.InvalidPasskey;
        }

        if (!Guid.TryParse(challengeId, out var id) || credential is null) return Refuse();
        if (await ConsumeAsync(projectId, id, ChallengePurposes.SignIn, null, null, ct) is not { } challenge) return Refuse();
        var check = await VerifyAsync(policy, projectId, challenge, credential, ct);
        if (!check.Valid) return Refuse();
        var passkey = check.Passkey!;
        await keys.GetActiveAsync(projectId, ct);

        var outcome = await store.WriteAsync<(Data.UserRow User, SessionGrant Grant)>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, passkey.UserId, token) is not { } user) return Failure.InvalidPasskey;
            if (!await PasskeyRows.UseAsync(uow, passkey.Id, rpId, check.SignCount, check.BackedUp, token)) return Failure.InvalidPasskey;
            if (user.Status == UserStatuses.Blocked) return Failure.UserBlocked;

            var strength = SessionStrength.PasskeySignIn(check.BackedUp);
            var grant = await sessions.CreateAsync(uow, projectId, user.Id, client, Actor.User(user.Id), SessionMethod.Passkey, token, strength: strength);
            var row = await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, token);
            return (row, grant);
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure == Failure.InvalidPasskey ? Refuse() : outcome.Failure!;
        return await accounts.SignedInAsync(projectId, outcome.Value.User, outcome.Value.Grant, ct);
    }

    /// <summary>
    /// Starts adding a passkey (AC-20) after the enrollment check (AC-17): passkeys on, a verified email when the user
    /// has one, and fewer than 10 passkeys, under the user lock. Answers the creation options with a 5 minute
    /// <c>register</c> challenge.
    /// </summary>
    public async Task<Outcome<RegistrationView>> CreateRegistrationAsync(string projectId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        if (await stepUp.EnrollmentAsync(projectId, userId, sessionId, ct) is { } refused) return refused;
        var rpName = await RpNameAsync(projectId, await ReadPolicyAsync(projectId, ct), ct);

        return await store.WriteAsync<RegistrationView>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            var policy = await policies.ReadAsync(uow.Tx.Connection!, uow.Tx, projectId, token);
            if (!policy.PasskeysEnabled || policy.RpId is not { } rpId) return Failure.FactorNotEnabled;
            if (VerifiedEmailRule.Blocks(projectId, user.Email, user.EmailVerifiedAt)) return Failure.EmailNotVerified;
            if (await PasskeyRows.CountAsync(uow, userId, token) >= PasskeyRules.MaxPerUser) return Failure.PasskeyLimit;

            var exclude = await PasskeyRows.RefsAsync(uow.Tx.Connection!, uow.Tx, userId, rpId, token);
            var (id, challenge) = await PasskeyRows.CreateChallengeAsync(uow, projectId, ChallengePurposes.Register, userId, null, token);
            var options = new CreationOptionsView(
                rpId,
                policy.RpName ?? rpName,
                Base64Codec.Encode(PasskeyRules.UserHandle(userId)),
                user.Email ?? userId.ToString(),
                user.Name ?? user.Email ?? "User",
                Base64Codec.Encode(challenge),
                exclude);
            return new RegistrationView(id, options);
        }, ct);
    }

    /// <summary>
    /// Finishes adding a passkey (AC-21): takes the user's <c>register</c> challenge out (else 400
    /// <c>invalid_passkey_challenge</c>), verifies the registration, and stores the passkey under the user lock with the
    /// RP ID it was made for. A credential ID the project already has is 409 <c>passkey_already_registered</c>.
    /// </summary>
    public async Task<Outcome<PasskeyView>> CompleteRegistrationAsync(
        string projectId, Guid userId, Guid sessionId, string? challengeId, AttestationInput? credential, string? name, CancellationToken ct)
    {
        if (await stepUp.EnrollmentAsync(projectId, userId, sessionId, ct) is { } refused) return refused;
        string? given = null;
        if (name is not null)
        {
            if (!PasskeyRules.TryName(name, out var trimmed)) return Failure.Invalid("name must be 1 to 64 characters.");
            given = trimmed;
        }

        if (credential is null) return Failure.Invalid("Send the credential the browser or the platform made.");
        var policy = await ReadPolicyAsync(projectId, ct);
        if (!policy.PasskeysEnabled || policy.RpId is not { } rpId) return Failure.FactorNotEnabled;
        if (!Guid.TryParse(challengeId, out var id)
            || await ConsumeAsync(projectId, id, ChallengePurposes.Register, userId, null, ct) is not { } challenge)
        {
            return Failure.InvalidPasskeyChallenge;
        }

        if (await verifier.VerifyRegistrationAsync(policy, projectId, challenge, userId, credential, ct) is not { } made) return Failure.InvalidPasskey;
        var projectName = await alerts.ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<PasskeyView>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            var now = await policies.ReadAsync(uow.Tx.Connection!, uow.Tx, projectId, token);
            if (!now.PasskeysEnabled) return Failure.FactorNotEnabled;
            if (VerifiedEmailRule.Blocks(projectId, user.Email, user.EmailVerifiedAt)) return Failure.EmailNotVerified;
            if (await PasskeyRows.CountAsync(uow, userId, token) >= PasskeyRules.MaxPerUser) return Failure.PasskeyLimit;

            if (await PasskeyRows.InsertAsync(uow, projectId, userId, made, PasskeyRules.NameFor(given, made.AaGuid), rpId, token) is not { } passkey)
                return Failure.PasskeyAlreadyRegistered;
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasskeyAdded, projectId, Actor.User(userId), userId.ToString(), Ids(userId, passkey.Id), ct: token);
            await alerts.QueueAsync(uow, projectId, projectName, user.Email, SecurityAlertKind.PasskeyAdded, token);
            return passkey;
        }, ct);
    }

    /// <summary>The user's passkeys, oldest first, including inactive ones (AC-2).</summary>
    public async Task<Outcome<PasskeyView[]>> ListAsync(string projectId, Guid userId, CancellationToken ct)
    {
        var list = await store.ReadAsync(async (db, token) =>
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            if (!await db.Users.AnyAsync(u => u.Id == userId && u.ProjectId == projectId, token)) return null;
            var policy = await policies.ReadAsync(conn, null, projectId, token);
            return (await PasskeyRows.ListAsync(conn, null, userId, policy.RpId, token)).ToArray();
        }, ct);
        return list is null ? Failure.UserNotFound : list;
    }

    /// <summary>Renames one of the user's passkeys (AC-33: <c>auth.passkey.renamed</c>). Works with passkeys off (AC-30).</summary>
    public async Task<Outcome<PasskeyView>> RenameAsync(string projectId, Guid userId, string passkeyId, string? name, CancellationToken ct)
    {
        if (!PasskeyRules.TryName(name, out var trimmed)) return Failure.Invalid("name must be 1 to 64 characters.");
        if (!Guid.TryParse(passkeyId, out var id)) return Failure.PasskeyNotFound;

        return await store.WriteAsync<PasskeyView>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is null) return Failure.UserNotFound;
            var policy = await policies.ReadAsync(uow.Tx.Connection!, uow.Tx, projectId, token);
            if (await PasskeyRows.RenameAsync(uow, userId, id, trimmed, policy.RpId, token) is not { } passkey) return Failure.PasskeyNotFound;
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasskeyRenamed, projectId, Actor.User(userId), userId.ToString(), Ids(userId, id),
                changed: ["name"], ct: token);
            return passkey;
        }, ct);
    }

    /// <summary>
    /// Removes one of the user's own passkeys (AC-18): with MFA on, a strong check within 10 minutes; without it, AC-17's
    /// rule. Works with passkeys off (AC-30).
    /// </summary>
    public async Task<Outcome<Done>> DeleteOwnAsync(string projectId, Guid userId, Guid sessionId, string passkeyId, CancellationToken ct)
    {
        if (await stepUp.EnrollmentAsync(projectId, userId, sessionId, ct) is { } refused) return refused;
        return await DeleteAsync(projectId, userId, passkeyId, Actor.User(userId), AuthEvents.RemovedByUser, ct);
    }

    /// <summary>Removes one passkey of a user under the user lock and writes <c>auth.passkey.removed</c> with the reason.</summary>
    public async Task<Outcome<Done>> DeleteAsync(string projectId, Guid userId, string passkeyId, Actor actor, string reason, CancellationToken ct)
    {
        if (!Guid.TryParse(passkeyId, out var id)) return Failure.PasskeyNotFound;
        var projectName = await alerts.ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<Done>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            if (!await PasskeyRows.DeleteAsync(uow, userId, id, token)) return Failure.PasskeyNotFound;
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasskeyRemoved, projectId, actor, userId.ToString(), Ids(userId, id), reason: reason, ct: token);
            await alerts.QueueAsync(uow, projectId, projectName, user.Email, SecurityAlertKind.PasskeyRemoved, token);
            return default(Done);
        }, ct);
    }

    /// <summary>
    /// The challenge for a passkey at step two (AC-11): the ticket must be live in this project (401
    /// <c>invalid_mfa_ticket</c>) and <c>passkey</c> one of its factors now (409 <c>factor_not_enabled</c>). Lists the
    /// user's active passkeys. Never counts as an attempt.
    /// </summary>
    public async Task<Outcome<PasskeyChallengeView>> CreateMfaChallengeAsync(string projectId, string? ticketValue, string ipKey, CancellationToken ct)
    {
        var ipLimit = limits.Check(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
        if (!ipLimit.Allowed) return Failure.RateLimited(ipLimit.RetryAfter);
        if (!MfaTicket.TryParse(ticketValue, out var ticket)) return Failure.Invalid("The ticket is not an MFA ticket.");

        var found = await store.ReadAsync((db, token) =>
            MfaFactorStore.ReadTicketAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, ticket.Hash, lockRow: false, token), ct);
        if (found is null)
        {
            limits.Acquire(RateLimitPolicies.FailedMfaTicketPerIp, ipKey);
            return Failure.InvalidMfaTicket;
        }

        var perUser = limits.Acquire(RateLimitPolicies.PasskeyChallengePerUser, found.UserId.ToString());
        if (!perUser.Allowed) return Failure.RateLimited(perUser.RetryAfter);

        return await store.WriteAsync<PasskeyChallengeView>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, found.UserId, token) is null
                || await MfaFactorStore.ReadTicketAsync(uow.Tx.Connection!, uow.Tx, projectId, ticket.Hash, lockRow: false, token) is not { } row)
            {
                return Failure.InvalidMfaTicket;
            }

            var state = await MfaFactorState.ReadAsync(policies, uow.Tx.Connection!, uow.Tx, projectId, row.UserId, token);
            if (!state.Factors.Contains(MfaFactors.Passkey)) return Failure.FactorNotEnabled;
            var allow = await PasskeyRows.RefsAsync(uow.Tx.Connection!, uow.Tx, row.UserId, state.Policy.RpId, token);
            var (id, challenge) = await PasskeyRows.CreateChallengeAsync(uow, projectId, ChallengePurposes.Mfa, row.UserId, row.Id, token);
            return new PasskeyChallengeView(id, new RequestOptionsView(Base64Codec.Encode(challenge), state.Policy.RpId!, allow));
        }, ct);
    }

    /// <summary>
    /// The challenge for a passkey step up (AC-19): passkeys on and an active passkey of the caller, else 409
    /// <c>factor_not_enabled</c>. The <c>step_up</c> challenge is bound to the caller's user.
    /// </summary>
    public Task<Outcome<PasskeyChallengeView>> CreateStepUpChallengeAsync(string projectId, Guid userId, CancellationToken ct) =>
        store.WriteAsync<PasskeyChallengeView>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is null) return Failure.UserNotFound;
            var policy = await policies.ReadAsync(uow.Tx.Connection!, uow.Tx, projectId, token);
            if (!policy.PasskeysEnabled || policy.RpId is not { } rpId) return Failure.FactorNotEnabled;
            var allow = await PasskeyRows.RefsAsync(uow.Tx.Connection!, uow.Tx, userId, rpId, token);
            if (allow.Count == 0) return Failure.FactorNotEnabled;
            var (id, challenge) = await PasskeyRows.CreateChallengeAsync(uow, projectId, ChallengePurposes.StepUp, userId, null, token);
            return new PasskeyChallengeView(id, new RequestOptionsView(Base64Codec.Encode(challenge), rpId, allow));
        }, ct);

    /// <summary>
    /// Takes a challenge out in its own transaction (AC-21, AC-22), so it is spent whether or not the answer passes.
    /// Null when there is no live one of this purpose (and user or ticket, when given).
    /// </summary>
    public async Task<ConsumedChallenge?> ConsumeAsync(string projectId, Guid challengeId, string purpose, Guid? userId, Guid? ticketId, CancellationToken ct)
    {
        var outcome = await store.WriteAsync<ConsumedChallenge?>(async (uow, token) =>
            await PasskeyRows.ConsumeChallengeAsync(uow, projectId, challengeId, purpose, userId, ticketId, token), ct);
        return outcome.Value;
    }

    /// <summary>
    /// Verifies an assertion (AC-22) and, when only the counter rule failed, writes
    /// <c>auth.passkey.counter_regressed</c> in its own transaction, beside the refusal the caller gives.
    /// </summary>
    public async Task<AssertionCheck> VerifyAsync(MethodPolicy policy, string projectId, ConsumedChallenge challenge, AssertionInput credential, CancellationToken ct)
    {
        var check = await store.ReadAsync((db, token) =>
            verifier.VerifyAssertionAsync(policy, projectId, challenge, credential, (NpgsqlConnection)db.Database.GetDbConnection(), token), ct);
        if (check.Regressed && check.Passkey is { } passkey)
        {
            await store.WriteAsync<Done>(async (uow, token) =>
            {
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasskeyCounterRegressed, projectId, Actor.System, passkey.UserId.ToString(),
                    Ids(passkey.UserId, passkey.Id), ct: token);
                return default(Done);
            }, ct);
        }

        return check;
    }

    /// <summary>The project's policy, read on its own connection.</summary>
    public Task<MethodPolicy> ReadPolicyAsync(string projectId, CancellationToken ct) =>
        store.ReadAsync((db, token) => policies.ReadAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, token), ct);

    private async Task<string> RpNameAsync(string projectId, MethodPolicy policy, CancellationToken ct) =>
        policy.RpName ?? (await projects.GetAsync(projectId, ct))?.Name ?? throw new InvalidOperationException($"Project {projectId} vanished during passkey registration.");

    private static Dictionary<string, string> Ids(Guid userId, Guid passkeyId) =>
        new() { ["userId"] = userId.ToString(), ["passkeyId"] = passkeyId.ToString() };
}
