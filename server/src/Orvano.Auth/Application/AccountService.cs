using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Data;
using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>A session's tokens as the API returns them (the contract's <c>SessionTokens</c>).</summary>
internal sealed record SessionTokensView(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, Guid SessionId);

/// <summary>A signed in user and their new session (the contract's <c>AuthResult</c>).</summary>
internal sealed record SignedIn(UserRow User, SessionTokensView Session);

/// <summary>
/// The signed in user's own account (spec 0004, <c>account</c> service): sign up, sign in, and the current user. Use
/// cases know no HTTP; they return an <see cref="Outcome{T}"/>.
/// </summary>
internal sealed class AccountService(AuthStore store, PasswordHasher hasher, Sessions sessions, AccessTokens tokens, SigningKeys keys)
{
    public const string EmailIndex = "auth_users_email_key";

    /// <summary>
    /// Sign up (AC-1 to AC-3): creates the user, their password row, and a session in one transaction. The password is
    /// hashed before the transaction opens, so no connection waits on Argon2id.
    /// </summary>
    public async Task<Outcome<SignedIn>> SignUpAsync(string projectId, string? email, string? password, string? name, ClientInfo client, CancellationToken ct)
    {
        if (!EmailRule.TryNormalize(email, out var trimmed)) return Failure.Invalid("The email must be an address of at most 320 characters.");
        if (!UserName.IsValid(name)) return Failure.Invalid($"The name must be at most {UserName.MaxLength} characters.");
        if (!PasswordPolicy.TryNormalize(password, out var normalized)) return Failure.InvalidPassword;

        var hash = await hasher.TryHashAsync(normalized, ct);
        if (hash is null) return Failure.Busy;
        await keys.GetActiveAsync(projectId, ct); // the first token of a project creates its key, before any commit

        var outcome = await store.WriteAsync<(UserRow User, SessionGrant Grant)>(async (uow, token) =>
        {
            if (await InsertUserAsync(uow, projectId, trimmed, name, token) is not { } userId) return Failure.UserAlreadyExists;

            await InsertPasswordAsync(uow, userId, projectId, hash, token);
            var actor = Actor.User(userId);
            await AuthEvents.WriteAsync(uow.Tx, AuthEvents.UserCreated, projectId, actor, userId.ToString(),
                new Dictionary<string, string> { ["userId"] = userId.ToString() }, ct: token);
            var grant = await sessions.CreateAsync(uow, projectId, userId, client, actor, token);
            return (await ReloadAsync(uow.Db, userId, token), grant);
        }, ct);

        return outcome.Succeeded ? await SignedInAsync(projectId, outcome.Value.User, outcome.Value.Grant, ct) : outcome.Failure!;
    }

    /// <summary>
    /// Password sign in (AC-4, AC-5). An unknown email is checked against the dummy hash, so a wrong password and an
    /// unknown email cost one Argon2id run each and answer the same. Block status shows only with the right password.
    /// A hash made with older parameters is replaced (rehash on sign in).
    /// </summary>
    public async Task<Outcome<SignedIn>> SignInAsync(string projectId, string? email, string? password, ClientInfo client, CancellationToken ct)
    {
        var known = EmailRule.TryNormalize(email, out var trimmed);
        var wellFormed = PasswordPolicy.TryNormalize(password, out var normalized);
        var account = known ? await FindByEmailAsync(projectId, trimmed, ct) : null;

        // A password that can't meet the policy can't match either; it is still checked, against the dummy hash.
        var check = await hasher.TryVerifyAsync(wellFormed ? normalized : password ?? "", wellFormed ? account?.Hash : null, ct);
        if (check is null) return Failure.Busy;
        if (!check.Value.Matches || account is null) return Failure.InvalidCredentials;
        if (account.Status == UserStatuses.Blocked) return Failure.UserBlocked;

        var rehash = check.Value.NeedsRehash ? await hasher.TryHashAsync(normalized, ct) : null;
        await keys.GetActiveAsync(projectId, ct);

        var outcome = await store.WriteAsync<(UserRow User, SessionGrant Grant)>(async (uow, token) =>
        {
            if (rehash is not null)
            {
                await using var update = new NpgsqlCommand(
                    "UPDATE orvano.auth_passwords SET hash = @hash, updated_at = now() WHERE user_id = @user", uow.Tx.Connection, uow.Tx);
                update.Parameters.AddWithValue("hash", rehash);
                update.Parameters.AddWithValue("user", account.Id);
                await update.ExecuteNonQueryAsync(token);
            }

            var grant = await sessions.CreateAsync(uow, projectId, account.Id, client, Actor.User(account.Id), token);
            return (await ReloadAsync(uow.Db, account.Id, token), grant);
        }, ct);

        return outcome.Succeeded ? await SignedInAsync(projectId, outcome.Value.User, outcome.Value.Grant, ct) : outcome.Failure!;
    }

    /// <summary>The signed in user (AC-12).</summary>
    public async Task<Outcome<UserRow>> GetAsync(string projectId, Guid userId, CancellationToken ct)
    {
        var user = await store.ReadAsync((db, token) =>
            db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.ProjectId == projectId, token), ct);
        return user is null ? Failure.UserNotFound : user;
    }

    private async Task<SignedIn> SignedInAsync(string projectId, UserRow user, SessionGrant grant, CancellationToken ct)
    {
        var access = await tokens.IssueAsync(projectId, user.Id, grant.SessionId, ct);
        return new SignedIn(user, new SessionTokensView(access.Token, access.ExpiresAt, grant.RefreshToken.Value, grant.RefreshTokenExpiresAt, grant.SessionId));
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

    /// <summary>
    /// Inserts the user and returns their ID, or null when the email is taken in the project: the unique index on
    /// (<c>project_id</c>, <c>lower(email)</c>) decides between racing sign ups (AC-3). The savepoint keeps the
    /// transaction usable after the violation.
    /// </summary>
    private static async Task<Guid?> InsertUserAsync(AuthUnitOfWork uow, string projectId, string email, string? name, CancellationToken ct)
    {
        await uow.Tx.SaveAsync("insert_user", ct);
        try
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO orvano.auth_users (project_id, email, name) VALUES (@project, @email, @name) RETURNING id", uow.Tx.Connection, uow.Tx);
            cmd.Parameters.AddWithValue("project", projectId);
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("name", NpgsqlDbType.Text, (object?)name ?? DBNull.Value);
            var id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
            await uow.Tx.ReleaseAsync("insert_user", ct);
            return id;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == EmailIndex)
        {
            await uow.Tx.RollbackAsync("insert_user", ct);
            return null;
        }
    }

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
