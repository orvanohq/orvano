namespace Orvano.Auth.Domain;

/// <summary>Every auth time constant in one place (spec 0004, token formats). Row 14 turns the first three into per project settings.</summary>
internal static class AuthTimings
{
    /// <summary>How long an access token is valid.</summary>
    public static readonly TimeSpan AccessToken = TimeSpan.FromSeconds(900);

    /// <summary>A session ends this long after its last refresh.</summary>
    public static readonly TimeSpan IdleExpiry = TimeSpan.FromDays(30);

    /// <summary>A session ends this long after it was created, however often it refreshes.</summary>
    public static readonly TimeSpan AbsoluteExpiry = TimeSpan.FromDays(365);

    /// <summary>For this long after a rotation, the previous refresh token replays the current pair instead of counting as reuse.</summary>
    public static readonly TimeSpan RefreshGrace = TimeSpan.FromSeconds(10);

    /// <summary>How long the api trusts its cached "session still active" answer.</summary>
    public static readonly TimeSpan SessionCache = TimeSpan.FromSeconds(30);

    /// <summary>How long a rotated signing key stays in the JWKS, so the tokens it signed keep verifying.</summary>
    public static readonly TimeSpan KeyOverlap = TimeSpan.FromHours(24);

    /// <summary>A session row is deleted this long after it ends or expires.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>The clock skew allowed when checking <c>exp</c>.</summary>
    public static readonly TimeSpan ClockLeeway = TimeSpan.FromSeconds(30);

    /// <summary>Client SDKs refresh before a call when less than this is left of the access token.</summary>
    public static readonly TimeSpan ClientRefreshMargin = TimeSpan.FromSeconds(60);

    /// <summary>How long a decrypted signing key stays in process memory.</summary>
    public static readonly TimeSpan SigningKeyCache = TimeSpan.FromMinutes(10);

    /// <summary>How long an email verification link works (spec 0010, AC-2).</summary>
    public static readonly TimeSpan VerificationToken = TimeSpan.FromHours(24);

    /// <summary>How long a password reset link works.</summary>
    public static readonly TimeSpan RecoveryToken = TimeSpan.FromHours(1);

    /// <summary>How long a magic link works.</summary>
    public static readonly TimeSpan MagicLinkToken = TimeSpan.FromMinutes(15);

    /// <summary>How long an email code works.</summary>
    public static readonly TimeSpan EmailCodeToken = TimeSpan.FromMinutes(10);

    /// <summary>How long an email change link works.</summary>
    public static readonly TimeSpan EmailChangeToken = TimeSpan.FromHours(1);

    /// <summary>A user without a password must call from a session at most this old to change sensitive things (AC-17, AC-19).</summary>
    public static readonly TimeSpan Reauthentication = TimeSpan.FromMinutes(10);

    /// <summary>The open email requests answer no sooner than this after they start, whether or not the account exists (AC-8).</summary>
    public static readonly TimeSpan OpenSendFloor = TimeSpan.FromMilliseconds(500);
}
