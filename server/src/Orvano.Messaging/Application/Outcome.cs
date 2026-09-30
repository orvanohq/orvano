using Orvano.Contract;
using Orvano.Messaging.Domain;

namespace Orvano.Messaging.Application;

/// <summary>
/// A refusal: the HTTP status it answers, a stable error code from the contract's catalog, and a short safe
/// sentence. SMTP failures take all three from <see cref="SmtpOutcomes"/>.
/// </summary>
internal sealed record Failure(int Status, string Code, string Detail)
{
    public static Failure Invalid(string detail) => new(400, ErrorCode.InvalidRequest, detail);

    public static Failure ProjectHeaderMissing { get; } = Invalid("Send the project ID in the X-Orvano-Project header.");

    public static Failure Forbidden { get; } = new(403, ErrorCode.Forbidden, "Developers and owners only.");

    public static Failure ProjectNotFound { get; } = new(404, ErrorCode.ProjectNotFound, "No such project.");

    public static Failure ProjectNotReady { get; } =
        new(409, ErrorCode.ProjectNotReady, "The project is not active, so its email settings can't change and it can't send email.");

    public static Failure SmtpHostNotAllowed { get; } = new(400, ErrorCode.SmtpHostNotAllowed, SmtpOutcomes.HostNotAllowedDetail);

    public static Failure Smtp(SmtpFailure failure) =>
        new(SmtpOutcomes.TestStatus(failure.Kind), SmtpOutcomes.Code(failure.Kind), SmtpOutcomes.Detail(failure));
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
