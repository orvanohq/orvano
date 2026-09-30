namespace Orvano.Messaging.Domain;

/// <summary>The stored <c>template</c> values of <c>messaging_emails</c>.</summary>
internal static class EmailTemplateNames
{
    public const string ConsoleInvitation = "console_invitation";
}

/// <summary>The stored <c>status</c> values of <c>messaging_emails</c>. <c>sent</c> and <c>failed</c> are final.</summary>
internal static class EmailStatuses
{
    public const string Queued = "queued";
    public const string Sent = "sent";
    public const string Failed = "failed";
}

/// <summary>The stored <c>smtp_source</c> values of <c>messaging_emails</c>.</summary>
internal static class SmtpSources
{
    public const string Project = "project";
    public const string Install = "install";
}

/// <summary>
/// The reasons an email fails that are not an SMTP outcome (spec 0009, AC-15 to AC-17). Together with
/// <see cref="SmtpOutcomes.Code"/> they are the contract's <c>EmailFailureCode</c> values.
/// </summary>
internal static class EmailFailures
{
    /// <summary>Neither the project nor the install had an SMTP server at the attempt.</summary>
    public const string NotConfigured = "email_not_configured";

    /// <summary>The project was deleted, or is otherwise not active.</summary>
    public const string ProjectNotActive = "project_not_active";

    /// <summary>The email waited longer than <see cref="EmailDelivery.StaleAfter"/>.</summary>
    public const string Expired = "email_expired";

    /// <summary>The sealed content or the SMTP password could not be opened.</summary>
    public const string Unreadable = "email_unreadable";
}

/// <summary>The timing rules of the send queue (spec 0009, AC-14 to AC-17, AC-20).</summary>
internal static class EmailDelivery
{
    /// <summary>The attempts one email gets; the 6th failure is final.</summary>
    public const int MaxAttempts = 6;

    /// <summary>The longest one attempt may take, connection to goodbye.</summary>
    public static readonly TimeSpan AttemptBudget = TimeSpan.FromSeconds(30);

    /// <summary>An email older than this is never sent: a worker that was down must not deliver stale links and codes.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    /// <summary>How long a row stays in the log.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>The window of the install cap (AC-19).</summary>
    public static readonly TimeSpan CapWindow = TimeSpan.FromHours(1);

    /// <summary>The wait after failed attempt <paramref name="attempt"/>: 30 seconds, then 1, 2, 4, and 8 minutes.</summary>
    public static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(30 * Math.Pow(2, Math.Clamp(attempt, 1, MaxAttempts - 1) - 1));

    /// <summary>True when an email created at <paramref name="createdAt"/> is too old to send at <paramref name="now"/>.</summary>
    public static bool IsStale(DateTimeOffset createdAt, DateTimeOffset now) => now - createdAt > StaleAfter;
}
