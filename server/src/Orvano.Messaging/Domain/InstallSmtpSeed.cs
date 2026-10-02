namespace Orvano.Messaging.Domain;

/// <summary>
/// The install SMTP that <c>ORVANO_INSTALL_SMTP_URL</c> and <c>ORVANO_INSTALL_SMTP_FROM</c> seed (spec 0011, AC-4),
/// checked and normalized like a console save. Never log any of it: the URL can carry a password.
/// </summary>
internal sealed record InstallSmtpSeed(string Host, int Port, SmtpSecurity Security, string? Username, string? Password, string FromEmail)
{
    public const string UrlSetting = "ORVANO_INSTALL_SMTP_URL";
    public const string FromSetting = "ORVANO_INSTALL_SMTP_FROM";

    /// <summary>The <c>updated_by_user_id</c> of a seeded row: the all zero UUID, "set from configuration".</summary>
    public static readonly Guid ConfigurationUserId = Guid.Empty;

    /// <summary>
    /// Reads the two settings. An unset or empty URL means no seed (null and no error). Every error names the
    /// setting and the rule, never the value.
    /// </summary>
    public static InstallSmtpSeed? Parse(string? url, string? from, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(url)) return null;

        const string Shape =
            $"{UrlSetting} must be an smtp://, smtp+starttls://, or smtps:// URL with a host and nothing after it, for example smtp+starttls://user:password@smtp.example.com:587.";

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.IdnHost.Length == 0
            || uri.AbsolutePath is not ("" or "/")
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            error = Shape;
            return null;
        }

        (SmtpSecurity Security, int DefaultPort)? scheme = uri.Scheme switch
        {
            "smtp" => (SmtpSecurity.None, 25),
            "smtp+starttls" => (SmtpSecurity.StartTls, 587),
            "smtps" => (SmtpSecurity.Tls, 465),
            _ => null,
        };
        if (scheme is not { } known)
        {
            error = Shape;
            return null;
        }

        var port = uri.IsDefaultPort ? known.DefaultPort : uri.Port;
        if (port is < 1 or > 65535)
        {
            error = $"{UrlSetting} has a port outside 1 to 65535.";
            return null;
        }

        string? username = null;
        string? password = null;
        if (uri.UserInfo.Length > 0)
        {
            if (known.Security == SmtpSecurity.None)
            {
                error = $"{UrlSetting} can't carry a user or password with smtp://, which would send them unencrypted. Use smtp+starttls:// or smtps://.";
                return null;
            }

            var colon = uri.UserInfo.IndexOf(':', StringComparison.Ordinal);
            username = Uri.UnescapeDataString(colon < 0 ? uri.UserInfo : uri.UserInfo[..colon]);
            password = colon < 0 ? "" : Uri.UnescapeDataString(uri.UserInfo[(colon + 1)..]);
            if (username.Length == 0 || password.Length == 0)
            {
                error = $"{UrlSetting} needs both a user and a password, or neither.";
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(from))
        {
            error = $"{FromSetting} must be set to an email address when {UrlSetting} is set.";
            return null;
        }

        var draft = new SmtpSettingsDraft(uri.IdnHost, port, known.Security, username, password, from, FromName: null, ReplyTo: null);
        if (!SmtpSettingsRule.TryValidate(draft, stored: null, out var valid, out var rule))
        {
            // The rule's field name tells which setting it is; its text never holds the value.
            error = rule.StartsWith("fromEmail:", StringComparison.Ordinal)
                ? $"{FromSetting} is not an email address of at most {EmailAddress.MaxLength} characters."
                : $"{UrlSetting} is not usable: {rule}";
            return null;
        }

        return new InstallSmtpSeed(valid.Host, valid.Port, valid.Security, valid.Username, valid.Password, valid.FromEmail);
    }

    /// <inheritdoc />
    public override string ToString() => $"{nameof(InstallSmtpSeed)} {{ (redacted) }}";
}
