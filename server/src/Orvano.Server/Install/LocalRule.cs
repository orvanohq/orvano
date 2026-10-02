using System.Globalization;
using System.Security.Cryptography;

namespace Orvano.Server.Install;

/// <summary>
/// The rules of a local install, <c>orvano install --local</c> (spec 0011, AC-1 to AC-4): Orvano on your own
/// machine, on <c>http://localhost:&lt;port&gt;</c>, with a Mailpit inbox. A folder is either local or a server
/// install, and never turns into the other.
/// </summary>
internal static class LocalRule
{
    public const int DefaultPort = 7700;
    public const int MailpitPort = 8025;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    public const string LocalKey = "ORVANO_LOCAL";
    public const string LocalPortKey = "ORVANO_LOCAL_PORT";
    public const string ComposeFileKey = "COMPOSE_FILE";
    public const string ComposePathSeparatorKey = "COMPOSE_PATH_SEPARATOR";
    public const string ComposeProjectNameKey = "COMPOSE_PROJECT_NAME";
    public const string SmtpUrlKey = "ORVANO_INSTALL_SMTP_URL";
    public const string SmtpFromKey = "ORVANO_INSTALL_SMTP_FROM";

    /// <summary>Compose reads both files from <c>.env</c>; the explicit separator makes the same value work on Windows.</summary>
    public const string ComposeFiles = InstallFiles.ComposeFile + ":" + InstallFiles.LocalComposeFile;

    /// <summary>Mailpit's SMTP port on the compose network; it is never published.</summary>
    public const string SmtpUrl = "smtp://mailpit:1025";

    public const string SmtpFrom = "orvano@local.test";

    /// <summary>True when the folder's <c>.env</c> is a local install's.</summary>
    public static bool IsLocal(EnvFile? env) => string.Equals(env?.Get(LocalKey), "true", StringComparison.Ordinal);

    /// <summary>True when the folder's <c>.env</c> is a server install's: it has a version but is not local (AC-3).</summary>
    public static bool IsServerInstall(EnvFile? env) => env?.HasValue(InstallPlan.Version) == true && !IsLocal(env);

    /// <summary>A port from 1024 to 65535 that is not Mailpit's inbox port; null and an error otherwise.</summary>
    public static int? ParsePort(string value, out string? error)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < MinPort or > MaxPort)
        {
            error = $"--port must be a whole number from {MinPort} to {MaxPort}, got '{value}'.";
            return null;
        }

        if (port == MailpitPort)
        {
            error = $"--port can't be {MailpitPort}: the local mail inbox uses it. Pick another port.";
            return null;
        }

        error = null;
        return port;
    }

    /// <summary>The port this run uses: the flag, else the one already in <c>.env</c>, else <see cref="DefaultPort"/>.</summary>
    public static int? Port(string? flag, EnvFile? existing, out string? error)
    {
        if (flag is not null) return ParsePort(flag, out error);

        if (existing?.Get(LocalPortKey) is { Length: > 0 } current)
        {
            var port = ParsePort(current, out _);
            error = port is null ? $"{LocalPortKey} in .env is '{current}', which is not a usable port. Pass --port to set a new one." : null;
            return port;
        }

        error = null;
        return DefaultPort;
    }

    /// <summary>
    /// <c>http://localhost:&lt;port&gt;</c>. Always <c>localhost</c>, never <c>127.0.0.1</c>: the gateway answers only
    /// that host, and token issuers are built from this URL.
    /// </summary>
    public static string PublicUrl(int port) => $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// <c>orvano-local-&lt;6 random hex&gt;</c>, generated once per folder, so two local folders (or a server install on
    /// the same machine) never share containers or a database volume (AC-2).
    /// </summary>
    public static string NewProjectName() => "orvano-local-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
}
