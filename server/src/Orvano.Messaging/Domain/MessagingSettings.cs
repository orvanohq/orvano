namespace Orvano.Messaging.Domain;

/// <summary>The module's two settings (spec 0009, AC-28), read and checked when a role starts.</summary>
/// <param name="AllowPrivateHosts">True lets a project's SMTP host be, or resolve to, a private network address.</param>
/// <param name="InstallHourlyLimit">Emails per project per hour through the install's SMTP server.</param>
internal sealed record MessagingSettings(bool AllowPrivateHosts, int InstallHourlyLimit)
{
    public const string AllowPrivateHostsSetting = "ORVANO_SMTP_ALLOW_PRIVATE_HOSTS";
    public const string InstallHourlyLimitSetting = "ORVANO_EMAIL_INSTALL_HOURLY_LIMIT";
    public const int MaxInstallHourlyLimit = 1_000_000;
    public const int DefaultInstallHourlyLimit = 200;
}
