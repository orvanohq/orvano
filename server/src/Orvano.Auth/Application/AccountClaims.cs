using Npgsql;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// Claiming an unverified account for whoever just proved the email (spec 0010, AC-32, amended by spec 0012, AC-12):
/// the password and every identity go, Apple ones with a revoke queued, the second factors and passkeys go (spec 0013,
/// AC-29), and the user's sessions end with reason <c>account_claimed</c>. Runs in the caller's transaction, under the
/// user's lock, before the caller signs in.
/// </summary>
internal static class AccountClaims
{
    /// <summary>What a claim removed: the ended session IDs to evict after the commit.</summary>
    public sealed record Claimed(Guid[] EndedSessions);

    /// <summary>
    /// Removes the password (writing <c>auth.password.removed</c>) and every identity (each with
    /// <c>auth.identity.unlinked</c>, reason <c>claimed</c>), and ends the user's sessions when <paramref name="endSessions"/>
    /// says so or anything was removed. <paramref name="removePassword"/> false keeps the password (the email link proves
    /// the inbox, not who chose the password). The caller marks the email verified and writes <c>auth.user.updated</c>.
    /// Refuses project <c>console</c> (spec 0013, AC-29): console accounts enroll factors with an unverified email, which
    /// is safe only while nothing claims one. A change that adds a console claim must drop this guard and bring the
    /// verified email rule back for <c>console</c> in the same pull request.
    /// </summary>
    public static async Task<Claimed> ClaimAsync(
        AuthUnitOfWork uow, Sessions sessions, string projectId, LockedUser user, Actor actor, bool endSessions, CancellationToken ct, bool removePassword = true)
    {
        if (projectId == ConsoleProject.Id)
            throw new InvalidOperationException("Console accounts are never claimed (spec 0013, AC-29).");

        var removed = false;
        if (removePassword && user.HasPassword)
        {
            await using (var remove = new NpgsqlCommand("DELETE FROM orvano.auth_passwords WHERE user_id = @user", uow.Tx.Connection, uow.Tx))
            {
                remove.Parameters.AddWithValue("user", user.Id);
                await remove.ExecuteNonQueryAsync(ct);
            }

            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasswordRemoved, projectId, actor, user.Id.ToString(),
                new Dictionary<string, string> { ["userId"] = user.Id.ToString() }, ct: ct);
            removed = true;
        }

        foreach (var identity in await Identities.OfUserAsync(uow, user.Id, ct))
        {
            await Identities.DeleteAsync(uow, projectId, identity, Identities.UnlinkReason.Claimed, actor, ct);
            removed = true;
        }

        // Spec 0013, AC-29: a claimed account keeps no second factor and no passkey, with no alert emails. Removing
        // them alone ends no session.
        var userKey = user.Id.ToString();
        var ids = new Dictionary<string, string> { ["userId"] = userKey };
        if (await MfaFactorStore.DeleteFactorsAsync(uow, user.Id, ct))
        {
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.MfaDisabled, projectId, actor, userKey, ids,
                fields: new Dictionary<string, string?> { ["reason"] = AuthEvents.RemovedByClaim }, ct: ct);
        }

        foreach (var passkeyId in await MfaFactorStore.DeletePasskeysAsync(uow, user.Id, ct))
        {
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasskeyRemoved, projectId, actor, userKey,
                new Dictionary<string, string> { ["userId"] = userKey, ["passkeyId"] = passkeyId.ToString() },
                fields: new Dictionary<string, string?> { ["reason"] = AuthEvents.RemovedByClaim }, ct: ct);
        }

        Guid[] ended = endSessions || removed
            ? [.. await sessions.EndAllAsync(uow, projectId, user.Id, SessionEndReason.AccountClaimed, actor, keep: null, ct)]
            : [];
        return new Claimed(ended);
    }
}
