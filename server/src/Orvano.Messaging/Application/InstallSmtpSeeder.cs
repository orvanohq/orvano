using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Core.Secrets;
using Orvano.Messaging.Domain;

namespace Orvano.Messaging.Application;

/// <summary>
/// Writes the install SMTP row (project <c>console</c>) from <c>ORVANO_INSTALL_SMTP_URL</c> when there is none yet
/// (spec 0011, AC-4). <c>ON CONFLICT DO NOTHING</c> means it never overwrites a row, so a console change wins, and two
/// <c>api</c> replicas starting together can't race. The password is sealed exactly as a console save seals it.
/// </summary>
internal sealed class InstallSmtpSeeder(MessagingStore store, SecretBox secrets, MessagingSettings settings, ILogger<InstallSmtpSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        if (settings.InstallSmtp is not { } seed) return;

        var id = SmtpSettingsService.InstallProjectId;
        var ciphertext = seed.Password is null
            ? null
            : secrets.Encrypt(Encoding.UTF8.GetBytes(seed.Password), SmtpSettingsService.PasswordBinding(id));
        var security = SmtpSettingsRule.Wire(seed.Security);

        var seeded = await store.WriteAsync<bool>(async (uow, ct) =>
        {
            var rows = await uow.Db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO orvano.messaging_smtp_settings
                    (project_id, host, port, security, username, password_ciphertext, from_email, updated_by_user_id)
                VALUES ({id}, {seed.Host}, {seed.Port}, {security}, {seed.Username}, {ciphertext}, {seed.FromEmail}, {InstallSmtpSeed.ConfigurationUserId})
                ON CONFLICT (project_id) DO NOTHING
                """,
                ct);
            return rows > 0;
        }, ct);

        // Never the host, user, or From: only that it happened.
        if (seeded.Value) logger.LogInformation("Seeded the install SMTP settings from {Setting}", InstallSmtpSeed.UrlSetting);
    }
}
