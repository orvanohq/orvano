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

    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}

/// <summary>A new or rotated session and its refresh token, before the access token is issued.</summary>
internal sealed record SessionGrant(Guid SessionId, RefreshToken RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Creates session rows inside the caller's transaction (spec 0004, data model and AC-31).</summary>
internal sealed class Sessions(SecretBox secrets)
{
    public const string Table = "auth_sessions";
    public const string RefreshColumn = "refresh_ciphertext";

    /// <summary>
    /// Inserts a session for the user, sets their <c>last_sign_in_at</c>, and writes <c>auth.session.created</c>. The
    /// session ID comes from Postgres' <c>uuidv7()</c> first, since the encrypted refresh token is bound to it.
    /// </summary>
    public async Task<SessionGrant> CreateAsync(AuthUnitOfWork uow, string projectId, Guid userId, ClientInfo client, Actor actor, CancellationToken ct)
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
                    created_at, last_refreshed_at, idle_expires_at, expires_at)
                VALUES (
                    @id, @project, @user, @hash, @ciphertext, @agent, @sdk, @ip, @ip,
                    now(), now(), least(now() + @idle, now() + @absolute), now() + @absolute)
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
        var endsAt = (DateTime)(await insert.ExecuteScalarAsync(ct))!;

        await AuthEvents.WriteAsync(uow.Tx, AuthEvents.SessionCreated, projectId, actor, userId.ToString(),
            new Dictionary<string, string> { ["userId"] = userId.ToString(), ["sessionId"] = sessionId.ToString() }, ct: ct);
        return new SessionGrant(sessionId, token, new DateTimeOffset(endsAt, TimeSpan.Zero));
    }

    /// <summary>The refresh token, envelope encrypted and bound to its session row.</summary>
    public byte[] Seal(RefreshToken token, Guid sessionId) =>
        secrets.Encrypt(System.Text.Encoding.ASCII.GetBytes(token.Value), SecretBox.AssociatedData(Table, sessionId.ToString(), RefreshColumn));

    /// <summary>Opens a sealed refresh token (the grace replay).</summary>
    public string Open(byte[] ciphertext, Guid sessionId) =>
        System.Text.Encoding.ASCII.GetString(secrets.Decrypt(ciphertext, SecretBox.AssociatedData(Table, sessionId.ToString(), RefreshColumn)));
}
