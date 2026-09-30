using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Messaging.Domain;
using Orvano.Platform.Contracts;

namespace Orvano.Messaging.Application;

/// <summary>
/// Queues the console's invite email through the install's SMTP (spec 0009, AC-23), in the transaction that creates
/// the invitation. Its rows belong to the reserved project <c>console</c> and count toward the install cap.
/// </summary>
internal sealed class InvitationMailer(EmailQueue queue, ILogger<InvitationMailer> logger) : IConsoleInvitationMailer
{
    public async Task<bool> QueueAsync(NpgsqlTransaction tx, InvitationEmail email, CancellationToken ct)
    {
        const string project = SmtpSettingsService.InstallProjectId;
        if (await queue.AdmitAsync(tx, project, ct) is { } refusal)
        {
            if (refusal is QueueRefusal.RateLimited)
                logger.LogWarning("An invite email was not queued: the console reached the install's hourly email limit");
            return false;
        }

        var role = email.Role switch
        {
            OrgRole.Owner => "owner",
            OrgRole.Developer => "developer",
            OrgRole.Viewer => "viewer",
            _ => throw new ArgumentOutOfRangeException(nameof(email), email.Role, null),
        };
        var content = InvitationEmailContent.Build(email.OrgName, email.InviterName ?? email.InviterEmail, role, email.Url, email.ExpiresAt);
        var id = await queue.InsertAsync(tx, project, EmailTemplateNames.ConsoleInvitation, email.To, content, ct);
        logger.LogInformation("Queued invite email {EmailId}", id);
        return true;
    }
}
