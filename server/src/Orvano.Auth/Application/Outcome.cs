using Orvano.Contract;

namespace Orvano.Auth.Application;

/// <summary>Why a use case refused. Endpoints map it to an HTTP status; the use cases know no HTTP.</summary>
internal enum FailureKind
{
    Invalid,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    RateLimited,
    Busy,

    /// <summary>The thing existed but is gone for good (410), such as an expired invitation.</summary>
    Gone,

    /// <summary>An outside service answered wrong (502), such as a sign in provider.</summary>
    BadGateway,

    /// <summary>An outside service did not answer in time (503), with no <c>Retry-After</c>.</summary>
    Unavailable,
}

/// <summary>
/// A refusal with a stable error code from the contract's catalog and a short safe sentence. A limit's refusal also
/// says when to try again (<see cref="RetryAfter"/>, sent as <c>Retry-After</c>).
/// </summary>
internal sealed record Failure(FailureKind Kind, string Code, string Detail, TimeSpan? RetryAfter = null)
{
    public static Failure Invalid(string detail) => new(FailureKind.Invalid, ErrorCode.InvalidRequest, detail);

    public static Failure InvalidPassword { get; } =
        new(FailureKind.Invalid, ErrorCode.InvalidPassword, "The password must be 8 to 256 characters.");

    public static Failure PasswordTooCommon { get; } =
        new(FailureKind.Invalid, ErrorCode.PasswordTooCommon, "This password is too common. Choose one that is harder to guess.");

    public static Failure PasswordBreached { get; } =
        new(FailureKind.Invalid, ErrorCode.PasswordBreached, "This password appears in a known data breach. Choose another one.");

    /// <summary>A new password under the project's minimum (spec 0014, AC-4); the detail names the minimum.</summary>
    public static Failure PasswordTooShort(int minLength) =>
        new(FailureKind.Invalid, ErrorCode.InvalidPassword,
            $"The password must be {minLength.ToString(System.Globalization.CultureInfo.InvariantCulture)} to 256 characters.");

    public static Failure SignUpDisabled { get; } =
        new(FailureKind.Forbidden, ErrorCode.SignUpDisabled, "Sign ups are closed for this project.");

    public static Failure EmailDomainNotAllowed { get; } =
        new(FailureKind.Forbidden, ErrorCode.EmailDomainNotAllowed, "This project doesn't allow email addresses from this domain.");

    public static Failure EmailVerificationRequired { get; } =
        new(FailureKind.Forbidden, ErrorCode.EmailVerificationRequired, "Verify your email first: open the link we sent, then sign in again.");

    public static Failure AnonymousDisabled { get; } =
        new(FailureKind.Forbidden, ErrorCode.AnonymousDisabled, "This project doesn't allow guest sign in.");

    public static Failure AnonymousNotAllowed { get; } =
        new(FailureKind.Forbidden, ErrorCode.AnonymousNotAllowed, "Guests can't do this. Upgrade to a permanent account first.");

    /// <summary><c>account.upgradeAnonymous</c> from a user who is not a guest (spec 0014, API surface).</summary>
    public static Failure NotAnonymous { get; } =
        new(FailureKind.Forbidden, ErrorCode.Forbidden, "Only a guest can be upgraded.");

    public static Failure InvalidCredentials { get; } =
        new(FailureKind.Unauthorized, ErrorCode.InvalidCredentials, "The email or password is wrong.");

    public static Failure InvalidRefreshToken { get; } =
        new(FailureKind.Unauthorized, ErrorCode.InvalidRefreshToken, "The refresh token is not valid. Sign in again.");

    public static Failure UserBlocked { get; } = new(FailureKind.Forbidden, ErrorCode.UserBlocked, "This user is blocked.");

    public static Failure UserAlreadyExists { get; } =
        new(FailureKind.Conflict, ErrorCode.UserAlreadyExists, "A user with this email already exists.");

    public static Failure SignupClosed { get; } =
        new(FailureKind.Forbidden, ErrorCode.SignupClosed, "Console sign up is invite only.");

    public static Failure SetupTokenInvalid { get; } =
        new(FailureKind.Forbidden, ErrorCode.SetupTokenInvalid, "This setup link is not valid. Run the installer again on your server to see the right link.");

    public static Failure InvitationNotFound { get; } =
        new(FailureKind.NotFound, ErrorCode.InvitationNotFound, "This invite link isn't valid anymore. It may have been used, replaced, or revoked.");

    public static Failure InvitationExpired { get; } =
        new(FailureKind.Gone, ErrorCode.InvitationExpired, "This invite expired. Ask an owner of the org for a new link.");

    public static Failure InvitationEmailMismatch { get; } =
        new(FailureKind.Forbidden, ErrorCode.InvitationEmailMismatch, "This invite is for another email address.");

    public static Failure OrgNotActive { get; } =
        new(FailureKind.Conflict, ErrorCode.OrgNotActive, "The org of this invite is being deleted.");

    public static Failure UserNotFound { get; } = new(FailureKind.NotFound, ErrorCode.UserNotFound, "No such user.");

    public static Failure SessionNotFound { get; } = new(FailureKind.NotFound, ErrorCode.SessionNotFound, "No such session.");

    public static Failure InvalidCursor { get; } = new(FailureKind.Invalid, ErrorCode.InvalidCursor, "The cursor is not one this server issued.");

    public static Failure RedirectUrlNotAllowed { get; } =
        new(FailureKind.Invalid, ErrorCode.RedirectUrlNotAllowed, "The redirect URL must be an absolute URL on one of the project's platforms.");

    public static Failure InvalidEmailToken { get; } =
        new(FailureKind.Unauthorized, ErrorCode.InvalidEmailToken, "This link is not valid anymore. It may have been used, expired, or replaced by a newer email.");

    public static Failure InvalidCode { get; } =
        new(FailureKind.Unauthorized, ErrorCode.InvalidCode, "The code is wrong or has expired.");

    public static Failure ReauthenticationRequired { get; } =
        new(FailureKind.Forbidden, ErrorCode.ReauthenticationRequired, "Sign in again with a link or code first, then try within 10 minutes.");

    public static Failure EmailAlreadyVerified { get; } =
        new(FailureKind.Conflict, ErrorCode.EmailAlreadyVerified, "The email is already verified.");

    public static Failure EmailAlreadyInUse { get; } =
        new(FailureKind.Conflict, ErrorCode.EmailAlreadyInUse, "Another user has this email.");

    public static Failure EmailNotConfigured { get; } =
        new(FailureKind.Conflict, ErrorCode.EmailNotConfigured, "No email server is set up for this project.");

    /// <summary>The project reached the install's hourly email limit (429 <c>email_rate_limited</c>).</summary>
    public static Failure EmailRateLimited(TimeSpan retryAfter) =>
        new(FailureKind.RateLimited, ErrorCode.EmailRateLimited, "The project sent too many emails this hour. Try again later.", retryAfter);

    /// <summary>A named limit refused the call (429 <c>rate_limited</c>).</summary>
    public static Failure RateLimited(TimeSpan retryAfter) =>
        new(FailureKind.RateLimited, ErrorCode.RateLimited, "Too many requests. Try again later.", retryAfter);

    public static Failure InvalidOAuthCode { get; } =
        new(FailureKind.Unauthorized, ErrorCode.InvalidOauthCode, "The code is not valid anymore, or the verifier does not match the flow that started it. Start again.");

    public static Failure InvalidIdToken { get; } =
        new(FailureKind.Unauthorized, ErrorCode.InvalidIdToken, "The ID token is not valid for this project, or was used before.");

    public static Failure ProviderNotEnabled { get; } =
        new(FailureKind.Conflict, ErrorCode.ProviderNotEnabled, "This sign in provider is not turned on for the project.");

    public static Failure ProviderNotConfigured { get; } =
        new(FailureKind.Conflict, ErrorCode.ProviderNotConfigured, "This sign in provider's settings for this kind of sign in are missing.");

    public static Failure ProviderAlreadyLinked { get; } =
        new(FailureKind.Conflict, ErrorCode.ProviderAlreadyLinked, "The user already has an identity of this provider with another provider account.");

    public static Failure IdentityAlreadyLinked { get; } =
        new(FailureKind.Conflict, ErrorCode.IdentityAlreadyLinked, "This provider account is linked to another user.");

    public static Failure IdentityNotFound { get; } = new(FailureKind.NotFound, ErrorCode.IdentityNotFound, "No such identity.");

    public static Failure LastSignInMethod { get; } =
        new(FailureKind.Conflict, ErrorCode.LastSignInMethod, "This is the user's only way to sign in.");

    public static Failure ProviderError { get; } =
        new(FailureKind.BadGateway, ErrorCode.ProviderError, "The sign in provider answered with an error.");

    public static Failure ProviderUnavailable { get; } =
        new(FailureKind.Unavailable, ErrorCode.ProviderUnavailable, "The sign in provider did not answer in time. Try again.");

    public static Failure InvalidMfaTicket { get; } =
        new(FailureKind.Unauthorized, ErrorCode.InvalidMfaTicket, "The MFA ticket is not valid anymore. Sign in again.");

    public static Failure InvalidMfaCode { get; } =
        new(FailureKind.Unauthorized, ErrorCode.InvalidMfaCode, "The code is wrong or was already used.");

    public static Failure MfaVerificationRequired { get; } =
        new(FailureKind.Forbidden, ErrorCode.MfaVerificationRequired, "Pass a second factor first, then try within 10 minutes.");

    public static Failure MfaAlreadyEnabled { get; } =
        new(FailureKind.Conflict, ErrorCode.MfaAlreadyEnabled, "An authenticator app is already turned on.");

    public static Failure MfaNotEnabled { get; } =
        new(FailureKind.Conflict, ErrorCode.MfaNotEnabled, "No authenticator app is turned on.");

    public static Failure EmailNotVerified { get; } =
        new(FailureKind.Conflict, ErrorCode.EmailNotVerified, "Verify the email first.");

    public static Failure TotpNotPending { get; } =
        new(FailureKind.Conflict, ErrorCode.TotpNotPending, "No authenticator app is waiting for its first code. Start again.");

    public static Failure FactorNotEnabled { get; } =
        new(FailureKind.Conflict, ErrorCode.FactorNotEnabled, "This factor is turned off for the project, or can't be used now.");

    public static Failure InvalidPasskey { get; } =
        new(FailureKind.Unauthorized, ErrorCode.InvalidPasskey, "The passkey could not be checked. Try again or use another way to sign in.");

    public static Failure InvalidPasskeyChallenge { get; } =
        new(FailureKind.Invalid, ErrorCode.InvalidPasskeyChallenge, "The passkey challenge is unknown, used, or expired. Start again.");

    public static Failure PasskeyAlreadyRegistered { get; } =
        new(FailureKind.Conflict, ErrorCode.PasskeyAlreadyRegistered, "This passkey is already registered.");

    public static Failure PasskeyLimit { get; } =
        new(FailureKind.Conflict, ErrorCode.PasskeyLimit, "A user can have at most 10 passkeys. Remove one first.");

    public static Failure PasskeyNotFound { get; } = new(FailureKind.NotFound, ErrorCode.PasskeyNotFound, "No such passkey.");

    public static Failure PasskeysExist(int count) =>
        new(FailureKind.Conflict, ErrorCode.PasskeysExist,
            $"{count.ToString(System.Globalization.CultureInfo.InvariantCulture)} passkeys are registered under the current RP ID and stop working after the change. Send confirmRpIdChange: true to go ahead.");

    public static Failure Busy { get; } =
        new(FailureKind.Busy, ErrorCode.ServerBusy, "The server is busy checking passwords. Try again in a moment.");
}

/// <summary>A use case's result: a value, or a <see cref="Failure"/>.</summary>
internal readonly struct Outcome<T>
{
    private Outcome(T? value, Failure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public Failure? Failure { get; }

    public bool Succeeded => Failure is null;

    public static implicit operator Outcome<T>(T value) => new(value, null);

    public static implicit operator Outcome<T>(Failure failure) => new(default, failure);
}

/// <summary>The value of a use case that returns nothing.</summary>
internal readonly record struct Done;
