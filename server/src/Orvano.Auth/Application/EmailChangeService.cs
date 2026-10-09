using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// Changing the signed in user's email (spec 0010, AC-17, AC-18): a confirmation link goes to the new address, and
/// the email changes only when that link is opened.
/// </summary>
internal sealed class EmailChangeService(AuthStore store, AuthMailer mailer, AccountService accounts, PolicySettings policies)
{
    /// <summary>
    /// <c>account.updateEmail</c> (AC-17). Checks run in this order: the body (the email rule, the redirect, and not
    /// the current email), the credential (the password, or a fresh session for a user without one), the address
    /// being free, the two recipient limits keyed by the new address, then the queue's answer.
    /// </summary>
    public async Task<Outcome<Done>> RequestAsync(
        string projectId, Guid userId, Guid sessionId, string? email, string? redirectUrl, string? password, string limitKey, CancellationToken ct)
    {
        if (!EmailRule.TryNormalize(email, out var newEmail)) return Failure.Invalid("The email must be an address of at most 320 characters.");
        if (await mailer.CheckRedirectAsync(projectId, redirectUrl, EmailTokenKind.EmailChange, ct) is not { } redirect) return Failure.RedirectUrlNotAllowed;
        var current = await store.ReadAsync((db, token) =>
            db.Users.AsNoTracking().Where(u => u.Id == userId && u.ProjectId == projectId).Select(u => u.Email).SingleOrDefaultAsync(token), ct);
        if (EmailRule.SameAddress(current, newEmail)) return Failure.Invalid("The new email is the current one.");
        // Spec 0014, AC-9: the new address passes the project's domain rule, before the credential check.
        if ((await policies.GetAsync(projectId, ct)).CheckDomain(newEmail) is { } domainRefused) return domainRefused;

        var credential = await accounts.CheckCredentialAsync(projectId, userId, sessionId, password, ct);
        if (credential.Failure is not null) return credential.Failure;
        var projectName = await mailer.ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<Done>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            if (await EmailTakenAsync(uow, projectId, newEmail, userId, token)) return Failure.EmailAlreadyInUse;
            if (mailer.TakeRecipientLimits(projectId, newEmail, EmailTokenKind.EmailChange, limitKey) is { } limited) return limited;

            var target = new AuthEmailTarget(projectId, projectName, userId, newEmail, user.Name);
            if (await mailer.SendLinkAsync(uow, target, EmailTokenKind.EmailChange, redirect, Actor.User(userId), token) is { } refused) return refused;
            return default(Done);
        }, ct);
    }

    /// <summary>
    /// <c>account.confirmEmailChange</c> (AC-18): consumes the token, checks the new address is still free, sets it as
    /// the verified email, deletes the user's other live tokens, and keeps every session.
    /// </summary>
    public async Task<Outcome<Data.UserRow>> ConfirmAsync(string projectId, string? tokenValue, CancellationToken ct)
    {
        if (!LinkToken.TryParse(tokenValue, out var link)) return Failure.InvalidEmailToken;

        return await store.WriteAsync<Data.UserRow>(async (uow, token) =>
        {
            if (await EmailTokens.ConsumeLinkAsync(uow, projectId, EmailTokenKind.EmailChange, link, token) is not { Expired: false, UserId: { } userId } consumed)
                return Failure.InvalidEmailToken;
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is null) return Failure.InvalidEmailToken;
            if (await EmailTakenAsync(uow, projectId, consumed.Email, userId, token)) return Failure.EmailAlreadyInUse;
            if (!await SetEmailAsync(uow, userId, consumed.Email, verified: true, token)) return Failure.EmailAlreadyInUse;

            await EmailTokens.DeleteForUserAsync(uow, projectId, userId, kind: null, token);
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpdated, projectId, Actor.User(userId), userId.ToString(),
                new Dictionary<string, string> { ["userId"] = userId.ToString() }, ["email", "emailVerified"], ct: token);
            return await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, token);
        }, ct);
    }

    /// <summary>Whether another user of the project has this email, ignoring case.</summary>
    internal static async Task<bool> EmailTakenAsync(AuthUnitOfWork uow, string projectId, string email, Guid exceptUserId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM orvano.auth_users WHERE project_id = @project AND lower(email) = lower(@email) AND id <> @user)",
            uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("user", exceptUserId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Sets the user's email, and <c>email_verified_at</c> to now or null. False when the unique index on
    /// <c>lower(email)</c> refuses it (a racing change took the address); the savepoint keeps the transaction usable.
    /// </summary>
    internal static async Task<bool> SetEmailAsync(AuthUnitOfWork uow, Guid userId, string email, bool verified, CancellationToken ct)
    {
        await uow.Tx.SaveAsync("set_email", ct);
        try
        {
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE orvano.auth_users SET email = @email, email_verified_at = CASE WHEN @verified THEN now() END, updated_at = now()
                WHERE id = @user
                """, uow.Tx.Connection, uow.Tx);
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("verified", verified);
            cmd.Parameters.AddWithValue("user", userId);
            await cmd.ExecuteNonQueryAsync(ct);
            await uow.Tx.ReleaseAsync("set_email", ct);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == UserRecords.EmailIndex)
        {
            await uow.Tx.RollbackAsync("set_email", ct);
            return false;
        }
    }
}
