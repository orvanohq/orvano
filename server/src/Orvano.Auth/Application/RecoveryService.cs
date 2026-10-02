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
    EmailTokens tokens,
    PasswordHasher hasher,
    Sessions sessions,
    SessionChecks checks,
    SigningKeys keys,
    AccountService accounts,
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
        var refused = mailer.TakeIpLimit(ipKey)
            ?? mailer.TakeRecipientLimits(projectId, trimmed, EmailTokenKind.Recovery)
            ?? await mailer.CheckAvailabilityAsync(projectId, ct);
        if (refused is not null) return refused;

        var projectName = await mailer.ProjectNameAsync(projectId, ct);
        var outcome = await store.WriteAsync<Done>(async (uow, token) =>
        {
            // AC-8: only an active user gets a link; a blocked or unknown email gets nothing, and the same answer.
            if (await UserLocks.ByEmailAsync(uow, projectId, trimmed, token) is not { Status: UserStatuses.Active, Email: { } to } user) return default(Done);

            var link = await tokens.CreateLinkAsync(uow, projectId, EmailTokenKind.Recovery, user.Id, to, token);
            var url = LinkUrl.Build(redirect.Url, EmailTokenKind.Recovery, link);
            if (await mailer.QueueLinkAsync(uow.Tx, projectId, projectName, EmailTokenKind.Recovery, to, user.Name, url, token) is { } notQueued) return notQueued;
            await EmailEvents.TokenCreatedAsync(uow, projectId, EmailTokenKind.Recovery, user.Id, Actor.User(user.Id), token);
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
    public async Task<Outcome<Done>> SendForUserAsync(string projectId, Guid userId, string? redirectUrl, Actor actor, CancellationToken ct)
    {
        if (await mailer.CheckRedirectAsync(projectId, redirectUrl, EmailTokenKind.Recovery, ct) is not { } redirect) return Failure.RedirectUrlNotAllowed;
        var projectName = await mailer.ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<Done>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            if (user.Status != UserStatuses.Active) return Failure.UserBlocked;
            if (user.Email is not { } to) return Failure.Invalid("The user has no email.");
            if (mailer.TakeRecipientLimits(projectId, to, EmailTokenKind.Recovery) is { } limited) return limited;

            var link = await tokens.CreateLinkAsync(uow, projectId, EmailTokenKind.Recovery, user.Id, to, token);
            var url = LinkUrl.Build(redirect.Url, EmailTokenKind.Recovery, link);
            if (await mailer.QueueLinkAsync(uow.Tx, projectId, projectName, EmailTokenKind.Recovery, to, user.Name, url, token) is { } refused) return refused;
            await EmailEvents.TokenCreatedAsync(uow, projectId, EmailTokenKind.Recovery, user.Id, actor, token);
            return default(Done);
        }, ct);
    }

    /// <summary>
    /// <c>account.completeRecovery</c> (AC-10): checks the password, then the token without a lock (so junk tokens
    /// never take a hashing slot), hashes outside any transaction, and in one transaction consumes the token, sets the
    /// password, verifies the email, ends every session, and signs the user in.
    /// </summary>
    public async Task<Outcome<SignedIn>> CompleteAsync(string projectId, string? tokenValue, string? password, ClientInfo client, CancellationToken ct)
    {
        if (!PasswordPolicy.TryNormalize(password, out var normalized)) return Failure.InvalidPassword;
        if (!LinkToken.TryParse(tokenValue, out var link)) return Failure.InvalidEmailToken;
        var live = await store.ReadAsync((db, token) =>
            EmailTokens.IsLiveAsync((NpgsqlConnection)db.Database.GetDbConnection(), projectId, EmailTokenKind.Recovery, link, token), ct);
        if (!live) return Failure.InvalidEmailToken;

        var hash = await hasher.TryHashAsync(normalized, ct);
        if (hash is null) return Failure.Busy;
        await keys.GetActiveAsync(projectId, ct);

        var outcome = await store.WriteAsync<(Guid UserId, SessionGrant Grant, Guid[] Ended)>(async (uow, token) =>
        {
            if (await EmailTokens.ConsumeLinkAsync(uow, projectId, EmailTokenKind.Recovery, link, token) is not { Expired: false, UserId: { } userId } consumed)
                return Failure.InvalidEmailToken;
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user || !SameEmail(user.Email, consumed.Email))
                return Failure.InvalidEmailToken;
            if (user.Status != UserStatuses.Active) return Failure.UserBlocked;

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
                await set.ExecuteNonQueryAsync(token);
            }

            var actor = Actor.User(userId);
            var ended = await sessions.EndAllAsync(uow, projectId, userId, SessionEndReason.PasswordReset, actor, keep: null, token);
            await EmailTokens.DeleteForUserAsync(uow, projectId, userId, EmailTokenKind.Recovery, token);
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasswordReset, projectId, actor, userId.ToString(),
                new Dictionary<string, string> { ["userId"] = userId.ToString() }, ct: token);
            if (user.EmailVerifiedAt is null)
            {
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpdated, projectId, actor, userId.ToString(),
                    new Dictionary<string, string> { ["userId"] = userId.ToString() }, ["emailVerified"], ct: token);
            }

            var grant = await sessions.CreateAsync(uow, projectId, userId, client, actor, SessionMethod.Recovery, token);
            return (userId, grant, ended.ToArray());
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        foreach (var id in outcome.Value.Ended) await checks.EvictAsync(id, ct);
        var row = await store.ReadAsync((db, token) => db.Users.AsNoTracking().SingleAsync(u => u.Id == outcome.Value.UserId, token), ct);
        return await accounts.SignedInAsync(projectId, row, outcome.Value.Grant, ct);
    }

    internal static bool SameEmail(string? userEmail, string tokenEmail) =>
        userEmail is not null && string.Equals(userEmail.ToLowerInvariant(), tokenEmail.ToLowerInvariant(), StringComparison.Ordinal);
}

/// <summary>The events every email flow shares (AC-30).</summary>
internal static class EmailEvents
{
    /// <summary><c>auth.email_token.created</c>: the kind, the user (null for an unknown email), and the actor; never the email.</summary>
    public static Task TokenCreatedAsync(AuthUnitOfWork uow, string projectId, EmailTokenKind kind, Guid? userId, Actor actor, CancellationToken ct) =>
        AuthEvents.WriteAsync(uow.Tx, AuthEvents.EmailTokenCreated, projectId, actor, userId?.ToString() ?? projectId,
            new Dictionary<string, string>(),
            fields: new Dictionary<string, string?> { ["kind"] = EmailTokenKinds.Wire(kind), ["userId"] = userId?.ToString() }, ct: ct);
}
