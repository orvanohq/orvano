using System.Net;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Core.Secrets;

namespace Orvano.Auth.Application;

/// <summary>
/// Where a request came from, recorded on its session (AC-31): the user agent (at most 512 characters), the SDK (at most
/// 100), and the IP address. Shown only; never used for limits or any security check.
/// </summary>
internal sealed record ClientInfo(string? UserAgent, string? Sdk, IPAddress? Ip)
{
    public const int MaxUserAgent = 512;
    public const int MaxSdk = 100;

    public static ClientInfo Of(string? userAgent, string? sdk, IPAddress? ip) =>
        new(Cut(userAgent, MaxUserAgent), Cut(sdk, MaxSdk), ip);

    // Never ends on the first half of a surrogate pair: a lone surrogate is not text, and Postgres refuses it.
    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= max ? value
        : char.IsHighSurrogate(value[max - 1]) ? value[..(max - 1)] : value[..max];
}

/// <summary>A new or rotated session and its refresh token, before the access token is issued.</summary>
internal sealed record SessionGrant(Guid SessionId, RefreshToken RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Creates and ends session rows inside the caller's transaction (spec 0004, data model, state transitions, AC-31).</summary>
internal sealed class Sessions(SecretBox secrets)
{
    public const string Table = "auth_sessions";
    public const string RefreshColumn = "refresh_ciphertext";

    /// <summary>
    /// Inserts a session for the user, sets their <c>last_sign_in_at</c>, and writes <c>auth.session.created</c> with
    /// the <paramref name="method"/> (one of <see cref="SessionMethod"/>), plus the <paramref name="provider"/> of an
    /// <c>oauth</c> or <c>id_token</c> session (spec 0012, AC-18). The session ID comes from Postgres' <c>uuidv7()</c>
    /// first, since the encrypted refresh token is bound to it.
    /// </summary>
    public async Task<SessionGrant> CreateAsync(
        AuthUnitOfWork uow, string projectId, Guid userId, ClientInfo client, Actor actor, string method, CancellationToken ct, string? provider = null)
    {
        var conn = uow.Tx.Connection!;
        Guid sessionId;
        await using (var id = new NpgsqlCommand("SELECT uuidv7()", conn, uow.Tx))
        {
            sessionId = (Guid)(await id.ExecuteScalarAsync(ct))!;
        }

        var token = RefreshToken.New(sessionId);
        await using var insert = new NpgsqlCommand(
            """
            WITH created AS (
                INSERT INTO orvano.auth_sessions (
                    id, project_id, user_id, refresh_hash, refresh_ciphertext, user_agent, sdk, ip_created, ip_last,
                    created_at, last_refreshed_at, idle_expires_at, expires_at, method, provider)
                VALUES (
                    @id, @project, @user, @hash, @ciphertext, @agent, @sdk, @ip, @ip,
                    now(), now(), least(now() + @idle, now() + @absolute), now() + @absolute, @method, @provider)
                RETURNING idle_expires_at, expires_at),
            signed_in AS (
                UPDATE orvano.auth_users SET last_sign_in_at = now() WHERE id = @user)
            SELECT least(idle_expires_at, expires_at) FROM created
            """, conn, uow.Tx);
        insert.Parameters.AddWithValue("id", sessionId);
        insert.Parameters.AddWithValue("project", projectId);
        insert.Parameters.AddWithValue("user", userId);
        insert.Parameters.AddWithValue("hash", token.SecretHash);
        insert.Parameters.AddWithValue("ciphertext", Seal(token, sessionId));
        insert.Parameters.AddWithValue("agent", NpgsqlDbType.Text, (object?)client.UserAgent ?? DBNull.Value);
        insert.Parameters.AddWithValue("sdk", NpgsqlDbType.Text, (object?)client.Sdk ?? DBNull.Value);
        insert.Parameters.AddWithValue("ip", NpgsqlDbType.Inet, (object?)client.Ip ?? DBNull.Value);
        insert.Parameters.AddWithValue("idle", AuthTimings.IdleExpiry);
        insert.Parameters.AddWithValue("absolute", AuthTimings.AbsoluteExpiry);
        insert.Parameters.AddWithValue("method", method);
        insert.Parameters.AddWithValue("provider", NpgsqlDbType.Text, (object?)provider ?? DBNull.Value);
        var endsAt = (DateTime)(await insert.ExecuteScalarAsync(ct))!;

        await AuthEvents.WriteAsync(uow.Tx, AuthEvents.SessionCreated, projectId, actor, userId.ToString(),
            new Dictionary<string, string> { ["userId"] = userId.ToString(), ["sessionId"] = sessionId.ToString() },
            fields: MethodFields(method, provider), ct: ct);
        return new SessionGrant(sessionId, token, new DateTimeOffset(endsAt, TimeSpan.Zero));
    }

    /// <summary>The <c>method</c> of a created event, and the <c>provider</c> when there is one (spec 0012, AC-18).</summary>
    public static Dictionary<string, string?> MethodFields(string method, string? provider) =>
        provider is null ? new() { ["method"] = method } : new() { ["method"] = method, ["provider"] = provider };

    /// <summary>
    /// Ends one session of the user, if it is still open: one conditional <c>UPDATE</c>, so two racing ends write one
    /// <c>auth.session.ended</c>. False when the session is not the user's or has already ended. Evict it from
    /// <see cref="SessionChecks"/> after the commit.
    /// </summary>
    public async Task<bool> EndAsync(AuthUnitOfWork uow, string projectId, Guid userId, Guid sessionId, string reason, Actor actor, CancellationToken ct)
    {
        await using var end = new NpgsqlCommand(
            """
            UPDATE orvano.auth_sessions SET ended_at = now(), end_reason = @reason
            WHERE id = @id AND user_id = @user AND project_id = @project AND ended_at IS NULL
            """, uow.Tx.Connection, uow.Tx);
        end.Parameters.AddWithValue("reason", reason);
        end.Parameters.AddWithValue("id", sessionId);
        end.Parameters.AddWithValue("user", userId);
        end.Parameters.AddWithValue("project", projectId);
        if (await end.ExecuteNonQueryAsync(ct) == 0) return false;

        await WriteEndedAsync(uow, projectId, userId, sessionId, reason, actor, ct);
        return true;
    }

    /// <summary>
    /// Ends every open session of the user except <paramref name="keep"/>, each with its own
    /// <c>auth.session.ended</c>, and returns the ended IDs to evict after the commit.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> EndAllAsync(AuthUnitOfWork uow, string projectId, Guid userId, string reason, Actor actor, Guid? keep, CancellationToken ct)
    {
        var ended = new List<Guid>();
        await using (var end = new NpgsqlCommand(
            """
            UPDATE orvano.auth_sessions SET ended_at = now(), end_reason = @reason
            WHERE user_id = @user AND project_id = @project AND ended_at IS NULL AND id IS DISTINCT FROM @keep
            RETURNING id
            """, uow.Tx.Connection, uow.Tx))
        {
            end.Parameters.AddWithValue("reason", reason);
            end.Parameters.AddWithValue("user", userId);
            end.Parameters.AddWithValue("project", projectId);
            end.Parameters.AddWithValue("keep", NpgsqlDbType.Uuid, (object?)keep ?? DBNull.Value);
            await using var reader = await end.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ended.Add(reader.GetGuid(0));
        }

        foreach (var sessionId in ended) await WriteEndedAsync(uow, projectId, userId, sessionId, reason, actor, ct);
        return ended;
    }

    private static Task WriteEndedAsync(AuthUnitOfWork uow, string projectId, Guid userId, Guid sessionId, string reason, Actor actor, CancellationToken ct) =>
        AuthEvents.WriteAsync(uow.Tx, AuthEvents.SessionEnded, projectId, actor, userId.ToString(),
            new Dictionary<string, string> { ["userId"] = userId.ToString(), ["sessionId"] = sessionId.ToString() }, reason: reason, ct: ct);

    /// <summary>The refresh token, envelope encrypted and bound to its session row.</summary>
    public byte[] Seal(RefreshToken token, Guid sessionId) =>
        secrets.Encrypt(System.Text.Encoding.ASCII.GetBytes(token.Value), SecretBox.AssociatedData(Table, sessionId.ToString(), RefreshColumn));

    /// <summary>Opens a sealed refresh token (the grace replay).</summary>
    public string Open(byte[] ciphertext, Guid sessionId) =>
        System.Text.Encoding.ASCII.GetString(secrets.Decrypt(ciphertext, SecretBox.AssociatedData(Table, sessionId.ToString(), RefreshColumn)));
}
