using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;

namespace Orvano.Auth.Application;

/// <summary>
/// Magic link and email code sign in (spec 0010, AC-5, AC-7, AC-8, AC-15, AC-16, AC-32): open requests that answer
/// the same whether or not the account exists, and the redemptions that sign in, create the user for a new email,
/// and claim an unverified account for the inbox owner.
/// </summary>
internal sealed class PasswordlessService(
    AuthStore store,
    AuthMailer mailer,
    EmailTokens tokens,
    Sessions sessions,
    SessionChecks checks,
    SigningKeys keys,
    AccountService accounts,
    RateLimits limits,
    MethodPolicies policies,
    PolicySettings policySettings,
    ILogger<PasswordlessService> logger)
{
    /// <summary>What a redemption decided: the user, their new session, whether it created them, and sessions to evict.</summary>
    private sealed record Redeemed(Guid UserId, SessionGrant? Grant, bool IsNewUser, Guid[] Ended, MfaChallengeView? Mfa = null);

    /// <summary><c>account.createMagicLink</c> (AC-7, AC-8).</summary>
    public async Task<Outcome<Done>> RequestLinkAsync(string projectId, string? email, string? redirectUrl, bool? createUser, string ipKey, CancellationToken ct)
    {
        if (!EmailRule.TryNormalize(email, out var trimmed)) return InvalidEmail;
        if (await mailer.CheckRedirectAsync(projectId, redirectUrl, EmailTokenKind.MagicLink, ct) is not { } redirect) return Failure.RedirectUrlNotAllowed;
        return await RequestAsync(projectId, trimmed, EmailTokenKind.MagicLink, redirect, createUser ?? true, ipKey, ct);
    }

    /// <summary><c>account.createEmailCode</c> (AC-7, AC-8).</summary>
    public async Task<Outcome<Done>> RequestCodeAsync(string projectId, string? email, bool? createUser, string ipKey, CancellationToken ct) =>
        EmailRule.TryNormalize(email, out var trimmed)
            ? await RequestAsync(projectId, trimmed, EmailTokenKind.EmailCode, redirect: null, createUser ?? true, ipKey, ct)
            : InvalidEmail;

    /// <summary>
    /// <c>account.createMagicLinkSession</c> (AC-3, AC-15): consumes the token and signs its user in, creating them for
    /// a new email. A failure after the token was found rolls back, so the token still works.
    /// </summary>
    public async Task<Outcome<SignedIn>> SignInWithLinkAsync(string projectId, string? tokenValue, ClientInfo client, string ipKey, CancellationToken ct)
    {
        if (!LinkToken.TryParse(tokenValue, out var link)) return Failure.InvalidEmailToken;
        await keys.GetActiveAsync(projectId, ct);

        var outcome = await store.WriteAsync<Redeemed>(async (uow, token) =>
            await EmailTokens.ConsumeLinkAsync(uow, projectId, EmailTokenKind.MagicLink, link, token) is { Expired: false } consumed
                ? await SignInAsync(uow, projectId, consumed.UserId, consumed.Email, SessionMethod.MagicLink, Failure.InvalidEmailToken, client, ipKey, token)
                : Failure.InvalidEmailToken, ct);
        return await FinishAsync(projectId, outcome, ct);
    }

    /// <summary>
    /// <c>account.createEmailCodeSession</c> (AC-5, AC-15): checks the code against the email's live code rows,
    /// locked, in fixed time. A wrong code counts an attempt on every live row and deletes those that reached 10 (spec
    /// 0014, AC-19); those writes commit even though the answer is 401 <c>invalid_code</c>, so parallel guesses can't exceed it.
    /// </summary>
    public async Task<Outcome<SignedIn>> SignInWithCodeAsync(string projectId, string? email, string? code, ClientInfo client, string ipKey, CancellationToken ct)
    {
        if (!EmailRule.TryNormalize(email, out var trimmed)) return InvalidEmail;
        if (!EmailCode.IsWellFormed(code)) return Failure.Invalid($"The code must be exactly {EmailCode.Length} digits.");
        // Spec 0014, AC-19: wrong codes count per email and limit IP, so a stranger elsewhere can't use up the owner's
        // tries, and per email overall as the ceiling of a 6 digit code. Reserved while in flight; only 401 counts.
        using var failures = limits.Reserve(
            (RateLimitPolicies.FailedEmailCodePerRecipientIp, ProjectLimits.EmailKey(ipKey, trimmed)),
            (RateLimitPolicies.FailedEmailCodePerRecipient, $"{projectId}\n{trimmed.ToLowerInvariant()}"));
        if (!failures.Allowed) return Failure.RateLimited(failures.Decision.RetryAfter);
        await keys.GetActiveAsync(projectId, ct);

        var outcome = await store.WriteDecidingAsync<Redeemed>(async (uow, token) =>
        {
            // The user before the code rows: the order every send takes, so a redemption racing a send can't deadlock.
            await UserLocks.ByEmailAsync(uow, projectId, trimmed, token);
            var rows = await EmailTokens.LockCodesAsync(uow, projectId, trimmed, token);
            if (rows.FirstOrDefault(row => tokens.CodeMatches(row, code!)) is not { } match)
            {
                await EmailTokens.CountWrongAttemptAsync(uow, rows, token);
                return (Failure.InvalidCode, true);
            }

            await EmailTokens.DeleteAsync(uow, match.Id, token);
            var redeemed = await SignInAsync(uow, projectId, match.UserId, match.Email, SessionMethod.EmailCode, Failure.InvalidCode, client, ipKey, token);
            return (redeemed, redeemed.Succeeded);
        }, ct);
        if (outcome.Failure?.Code == Contract.ErrorCode.InvalidCode) failures.Fail();
        return await FinishAsync(projectId, outcome, ct);
    }

    private static Failure InvalidEmail => Failure.Invalid("The email must be an address of at most 320 characters.");

    /// <summary>The open request: every refusal before the account is read, then the same success whatever it finds (AC-8, AC-9).</summary>
    private async Task<Outcome<Done>> RequestAsync(
        string projectId, string email, EmailTokenKind kind, RedirectUrl? redirect, bool createUser, string ipKey, CancellationToken ct)
    {
        var refused = await mailer.TakeIpLimitAsync(projectId, ipKey, ct)
            ?? mailer.TakeRecipientLimits(projectId, email, kind, ipKey)
            ?? await mailer.CheckAvailabilityAsync(projectId, ct);
        if (refused is not null) return refused;

        var projectName = await mailer.ProjectNameAsync(projectId, ct);
        var outcome = await store.WriteAsync<Done>(async (uow, token) =>
        {
            var user = await UserLocks.ByEmailAsync(uow, projectId, email, token);
            // AC-8: a blocked user, and an unknown email with createUser false, get nothing, and the same answer.
            if (user is { Status: not UserStatuses.Active } || user is null && !createUser) return default(Done);

            var target = new AuthEmailTarget(projectId, projectName, user?.Id, user?.Email ?? email, user?.Name);
            var actor = user is null ? Actor.UnknownUser : Actor.User(user.Id);
            var notQueued = kind == EmailTokenKind.EmailCode
                ? await mailer.SendCodeAsync(uow, target, actor, token)
                : await mailer.SendLinkAsync(uow, target, kind, redirect!, actor, token);
            return notQueued is null ? default(Done) : notQueued;
        }, ct);

        if (!outcome.Succeeded)
            logger.LogWarning("A {Kind} email of project {ProjectId} was not queued: {Code}", EmailTokenKinds.Wire(kind), projectId, outcome.Failure!.Code);
        return default(Done);
    }

    /// <summary>
    /// AC-15 and AC-32 inside the redeeming transaction: resolves the user (the token's, else the one with the email
    /// now, else a new verified user under the sign up limit), refuses a blocked user, claims an unverified account
    /// that has a password or a provider identity, marks the email verified, and creates the session.
    /// </summary>
    private async Task<Outcome<Redeemed>> SignInAsync(
        AuthUnitOfWork uow, string projectId, Guid? tokenUserId, string tokenEmail, string method, Failure invalid, ClientInfo client, string ipKey,
        CancellationToken ct)
    {
        LockedUser? user;
        if (tokenUserId is { } id)
        {
            user = await UserLocks.ByIdAsync(uow, projectId, id, ct);
            // AC-3: the account's email moved since the token was sent.
            if (user is null || !EmailRule.SameAddress(user.Email, tokenEmail)) return invalid;
        }
        else
        {
            user = await UserLocks.ByEmailAsync(uow, projectId, tokenEmail, ct);
        }

        var created = false;
        if (user is null)
        {
            var project = await policySettings.GetAsync(uow.Tx.Connection!, uow.Tx, projectId, ct);
            var signUp = limits.Acquire(ProjectLimits.SignUpPerIp(project.Auth), ipKey);
            if (!signUp.Allowed) return Failure.RateLimited(signUp.RetryAfter);
            // Spec 0014, AC-9 and AC-10: decided here, once the inbox proved the address; the refusal rolls back, so the
            // link or code stays unspent.
            if (project.CheckSignUp(tokenEmail) is { } refused) return refused;
            if (await UserRecords.TryInsertAsync(uow, projectId, tokenEmail, name: null, verified: true, ct) is { } newId)
            {
                created = true;
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserCreated, projectId, Actor.User(newId), newId.ToString(),
                    new Dictionary<string, string> { ["userId"] = newId.ToString() }, fields: new Dictionary<string, string?> { ["method"] = method }, ct: ct);
            }

            // Two redemptions created the same email at once: the other won, so this one signs in to that user.
            user = await UserLocks.ByEmailAsync(uow, projectId, tokenEmail, ct)
                ?? throw new InvalidOperationException("A user that just existed is gone.");
        }

        if (user.Status != UserStatuses.Active) return Failure.UserBlocked;

        var actor = Actor.User(user.Id);
        Guid[] ended = [];
        if (!created && user.EmailVerifiedAt is null)
        {
            // AC-32 (amended by spec 0012, AC-12): the inbox owner just proved the email, so a password, provider
            // identities, and the sessions made before that proof go.
            ended = (await AccountClaims.ClaimAsync(uow, sessions, projectId, user, actor, endSessions: false, ct)).EndedSessions;
            await VerificationService.MarkVerifiedAsync(uow, user.Id, ct);
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpdated, projectId, actor, user.Id.ToString(),
                new Dictionary<string, string> { ["userId"] = user.Id.ToString() }, ["emailVerified"], ct: ct);
        }

        // Spec 0013, AC-6: after every check and the claim (which removes factors, AC-29), a user with MFA on gets a
        // challenge in place of the session. Spec 0014, AC-27: under required MFA, a user with no factor (a new one too)
        // gets an enrollment ticket instead. The link or code stays used.
        var gate = await MfaGate.ChallengeAsync(policies, uow, projectId, user.Id, method, null, client, ct);
        if (!gate.Succeeded) return gate.Failure!;
        if (gate.Value is { } challenge) return new Redeemed(user.Id, null, false, ended, challenge);

        var grant = await sessions.CreateAsync(uow, projectId, user.Id, client, actor, method, ct);
        return new Redeemed(user.Id, grant, created, ended);
    }

    private async Task<Outcome<SignedIn>> FinishAsync(string projectId, Outcome<Redeemed> outcome, CancellationToken ct)
    {
        if (!outcome.Succeeded) return outcome.Failure!;
        var redeemed = outcome.Value!;
        foreach (var id in redeemed.Ended) await checks.EvictAsync(id, ct);
        if (redeemed.Mfa is { } mfa) return SignedIn.Challenged(mfa);
        var row = await store.ReadAsync((db, token) => db.Users.AsNoTracking().SingleAsync(u => u.Id == redeemed.UserId, token), ct);
        return await accounts.SignedInAsync(projectId, row, redeemed.Grant!, ct, redeemed.IsNewUser);
    }
}
