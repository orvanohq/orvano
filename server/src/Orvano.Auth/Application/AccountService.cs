using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Data;
using Orvano.Auth.Domain;
using Orvano.Platform.Contracts;

namespace Orvano.Auth.Application;

/// <summary>A session's tokens as the API returns them (the contract's <c>SessionTokens</c>).</summary>
internal sealed record SessionTokensView(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, Guid SessionId);

/// <summary>What happened to the verification email a sign up asked for (spec 0010, AC-11).</summary>
internal enum VerificationEmail
{
    Queued,
    NotConfigured,
    RateLimited,
}

/// <summary>
/// A signed in user and their new session (the contract's <c>AuthResult</c>); <paramref name="IsNewUser"/> when this
/// call created them, and <paramref name="VerificationEmail"/> when a sign up asked for a verification email. For a
/// user with MFA on, step one answers <see cref="Challenged"/> instead: no user, no session, only the
/// <paramref name="Mfa"/> challenge (spec 0013, AC-6).
/// </summary>
internal sealed record SignedIn(
    UserRow? User, SessionTokensView? Session, bool IsNewUser = false, VerificationEmail? VerificationEmail = null, MfaChallengeView? Mfa = null)
{
    public static SignedIn Challenged(MfaChallengeView mfa) => new(null, null, Mfa: mfa);
}

/// <summary>
/// Changes to the signed in user (AC-13). A null <see cref="Metadata"/> leaves the metadata alone; <see cref="SetName"/>
/// false leaves the name alone, and true with a null <see cref="Name"/> removes it.
/// </summary>
internal sealed record AccountChanges(bool SetName, string? Name, string? Metadata);

/// <summary>
/// What a console sign up carried for the install's sign up gate (spec 0003 AC-7, spec 0006). <paramref name="Seeded"/>
/// is only for the <c>Test</c> fixtures' later accounts: admitted as if sign up were open, never as the first account.
/// </summary>
internal sealed record ConsoleGate(string? InviteToken, string? SetupToken, bool Seeded = false);

/// <summary>
/// The signed in user's own account (spec 0004, <c>account</c> service): sign up, sign in, the current user, and self
/// service. Use cases know no HTTP; they return an <see cref="Outcome{T}"/>.
/// </summary>
internal sealed class AccountService(
    AuthStore store,
    PasswordHasher hasher,
    Sessions sessions,
    SessionChecks checks,
    AccessTokens tokens,
    SigningKeys keys,
    IConsoleSignupPolicy signupPolicy,
    IConsoleAccountCreated accountCreated,
    AuthMailer mailer,
    StepUp stepUp)
{
    public const string EmailIndex = UserRecords.EmailIndex;

    /// <summary>
    /// Sign up (AC-1 to AC-3): creates the user, their password row, and a session in one transaction. The password is
    /// hashed before the transaction opens, so no connection waits on Argon2id. With
    /// <paramref name="verificationRedirectUrl"/>, the same transaction also queues a verification email (spec 0010,
    /// AC-11); the user and session are created whatever happens to it.
    /// </summary>
    public async Task<Outcome<SignedIn>> SignUpAsync(
        string projectId, string? email, string? password, string? name, ClientInfo client, CancellationToken ct, ConsoleGate? gate = null,
        string? verificationRedirectUrl = null)
    {
        await keys.GetActiveAsync(projectId, ct); // the first token of a project creates its key, before any commit
        var outcome = await CreateAsync(projectId, email, password, name, userId => Actor.User(userId), client, ct, gate, verificationRedirectUrl);
        if (!outcome.Succeeded) return outcome.Failure!;
        var signedIn = await SignedInAsync(projectId, outcome.Value.User, outcome.Value.Grant!, ct, isNewUser: true);
        return signedIn with { VerificationEmail = outcome.Value.Verification };
    }

    /// <summary>A console account created without signing it in: the <c>Test</c> fixtures' seeding.</summary>
    public async Task<Outcome<UserRow>> CreateConsoleAccountAsync(string? email, string? password, string? name, ConsoleGate gate, CancellationToken ct)
    {
        var outcome = await CreateAsync(ConsoleProject.Id, email, password, name, userId => Actor.User(userId), client: null, ct, gate);
        return outcome.Succeeded ? outcome.Value.User : outcome.Failure!;
    }

    /// <summary>
    /// A server or console creates a user (AC-17): the same rules as sign up, and no session. <paramref name="emailVerified"/>
    /// creates them already verified, as when importing accounts (spec 0010, AC-21).
    /// </summary>
    public async Task<Outcome<UserRow>> CreateUserAsync(
        string projectId, string? email, string? password, string? name, Actor actor, CancellationToken ct, bool emailVerified = false)
    {
        var outcome = await CreateAsync(projectId, email, password, name, _ => actor, client: null, ct, verified: emailVerified);
        return outcome.Succeeded ? outcome.Value.User : outcome.Failure!;
    }

    /// <summary>
    /// Creates the user and their password row, and a session when <paramref name="client"/> is given. A console sign
    /// up passes <paramref name="gate"/>: Platform's <c>AdmitAsync</c> decides first and <c>OnCreatedAsync</c> runs
    /// after the insert, both in this transaction (spec 0003 AC-7, AC-8).
    /// </summary>
    private async Task<Outcome<(UserRow User, SessionGrant? Grant, VerificationEmail? Verification)>> CreateAsync(
        string projectId, string? email, string? password, string? name, Func<Guid, Actor> actorOf, ClientInfo? client, CancellationToken ct,
        ConsoleGate? gate = null, string? verificationRedirectUrl = null, bool verified = false)
    {
        if (!EmailRule.TryNormalize(email, out var trimmed)) return Failure.Invalid("The email must be an address of at most 320 characters.");
        if (!UserName.IsValid(name)) return Failure.Invalid($"The name must be at most {UserName.MaxLength} characters.");
        if (!PasswordPolicy.TryNormalize(password, out var normalized)) return Failure.InvalidPassword;
        // Spec 0010 AC-11: checked with the rest of the body, before the 409 and before anything is created.
        RedirectUrl? verification = null;
        if (verificationRedirectUrl is not null)
        {
            verification = await mailer.CheckRedirectAsync(projectId, verificationRedirectUrl, EmailTokenKind.Verification, ct);
            if (verification is null) return Failure.RedirectUrlNotAllowed;
        }

        var projectName = verification is null ? null : await mailer.ProjectNameAsync(projectId, ct);
        var hash = await hasher.TryHashAsync(normalized, ct);
        if (hash is null) return Failure.Busy;

        return await store.WriteAsync<(UserRow User, SessionGrant? Grant, VerificationEmail? Verification)>(async (uow, token) =>
        {
            SignupAdmission.Admitted? admitted = null;
            if (gate is { Seeded: true })
            {
                admitted = new SignupAdmission.Admitted(IsFirstAccount: false);
            }
            else if (gate is not null)
            {
                var decision = await signupPolicy.AdmitAsync(uow.Tx, trimmed, gate.InviteToken, gate.SetupToken, token);
                switch (decision)
                {
                    case SignupAdmission.Admitted admission:
                        admitted = admission;
                        break;
                    case SignupAdmission.SetupTokenInvalid:
                        return Failure.SetupTokenInvalid;
                    case SignupAdmission.InvitationNotFound:
                        return Failure.InvitationNotFound;
                    case SignupAdmission.InvitationExpired:
                        return Failure.InvitationExpired;
                    case SignupAdmission.InvitationEmailMismatch:
                        return Failure.InvitationEmailMismatch;
                    case SignupAdmission.OrgNotActive:
                        return Failure.OrgNotActive;
                    case SignupAdmission.Refused:
                        return Failure.SignupClosed;
                    default:
                        throw new InvalidOperationException($"Unknown sign up admission {decision.GetType().Name}.");
                }
            }

            if (await UserRecords.TryInsertAsync(uow, projectId, trimmed, name, verified, token) is not { } userId) return Failure.UserAlreadyExists;

            await InsertPasswordAsync(uow, userId, projectId, hash, token);
            if (admitted is not null) await accountCreated.OnCreatedAsync(uow.Tx, userId, name, trimmed, admitted, token);
            var actor = actorOf(userId);
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserCreated, projectId, actor, userId.ToString(),
                new Dictionary<string, string> { ["userId"] = userId.ToString() },
                fields: new Dictionary<string, string?> { ["method"] = client is null ? null : SessionMethod.SignUp }, ct: token);
            var grant = client is null ? null : await sessions.CreateAsync(uow, projectId, userId, client, actor, SessionMethod.SignUp, token);
            var sent = verification is null ? (VerificationEmail?)null
                : await SendSignUpVerificationAsync(uow, projectId, projectName!, userId, trimmed, name, verification, actor, token);
            return (await ReloadAsync(uow.Db, userId, token), grant, sent);
        }, ct);
    }

    /// <summary>
    /// The verification email of a sign up (spec 0010, AC-11), inside its transaction: the recipient limits, the
    /// token, and the email, behind a savepoint, so a refusal leaves no token and never fails the sign up.
    /// </summary>
    private async Task<VerificationEmail> SendSignUpVerificationAsync(
        AuthUnitOfWork uow, string projectId, string projectName, Guid userId, string email, string? name, RedirectUrl redirect, Actor actor, CancellationToken ct)
    {
        if (mailer.TakeRecipientLimits(projectId, email, EmailTokenKind.Verification) is not null) return VerificationEmail.RateLimited;

        await uow.Tx.SaveAsync("verification_email", ct);
        var target = new AuthEmailTarget(projectId, projectName, userId, email, name);
        if (await mailer.SendLinkAsync(uow, target, EmailTokenKind.Verification, redirect, actor, ct) is { } refused)
        {
            await uow.Tx.RollbackAsync("verification_email", ct);
            return refused.Code == Orvano.Contract.ErrorCode.EmailNotConfigured ? VerificationEmail.NotConfigured : VerificationEmail.RateLimited;
        }

        await uow.Tx.ReleaseAsync("verification_email", ct);
        return VerificationEmail.Queued;
    }

    /// <summary>
    /// Password sign in (AC-4, AC-5). An unknown email is checked against the dummy hash, so a wrong password and an
    /// unknown email cost one Argon2id run each and answer the same. Block status shows only with the right password.
    /// A hash made with older parameters is replaced (rehash on sign in). A user with MFA on gets a challenge in place
    /// of a session (spec 0013, AC-6).
    /// </summary>
    public async Task<Outcome<SignedIn>> SignInAsync(string projectId, string? email, string? password, ClientInfo client, CancellationToken ct)
    {
        var known = EmailRule.TryNormalize(email, out var trimmed);
        var wellFormed = PasswordPolicy.TryNormalize(password, out var normalized);
        var account = known ? await FindByEmailAsync(projectId, trimmed, ct) : null;

        // A password that can't meet the policy can't match either; it still costs one run, against the dummy hash, but
        // is never hashed itself, so an oversized one buys no extra work.
        var check = await hasher.TryVerifyAsync(normalized, wellFormed ? account?.Hash : null, ct);
        if (check is null) return Failure.Busy;
        if (!check.Value.Matches || account is null) return Failure.InvalidCredentials;
        if (account.Status == UserStatuses.Blocked) return Failure.UserBlocked;

        var rehash = check.Value.NeedsRehash ? await hasher.TryHashAsync(normalized, ct) : null;
        await keys.GetActiveAsync(projectId, ct);

        var outcome = await store.WriteAsync<(UserRow? User, SessionGrant? Grant, MfaChallengeView? Mfa)>(async (uow, token) =>
        {
            // First, since it locks the user for a user with MFA on: the user row before the password row.
            var challenge = await MfaGate.ChallengeAsync(uow, projectId, account.Id, SessionMethod.Password, null, client, token);
            if (rehash is not null)
            {
                await using var update = new NpgsqlCommand(
                    "UPDATE orvano.auth_passwords SET hash = @hash, updated_at = now() WHERE user_id = @user", uow.Tx.Connection, uow.Tx);
                update.Parameters.AddWithValue("hash", rehash);
                update.Parameters.AddWithValue("user", account.Id);
                await update.ExecuteNonQueryAsync(token);
            }

            if (challenge is not null) return (null, null, challenge);
            var grant = await sessions.CreateAsync(uow, projectId, account.Id, client, Actor.User(account.Id), SessionMethod.Password, token);
            return (await ReloadAsync(uow.Db, account.Id, token), grant, null);
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        var (user, signedIn, mfa) = outcome.Value;
        return mfa is not null ? SignedIn.Challenged(mfa) : await SignedInAsync(projectId, user!, signedIn!, ct);
    }

    /// <summary>The signed in user (AC-12).</summary>
    public async Task<Outcome<UserRow>> GetAsync(string projectId, Guid userId, CancellationToken ct)
    {
        var user = await store.ReadAsync((db, token) =>
            db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.ProjectId == projectId, token), ct);
        return user is null ? Failure.UserNotFound : user;
    }

    /// <summary>
    /// Changes the name or metadata (AC-13) and writes <c>auth.user.updated</c> with the fields whose value changed.
    /// Nothing changed: no write and no event.
    /// </summary>
    public async Task<Outcome<UserRow>> UpdateAsync(string projectId, Guid userId, AccountChanges changes, CancellationToken ct)
    {
        if (changes.SetName && !UserName.IsValid(changes.Name)) return Failure.Invalid($"The name must be at most {UserName.MaxLength} characters.");
        if (changes.Metadata is { } metadata && !UserMetadata.IsValidSize(metadata))
            return Failure.Invalid($"The metadata must be at most {UserMetadata.MaxBytes / 1024} KB as JSON.");

        return await store.WriteAsync<UserRow>(async (uow, token) =>
        {
            string? name;
            bool sameMetadata;
            await using (var read = new NpgsqlCommand(
                "SELECT name, metadata = @metadata::jsonb FROM orvano.auth_users WHERE id = @id AND project_id = @project FOR UPDATE",
                uow.Tx.Connection, uow.Tx))
            {
                read.Parameters.AddWithValue("metadata", NpgsqlDbType.Jsonb, (object?)changes.Metadata ?? DBNull.Value);
                read.Parameters.AddWithValue("id", userId);
                read.Parameters.AddWithValue("project", projectId);
                await using var reader = await read.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token)) return Failure.UserNotFound;
                name = reader.IsDBNull(0) ? null : reader.GetString(0);
                sameMetadata = reader.IsDBNull(1) || reader.GetBoolean(1);
            }

            var changed = new List<string>();
            if (changes.SetName && !string.Equals(name, changes.Name, StringComparison.Ordinal)) changed.Add("name");
            if (!sameMetadata) changed.Add("metadata");
            if (changed.Count > 0)
            {
                await using var update = new NpgsqlCommand(
                    """
                    UPDATE orvano.auth_users
                    SET name = CASE WHEN @setName THEN @name ELSE name END, metadata = coalesce(@metadata::jsonb, metadata), updated_at = now()
                    WHERE id = @id
                    """, uow.Tx.Connection, uow.Tx);
                update.Parameters.AddWithValue("setName", changed.Contains("name"));
                update.Parameters.AddWithValue("name", NpgsqlDbType.Text, (object?)changes.Name ?? DBNull.Value);
                update.Parameters.AddWithValue("metadata", NpgsqlDbType.Jsonb, sameMetadata ? DBNull.Value : changes.Metadata!);
                update.Parameters.AddWithValue("id", userId);
                await update.ExecuteNonQueryAsync(token);
                await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserUpdated, projectId, Actor.User(userId), userId.ToString(),
                    new Dictionary<string, string> { ["userId"] = userId.ToString() }, changed, ct: token);
            }

            return await ReloadAsync(uow.Db, userId, token);
        }, ct);
    }

    /// <summary>
    /// Changes the password (AC-14): the current one must match, the new one must meet the policy. Every other session
    /// of the user ends (<c>password_changed</c>) in the same transaction; the caller's stays, and the user's live reset
    /// links are deleted (spec 0010, AC-29). A user without a password sets a first one from a session at most 10
    /// minutes old, and any current password they send is ignored (spec 0010, AC-19).
    /// </summary>
    public async Task<Outcome<Done>> UpdatePasswordAsync(string projectId, Guid userId, Guid sessionId, string? currentPassword, string? newPassword, CancellationToken ct)
    {
        if (!PasswordPolicy.TryNormalize(newPassword, out var normalized)) return Failure.InvalidPassword;
        var check = await CheckCredentialAsync(projectId, userId, sessionId, currentPassword, ct);
        if (check.Failure is not null) return check.Failure;
        var verified = check.Value!.Hash;

        var hash = await hasher.TryHashAsync(normalized, ct);
        if (hash is null) return Failure.Busy;

        var outcome = await store.WriteAsync<Guid[]>(async (uow, token) =>
        {
            // The user first, the order a reset redemption takes, so the two can't deadlock over the password row.
            await UserLocks.ByIdAsync(uow, projectId, userId, token);

            // Only over the hash just verified (or none at all): a racing change in between makes this one fail as a
            // wrong password.
            await using (var update = new NpgsqlCommand(
                verified is null
                    ? "INSERT INTO orvano.auth_passwords (user_id, project_id, hash) VALUES (@user, @project, @hash) ON CONFLICT (user_id) DO NOTHING"
                    : "UPDATE orvano.auth_passwords SET hash = @hash, updated_at = now() WHERE user_id = @user AND hash = @verified",
                uow.Tx.Connection, uow.Tx))
            {
                update.Parameters.AddWithValue("hash", hash);
                update.Parameters.AddWithValue("user", userId);
                update.Parameters.AddWithValue("project", projectId);
                update.Parameters.AddWithValue("verified", NpgsqlDbType.Text, (object?)verified ?? DBNull.Value);
                if (await update.ExecuteNonQueryAsync(token) == 0) return Failure.InvalidCredentials;
            }

            var actor = Actor.User(userId);
            var ended = await sessions.EndAllAsync(uow, projectId, userId, SessionEndReason.PasswordChanged, actor, sessionId, token);
            await EmailTokens.DeleteForUserAsync(uow, projectId, userId, EmailTokenKind.Recovery, token);
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.PasswordChanged, projectId, actor, userId.ToString(),
                new Dictionary<string, string> { ["userId"] = userId.ToString() }, ct: token);
            return ended.ToArray();
        }, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        foreach (var id in outcome.Value!) await checks.EvictAsync(id, ct);
        return default(Done);
    }

    /// <summary>
    /// Deletes the user (AC-15) after checking their password: the user, their password, and all their sessions in one
    /// transaction, with <c>auth.user.deleted</c>. Every token of the user then fails at the api.
    /// </summary>
    public async Task<Outcome<Done>> DeleteAsync(string projectId, Guid userId, Guid sessionId, string? password, CancellationToken ct)
    {
        var check = await CheckCredentialAsync(projectId, userId, sessionId, password, ct);
        if (check.Failure is not null) return check.Failure;

        var outcome = await store.WriteAsync<Guid[]>(async (uow, token) =>
            await UserRecords.DeleteAsync(uow, projectId, userId, Actor.User(userId), token) is { } ended ? ended : Failure.UserNotFound, ct);

        if (!outcome.Succeeded) return outcome.Failure!;
        foreach (var id in outcome.Value!) await checks.EvictAsync(id, ct);
        return default(Done);
    }

    /// <summary>The credential a sensitive change was checked with: the verified hash, or null for a user without a password.</summary>
    internal sealed record Credential(string? Hash);

    /// <summary>
    /// Checks the signed in user's credential (spec 0004 AC-14, AC-15; spec 0010 AC-17, AC-19): first, for a user with
    /// MFA on, a second factor on this session within 10 minutes (spec 0013, AC-18: 403
    /// <c>mfa_verification_required</c>). Then a user with a password must send it (missing or wrong: 401
    /// <c>invalid_credentials</c>, one Argon2id run); a user without one must call from a session created at most 10
    /// minutes ago (otherwise 403 <c>reauthentication_required</c>), and any password they send is ignored.
    /// </summary>
    internal async Task<Outcome<Credential>> CheckCredentialAsync(string projectId, Guid userId, Guid sessionId, string? password, CancellationToken ct)
    {
        if (await stepUp.SensitiveChangeAsync(projectId, userId, sessionId, ct) is { } stepUpRefused) return stepUpRefused;
        var hash = await store.ReadAsync((db, token) =>
            db.Passwords.AsNoTracking().Where(p => p.UserId == userId && p.ProjectId == projectId).Select(p => p.Hash).SingleOrDefaultAsync(token), ct);
        if (hash is null)
        {
            var fresh = await store.ReadAsync((db, token) =>
                UserRecords.IsSessionFreshAsync((NpgsqlConnection)db.Database.GetDbConnection(), null, userId, sessionId, token), ct);
            return fresh ? new Credential(null) : Failure.ReauthenticationRequired;
        }

        var wellFormed = PasswordPolicy.TryNormalize(password, out var normalized);
        var check = await hasher.TryVerifyAsync(normalized, wellFormed ? hash : null, ct);
        if (check is null) return Failure.Busy;
        if (!check.Value.Matches) return Failure.InvalidCredentials;
        return new Credential(hash);
    }

    /// <summary>Issues the new session's access token and returns the <c>AuthResult</c> value.</summary>
    public async Task<SignedIn> SignedInAsync(string projectId, UserRow user, SessionGrant grant, CancellationToken ct, bool isNewUser = false)
    {
        var access = await tokens.IssueAsync(projectId, user.Id, grant.SessionId, user.EmailVerifiedAt is not null, grant.Strength, ct);
        return new SignedIn(user, new SessionTokensView(access.Token, access.ExpiresAt, grant.RefreshToken.Value, grant.RefreshTokenExpiresAt, grant.SessionId), isNewUser);
    }

    private sealed record Account(Guid Id, string Status, string? Hash);

    private Task<Account?> FindByEmailAsync(string projectId, string email, CancellationToken ct) =>
        store.ReadAsync(async (db, token) =>
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT u.id, u.status, p.hash
                FROM orvano.auth_users u
                LEFT JOIN orvano.auth_passwords p ON p.user_id = u.id
                WHERE u.project_id = @project AND lower(u.email) = lower(@email)
                """, conn);
            cmd.Parameters.AddWithValue("project", projectId);
            cmd.Parameters.AddWithValue("email", email);
            await using var reader = await cmd.ExecuteReaderAsync(token);
            return await reader.ReadAsync(token)
                ? new Account(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))
                : null;
        }, ct);

    private static async Task InsertPasswordAsync(AuthUnitOfWork uow, Guid userId, string projectId, string hash, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO orvano.auth_passwords (user_id, project_id, hash) VALUES (@user, @project, @hash)", uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("hash", hash);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Task<UserRow> ReloadAsync(AuthDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
}
