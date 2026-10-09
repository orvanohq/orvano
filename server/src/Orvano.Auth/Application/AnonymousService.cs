using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Data;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// An upgrade's answer (the contract's <c>AnonymousUpgradeResult</c>, spec 0014, AC-30): the permanent user and what
/// happened to the verification email, or under the verified email flow <see cref="Pending"/>, the same whatever happened.
/// </summary>
internal sealed record AnonymousUpgradeView(UserRow? User, bool VerificationRequired, VerificationEmail? VerificationEmail)
{
    public static AnonymousUpgradeView Pending { get; } = new(null, true, null);
}

/// <summary>
/// Guests (spec 0014, AC-28 to AC-30): guest sign in, and the upgrade to a permanent user with an email and password,
/// keeping the user ID. A provider link upgrades a guest too (<see cref="GuestUpgrades"/>, called by
/// <c>IdentityService</c>), and so does the email change link an upgrade under the verified email flow sends
/// (<c>EmailChangeService.ConfirmAsync</c>).
/// </summary>
internal sealed class AnonymousService(
    AuthStore store,
    PasswordHasher hasher,
    Sessions sessions,
    SigningKeys keys,
    AccountService accounts,
    AuthMailer mailer,
    PasswordRules passwordRules,
    PolicySettings policySettings,
    SecurityAlerts alerts)
{
    /// <summary>The <c>kind</c> the <c>sign_up_attempt</c> alert's recipient limits are keyed by, as for sign up (AC-12).</summary>
    private const string SignUpAttemptKind = "sign_up_attempt";

    /// <summary>
    /// <c>account.createAnonymousSession</c> (AC-28), after the endpoint's two limits: 403 <c>anonymous_disabled</c>
    /// while the project's switch is off, 403 <c>sign_up_disabled</c> while sign ups are closed, then a user with no
    /// email, name, or password, and an <c>anonymous</c> session (level 1, empty <c>amr</c>) in one transaction. A guest
    /// is never challenged for MFA (AC-27), so no MFA gate runs.
    /// </summary>
    public async Task<Outcome<SignedIn>> SignInAsync(string projectId, ClientInfo client, CancellationToken ct)
    {
        var project = await policySettings.GetAsync(projectId, ct);
        if (!project.Methods.AnonymousEnabled) return Failure.AnonymousDisabled;
        if (!project.Auth.SignUpsEnabled) return Failure.SignUpDisabled;
        await keys.GetActiveAsync(projectId, ct); // the first token of a project creates its key, before any commit

        var outcome = await store.WriteAsync<(UserRow User, SessionGrant Grant)>(async (uow, token) =>
        {
            Guid userId;
            await using (var insert = new NpgsqlCommand(
                "INSERT INTO orvano.auth_users (project_id, is_anonymous) VALUES (@project, true) RETURNING id", uow.Tx.Connection, uow.Tx))
            {
                insert.Parameters.AddWithValue("project", projectId);
                userId = (Guid)(await insert.ExecuteScalarAsync(token))!;
            }

            var actor = Actor.User(userId);
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserCreated, projectId, actor, userId.ToString(),
                new Dictionary<string, string> { ["userId"] = userId.ToString() },
                fields: new Dictionary<string, string?> { ["method"] = SessionMethod.Anonymous }, ct: token);
            var grant = await sessions.CreateAsync(uow, projectId, userId, client, actor, SessionMethod.Anonymous, token);
            return (await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, token), grant);
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        return await accounts.SignedInAsync(projectId, outcome.Value.User, outcome.Value.Grant, ct, isNewUser: true);
    }

    /// <summary>
    /// <c>account.upgradeAnonymous</c> (AC-30). A caller who is not a guest gets 403 <c>forbidden</c>. Then, in order,
    /// the email rule, the name, the project's domain rule (AC-9), and the password rules (AC-4), before any account is
    /// read. Without the verified email flow the guest becomes permanent at once (<see cref="UpgradeNowAsync"/>); with
    /// it, the upgrade waits for the emailed link (<see cref="UpgradePendingAsync"/>), and the endpoint holds the
    /// answer to the 500 ms floor.
    /// </summary>
    public async Task<Outcome<AnonymousUpgradeView>> UpgradeAsync(
        string projectId, Guid userId, string? email, string? password, string? name, string? redirectUrl, string limitKey, CancellationToken ct)
    {
        var isGuest = await store.ReadAsync((db, token) =>
            db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.ProjectId == projectId && u.IsAnonymous, token), ct);
        if (!isGuest) return Failure.NotAnonymous;
        if (!EmailRule.TryNormalize(email, out var trimmed)) return Failure.Invalid("The email must be an address of at most 320 characters.");
        if (!UserName.IsValid(name)) return Failure.Invalid($"The name must be at most {UserName.MaxLength} characters.");
        var project = await policySettings.GetAsync(projectId, ct);
        if (project.CheckDomain(trimmed) is { } domainRefused) return domainRefused;
        var rules = await passwordRules.CheckNewAsync(projectId, password, ct);
        if (!rules.Succeeded) return rules.Failure!;

        return project.VerifiedEmailFlow
            ? await UpgradePendingAsync(projectId, userId, trimmed, rules.Value!, name, redirectUrl, limitKey, ct)
            : await UpgradeNowAsync(projectId, userId, trimmed, rules.Value!, name, redirectUrl, limitKey, ct);
    }

    /// <summary>
    /// The upgrade without the verified email flow: under the guest's lock, the email (unverified, 409
    /// <c>user_already_exists</c> when another user has it), the password, the name when given, and
    /// <c>is_anonymous</c> false, voiding any earlier upgrade link; then a verification email when
    /// <paramref name="redirectUrl"/> was given (spec 0010, AC-11), which never fails the upgrade.
    /// </summary>
    private async Task<Outcome<AnonymousUpgradeView>> UpgradeNowAsync(
        string projectId, Guid userId, string email, string normalized, string? name, string? redirectUrl, string limitKey, CancellationToken ct)
    {
        RedirectUrl? redirect = null;
        if (redirectUrl is not null)
        {
            redirect = await mailer.CheckRedirectAsync(projectId, redirectUrl, EmailTokenKind.Verification, ct);
            if (redirect is null) return Failure.RedirectUrlNotAllowed;
        }

        var projectName = redirect is null ? null : await mailer.ProjectNameAsync(projectId, ct);
        var hash = await hasher.TryHashAsync(normalized, ct);
        if (hash is null) return Failure.Busy;

        return await store.WriteAsync<AnonymousUpgradeView>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { IsAnonymous: true } guest) return Failure.NotAnonymous;
            if (await EmailChangeService.EmailTakenAsync(uow, projectId, email, userId, token)) return Failure.UserAlreadyExists;
            // Sets is_anonymous false with the email; a racing sign up that took the address loses to the index here.
            if (!await EmailChangeService.SetEmailAsync(uow, userId, email, verified: false, token)) return Failure.UserAlreadyExists;
            if (name is not null) await SetNameAsync(uow, userId, name, token);
            await UpsertPasswordAsync(uow, projectId, userId, hash, token);
            await EmailTokens.DeleteForUserAsync(uow, projectId, userId, EmailTokenKind.EmailChange, token);

            var actor = Actor.User(userId);
            await GuestUpgrades.WriteUpgradedAsync(uow, projectId, userId, GuestUpgrades.ByPassword, token);
            var sent = redirect is null ? (VerificationEmail?)null
                : await accounts.SendSignUpVerificationAsync(uow, projectId, projectName!, userId, email, name ?? guest.Name, redirect, actor, limitKey, token);
            return new AnonymousUpgradeView(await uow.Db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, token), false, sent);
        }, ct);
    }

    /// <summary>
    /// The upgrade under the verified email flow: <paramref name="redirectUrl"/> is required and the project or the
    /// install must have SMTP (409 <c>email_not_configured</c>); then every branch answers
    /// <see cref="AnonymousUpgradeView.Pending"/>. The name, when given, is set at once, and any earlier upgrade link is
    /// voided. A free email gets the password hash stored (unusable until the guest has an email) and spec 0010's email
    /// change link, whose redemption makes the guest permanent. A taken email changes nothing else and sends its owner the
    /// <c>sign_up_attempt</c> alert. Both branches cost one Argon2id run, and no limit or SMTP answer changes the reply.
    /// </summary>
    private async Task<Outcome<AnonymousUpgradeView>> UpgradePendingAsync(
        string projectId, Guid userId, string email, string normalized, string? name, string? redirectUrl, string limitKey, CancellationToken ct)
    {
        if (redirectUrl is null) return Failure.Invalid("verificationRedirectUrl is required while the project requires verified emails or MFA.");
        if (await mailer.CheckRedirectAsync(projectId, redirectUrl, EmailTokenKind.EmailChange, ct) is not { } redirect) return Failure.RedirectUrlNotAllowed;
        if (await mailer.CheckAvailabilityAsync(projectId, ct) is { Code: Orvano.Contract.ErrorCode.EmailNotConfigured } notConfigured) return notConfigured;

        var projectName = await mailer.ProjectNameAsync(projectId, ct);
        string? hash = null;
        if (await FindOwnerAsync(projectId, email, userId, ct) is null)
        {
            hash = await hasher.TryHashAsync(normalized, ct);
            if (hash is null) return Failure.Busy;
        }
        else if (await hasher.TryVerifyAsync(normalized, null, ct) is null)
        {
            return Failure.Busy;
        }

        var outcome = await store.WriteAsync<Done>(async (uow, token) =>
        {
            if (await UserLocks.ByIdAsync(uow, projectId, userId, token) is not { IsAnonymous: true } guest) return Failure.NotAnonymous;
            if (name is not null) await SetNameAsync(uow, userId, name, token);
            // A later upgrade wins in every branch: the earlier link stops working.
            await EmailTokens.DeleteForUserAsync(uow, projectId, userId, EmailTokenKind.EmailChange, token);

            var owner = await FindOwnerAsync(uow.Tx.Connection!, uow.Tx, projectId, email, userId, token);
            if (owner is null)
            {
                // Freed since the read before the transaction: nothing was hashed, so nothing is stored or sent.
                if (hash is null) return default(Done);
                await UpsertPasswordAsync(uow, projectId, userId, hash, token);
                if (mailer.TakeRecipientLimits(projectId, email, EmailTokenKind.EmailChange, limitKey) is not null) return default(Done);
                await uow.Tx.SaveAsync("upgrade_email", token);
                var target = new AuthEmailTarget(projectId, projectName, userId, email, name ?? guest.Name);
                if (await mailer.SendLinkAsync(uow, target, EmailTokenKind.EmailChange, redirect, Actor.User(userId), token) is not null)
                    await uow.Tx.RollbackAsync("upgrade_email", token);
                else
                    await uow.Tx.ReleaseAsync("upgrade_email", token);
                return default(Done);
            }

            // Taken: its active owner hears that someone tried to use the address. A blocked owner gets nothing.
            if (owner is { Active: true, Email: var to } && mailer.TakeRecipientLimits(projectId, to, SignUpAttemptKind, limitKey) is null)
                await alerts.QueueAsync(uow, projectId, projectName, to, Orvano.Messaging.Contracts.SecurityAlertKind.SignUpAttempt, token);
            return default(Done);
        }, ct);
        return outcome.Succeeded ? AnonymousUpgradeView.Pending : outcome.Failure!;
    }

    /// <summary>The other user of the project with this email, ignoring case: whether they are active, and their email as stored.</summary>
    private sealed record Owner(bool Active, string Email);

    private Task<Owner?> FindOwnerAsync(string projectId, string email, Guid exceptUserId, CancellationToken ct) =>
        store.ReadAsync((db, token) => FindOwnerAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, projectId, email, exceptUserId, token), ct);

    private static async Task<Owner?> FindOwnerAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string projectId, string email, Guid exceptUserId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT status = 'active', email FROM orvano.auth_users WHERE project_id = @project AND lower(email) = lower(@email) AND id <> @user",
            conn, tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("user", exceptUserId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new Owner(reader.GetBoolean(0), reader.GetString(1)) : null;
    }

    private static async Task SetNameAsync(AuthUnitOfWork uow, Guid userId, string name, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("UPDATE orvano.auth_users SET name = @name, updated_at = now() WHERE id = @user", uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("name", NpgsqlDbType.Text, name);
        cmd.Parameters.AddWithValue("user", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The guest's password: a pending upgrade's earlier hash is replaced.</summary>
    private static async Task UpsertPasswordAsync(AuthUnitOfWork uow, string projectId, Guid userId, string hash, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO orvano.auth_passwords (user_id, project_id, hash) VALUES (@user, @project, @hash)
            ON CONFLICT (user_id) DO UPDATE SET hash = excluded.hash, updated_at = now()
            """, uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("hash", hash);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// What every way a guest becomes permanent shares (spec 0014, AC-30): the <c>auth.user.upgraded</c> event with the
/// method, and, while the project requires MFA, the end of all the user's sessions (<c>mfa_required</c>), so a permanent
/// user without a factor never holds one.
/// </summary>
internal static class GuestUpgrades
{
    /// <summary>The <c>method</c> of an upgrade with an email and password, at once or by its emailed link.</summary>
    public const string ByPassword = "password";

    public static Task WriteUpgradedAsync(AuthUnitOfWork uow, string projectId, Guid userId, string method, CancellationToken ct) =>
        AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpgraded, projectId, Actor.User(userId), userId.ToString(),
            new Dictionary<string, string> { ["userId"] = userId.ToString() }, fields: new Dictionary<string, string?> { ["method"] = method }, ct: ct);

    /// <summary>
    /// The guest just became permanent in this transaction: writes the event and, under required MFA, ends every
    /// session of the user, evicted once committed.
    /// </summary>
    public static async Task BecamePermanentAsync(
        AuthUnitOfWork uow, Sessions sessions, SessionChecks checks, PolicySettings policies, string projectId, Guid userId, string method, CancellationToken ct)
    {
        await WriteUpgradedAsync(uow, projectId, userId, method, ct);
        if (!(await policies.GetAsync(uow.Tx.Connection!, uow.Tx, projectId, ct)).Methods.MfaRequired) return;
        foreach (var sessionId in await sessions.EndAllAsync(uow, projectId, userId, SessionEndReason.MfaRequired, Actor.System, keep: null, ct))
            uow.AfterCommit.Add(token => checks.EvictAsync(sessionId, token));
    }

    /// <summary>
    /// Sets <c>is_anonymous</c> false without an email: a provider link whose verified email is missing or taken
    /// (AC-30). The email check of the table holds, since a permanent user may have none.
    /// </summary>
    public static async Task ClearAnonymousAsync(AuthUnitOfWork uow, Guid userId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE orvano.auth_users SET is_anonymous = false, updated_at = now() WHERE id = @user", uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("user", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
