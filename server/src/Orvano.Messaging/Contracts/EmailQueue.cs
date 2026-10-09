using Npgsql;

namespace Orvano.Messaging.Contracts;

/// <summary>The five auth emails a project can send, each with its own editable template (spec 0009, spec 0013).</summary>
public enum AuthEmailKind
{
    /// <summary>Confirms a user owns their email address.</summary>
    Verification,

    /// <summary>Lets a user reset their password.</summary>
    Recovery,

    /// <summary>Signs a user in with a link.</summary>
    MagicLink,

    /// <summary>Signs a user in with a one time code.</summary>
    EmailCode,

    /// <summary>
    /// Tells a user their sign in security changed (spec 0013, AC-31). Queued only through
    /// <see cref="IEmailQueue.QueueSecurityAlertAsync"/>: it has no link and no code.
    /// </summary>
    SecurityAlert,
}

/// <summary>Which change a security alert tells the user about (spec 0013, AC-31); the template's <c>alert</c>.</summary>
public enum SecurityAlertKind
{
    /// <summary>An authenticator app was turned on.</summary>
    MfaEnabled,

    /// <summary>MFA was turned off, by the user or by a reset.</summary>
    MfaDisabled,

    /// <summary>A passkey was added.</summary>
    PasskeyAdded,

    /// <summary>A passkey was removed.</summary>
    PasskeyRemoved,

    /// <summary>New recovery codes replaced the old ones.</summary>
    RecoveryCodesCreated,

    /// <summary>A recovery code was used to sign in or to step up.</summary>
    RecoveryCodeUsed,

    /// <summary>
    /// Someone signed up with an email that already has a verified account (spec 0014, AC-12). The sign up changed
    /// nothing; the alert tells the owner it happened.
    /// </summary>
    SignUpAttempt,
}

/// <summary>One auth email to queue. Never log <paramref name="To"/>, <paramref name="ActionUrl"/>, or <paramref name="Code"/>.</summary>
/// <param name="ProjectId">The project that sends it.</param>
/// <param name="ProjectName">The project's name, read by the caller before it opens its transaction.</param>
/// <param name="Kind">Which email it is.</param>
/// <param name="To">The recipient's address.</param>
/// <param name="UserName">The user's name, or null when they have none.</param>
/// <param name="ActionUrl">The link the user opens: required for the three link kinds, null for <see cref="AuthEmailKind.EmailCode"/>.</param>
/// <param name="Code">The code the user types in: required for <see cref="AuthEmailKind.EmailCode"/>, null otherwise.</param>
/// <param name="ExpiresInMinutes">How many minutes the link or code works, 1 to 10,080.</param>
/// <param name="RejectUrl">
/// The second link of a verification email, which says the sign up wasn't the inbox owner's (spec 0014, AC-15): only
/// for <see cref="AuthEmailKind.Verification"/>, and null for an email change, which uses the same template.
/// </param>
public sealed record AuthEmail(
    string ProjectId,
    string ProjectName,
    AuthEmailKind Kind,
    string To,
    string? UserName,
    string? ActionUrl,
    string? Code,
    int ExpiresInMinutes,
    string? RejectUrl = null);

/// <summary>What <see cref="IEmailQueue.QueueAuthEmailAsync"/> did.</summary>
public abstract record EmailQueueResult
{
    private EmailQueueResult() { }

    /// <summary>The email is queued and is sent if the caller's transaction commits.</summary>
    /// <param name="EmailId">The ID of the queued email.</param>
    public sealed record Queued(Guid EmailId) : EmailQueueResult;

    /// <summary>Neither the project nor the install has an SMTP server. Nothing was written.</summary>
    public sealed record NotConfigured : EmailQueueResult;

    /// <summary>The project reached the install's hourly email limit. Nothing was written.</summary>
    /// <param name="RetryAfter">How long until a slot frees.</param>
    public sealed record RateLimited(TimeSpan RetryAfter) : EmailQueueResult;
}

/// <summary>Whether a project can send an email right now (<see cref="IEmailQueue.CheckAvailabilityAsync"/>).</summary>
public abstract record EmailAvailability
{
    private EmailAvailability() { }

    /// <summary>An SMTP server is set up and the project is under the install's hourly limit.</summary>
    public sealed record Available : EmailAvailability;

    /// <summary>Neither the project nor the install has an SMTP server.</summary>
    public sealed record NotConfigured : EmailAvailability;

    /// <summary>The project reached the install's hourly email limit.</summary>
    /// <param name="RetryAfter">How long until a slot frees.</param>
    public sealed record RateLimited(TimeSpan RetryAfter) : EmailAvailability;
}

/// <summary>Queues auth emails (spec 0009, AC-14). Registered only in the <c>api</c> role.</summary>
public interface IEmailQueue
{
    /// <summary>
    /// Whether <paramref name="projectId"/> could queue an email now, by the same SMTP resolution and hourly limit
    /// <see cref="QueueAuthEmailAsync"/> uses, on a connection of its own (spec 0010, AC-31). A caller that must not
    /// reveal whether an account exists asks this first, so every mail problem is decided before the account is read.
    /// </summary>
    /// <param name="projectId">The project that would send.</param>
    /// <param name="ct">Cancels the call.</param>
    Task<EmailAvailability> CheckAvailabilityAsync(string projectId, CancellationToken ct);

    /// <summary>
    /// Renders the project's template for <paramref name="email"/> and queues it in <paramref name="tx"/>, so the
    /// email exists only if the caller's change commits. The worker sends it with retries.
    /// </summary>
    /// <param name="tx">The caller's open transaction.</param>
    /// <param name="email">The email to queue.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>Whether it was queued, and why not when it was not.</returns>
    /// <exception cref="ArgumentException">A value of <paramref name="email"/> breaks its rule, which is a caller bug.</exception>
    Task<EmailQueueResult> QueueAuthEmailAsync(NpgsqlTransaction tx, AuthEmail email, CancellationToken ct);

    /// <summary>
    /// Queues a security alert (spec 0013, AC-31) in <paramref name="tx"/> the same way as
    /// <see cref="QueueAuthEmailAsync"/>: the SMTP and the hourly limit, the project's <c>security_alert</c> template
    /// (or the default), and the sealed row and its job, so the email exists only if the change commits. Never log
    /// <paramref name="to"/>.
    /// </summary>
    /// <param name="tx">The caller's open transaction, the one that makes the change.</param>
    /// <param name="projectId">The project that sends it.</param>
    /// <param name="to">The user's email address.</param>
    /// <param name="projectName">The project's name (<c>Orvano</c> for <c>console</c>), read before the transaction.</param>
    /// <param name="alert">Which change happened.</param>
    /// <param name="occurredAt">When it happened: the change's transaction time.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>Whether it was queued, and why not when it was not; a caller never stops its change for a refusal.</returns>
    /// <exception cref="ArgumentException">A value breaks its rule, which is a caller bug.</exception>
    Task<EmailQueueResult> QueueSecurityAlertAsync(
        NpgsqlTransaction tx, string projectId, string to, string projectName, SecurityAlertKind alert, DateTimeOffset occurredAt, CancellationToken ct);
}
