using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// Email verification (spec 0010, AC-12, AC-13): the signed in user asks for a link, and anyone holding the link
/// verifies the email. Verification is data the app reads, never a gate the API enforces.
/// </summary>
internal sealed class VerificationService(AuthStore store, AuthMailer mailer)
{
    /// <summary>
    /// <c>account.createVerification</c> (AC-12): 409 <c>email_already_verified</c> for a verified user, then the two
    /// recipient limits, then the queue's answer. A signed in caller already knows the account exists, so no privacy
    /// padding applies.
    /// </summary>
    public async Task<Outcome<Done>> RequestAsync(string projectId, Guid userId, string? redirectUrl, Actor actor, CancellationToken ct)
    {
        if (await mailer.CheckRedirectAsync(projectId, redirectUrl, EmailTokenKind.Verification, ct) is not { } redirect) return Failure.RedirectUrlNotAllowed;
        var projectName = await mailer.ProjectNameAsync(projectId, ct);

        return await store.WriteAsync<Done>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user) return Failure.UserNotFound;
            if (user.EmailVerifiedAt is not null) return Failure.EmailAlreadyVerified;
            if (user.Email is not { } to) return Failure.Invalid("The user has no email to verify.");
            if (mailer.TakeRecipientLimits(projectId, to, EmailTokenKind.Verification) is { } limited) return limited;

            var target = new AuthEmailTarget(projectId, projectName, user.Id, to, user.Name);
            if (await mailer.SendLinkAsync(uow, target, EmailTokenKind.Verification, redirect, actor, token) is { } refused) return refused;
            return default(Done);
        }, ct);
    }

    /// <summary>
    /// <c>account.verifyEmail</c> (AC-13): consumes a verification token whose email still equals the user's, sets
    /// <c>email_verified_at</c> when it was null, and writes <c>auth.user.updated</c>. It neither needs nor creates a
    /// session.
    /// </summary>
    public async Task<Outcome<Data.UserRow>> VerifyAsync(string projectId, string? tokenValue, CancellationToken ct)
    {
        if (!LinkToken.TryParse(tokenValue, out var link)) return Failure.InvalidEmailToken;

        return await store.WriteAsync<Data.UserRow>(async (uow, token) =>
        {
            if (await EmailTokens.ConsumeLinkAsync(uow, projectId, EmailTokenKind.Verification, link, token) is not { Expired: false, UserId: { } userId } consumed)
                return Failure.InvalidEmailToken;
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { } user || !RecoveryService.SameEmail(user.Email, consumed.Email))
                return Failure.InvalidEmailToken;

            if (user.EmailVerifiedAt is null)
            {
                await MarkVerifiedAsync(uow, userId, token);
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpdated, projectId, Actor.User(userId), userId.ToString(),
                    new Dictionary<string, string> { ["userId"] = userId.ToString() }, ["emailVerified"], ct: token);
            }

            return await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, token);
        }, ct);
    }

    /// <summary>Sets <c>email_verified_at = now()</c> when it is null.</summary>
    internal static async Task MarkVerifiedAsync(AuthUnitOfWork uow, Guid userId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE orvano.auth_users SET email_verified_at = coalesce(email_verified_at, now()), updated_at = now() WHERE id = @user",
            uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("user", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
