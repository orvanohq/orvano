using Orvano.Auth.Domain;
using Orvano.Messaging.Contracts;

namespace Orvano.Auth.Application;

/// <summary>What a reset changed: whether a confirmed factor was there, the passkeys it removed, and the sessions it ended.</summary>
internal sealed record MfaResetResult(bool HadFactor, Guid[] PasskeysRemoved, Guid[] Ended)
{
    /// <summary>True when the reset removed anything, so it ended the user's sessions.</summary>
    public bool Changed => HadFactor || PasskeysRemoved.Length > 0;
}

/// <summary>
/// Resets a user's MFA for someone who lost every factor (spec 0013, AC-27, AC-28): deletes the TOTP factor and every
/// recovery code, optionally the passkeys too, and ends every session (reason <c>mfa_reset</c>), all under the user
/// lock in one transaction. A user with nothing to remove is left as they are, with no event. The server
/// (<c>users.resetMfa</c>), the console (<c>consoleUsers.resetMfa</c>), and <c>orvano mfa reset</c> differ only in the
/// actor and the passkey reason.
/// </summary>
internal sealed class MfaResets(AuthStore store, Sessions sessions, SessionChecks checks, SecurityAlerts alerts)
{
    /// <summary>The <c>reason</c> of <c>auth.passkey.removed</c> for each caller (AC-33).</summary>
    public const string ByServer = "server";

    /// <inheritdoc cref="ByServer"/>
    public const string ByConsole = "console";

    /// <inheritdoc cref="ByServer"/>
    public const string ByCli = "cli";

    /// <summary>Resets the MFA of a project's user by ID; an unknown ID is 404 <c>user_not_found</c>.</summary>
    public Task<Outcome<MfaResetResult>> ResetAsync(string projectId, string userId, Actor actor, CancellationToken ct) =>
        Guid.TryParse(userId, out var id)
            ? ResetAsync(projectId, (uow, token) => UserLocks.ByIdAsync(uow, projectId, id, token), actor, removePasskeys: false, ByServer, ct)
            : Task.FromResult<Outcome<MfaResetResult>>(Failure.UserNotFound);

    /// <summary>
    /// Resets the MFA of the console account with this email, ignoring case (AC-28), as the system; with
    /// <paramref name="removePasskeys"/> its passkeys go too.
    /// </summary>
    public Task<Outcome<MfaResetResult>> ResetConsoleAccountAsync(string email, bool removePasskeys, CancellationToken ct) =>
        ResetAsync(ConsoleProject.Id, (uow, token) => UserLocks.ByEmailAsync(uow, ConsoleProject.Id, email, token), Actor.System, removePasskeys, ByCli, ct);

    private async Task<Outcome<MfaResetResult>> ResetAsync(
        string projectId, Func<AuthUnitOfWork, CancellationToken, Task<LockedUser?>> find, Actor actor, bool removePasskeys, string passkeyReason,
        CancellationToken ct)
    {
        var projectName = await alerts.ProjectNameAsync(projectId, ct);
        var outcome = await store.WriteAsync<MfaResetResult>(async (uow, token) =>
        {
            if (await find(uow, token) is not { } user) return Failure.UserNotFound;

            var userKey = user.Id.ToString();
            var hadFactor = await MfaFactorStore.DeleteFactorsAsync(uow, user.Id, token);
            var passkeys = removePasskeys ? await MfaFactorStore.DeletePasskeysAsync(uow, user.Id, token) : [];
            if (!hadFactor && passkeys.Length == 0) return new MfaResetResult(false, [], []);

            if (hadFactor)
            {
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.MfaReset, projectId, actor, userKey,
                    new Dictionary<string, string> { ["userId"] = userKey }, ct: token);
            }

            foreach (var passkeyId in passkeys)
            {
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasskeyRemoved, projectId, actor, userKey,
                    new Dictionary<string, string> { ["userId"] = userKey, ["passkeyId"] = passkeyId.ToString() }, reason: passkeyReason, ct: token);
            }

            // One alert per change (AC-31): MFA off, and the passkeys gone when `orvano mfa reset --passkeys` took them.
            if (hadFactor) await alerts.QueueAsync(uow, projectId, projectName, user.Email, SecurityAlertKind.MfaDisabled, token);
            if (passkeys.Length > 0) await alerts.QueueAsync(uow, projectId, projectName, user.Email, SecurityAlertKind.PasskeyRemoved, token);

            var ended = await sessions.EndAllAsync(uow, projectId, user.Id, SessionEndReason.MfaReset, actor, keep: null, token);
            return new MfaResetResult(hadFactor, passkeys, [.. ended]);
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        var result = outcome.Value!;
        foreach (var session in result.Ended) await checks.EvictAsync(session, ct);
        return result;
    }
}
