using Npgsql;
using Orvano.Auth.Domain;
using Orvano.Core.RateLimiting;
using Orvano.Messaging.Contracts;
using Orvano.Platform.Contracts;

namespace Orvano.Auth.Application;

/// <summary>
/// What every auth email shares (spec 0010): the redirect check, the send limits, Messaging's availability, and
/// queueing the email in the caller's transaction. Use cases decide when; this decides how.
/// </summary>
internal sealed class AuthMailer(IEmailQueue queue, IProjectDirectory projects, IWebOriginPolicy origins, RateLimits limits)
{
    /// <summary>
    /// The redirect URL, when its shape passes <see cref="RedirectUrlRule"/> and the project's platforms allow it
    /// (AC-6); else null, for 400 <c>redirect_url_not_allowed</c>.
    /// </summary>
    public async Task<RedirectUrl?> CheckRedirectAsync(string projectId, string? redirectUrl, EmailTokenKind kind, CancellationToken ct) =>
        RedirectUrlRule.TryCheck(redirectUrl, kind, out var redirect) && await origins.AllowsRedirectAsync(projectId, redirect.Url, ct)
            ? redirect
            : null;

    /// <summary>The open sends' per IP limit (AC-7): 300 an hour across <c>createRecovery</c>, <c>createMagicLink</c>, and <c>createEmailCode</c>.</summary>
    public Failure? TakeIpLimit(string ipKey) => Refused(limits.Acquire(RateLimitPolicies.EmailSendPerIp, ipKey));

    /// <summary>
    /// The two recipient limits every send takes (AC-7), keyed by project, lowercased address, and kind: one a minute
    /// and five an hour.
    /// </summary>
    public Failure? TakeRecipientLimits(string projectId, string email, EmailTokenKind kind)
    {
        var key = $"{projectId}\n{email.ToLowerInvariant()}\n{EmailTokenKinds.Wire(kind)}";
        return Refused(limits.Acquire(RateLimitPolicies.EmailSendPerRecipientShort, key))
            ?? Refused(limits.Acquire(RateLimitPolicies.EmailSendPerRecipient, key));
    }

    /// <summary>Messaging's answer before anything is looked up (AC-7): 409 <c>email_not_configured</c> or 429 <c>email_rate_limited</c>.</summary>
    public async Task<Failure?> CheckAvailabilityAsync(string projectId, CancellationToken ct) =>
        await queue.CheckAvailabilityAsync(projectId, ct) switch
        {
            EmailAvailability.Available => null,
            EmailAvailability.NotConfigured => Failure.EmailNotConfigured,
            EmailAvailability.RateLimited limited => Failure.EmailRateLimited(limited.RetryAfter),
            var other => throw new InvalidOperationException($"Unknown email availability {other.GetType().Name}."),
        };

    /// <summary>The project's name for the email, read before the caller opens its transaction.</summary>
    public async Task<string> ProjectNameAsync(string projectId, CancellationToken ct) =>
        (await projects.GetAsync(projectId, ct))?.Name ?? throw new InvalidOperationException($"Project {projectId} vanished while sending an email.");

    /// <summary>
    /// Queues a link email in <paramref name="tx"/>. Returns null when queued, else the refusal (AC-12's mapping:
    /// 409 <c>email_not_configured</c>, 429 <c>email_rate_limited</c>); the caller's transaction then rolls back.
    /// </summary>
    public async Task<Failure?> QueueLinkAsync(
        NpgsqlTransaction tx, string projectId, string projectName, EmailTokenKind kind, string to, string? userName, string actionUrl, CancellationToken ct) =>
        Refused(await queue.QueueAuthEmailAsync(tx, new AuthEmail(projectId, projectName, TemplateOf(kind), to, userName, actionUrl, null, Minutes(kind)), ct));

    /// <summary>Queues a code email in <paramref name="tx"/>; null when queued, else the refusal.</summary>
    public async Task<Failure?> QueueCodeAsync(
        NpgsqlTransaction tx, string projectId, string projectName, string to, string? userName, string code, CancellationToken ct) =>
        Refused(await queue.QueueAuthEmailAsync(
            tx, new AuthEmail(projectId, projectName, AuthEmailKind.EmailCode, to, userName, null, code, Minutes(EmailTokenKind.EmailCode)), ct));

    /// <summary>The template each kind is sent with; an email change uses the verification template.</summary>
    public static AuthEmailKind TemplateOf(EmailTokenKind kind) => kind switch
    {
        EmailTokenKind.Verification or EmailTokenKind.EmailChange => AuthEmailKind.Verification,
        EmailTokenKind.Recovery => AuthEmailKind.Recovery,
        EmailTokenKind.MagicLink => AuthEmailKind.MagicLink,
        EmailTokenKind.EmailCode => AuthEmailKind.EmailCode,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static int Minutes(EmailTokenKind kind) => (int)EmailTokenKinds.Lifetime(kind).TotalMinutes;

    private static Failure? Refused(RateLimitDecision decision) => decision.Allowed ? null : Failure.RateLimited(decision.RetryAfter);

    private static Failure? Refused(EmailQueueResult result) => result switch
    {
        EmailQueueResult.Queued => null,
        EmailQueueResult.NotConfigured => Failure.EmailNotConfigured,
        EmailQueueResult.RateLimited limited => Failure.EmailRateLimited(limited.RetryAfter),
        _ => throw new InvalidOperationException($"Unknown queue result {result.GetType().Name}."),
    };
}
