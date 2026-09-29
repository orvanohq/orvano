using Orvano.Contract;

namespace Orvano.Platform.Application;

/// <summary>Why a use case refused. Endpoints map it to an HTTP status; the use cases know no HTTP.</summary>
internal enum FailureKind
{
    Invalid,
    Forbidden,
    NotFound,
    Conflict,

    /// <summary>The thing existed but is gone for good (410), such as an expired invitation.</summary>
    Gone,
}

/// <summary>A refusal with a stable error code from the contract's catalog and a short safe sentence.</summary>
internal sealed record Failure(FailureKind Kind, string Code, string Detail)
{
    public static Failure Invalid(string detail) => new(FailureKind.Invalid, ErrorCode.InvalidRequest, detail);

    public static Failure InvalidCursor { get; } = new(FailureKind.Invalid, ErrorCode.InvalidCursor, "The cursor is not one this server issued.");

    public static Failure Forbidden { get; } = new(FailureKind.Forbidden, ErrorCode.Forbidden, "Your role in this org does not allow this.");

    public static Failure InstallAdminsOnly { get; } = new(FailureKind.Forbidden, ErrorCode.Forbidden, "Only install admins can do this.");

    public static Failure ProjectHeaderMissing { get; } = Invalid("Send the project ID in the X-Orvano-Project header.");

    public static Failure NotFound(string what) => new(FailureKind.NotFound, ErrorCode.NotFound, $"No such {what}.");

    public static Failure OrgNotFound { get; } = NotFound("org");

    public static Failure ProjectNotFound { get; } = new(FailureKind.NotFound, ErrorCode.ProjectNotFound, "No such project.");

    public static Failure ProjectNotReady(string detail) => new(FailureKind.Conflict, ErrorCode.ProjectNotReady, detail);

    public static Failure OrgNotActive { get; } =
        new(FailureKind.Conflict, ErrorCode.OrgNotActive, "The org is being deleted; restore it first.");

    public static Failure OrgNotEmpty { get; } =
        new(FailureKind.Conflict, ErrorCode.OrgNotEmpty, "Delete every project of the org first.");

    public static Failure LastOwner { get; } =
        new(FailureKind.Conflict, ErrorCode.LastOwner, "An org needs at least one owner. Make someone else an owner first.");

    public static Failure MemberNotFound { get; } = NotFound("member");

    public static Failure InvitationNotFound { get; } =
        new(FailureKind.NotFound, ErrorCode.InvitationNotFound, "This invite link isn't valid anymore. It may have been used, replaced, or revoked.");

    public static Failure InvitationExpired { get; } =
        new(FailureKind.Gone, ErrorCode.InvitationExpired, "This invite expired. Ask an owner of the org for a new link.");

    public static Failure InvitationEmailMismatch { get; } =
        new(FailureKind.Forbidden, ErrorCode.InvitationEmailMismatch, "This invite is for another email address.");

    public static Failure AlreadyMember { get; } =
        new(FailureKind.Conflict, ErrorCode.AlreadyMember, "This email's account is already a member of the org.");

    public static Failure InvitationLimit { get; } =
        new(FailureKind.Conflict, ErrorCode.InvitationLimit, "The org already holds 100 invitations. Revoke some first.");
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
