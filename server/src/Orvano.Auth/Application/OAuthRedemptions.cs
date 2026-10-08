using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Orvano.Auth.Application;

/// <summary>
/// A redeemed sign in code: the user it resolved to and the new session, or for a user with MFA on, the challenge in
/// its place (spec 0013, AC-6).
/// </summary>
internal sealed record OAuthRedeemed(Resolved Resolved, SessionGrant? Grant, MfaChallengeView? Mfa = null);

/// <summary>
/// What every redeeming flow shares (spec 0012, AC-7, AC-9, AC-10): the unit of work that reruns once after a lost
/// race, and the step after the commit that evicts claimed sessions and issues the access token. Used by
/// <see cref="OAuthService"/> and <see cref="IdentityService"/>, so neither depends on the other for it.
/// </summary>
internal sealed class OAuthRedemptions(
    AuthStore store, SessionChecks checks, AccountService accounts, MethodPolicies policies, ILogger<OAuthRedemptions> logger)
{
    /// <summary>
    /// The session of a resolved provider sign in (spec 0012, AC-7, AC-9), or for an existing user with MFA on, a
    /// challenge in its place (spec 0013, AC-6). A user this sign in created has no factor, so it is never challenged.
    /// </summary>
    public async Task<OAuthRedeemed> SignInAsync(
        AuthUnitOfWork uow, Sessions sessions, string projectId, Resolved resolved, ClientInfo client, string method, string provider, CancellationToken ct)
    {
        if (!resolved.IsNewUser && await MfaGate.ChallengeAsync(policies, uow, projectId, resolved.UserId, method, provider, client, ct) is { } challenge)
            return new OAuthRedeemed(resolved, null, challenge);
        var grant = await sessions.CreateAsync(uow, projectId, resolved.UserId, client, Actor.User(resolved.UserId), method, ct, provider);
        return new OAuthRedeemed(resolved, grant);
    }

    /// <summary>
    /// Runs a redeeming unit of work, and once more when it lost a race (AC-10): a unique violation aborts the Postgres
    /// transaction, so the whole of it reruns, including the code's conditional delete.
    /// </summary>
    public async Task<Outcome<T>> WriteRetryingAsync<T>(Func<AuthUnitOfWork, CancellationToken, Task<Outcome<T>>> work, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await store.WriteAsync(work, ct);
            }
            catch (Exception ex) when (attempt == 0 && SignInResolution.IsRace(ex))
            {
                logger.LogDebug("A provider sign in lost a race and runs again");
            }
        }
    }

    /// <summary>
    /// After a redeeming commit: evicts sessions a claim ended, then issues the new session's access token, or answers
    /// the MFA challenge.
    /// </summary>
    public async Task<Outcome<SignedIn>> FinishAsync(string projectId, Outcome<OAuthRedeemed> outcome, CancellationToken ct)
    {
        if (!outcome.Succeeded) return outcome.Failure!;
        var redeemed = outcome.Value!;
        foreach (var id in redeemed.Resolved.EndedSessions) await checks.EvictAsync(id, ct);
        if (redeemed.Mfa is { } mfa) return SignedIn.Challenged(mfa);
        var row = await store.ReadAsync((db, token) => db.Users.AsNoTracking().SingleAsync(u => u.Id == redeemed.Resolved.UserId, token), ct);
        return await accounts.SignedInAsync(projectId, row, redeemed.Grant!, ct, redeemed.Resolved.IsNewUser);
    }
}
