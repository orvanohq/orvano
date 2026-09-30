using Npgsql;

namespace Orvano.Messaging.Contracts;

/// <summary>The four auth emails a project can send, each with its own editable template (spec 0009).</summary>
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
public sealed record AuthEmail(
    string ProjectId,
    string ProjectName,
    AuthEmailKind Kind,
    string To,
    string? UserName,
    string? ActionUrl,
    string? Code,
    int ExpiresInMinutes);

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

/// <summary>Queues auth emails (spec 0009, AC-14). Registered only in the <c>api</c> role.</summary>
public interface IEmailQueue
{
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
}
