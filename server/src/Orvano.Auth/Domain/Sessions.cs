using System.Buffers.Text;
using System.Security.Cryptography;

namespace Orvano.Auth.Domain;

/// <summary>
/// A refresh token (spec 0004, token formats): <c>orv_rt_</c>, the session ID as 16 bytes of base64url (22 characters),
/// a dot, and a 32 byte random secret as base64url (43 characters). The session ID is not secret (it is the access
/// token's <c>sid</c>); only the secret proves anything, and the database keeps only its SHA-256.
/// </summary>
internal sealed class RefreshToken
{
    public const string Prefix = "orv_rt_";
    public const int SecretSize = 32;
    private const int IdChars = 22;
    private const int SecretChars = 43;

    private RefreshToken(Guid sessionId, byte[] secret)
    {
        SessionId = sessionId;
        Secret = secret;
    }

    public Guid SessionId { get; }

    public byte[] Secret { get; }

    /// <summary>The wire form. Never log it.</summary>
    public string Value => Prefix + Base64Url.EncodeToString(SessionId.ToByteArray(bigEndian: true)) + "." + Base64Url.EncodeToString(Secret);

    /// <summary>SHA-256 of the secret, the only form the database keeps next to the encrypted token.</summary>
    public byte[] SecretHash => SHA256.HashData(Secret);

    /// <summary>A new token for a session, with a fresh secret from <see cref="RandomNumberGenerator"/>.</summary>
    public static RefreshToken New(Guid sessionId) => new(sessionId, RandomNumberGenerator.GetBytes(SecretSize));

    /// <summary>Reads a token; false for anything not exactly in the format.</summary>
    public static bool TryParse(string? value, out RefreshToken token)
    {
        token = null!;
        if (value is null || value.Length != Prefix.Length + IdChars + 1 + SecretChars || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var id = value.AsSpan(Prefix.Length, IdChars);
        var secret = value.AsSpan(Prefix.Length + IdChars + 1);
        if (value[Prefix.Length + IdChars] != '.' || !Base64Url.IsValid(id) || !Base64Url.IsValid(secret)) return false;

        Span<byte> idBytes = stackalloc byte[16];
        var secretBytes = new byte[SecretSize];
        if (Base64Url.DecodeFromChars(id, idBytes) != 16 || Base64Url.DecodeFromChars(secret, secretBytes) != SecretSize) return false;

        token = new RefreshToken(new Guid(idBytes, bigEndian: true), secretBytes);
        return true;
    }
}

/// <summary>
/// Session expiry (AC-9) at the default lifetimes: 30 days after the last refresh, 365 days after creation, whichever
/// comes first. A project may set its own (spec 0014, AC-25); the database computes those from its policy.
/// </summary>
internal static class SessionLifetime
{
    /// <summary>The absolute expiry of a session created at <paramref name="createdAt"/>.</summary>
    public static DateTimeOffset ExpiresAt(DateTimeOffset createdAt) => createdAt + AuthTimings.AbsoluteExpiry;

    /// <summary>The idle expiry after a refresh at <paramref name="lastRefreshedAt"/>; never past <paramref name="expiresAt"/>.</summary>
    public static DateTimeOffset IdleExpiresAt(DateTimeOffset lastRefreshedAt, DateTimeOffset expiresAt) =>
        Min(lastRefreshedAt + AuthTimings.IdleExpiry, expiresAt);

    /// <summary>When the session's refresh token stops working: <c>least(idle_expires_at, expires_at)</c>.</summary>
    public static DateTimeOffset EndsAt(DateTimeOffset idleExpiresAt, DateTimeOffset expiresAt) => Min(idleExpiresAt, expiresAt);

    /// <summary>Whether the session has expired at <paramref name="now"/>.</summary>
    public static bool IsExpired(DateTimeOffset now, DateTimeOffset idleExpiresAt, DateTimeOffset expiresAt) =>
        now >= EndsAt(idleExpiresAt, expiresAt);

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;
}

/// <summary>Why a session ended (the <c>end_reason</c> column).</summary>
internal static class SessionEndReason
{
    public const string SignOut = "sign_out";
    public const string Revoked = "revoked";
    public const string PasswordChanged = "password_changed";
    public const string UserBlocked = "user_blocked";
    public const string ReuseDetected = "reuse_detected";

    /// <summary>A password reset ends every other session of the user (spec 0010, AC-10).</summary>
    public const string PasswordReset = "password_reset";

    /// <summary>The inbox owner signed in by link or code to an unverified account (spec 0010, AC-32).</summary>
    public const string AccountClaimed = "account_claimed";

    /// <summary>Turning MFA on ends every other session of the user (spec 0013, AC-13).</summary>
    public const string MfaEnabled = "mfa_enabled";

    /// <summary>A server, console, or CLI reset of a user's MFA ends every session (spec 0013, AC-27).</summary>
    public const string MfaReset = "mfa_reset";

    /// <summary>A new session ended the user's least recently used one past the project's cap (spec 0014, AC-26).</summary>
    public const string SessionLimit = "session_limit";

    /// <summary>A guest became permanent while the project requires MFA (spec 0014, AC-30).</summary>
    public const string MfaRequired = "mfa_required";
}

/// <summary>How a session began (spec 0010, the <c>method</c> column, AC-20).</summary>
internal static class SessionMethod
{
    public const string Password = "password";
    public const string SignUp = "sign_up";
    public const string MagicLink = "magic_link";
    public const string EmailCode = "email_code";
    public const string Recovery = "recovery";

    /// <summary>A provider's redirect flow (spec 0012, AC-7); the session also records the provider.</summary>
    public const string OAuth = "oauth";

    /// <summary>A provider's ID token from a native app (spec 0012, AC-9); the session also records the provider.</summary>
    public const string IdToken = "id_token";

    /// <summary>A passkey sign in (spec 0013, AC-24); never challenged.</summary>
    public const string Passkey = "passkey";
}

/// <summary>What a refresh does (spec 0004, refresh decision).</summary>
internal enum RefreshAction
{
    /// <summary>The current token: issue a new pair and keep the old secret as the previous one.</summary>
    Rotate,

    /// <summary>The previous token within the grace: answer the current pair again, with a fresh access token.</summary>
    Replay,

    /// <summary>The previous token after the grace: someone else refreshed first, so end the session.</summary>
    Reuse,

    /// <summary>Anything else: 401 and change nothing.</summary>
    Refuse,
}

/// <summary>A session row as the refresh decision needs it.</summary>
internal sealed record RefreshState(
    bool Ended,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset ExpiresAt,
    byte[] RefreshHash,
    byte[]? PreviousRefreshHash,
    DateTimeOffset? RotatedAt);

/// <summary>
/// The refresh decision, on a row locked <c>FOR UPDATE</c>. Only a match with the current or the previous secret can
/// change anything, so knowing a session ID is never enough to end a session.
/// </summary>
internal static class RefreshDecision
{
    public static RefreshAction Decide(RefreshState session, ReadOnlySpan<byte> presentedHash, DateTimeOffset now)
    {
        if (session.Ended || SessionLifetime.IsExpired(now, session.IdleExpiresAt, session.ExpiresAt)) return RefreshAction.Refuse;
        if (CryptographicOperations.FixedTimeEquals(presentedHash, session.RefreshHash)) return RefreshAction.Rotate;

        if (session.PreviousRefreshHash is { } previous && session.RotatedAt is { } rotatedAt
            && CryptographicOperations.FixedTimeEquals(presentedHash, previous))
        {
            return now - rotatedAt <= AuthTimings.RefreshGrace ? RefreshAction.Replay : RefreshAction.Reuse;
        }

        return RefreshAction.Refuse;
    }
}
