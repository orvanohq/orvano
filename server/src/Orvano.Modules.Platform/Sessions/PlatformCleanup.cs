using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Orvano.Modules.Platform.Console;
using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Projects;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;

namespace Orvano.Modules.Platform.Sessions;

/// <summary>The hourly platform sweep (spec 0017 CLN-1, spec 0011 RET-1).</summary>
public sealed record PlatformCleanup : IJob
{
    public const string ScheduleName = "platform.cleanup";

    /// <summary>Hourly, at 41 minutes past.</summary>
    public const string Cron = "41 * * * *";

    public static string Kind => "platform.cleanup";
}

/// <summary>
/// Deletes sessions 7 days after they end and reset tokens 1 day after they expire or are used, and sends a project stuck
/// in <c>creating</c> for over an hour to teardown (PJT-2). All of it is safe to run twice.
/// </summary>
internal sealed class PlatformCleanupHandler(
    PlatformDbContext db, ProjectDeletion deletion, ConsoleAudit audit, TimeProvider clock, ILogger<PlatformCleanupHandler> logger) : IJobHandler<PlatformCleanup>
{
    public static readonly TimeSpan SessionRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan ResetTokenRetention = TimeSpan.FromDays(1);
    public static readonly TimeSpan StuckAfter = TimeSpan.FromHours(1);

    public async Task HandleAsync(PlatformCleanup job, JobContext context, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset sessionsBefore = now - SessionRetention;
        int sessions = await db.OperatorSessions
            .Where(x => x.ExpiresAt < sessionsBefore || x.RevokedAt < sessionsBefore)
            .ExecuteDeleteAsync(cancellationToken);
        DateTimeOffset tokensBefore = now - ResetTokenRetention;
        int tokens = await db.OperatorResetTokens
            .Where(x => x.ExpiresAt < tokensBefore || x.UsedAt < tokensBefore)
            .ExecuteDeleteAsync(cancellationToken);

        DateTimeOffset stuckBefore = now - StuckAfter;
        List<Guid> stuck = await db.Projects.AsNoTracking()
            .Where(x => x.Status == ProjectStatus.Creating && x.CreatedAt < stuckBefore)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (Guid projectId in stuck)
        {
            await SendToTeardownAsync(projectId, cancellationToken);
        }

        logger.PlatformCleanedUp(sessions, tokens, stuck.Count);
    }

    private async Task SendToTeardownAsync(Guid projectId, CancellationToken cancellationToken)
    {
        Guid? organizationId = await db.InTransactionAsync<Guid?>(
            async token =>
            {
                // A creation that finished or failed since the read above leaves it as it is.
                await db.Database.ExecuteSqlAsync($"SELECT 1 FROM platform.project WHERE id = {projectId} FOR UPDATE", token);
                Project? project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId && x.Status == ProjectStatus.Creating, token);
                if (project is null)
                {
                    return null;
                }

                await deletion.MarkAsync(project, token);
                return project.OrganizationId;
            },
            cancellationToken);
        if (organizationId is { } organization)
        {
            await audit.RecordSystemAsync(AuditActions.ProjectDeleteRequested, "project", projectId, organization, projectId);
        }
    }
}
