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
}

/// <summary>A refusal with a stable error code from the contract's catalog and a short safe sentence.</summary>
internal sealed record Failure(FailureKind Kind, string Code, string Detail)
{
    public static Failure Invalid(string detail) => new(FailureKind.Invalid, ErrorCode.InvalidRequest, detail);

    public static Failure InvalidPassword { get; } =
        new(FailureKind.Invalid, ErrorCode.InvalidPassword, "The password must be 8 to 256 characters.");

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
