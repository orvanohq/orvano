using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Messaging.Application;
using Orvano.Messaging.Data;
using Orvano.Messaging.Domain;

namespace Orvano.Messaging.Fixtures;

/// <summary>The install SMTP to seed: a server that needs no sign in (the scenarios' Mailpit).</summary>
internal sealed record FixtureInstallSmtp(string Host, int Port, SmtpSecurity Security, string FromEmail);

/// <summary>
/// Seeds the install's SMTP settings in the <c>Test</c> environment only (spec 0009, AC-29), so the shared scenarios
/// and the console's end to end tests can read real emails from Mailpit. Seeding twice changes nothing.
/// </summary>
internal static class MessagingFixtures
{
    public static Task SeedInstallSmtpAsync(MessagingStore store, Guid admin, FixtureInstallSmtp smtp, ILogger logger, CancellationToken ct) =>
        store.WriteAsync<Done>(async (uow, ct) =>
        {
            if (await uow.Db.SmtpSettings.AnyAsync(s => s.ProjectId == SmtpSettingsService.InstallProjectId, ct)) return new Done();

            var now = await uow.NowAsync(ct);
            uow.Db.SmtpSettings.Add(new SmtpSettingsRow
            {
                ProjectId = SmtpSettingsService.InstallProjectId,
                Host = smtp.Host,
                Port = smtp.Port,
                Security = SmtpSettingsRule.Wire(smtp.Security),
                FromEmail = smtp.FromEmail,
                UpdatedByUserId = admin,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await uow.Db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded the fixture install SMTP settings");
            return new Done();
        }, ct);
}
