using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Core.Secrets;

namespace Orvano.Auth.Application;

/// <summary>A token row a redemption just deleted. <see cref="Expired"/> is decided by the database clock.</summary>
internal sealed record ConsumedToken(Guid Id, Guid? UserId, string Email, bool Expired);

/// <summary>A live email code row, locked for the check (AC-5).</summary>
internal sealed record CodeRow(Guid Id, Guid? UserId, string Email, byte[] SecretHash, string MacKeyId);

/// <summary>
/// The <c>auth_email_tokens</c> rows (spec 0010), in raw Npgsql inside the caller's unit of work. Only the Auth module
/// reads or writes them. Creating replaces the older token of the same kind (AC-4); redeeming is one conditional
/// <c>DELETE ... RETURNING</c>, so only a returned row counts and a token works at most once (AC-3).
/// </summary>
internal sealed class EmailTokens(SecretBox secrets)
{
    /// <summary>Creates a link token for <paramref name="email"/>, replacing older ones (AC-4), and returns it.</summary>
    public async Task<LinkToken> CreateLinkAsync(AuthUnitOfWork uow, string projectId, EmailTokenKind kind, Guid? userId, string email, CancellationToken ct)
    {
        if (kind == EmailTokenKind.EmailCode) throw new ArgumentOutOfRangeException(nameof(kind), kind, "An email code is not a link.");
        var token = LinkToken.New();
        await UpsertAsync(uow, Guid.CreateVersion7(), projectId, kind, userId, email, token.Hash, macKeyId: null, ct);
        return token;
    }

    /// <summary>Creates an email code for <paramref name="email"/>, replacing an older one (AC-4), and returns the code.</summary>
    public async Task<string> CreateCodeAsync(AuthUnitOfWork uow, string projectId, Guid? userId, string email, CancellationToken ct)
    {
        // Set in code: the row ID is part of the HMAC input.
        var id = Guid.CreateVersion7();
        var code = EmailCode.New();
        var mac = secrets.Mac(EmailCode.MacPurpose, EmailCode.MacInput(id, code));
        await UpsertAsync(uow, id, projectId, EmailTokenKind.EmailCode, userId, email, mac.Tag, mac.KeyId, ct);
        return code;
    }

    /// <summary>Whether <paramref name="code"/> is the one <paramref name="row"/> was made for, in fixed time.</summary>
    public bool CodeMatches(CodeRow row, string code) =>
        secrets.VerifyMac(row.MacKeyId, EmailCode.MacPurpose, EmailCode.MacInput(row.Id, code), row.SecretHash);

    /// <summary>
    /// Deletes the link token and returns its row, or null when no live row has that hash. The caller fails an
    /// <see cref="ConsumedToken.Expired"/> row, which rolls the delete back; the sweep removes it later.
    /// </summary>
    /// <remarks>
    /// Locks the token's user first (by ID, or by email for an unknown email's token), the order every send and
    /// every other user write takes, so a redemption racing a new send for the same user can't deadlock.
    /// </remarks>
    public static async Task<ConsumedToken?> ConsumeLinkAsync(AuthUnitOfWork uow, string projectId, EmailTokenKind kind, LinkToken token, CancellationToken ct)
    {
        if (!await LockOwnerAsync(uow, projectId, kind, token, ct)) return null;

        // kind <> 'email_code' lets the planner use the partial unique index on secret_hash.
        await using var cmd = new NpgsqlCommand(
            """
            DELETE FROM orvano.auth_email_tokens
            WHERE project_id = @project AND kind = @kind AND secret_hash = @hash AND kind <> 'email_code'
            RETURNING id, user_id, email, expires_at <= now()
            """, uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("kind", EmailTokenKinds.Wire(kind));
        cmd.Parameters.AddWithValue("hash", token.Hash);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new ConsumedToken(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetBoolean(3));
    }

    /// <summary>Locks the user a link token belongs to (by ID, or by email for an unknown email); false when no row has that hash.</summary>
    private static async Task<bool> LockOwnerAsync(AuthUnitOfWork uow, string projectId, EmailTokenKind kind, LinkToken token, CancellationToken ct)
    {
        Guid? owner;
        string email;
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT user_id, email FROM orvano.auth_email_tokens
            WHERE project_id = @project AND kind = @kind AND secret_hash = @hash AND kind <> 'email_code'
            """, uow.Tx.Connection, uow.Tx))
        {
            cmd.Parameters.AddWithValue("project", projectId);
            cmd.Parameters.AddWithValue("kind", EmailTokenKinds.Wire(kind));
            cmd.Parameters.AddWithValue("hash", token.Hash);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return false;
            owner = reader.IsDBNull(0) ? null : reader.GetGuid(0);
            email = reader.GetString(1);
        }

        _ = owner is { } id ? await UserLocks.ByIdAsync(uow, projectId, id, ct) : await UserLocks.ByEmailAsync(uow, projectId, email, ct);
        return true;
    }

    /// <summary>Whether a live, unexpired link token has this hash, read without a lock (AC-10's early check).</summary>
    public static async Task<bool> IsLiveAsync(NpgsqlConnection conn, string projectId, EmailTokenKind kind, LinkToken token, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM orvano.auth_email_tokens
                WHERE project_id = @project AND kind = @kind AND secret_hash = @hash AND kind <> 'email_code' AND expires_at > now())
            """, conn);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("kind", EmailTokenKinds.Wire(kind));
        cmd.Parameters.AddWithValue("hash", token.Hash);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// The project's unexpired email code rows for <paramref name="email"/>, locked <c>FOR UPDATE</c> so parallel
    /// guesses take turns (AC-5). An expired row is left out, so it counts no attempt.
    /// </summary>
    public static async Task<IReadOnlyList<CodeRow>> LockCodesAsync(AuthUnitOfWork uow, string projectId, string email, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, user_id, email, secret_hash, mac_key_id
            FROM orvano.auth_email_tokens
            WHERE project_id = @project AND lower(email) = lower(@email) AND kind = 'email_code' AND expires_at > now()
            ORDER BY created_at DESC
            FOR UPDATE
            """, uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("email", email);
        var rows = new List<CodeRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new CodeRow(
                reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetFieldValue<byte[]>(3), reader.GetString(4)));
        }

        return rows;
    }

    /// <summary>Deletes one row by ID; false when it is already gone.</summary>
    public static async Task<bool> DeleteAsync(AuthUnitOfWork uow, Guid id, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("DELETE FROM orvano.auth_email_tokens WHERE id = @id", uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>
    /// A wrong code (AC-5): adds 1 to <c>attempts</c> on every given row, and deletes the rows that reached
    /// <see cref="EmailCode.MaxAttempts"/>.
    /// </summary>
    public static async Task CountWrongAttemptAsync(AuthUnitOfWork uow, IReadOnlyList<CodeRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        // Two statements: one statement can't both update and delete a row.
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE orvano.auth_email_tokens SET attempts = attempts + 1 WHERE id = ANY(@ids);
            DELETE FROM orvano.auth_email_tokens WHERE id = ANY(@ids) AND attempts >= @max;
            """, uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("ids", rows.Select(r => r.Id).ToArray());
        cmd.Parameters.AddWithValue("max", (short)EmailCode.MaxAttempts);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deletes the user's live tokens: all of them, or one <paramref name="kind"/> (a password change deletes
    /// <c>recovery</c> only, AC-29). <paramref name="except"/> keeps one row.
    /// </summary>
    public static async Task DeleteForUserAsync(AuthUnitOfWork uow, string projectId, Guid userId, EmailTokenKind? kind, CancellationToken ct, Guid? except = null)
    {
        await using var cmd = new NpgsqlCommand(
            """
            DELETE FROM orvano.auth_email_tokens
            WHERE project_id = @project AND user_id = @user AND (@kind::text IS NULL OR kind = @kind) AND id IS DISTINCT FROM @except
            """, uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("kind", NpgsqlDbType.Text, kind is { } k ? EmailTokenKinds.Wire(k) : DBNull.Value);
        cmd.Parameters.AddWithValue("except", NpgsqlDbType.Uuid, (object?)except ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// AC-4: deletes the project's rows of the same kind for the same user, and those for the same unknown email,
    /// then inserts. The insert updates on a conflict with the matching partial unique index, so two racing requests
    /// both succeed and the later one wins.
    /// </summary>
    private static async Task UpsertAsync(
        AuthUnitOfWork uow, Guid id, string projectId, EmailTokenKind kind, Guid? userId, string email, byte[] hash, string? macKeyId, CancellationToken ct)
    {
        var arbiter = userId is null
            ? "(project_id, kind, lower(email)) WHERE user_id IS NULL"
            : "(project_id, kind, user_id) WHERE user_id IS NOT NULL";
        await using var cmd = new NpgsqlCommand(
            $"""
            DELETE FROM orvano.auth_email_tokens
            WHERE project_id = @project AND kind = @kind AND (user_id = @user OR (user_id IS NULL AND lower(email) = lower(@email)));
            INSERT INTO orvano.auth_email_tokens (id, project_id, kind, user_id, email, secret_hash, mac_key_id, expires_at)
            VALUES (@id, @project, @kind, @user, @email, @hash, @mac, now() + @lifetime)
            ON CONFLICT {arbiter} DO UPDATE
            SET id = excluded.id, email = excluded.email, secret_hash = excluded.secret_hash, mac_key_id = excluded.mac_key_id,
                attempts = 0, expires_at = excluded.expires_at, created_at = excluded.created_at;
            """, uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("kind", EmailTokenKinds.Wire(kind));
        cmd.Parameters.AddWithValue("user", NpgsqlDbType.Uuid, (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("mac", NpgsqlDbType.Text, (object?)macKeyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lifetime", EmailTokenKinds.Lifetime(kind));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
