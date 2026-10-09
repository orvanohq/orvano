namespace Orvano.Auth.Domain;

/// <summary>Every auth time constant in one place (spec 0004, token formats). Row 14 turns the first three into per project settings.</summary>
internal static class AuthTimings
{
    /// <summary>How long an access token is valid by default; a project sets its own (spec 0014, AC-25).</summary>
    public static readonly TimeSpan AccessToken = TimeSpan.FromSeconds(900);

    /// <summary>A session ends this long after its last refresh, by default (spec 0014, AC-25).</summary>
    public static readonly TimeSpan IdleExpiry = TimeSpan.FromDays(30);

    /// <summary>A session ends this long after it was created, however often it refreshes, by default (spec 0014, AC-25).</summary>
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

    /// <summary>How long an OAuth flow waits for the provider's callback (spec 0012, AC-4).</summary>
    public static readonly TimeSpan OAuthFlow = TimeSpan.FromMinutes(10);

    /// <summary>How long the handoff code of a finished callback works (spec 0012, AC-6).</summary>
    public static readonly TimeSpan OAuthHandoff = TimeSpan.FromMinutes(2);

    /// <summary>How long Orvano's own Apple client secret JWT is valid (spec 0012, AC-3).</summary>
    public static readonly TimeSpan AppleClientSecret = TimeSpan.FromHours(1);

    /// <summary>How long a made Apple client secret is reused, well inside its validity (spec 0012, AC-3).</summary>
    public static readonly TimeSpan AppleClientSecretCache = TimeSpan.FromMinutes(50);

    /// <summary>How long a provider's discovery document and keys are cached (spec 0012, AC-8).</summary>
    public static readonly TimeSpan ProviderKeysCache = TimeSpan.FromHours(12);

    /// <summary>The soonest a provider's keys are fetched again for an unknown <c>kid</c> (spec 0012, AC-8).</summary>
    public static readonly TimeSpan ProviderKeysRefresh = TimeSpan.FromMinutes(5);

    /// <summary>The soonest a provider's keys are tried again after a fetch failed with nothing cached, or for an unknown <c>kid</c>.</summary>
    public static readonly TimeSpan ProviderKeysRetry = TimeSpan.FromSeconds(30);

    /// <summary>The open email requests answer no sooner than this after they start, whether or not the account exists (AC-8).</summary>
    public static readonly TimeSpan OpenSendFloor = TimeSpan.FromMilliseconds(500);

    /// <summary>How long an MFA ticket waits for the second step (spec 0013, AC-7).</summary>
    public static readonly TimeSpan MfaTicket = TimeSpan.FromMinutes(5);

    /// <summary>How long an enrollment ticket waits for a first factor (spec 0014, AC-27).</summary>
    public static readonly TimeSpan MfaEnrollmentTicket = TimeSpan.FromMinutes(15);

    /// <summary>How long a WebAuthn challenge works (spec 0013, AC-20, AC-23).</summary>
    public static readonly TimeSpan WebAuthnChallenge = TimeSpan.FromMinutes(5);

    /// <summary>How long a TOTP factor waits for its first code before it is swept (spec 0013, AC-12, AC-13).</summary>
    public static readonly TimeSpan PendingTotp = TimeSpan.FromMinutes(15);

    /// <summary>How recent a session's strong check must be for enrollment and step up (spec 0013, AC-17, AC-18).</summary>
    public static readonly TimeSpan StrongAuthWindow = TimeSpan.FromMinutes(10);

    /// <summary>Wrong factors one ticket allows; the last one deletes it (spec 0013, AC-8).</summary>
    public const int MfaTicketAttempts = 5;

    /// <summary>Live tickets one user may hold; a new one past this deletes the oldest (spec 0013, AC-7).</summary>
    public const int MfaTicketsPerUser = 5;
}
