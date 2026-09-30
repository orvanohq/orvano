using Microsoft.EntityFrameworkCore;
using Orvano.Messaging.Data;
using Orvano.Messaging.Domain;

namespace Orvano.Messaging.Application;

/// <summary>The SMTP settings a project sends through right now: its own row, else the install's (AC-4).</summary>
/// <param name="Row">The settings.</param>
/// <param name="Source"><c>project</c> or <c>install</c>, as stored in <c>smtp_source</c>.</param>
internal sealed record EffectiveSmtp(SmtpSettingsRow Row, string Source)
{
    /// <summary>The effective settings of <paramref name="projectId"/>, or null when neither it nor the install has any.</summary>
    public static async Task<EffectiveSmtp?> FindAsync(MessagingDbContext db, string projectId, CancellationToken ct)
    {
        const string install = SmtpSettingsService.InstallProjectId;
        var rows = await db.SmtpSettings.AsNoTracking().Where(s => s.ProjectId == projectId || s.ProjectId == install).ToListAsync(ct);
        var row = rows.SingleOrDefault(r => r.ProjectId == projectId) ?? rows.SingleOrDefault(r => r.ProjectId == install);
        return row is null ? null : new EffectiveSmtp(row, row.ProjectId == install ? SmtpSources.Install : SmtpSources.Project);
    }
}
