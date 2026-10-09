using Orvano.Messaging.Contracts;

namespace Orvano.Messaging.Domain;

/// <summary>
/// The rules an <see cref="AuthEmail"/> or a security alert must meet before it is queued (spec 0009, AC-14; spec 0013, AC-31). A value that breaks one
/// is a bug in the calling module, so each failure is an <see cref="ArgumentException"/> that never names the value.
/// </summary>
internal static class AuthEmailRule
{
    public const int MinCodeLength = 4;
    public const int MaxCodeLength = 12;
    public const int MaxExpiresInMinutes = 10_080;

    /// <summary>Schemes a link must never use, since a mail client could run or read something local (spec 0010, AC-31).</summary>
    private static readonly string[] RefusedSchemes = ["javascript", "data", "vbscript", "file", "blob", "about"];

    /// <summary>Checks <paramref name="email"/> and returns the values its template is rendered with.</summary>
    /// <param name="email">The email a module asked to queue.</param>
    /// <exception cref="ArgumentException">A value breaks its rule.</exception>
    /// <remarks>
    /// The link is only checked to be an absolute URL with no user info and a scheme outside a deny list: the calling
    /// module owns the real rule (Auth checks it against the project's platforms, spec 0010 AC-6), so an app's own
    /// custom scheme passes here.
    /// </remarks>
    public static TemplateValues Check(AuthEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (string.IsNullOrWhiteSpace(email.ProjectId)) throw Broken(nameof(email.ProjectId), "is empty");
        if (email.ProjectName is null) throw Broken(nameof(email.ProjectName), "is null");
        if (!Enum.IsDefined(email.Kind)) throw Broken(nameof(email.Kind), "is not a known kind");
        if (email.Kind == AuthEmailKind.SecurityAlert) throw Broken(nameof(email.Kind), "is a security alert, which QueueSecurityAlertAsync sends");
        if (!EmailAddress.TryNormalize(email.To, out var to) || to != email.To || to.AsSpan().ContainsAny('\r', '\n'))
            throw Broken(nameof(email.To), $"must be one email address of at most {EmailAddress.MaxLength} characters with no line break");
        if (email.ExpiresInMinutes is < 1 or > MaxExpiresInMinutes)
            throw Broken(nameof(email.ExpiresInMinutes), $"must be 1 to {MaxExpiresInMinutes}");

        if (email.RejectUrl is not null && (email.Kind != AuthEmailKind.Verification || !IsSafeLink(email.RejectUrl)))
            throw Broken(nameof(email.RejectUrl), "must be null except for a verification email, and then a link like ActionUrl");

        if (email.Kind == AuthEmailKind.EmailCode)
        {
            if (email.ActionUrl is not null) throw Broken(nameof(email.ActionUrl), "must be null for an email code");
            if (email.Code is not { Length: >= MinCodeLength and <= MaxCodeLength } code || !code.All(char.IsAsciiLetterOrDigit))
                throw Broken(nameof(email.Code), $"must be {MinCodeLength} to {MaxCodeLength} letters or digits");
        }
        else
        {
            if (email.Code is not null) throw Broken(nameof(email.Code), "must be null for a link email");
            if (!IsSafeLink(email.ActionUrl))
                throw Broken(nameof(email.ActionUrl), "must be an absolute URL with no user info, and not a javascript, data, vbscript, file, blob, or about URL");
        }

        return new TemplateValues(email.ProjectName, to, email.UserName ?? "", email.ActionUrl, email.Code, email.ExpiresInMinutes, RejectUrl: email.RejectUrl);
    }

    private static bool IsSafeLink(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var url)
        && url.UserInfo.Length == 0
        && !RefusedSchemes.Contains(url.Scheme, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks a security alert (spec 0013, AC-31) and returns the values its template is rendered with: no link, no
    /// code, and no expiry.
    /// </summary>
    /// <exception cref="ArgumentException">A value breaks its rule.</exception>
    public static TemplateValues CheckAlert(string projectId, string to, string projectName, SecurityAlertKind alert, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw Alert("projectId", "is empty");
        ArgumentNullException.ThrowIfNull(projectName);
        if (!Enum.IsDefined(alert)) throw Alert("alert", "is not a known alert");
        if (!EmailAddress.TryNormalize(to, out var normalized) || normalized != to || normalized.AsSpan().ContainsAny('\r', '\n'))
            throw Alert("to", $"must be one email address of at most {EmailAddress.MaxLength} characters with no line break");
        return new TemplateValues(projectName, normalized, "", null, null, 0,
            EmailTemplateCatalog.AlertWire(alert), EmailTemplateCatalog.AlertTime(occurredAt));
    }

    private static ArgumentException Alert(string member, string rule) => new($"The security alert's {member} {rule}.", member);

    private static ArgumentException Broken(string member, string rule) => new($"{nameof(AuthEmail)}.{member} {rule}.", "email");
}
