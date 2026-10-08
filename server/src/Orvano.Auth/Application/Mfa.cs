using System.Net;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Core.Secrets;

namespace Orvano.Auth.Application;

/// <summary>
/// What a project allows now (spec 0013, AC-1, AC-3): TOTP, and passkeys with their RP ID. A project without an
/// <c>auth_method_settings</c> row reads as the defaults. The <c>console</c> project has no row: TOTP is always on.
/// </summary>
internal sealed record MethodPolicy(bool TotpEnabled, bool PasskeysEnabled, string? RpId)
{
    public static MethodPolicy Defaults { get; } = new(true, false, null);

    /// <summary>The project's policy, read on the caller's connection (and transaction, when given).</summary>
    public static async Task<MethodPolicy> ReadAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string projectId, CancellationToken ct)
    {
        if (projectId == ConsoleProject.Id) return Defaults;
        await using var cmd = new NpgsqlCommand(
            "SELECT totp_enabled, passkeys_enabled, rp_id FROM orvano.auth_method_settings WHERE project_id = @project", conn, tx);
        cmd.Parameters.AddWithValue("project", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new MethodPolicy(reader.GetBoolean(0), reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetString(2))
            : Defaults;
    }
}

/// <summary>An MFA challenge as the API returns it (the contract's <c>MfaChallenge</c>).</summary>
internal sealed record MfaChallengeView(string Ticket, IReadOnlyList<string> Factors, DateTimeOffset ExpiresAt);

/// <summary>A user's second factor state, read without locks (spec 0013, AC-5, AC-7, AC-16).</summary>
internal sealed record MfaFactorState(DateTimeOffset? TotpConfirmedAt, int RecoveryCodesRemaining, int ActivePasskeys, MethodPolicy Policy)
{
    /// <summary>AC-5: a confirmed TOTP factor while the project allows TOTP. A passkey alone never turns MFA on.</summary>
    public bool MfaEnabled => TotpConfirmedAt is not null && Policy.TotpEnabled;

    /// <summary>AC-7's list, in its order, each present only when usable now.</summary>
    public IReadOnlyList<string> Factors
    {
        get
        {
            var factors = new List<string>();
            if (MfaEnabled) factors.Add(MfaFactors.Totp);
            if (MfaEnabled && RecoveryCodesRemaining > 0) factors.Add(MfaFactors.RecoveryCode);
            if (Policy.PasskeysEnabled && ActivePasskeys > 0) factors.Add(MfaFactors.Passkey);
            return factors;
        }
    }

    public static async Task<MfaFactorState> ReadAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string projectId, Guid userId, CancellationToken ct)
    {
        var policy = await MethodPolicy.ReadAsync(conn, tx, projectId, ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT (SELECT confirmed_at FROM orvano.auth_totp_factors WHERE user_id = @user AND confirmed_at IS NOT NULL),
                   (SELECT count(*)::int FROM orvano.auth_recovery_codes WHERE user_id = @user AND used_at IS NULL),
                   (SELECT count(*)::int FROM orvano.auth_passkeys WHERE user_id = @user AND rp_id = @rpId)
            """, conn, tx);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("rpId", NpgsqlDbType.Text, (object?)policy.RpId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new MfaFactorState(
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0), reader.GetInt32(1), reader.GetInt32(2), policy);
    }
}

/// <summary>
/// The step one gate (spec 0013, AC-6, AC-7): for a user with MFA on, a sign in creates a ticket in place of a
/// session. Runs inside the sign in's transaction, after its own checks, and locks the user before the ticket rows.
/// </summary>
internal static class MfaGate
{
    /// <summary>
    /// The challenge to answer instead of a session, or null when the user has MFA off and the sign in goes on.
    /// Keeps the user's live tickets at 5 by deleting the oldest, and drops their expired ones.
    /// </summary>
    public static async Task<MfaChallengeView?> ChallengeAsync(
        AuthUnitOfWork uow, string projectId, Guid userId, string method, string? provider, ClientInfo client, CancellationToken ct,
        string? pendingPasswordHash = null)
    {
        var conn = uow.Tx.Connection!;
        if (!(await MfaFactorState.ReadAsync(conn, uow.Tx, projectId, userId, ct)).MfaEnabled) return null;

        await UserLocks.ByIdAsync(uow, projectId, userId, ct);
        var state = await MfaFactorState.ReadAsync(conn, uow.Tx, projectId, userId, ct);
        if (!state.MfaEnabled) return null;

        await using (var prune = new NpgsqlCommand(
            """
            DELETE FROM orvano.auth_mfa_tickets
            WHERE user_id = @user AND (expires_at <= now() OR id IN (
                SELECT id FROM orvano.auth_mfa_tickets WHERE user_id = @user AND expires_at > now()
                ORDER BY created_at DESC, id DESC OFFSET @keep))
            """, conn, uow.Tx))
        {
            prune.Parameters.AddWithValue("user", userId);
            prune.Parameters.AddWithValue("keep", AuthTimings.MfaTicketsPerUser - 1);
            await prune.ExecuteNonQueryAsync(ct);
        }

        var ticket = MfaTicket.New();
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO orvano.auth_mfa_tickets (
                project_id, user_id, ticket_hash, method, pending_password_hash, provider, user_agent, sdk, ip, expires_at)
            VALUES (@project, @user, @hash, @method, @pending, @provider, @agent, @sdk, @ip, now() + @lifetime)
            RETURNING expires_at
            """, conn, uow.Tx);
        insert.Parameters.AddWithValue("project", projectId);
        insert.Parameters.AddWithValue("user", userId);
        insert.Parameters.AddWithValue("hash", ticket.Hash);
        insert.Parameters.AddWithValue("method", method);
        insert.Parameters.AddWithValue("pending", NpgsqlDbType.Text, (object?)pendingPasswordHash ?? DBNull.Value);
        insert.Parameters.AddWithValue("provider", NpgsqlDbType.Text, (object?)provider ?? DBNull.Value);
        insert.Parameters.AddWithValue("agent", NpgsqlDbType.Text, (object?)client.UserAgent ?? DBNull.Value);
        insert.Parameters.AddWithValue("sdk", NpgsqlDbType.Text, (object?)client.Sdk ?? DBNull.Value);
        insert.Parameters.AddWithValue("ip", NpgsqlDbType.Inet, (object?)client.Ip ?? DBNull.Value);
        insert.Parameters.AddWithValue("lifetime", AuthTimings.MfaTicket);
        var expiresAt = (DateTime)(await insert.ExecuteScalarAsync(ct))!;
        return new MfaChallengeView(ticket.Value, state.Factors, new DateTimeOffset(expiresAt, TimeSpan.Zero));
    }
}

/// <summary>A live MFA ticket row, as step two reads it.</summary>
internal sealed record TicketRow(
    Guid Id, Guid UserId, string Method, string? Provider, string? PendingPasswordHash, ClientInfo Client, short Attempts);

/// <summary>
/// The TOTP factor and recovery code rows (spec 0013, AC-9, AC-10, AC-34): the secret only sealed, codes only as
/// <c>SecretBox.Mac</c> tags, and every use a conditional update, so a code works once even under parallel requests.
/// </summary>
internal sealed class MfaFactorStore(SecretBox secrets, TimeProvider clock)
{
    public const string TotpTable = "auth_totp_factors";
    public const string TotpSecretColumn = "secret_ciphertext";

    public byte[] SealSecret(Guid userId, ReadOnlySpan<byte> secret) =>
        secrets.Encrypt(secret, SecretBox.AssociatedData(TotpTable, userId.ToString(), TotpSecretColumn));

    private byte[] OpenSecret(Guid userId, byte[] ciphertext) =>
        secrets.Decrypt(ciphertext, SecretBox.AssociatedData(TotpTable, userId.ToString(), TotpSecretColumn));

    /// <summary>
    /// Checks <paramref name="code"/> against the user's confirmed factor and, when it matches a step newer than the
    /// last used one, records that step. False for a wrong code, a reused step, or no confirmed factor.
    /// </summary>
    public async Task<bool> UseTotpAsync(AuthUnitOfWork uow, Guid userId, string? code, CancellationToken ct)
    {
        byte[] ciphertext;
        long? lastUsed;
        await using (var read = new NpgsqlCommand(
            "SELECT secret_ciphertext, last_used_step FROM orvano.auth_totp_factors WHERE user_id = @user AND confirmed_at IS NOT NULL",
            uow.Tx.Connection, uow.Tx))
        {
            read.Parameters.AddWithValue("user", userId);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return false;
            ciphertext = reader.GetFieldValue<byte[]>(0);
            lastUsed = reader.IsDBNull(1) ? null : reader.GetInt64(1);
        }

        if (Totp.Match(OpenSecret(userId, ciphertext), code, clock.GetUtcNow(), lastUsed) is not { } step) return false;

        await using var use = new NpgsqlCommand(
            """
            UPDATE orvano.auth_totp_factors SET last_used_step = @step, updated_at = now()
            WHERE user_id = @user AND confirmed_at IS NOT NULL AND (last_used_step IS NULL OR last_used_step < @step)
            """, uow.Tx.Connection, uow.Tx);
        use.Parameters.AddWithValue("step", step);
        use.Parameters.AddWithValue("user", userId);
        return await use.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>
    /// Confirms the user's pending factor with its first code (AC-13): false when the code does not match. The caller
    /// has checked that the pending row exists and is at most 15 minutes old.
    /// </summary>
    public async Task<bool> ConfirmTotpAsync(AuthUnitOfWork uow, Guid userId, byte[] ciphertext, string? code, CancellationToken ct)
    {
        if (Totp.Match(OpenSecret(userId, ciphertext), code, clock.GetUtcNow(), lastUsedStep: null) is not { } step) return false;

        await using var confirm = new NpgsqlCommand(
            """
            UPDATE orvano.auth_totp_factors SET confirmed_at = now(), last_used_step = @step, updated_at = now()
            WHERE user_id = @user AND confirmed_at IS NULL
            """, uow.Tx.Connection, uow.Tx);
        confirm.Parameters.AddWithValue("step", step);
        confirm.Parameters.AddWithValue("user", userId);
        return await confirm.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>Replaces every recovery code of the user with 10 new ones and returns them in display form (AC-13, AC-15).</summary>
    public async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(AuthUnitOfWork uow, string projectId, Guid userId, CancellationToken ct)
    {
        await using (var delete = new NpgsqlCommand("DELETE FROM orvano.auth_recovery_codes WHERE user_id = @user", uow.Tx.Connection, uow.Tx))
        {
            delete.Parameters.AddWithValue("user", userId);
            await delete.ExecuteNonQueryAsync(ct);
        }

        var codes = new List<string>(RecoveryCode.Count);
        var macs = new byte[RecoveryCode.Count][];
        var keyIds = new string[RecoveryCode.Count];
        for (var i = 0; i < RecoveryCode.Count; i++)
        {
            var code = RecoveryCode.New();
            var tag = secrets.Mac(RecoveryCode.MacPurpose(userId), RecoveryCode.MacInput(code));
            codes.Add(RecoveryCode.Display(code));
            macs[i] = tag.Tag;
            keyIds[i] = tag.KeyId;
        }

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO orvano.auth_recovery_codes (project_id, user_id, code_mac, mac_key_id)
            SELECT @project, @user, mac, key_id FROM unnest(@macs, @keyIds) AS c(mac, key_id)
            """, uow.Tx.Connection, uow.Tx);
        insert.Parameters.AddWithValue("project", projectId);
        insert.Parameters.AddWithValue("user", userId);
        insert.Parameters.AddWithValue("macs", NpgsqlDbType.Array | NpgsqlDbType.Bytea, macs);
        insert.Parameters.AddWithValue("keyIds", NpgsqlDbType.Array | NpgsqlDbType.Text, keyIds);
        await insert.ExecuteNonQueryAsync(ct);
        return codes;
    }

    /// <summary>
    /// Uses one of the user's unused recovery codes (AC-10): each row is tested under its own key ID, and the match is
    /// marked used with one conditional update, so two parallel uses give one success. False when none matches.
    /// </summary>
    public async Task<bool> UseRecoveryCodeAsync(AuthUnitOfWork uow, Guid userId, string? input, CancellationToken ct)
    {
        if (!RecoveryCode.TryNormalize(input, out var normalized)) return false;

        var rows = new List<(Guid Id, byte[] Mac, string KeyId)>();
        await using (var read = new NpgsqlCommand(
            "SELECT id, code_mac, mac_key_id FROM orvano.auth_recovery_codes WHERE user_id = @user AND used_at IS NULL", uow.Tx.Connection, uow.Tx))
        {
            read.Parameters.AddWithValue("user", userId);
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rows.Add((reader.GetGuid(0), reader.GetFieldValue<byte[]>(1), reader.GetString(2)));
        }

        var purpose = RecoveryCode.MacPurpose(userId);
        var data = RecoveryCode.MacInput(normalized);
        Guid? match = null;
        foreach (var row in rows)
        {
            if (secrets.VerifyMac(row.KeyId, purpose, data, row.Mac) && match is null) match = row.Id;
        }

        if (match is not { } id) return false;
        await using var use = new NpgsqlCommand(
            "UPDATE orvano.auth_recovery_codes SET used_at = now() WHERE id = @id AND used_at IS NULL", uow.Tx.Connection, uow.Tx);
        use.Parameters.AddWithValue("id", id);
        return await use.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>
    /// Deletes the user's TOTP factor, pending or confirmed, and every recovery code (AC-14, AC-27, AC-29). The caller
    /// holds the user lock. True when a confirmed factor was there, so MFA was on.
    /// </summary>
    public static async Task<bool> DeleteFactorsAsync(AuthUnitOfWork uow, Guid userId, CancellationToken ct)
    {
        await using var delete = new NpgsqlCommand(
            """
            WITH codes AS (DELETE FROM orvano.auth_recovery_codes WHERE user_id = @user)
            DELETE FROM orvano.auth_totp_factors WHERE user_id = @user RETURNING confirmed_at IS NOT NULL
            """, uow.Tx.Connection, uow.Tx);
        delete.Parameters.AddWithValue("user", userId);
        return await delete.ExecuteScalarAsync(ct) is true;
    }

    /// <summary>Deletes every passkey of the user (AC-28, AC-29) and answers their IDs. The caller holds the user lock.</summary>
    public static async Task<Guid[]> DeletePasskeysAsync(AuthUnitOfWork uow, Guid userId, CancellationToken ct)
    {
        await using var delete = new NpgsqlCommand(
            "DELETE FROM orvano.auth_passkeys WHERE user_id = @user RETURNING id", uow.Tx.Connection, uow.Tx);
        delete.Parameters.AddWithValue("user", userId);
        var ids = new List<Guid>();
        await using var reader = await delete.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return [.. ids];
    }

    /// <summary>Reads a live ticket by (project, SHA-256), optionally locked; null when there is none or it expired.</summary>
    public static async Task<TicketRow?> ReadTicketAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string projectId, byte[] hash, bool lockRow, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT id, user_id, method, provider, pending_password_hash, user_agent, sdk, ip, attempts
            FROM orvano.auth_mfa_tickets
            WHERE project_id = @project AND ticket_hash = @hash AND expires_at > now()
            {(lockRow ? "FOR UPDATE" : "")}
            """, conn, tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("hash", hash);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new TicketRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            new ClientInfo(
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<IPAddress>(7)),
            reader.GetInt16(8));
    }
}
