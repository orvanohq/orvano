using Orvano.Messaging.Contracts;

namespace Orvano.Messaging.Domain;

/// <summary>
/// The rules an <see cref="AuthEmail"/> must meet before it is queued (spec 0009, AC-14). A value that breaks one
/// is a bug in the calling module, so each failure is an <see cref="ArgumentException"/> that never names the value.
/// </summary>
internal static class AuthEmailRule
{
    public const int MinCodeLength = 4;
    public const int MaxCodeLength = 12;
    public const int MaxExpiresInMinutes = 10_080;

    /// <summary>Checks <paramref name="email"/> and returns the values its template is rendered with.</summary>
    /// <param name="email">The email a module asked to queue.</param>
    /// <param name="allowHttp">True when <c>ORVANO_PUBLIC_URL</c> is <c>http</c>, which lets the link be <c>http</c> too.</param>
    /// <exception cref="ArgumentException">A value breaks its rule.</exception>
    public static TemplateValues Check(AuthEmail email, bool allowHttp)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (string.IsNullOrWhiteSpace(email.ProjectId)) throw Broken(nameof(email.ProjectId), "is empty");
        if (email.ProjectName is null) throw Broken(nameof(email.ProjectName), "is null");
        if (!Enum.IsDefined(email.Kind)) throw Broken(nameof(email.Kind), "is not a known kind");
        if (!EmailAddress.TryNormalize(email.To, out var to) || to != email.To || to.AsSpan().ContainsAny('\r', '\n'))
            throw Broken(nameof(email.To), $"must be one email address of at most {EmailAddress.MaxLength} characters with no line break");
        if (email.ExpiresInMinutes is < 1 or > MaxExpiresInMinutes)
            throw Broken(nameof(email.ExpiresInMinutes), $"must be 1 to {MaxExpiresInMinutes}");

        if (email.Kind == AuthEmailKind.EmailCode)
        {
            if (email.ActionUrl is not null) throw Broken(nameof(email.ActionUrl), "must be null for an email code");
            if (email.Code is not { Length: >= MinCodeLength and <= MaxCodeLength } code || !code.All(char.IsAsciiLetterOrDigit))
                throw Broken(nameof(email.Code), $"must be {MinCodeLength} to {MaxCodeLength} letters or digits");
        }
        else
        {
            if (email.Code is not null) throw Broken(nameof(email.Code), "must be null for a link email");
            if (!Uri.TryCreate(email.ActionUrl, UriKind.Absolute, out var url)
                || !(url.Scheme == Uri.UriSchemeHttps || allowHttp && url.Scheme == Uri.UriSchemeHttp))
            {
                throw Broken(nameof(email.ActionUrl), allowHttp ? "must be an absolute http or https URL" : "must be an absolute https URL");
            }
        }

        return new TemplateValues(email.ProjectName, to, email.UserName ?? "", email.ActionUrl, email.Code, email.ExpiresInMinutes);
    }

    private static ArgumentException Broken(string member, string rule) => new($"{nameof(AuthEmail)}.{member} {rule}.", "email");
}
