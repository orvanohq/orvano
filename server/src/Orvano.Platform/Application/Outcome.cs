using Orvano.Contract;

namespace Orvano.Platform.Application;

/// <summary>Why a use case refused. Endpoints map it to an HTTP status; the use cases know no HTTP.</summary>
internal enum FailureKind
{
    Invalid,
    Forbidden,
    NotFound,
    Conflict,
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
