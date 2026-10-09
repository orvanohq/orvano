using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// Password reset (spec 0010, AC-7 to AC-10): an open request that answers the same whether or not the account
/// exists, and the redemption that sets a new password, signs the user in, and ends every other session.
/// </summary>
internal sealed class RecoveryService(
    AuthStore store,
    AuthMailer mailer,
    PasswordHasher hasher,
    Sessions sessions,
    SessionChecks checks,
    SigningKeys keys,
    AccountService accounts,
    MethodPolicies policies,
    PasswordRules passwordRules,
    ILogger<RecoveryService> logger)
{
    /// <summary>
    /// <c>account.createRecovery</c> (AC-7 to AC-9). Every refusal is decided from the input and the project alone,
    /// before the account is read; after that the answer is always success, whether a link was sent or not.
    /// </summary>
    public async Task<Outcome<Done>> RequestAsync(string projectId, string? email, string? redirectUrl, string ipKey, CancellationToken ct)
    {
        if (!EmailRule.TryNormalize(email, out var trimmed)) return Failure.Invalid("The email must be an address of at most 320 characters.");
        if (await mailer.CheckRedirectAsync(projectId, redirectUrl, EmailTokenKind.Recovery, ct) is not { } redirect) return Failure.RedirectUrlNotAllowed;
        var refused = await mailer.TakeIpLimitAsync(projectId, ipKey, ct)
            ?? mailer.TakeRecipientLimits(projectId, trimmed, EmailTokenKind.Recovery, ipKey)
            ?? await mailer.CheckAvailabilityAsync(projectId, ct);
        if (refused is not null) return refused;

        var projectName = await mailer.ProjectNameAsync(projectId, ct);
        var outcome = await store.WriteAsync<Done>(async (uow, token) =>
        {
            // AC-8: only an active user gets a link; a blocked or unknown email gets nothing, and the same answer.
            if (await UserLocks.ByEmailAsync(uow, projectId, trimmed, token) is not { Status: UserStatuses.Active, Email: { } to } user) return default(Done);

            var target = new AuthEmailTarget(projectId, projectName, user.Id, to, user.Name);
            if (await mailer.SendLinkAsync(uow, target, EmailTokenKind.Recovery, redirect, Actor.User(user.Id), token) is { } notQueued) return notQueued;
            return default(Done);
        }, ct);

        // AC-9: the settings changed after the availability check; the transaction rolled back, the answer stays the same.
        if (!outcome.Succeeded)
            logger.LogWarning("A {Kind} email of project {ProjectId} was not queued: {Code}", EmailTokenKinds.Recovery, projectId, outcome.Failure!.Code);
        return default(Done);
    }

    /// <summary>
    /// <c>users.createRecovery</c> and its console twin (AC-21): a reset link for a known user, so no privacy padding;
    /// 403 <c>user_blocked</c> for a blocked user, then the two recipient limits and the queue's answer.
    /// </summary>
    public async Task<Outcome<Done>> SendForUserAsync(string projectId, Guid userId, string? redirectUrl, Actor actor, string limitKey, CancellationToken ct)
    {
        if (await mailer.CheckRedirectAsync(projectId, redirectUrl, EmailTokenKind.Recovery, ct) is not { } redirect) return Failure.RedirectUrlNotAllowed;
        var projectName = await mailer.ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<Done>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            if (user.Status != UserStatuses.Active) return Failure.UserBlocked;
            if (user.Email is not { } to) return Failure.Invalid("The user has no email.");
            if (mailer.TakeRecipientLimits(projectId, to, EmailTokenKind.Recovery, limitKey) is { } limited) return limited;

            var target = new AuthEmailTarget(projectId, projectName, user.Id, to, user.Name);
            if (await mailer.SendLinkAsync(uow, target, EmailTokenKind.Recovery, redirect, actor, token) is { } refused) return refused;
            return default(Done);
        }, ct);
    }

    /// <summary>
    /// <c>account.completeRecovery</c> (AC-10): checks the password, then the token without a lock (so junk tokens
    /// never take a hashing slot), hashes outside any transaction, and in one transaction consumes the token, sets the
    /// password, verifies the email, ends every session, and signs the user in. A user with MFA on gets a challenge
    /// instead, and the reset waits for step two (spec 0013, AC-6).
    /// </summary>
    public async Task<Outcome<SignedIn>> CompleteAsync(string projectId, string? tokenValue, string? password, ClientInfo client, CancellationToken ct)
    {
        var rules = await passwordRules.CheckNewAsync(projectId, password, ct);
        if (!rules.Succeeded) return rules.Failure!;
        var normalized = rules.Value!;
        if (!LinkToken.TryParse(tokenValue, out var link)) return Failure.InvalidEmailToken;
        var live = await store.ReadAsync((db, token) =>
            EmailTokens.IsLiveAsync((NpgsqlConnection)db.Database.GetDbConnection(), projectId, EmailTokenKind.Recovery, link, token), ct);
        if (!live) return Failure.InvalidEmailToken;

        var hash = await hasher.TryHashAsync(normalized, ct);
        if (hash is null) return Failure.Busy;
        await keys.GetActiveAsync(projectId, ct);

        var outcome = await store.WriteAsync<(Guid UserId, SessionGrant? Grant, Guid[] Ended, MfaChallengeView? Mfa)>(async (uow, token) =>
        {
            if (await EmailTokens.ConsumeLinkAsync(uow, projectId, EmailTokenKind.Recovery, link, token) is not { Expired: false, UserId: { } userId } consumed)
                return Failure.InvalidEmailToken;
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user || !EmailRule.SameAddress(user.Email, consumed.Email))
                return Failure.InvalidEmailToken;
            if (user.Status != UserStatuses.Active) return Failure.UserBlocked;

            // The inbox owner just proved the email, so an unverified account's linked identities go (spec 0012, AC-12):
            // a link planted by whoever pre registered the email must not outlive this. The new password replaces the old.
            Guid[] claimed = user.EmailVerifiedAt is null
                ? (await AccountClaims.ClaimAsync(uow, sessions, projectId, user, Actor.User(userId), endSessions: false, token, removePassword: false)).EndedSessions
                : [];

            // Spec 0013, AC-6: for a user with MFA on, the new password waits on the ticket, and the reset happens at step
            // two, so the inbox alone changes nothing. The link stays used.
            var gate = await MfaGate.ChallengeAsync(policies, uow, projectId, userId, SessionMethod.Recovery, null, client, token, pendingPasswordHash: hash);
            if (!gate.Succeeded) return gate.Failure!;
            if (gate.Value is { } challenge) return (userId, null, claimed, challenge);

            var actor = Actor.User(userId);
            var ended = await ResetPasswordAsync(uow, sessions, projectId, userId, hash, actor, token);
            if (user.EmailVerifiedAt is null)
            {
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpdated, projectId, actor, userId.ToString(),
                    new Dictionary<string, string> { ["userId"] = userId.ToString() }, ["emailVerified"], ct: token);
            }

            var grant = await sessions.CreateAsync(uow, projectId, userId, client, actor, SessionMethod.Recovery, token);
            return (userId, grant, [.. ended, .. claimed], null);
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        foreach (var id in outcome.Value.Ended) await checks.EvictAsync(id, ct);
        if (outcome.Value.Mfa is { } mfa) return SignedIn.Challenged(mfa);
        var row = await store.ReadAsync((db, token) => db.Users.AsNoTracking().SingleAsync(u => u.Id == outcome.Value.UserId, token), ct);
        return await accounts.SignedInAsync(projectId, row, outcome.Value.Grant!, ct);
    }

    /// <summary>
    /// The reset itself (spec 0010, AC-10), in the caller's transaction under the user lock: sets the password hash,
    /// verifies the email, ends every session of the user (<c>password_reset</c>), deletes their live reset links, and
    /// writes <c>auth.password.reset</c>. Runs at once for a user without MFA, and at step two for one with it (spec
    /// 0013, AC-8). Answers the ended session IDs to evict after the commit.
    /// </summary>
    public static async Task<Guid[]> ResetPasswordAsync(
        AuthUnitOfWork uow, Sessions sessions, string projectId, Guid userId, string hash, Actor actor, CancellationToken ct)
    {
        await using (var set = new NpgsqlCommand(
            """
            INSERT INTO orvano.auth_passwords (user_id, project_id, hash) VALUES (@user, @project, @hash)
            ON CONFLICT (user_id) DO UPDATE SET hash = excluded.hash, updated_at = now();
            UPDATE orvano.auth_users SET email_verified_at = coalesce(email_verified_at, now()), updated_at = now() WHERE id = @user;
            """, uow.Tx.Connection, uow.Tx))
        {
            set.Parameters.AddWithValue("user", userId);
            set.Parameters.AddWithValue("project", projectId);
            set.Parameters.AddWithValue("hash", hash);
            await set.ExecuteNonQueryAsync(ct);
        }

        var ended = await sessions.EndAllAsync(uow, projectId, userId, SessionEndReason.PasswordReset, actor, keep: null, ct);
        await EmailTokens.DeleteForUserAsync(uow, projectId, userId, EmailTokenKind.Recovery, ct);
        await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasswordReset, projectId, actor, userId.ToString(),
            new Dictionary<string, string> { ["userId"] = userId.ToString() }, ct: ct);
        return [.. ended];
    }
}
