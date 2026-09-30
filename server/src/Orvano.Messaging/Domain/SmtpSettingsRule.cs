namespace Orvano.Messaging.Domain;

/// <summary>How the connection to the SMTP server is secured. Stored as <c>starttls</c>, <c>tls</c>, or <c>none</c>.</summary>
internal enum SmtpSecurity
{
    StartTls,
    Tls,
    None,
}

/// <summary>SMTP settings as they arrive, unchecked. A <see langword="null"/> <paramref name="Security"/> is a value this server does not know.</summary>
internal sealed record SmtpSettingsDraft(
    string? Host, int Port, SmtpSecurity? Security, string? Username, string? Password, string? FromEmail, string? FromName, string? ReplyTo);

/// <summary>What the stored row says about its password, for the reuse rule. The password itself is never read here.</summary>
internal sealed record StoredSmtpIdentity(string Host, int Port, string? Username, bool HasPassword);

/// <summary>Where the password of checked settings comes from.</summary>
internal enum SmtpPasswordSource
{
    /// <summary>The server needs no sign in, so there is no password.</summary>
    None,

    /// <summary>The password sent with the request.</summary>
    Given,

    /// <summary>The password already stored for this same host, port, and username.</summary>
    Stored,
}

/// <summary>SMTP settings that passed every rule of AC-1 and AC-2, normalized as they are stored.</summary>
internal sealed record ValidSmtpSettings(
    string Host, int Port, SmtpSecurity Security, string? Username, SmtpPasswordSource PasswordSource, string? Password,
    string FromEmail, string? FromName, string? ReplyTo);

/// <summary>
/// The SMTP settings rules (spec 0009, AC-1 and AC-2). Every error starts with the input's camelCase name and a
/// colon, so the console can show the rest under that field.
/// </summary>
internal static class SmtpSettingsRule
{
    public const int MaxUsernameLength = 256;
    public const int MaxPasswordLength = 1024;
    public const int MaxFromNameLength = 128;

    /// <summary>
    /// Checks <paramref name="draft"/> against the rules and against the <paramref name="stored"/> row, if any.
    /// Returns the normalized settings, or the first field error.
    /// </summary>
    public static bool TryValidate(SmtpSettingsDraft draft, StoredSmtpIdentity? stored, out ValidSmtpSettings settings, out string error)
    {
        settings = null!;
        if (!SmtpHost.TryNormalize(draft.Host, out var host))
        {
            error = "host: Enter a host name or an IP address of at most 253 characters.";
            return false;
        }

        if (draft.Port is < 1 or > 65535)
        {
            error = "port: Enter a port from 1 to 65535.";
            return false;
        }

        if (draft.Security is not { } security)
        {
            error = "security: Choose STARTTLS, TLS, or None.";
            return false;
        }

        var username = string.IsNullOrEmpty(draft.Username) ? null : draft.Username;
        if (username is { Length: > MaxUsernameLength })
        {
            error = $"username: Enter a username of at most {MaxUsernameLength} characters.";
            return false;
        }

        if (username is not null && security == SmtpSecurity.None)
        {
            error = "username: A username needs STARTTLS or TLS.";
            return false;
        }

        // No username means no sign in: the password goes with it (AC-1).
        var password = username is null || string.IsNullOrEmpty(draft.Password) ? null : draft.Password;
        if (password is { Length: > MaxPasswordLength })
        {
            error = $"password: Enter a password of at most {MaxPasswordLength} characters.";
            return false;
        }

        var source = SmtpPasswordSource.None;
        if (username is not null)
        {
            if (password is not null)
            {
                source = SmtpPasswordSource.Given;
            }
            else if (MayReuseStoredPassword(stored, host, draft.Port, username))
            {
                source = SmtpPasswordSource.Stored;
            }
            else
            {
                error = "password: Enter the password again when you change the host, port, or username.";
                return false;
            }
        }

        if (!EmailAddress.TryNormalize(draft.FromEmail, out var fromEmail))
        {
            error = "fromEmail: Enter an email address of at most 320 characters.";
            return false;
        }

        var fromName = string.IsNullOrWhiteSpace(draft.FromName) ? null : draft.FromName.Trim();
        if (fromName is not null && (fromName.Length > MaxFromNameLength || fromName.AsSpan().IndexOfAny('\r', '\n') >= 0))
        {
            error = $"fromName: Enter a name of at most {MaxFromNameLength} characters on one line.";
            return false;
        }

        string? replyTo = null;
        if (!string.IsNullOrWhiteSpace(draft.ReplyTo) && !EmailAddress.TryNormalize(draft.ReplyTo, out replyTo))
        {
            error = "replyTo: Enter an email address of at most 320 characters.";
            return false;
        }

        settings = new ValidSmtpSettings(host, draft.Port, security, username, source, password, fromEmail, fromName, replyTo);
        error = "";
        return true;
    }

    /// <summary>
    /// A stored password is reused only for the very host, port, and username it was saved with (AC-1), so someone
    /// who can edit the settings but never saw the password can't have it sent to a server they run.
    /// </summary>
    public static bool MayReuseStoredPassword(StoredSmtpIdentity? stored, string host, int port, string username) =>
        stored is { HasPassword: true }
        && string.Equals(stored.Host, host, StringComparison.Ordinal)
        && stored.Port == port
        && string.Equals(stored.Username, username, StringComparison.Ordinal);

    /// <summary>The stored form of <paramref name="security"/>.</summary>
    public static string Wire(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => "starttls",
        SmtpSecurity.Tls => "tls",
        SmtpSecurity.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(security), security, null),
    };

    /// <summary>Reads a stored <c>security</c> value.</summary>
    public static SmtpSecurity ParseSecurity(string wire) => wire switch
    {
        "starttls" => SmtpSecurity.StartTls,
        "tls" => SmtpSecurity.Tls,
        "none" => SmtpSecurity.None,
        _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, null),
    };
}
