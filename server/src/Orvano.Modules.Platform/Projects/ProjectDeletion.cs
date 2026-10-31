using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Signals;

namespace Orvano.Modules.Platform.Projects;

/// <summary>
/// Spec 0011 LIF-2, spec 0017 PJT-2, PJT-7: flips a project to <c>deleting</c>, queues its teardown, and drops the
/// directory's cached answer, all in the caller's transaction.
/// </summary>
internal sealed class ProjectDeletion(PlatformDbContext db, IJobQueue jobs, ICacheInvalidator invalidator, TimeProvider clock)
{
    /// <summary>Call inside <c>InTransactionAsync</c>, with <paramref name="project"/> tracked.</summary>
    public async Task MarkAsync(Project project, CancellationToken cancellationToken)
    {
        project.Status = ProjectStatus.Deleting;
        project.DeletionRequestedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await jobs.EnqueueAsync(
            db,
            new ProjectTeardown(),
            new EnqueueOptions { DedupeKey = $"{ProjectTeardown.Kind}:{project.Id}", ProjectId = project.Id },
            cancellationToken);

        // PJT-4: a status flip drops the directory's cached answer.
        invalidator.Invalidate(db, ProjectDirectory.Tag(project.Id));
    }
}
