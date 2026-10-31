using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Projects;
using Orvano.SharedKernel.Signals;

namespace Orvano.Modules.Platform.Projects;

/// <summary>
/// Spec 0011 TXN-1, spec 0017 PJT-1: commit the project as <c>creating</c>, run each participant in its own transaction,
/// then flip to <c>active</c>. The one way a project is made, for the API and the bootstrap command alike. A participant
/// that throws sends the project to teardown (PJT-2), and the error surfaces.
/// </summary>
internal sealed class ProjectCreation(
    PlatformDbContext db, IEnumerable<IProjectLifecycleParticipant> participants, ProjectDeletion deletion, ICacheInvalidator invalidator)
{
    public async Task<Project> CreateAsync(Guid organizationId, string name, string[] allowedOrigins, CancellationToken cancellationToken)
    {
        var project = new Project { OrganizationId = organizationId, Name = name };
        var settings = new ProjectSettings { ProjectId = project.Id, AllowedOrigins = allowedOrigins };
        await db.InTransactionAsync(
            async token =>
            {
                db.Projects.Add(project);
                db.ProjectSettings.Add(settings);
                await db.SaveChangesAsync(token);
            },
            cancellationToken);

        try
        {
            foreach (IProjectLifecycleParticipant participant in participants)
            {
                await participant.CreateAsync(project.Id, cancellationToken);
            }
        }
        catch (Exception)
        {
            // Not the caller's token: a request that's gone must still leave the project on its way out.
            await db.InTransactionAsync(token => deletion.MarkAsync(project, token), CancellationToken.None);
            throw;
        }

        return await db.InTransactionAsync(
            async token =>
            {
                Project row = await db.Projects.SingleAsync(x => x.Id == project.Id, token);
                row.Status = ProjectStatus.Active;
                await db.SaveChangesAsync(token);

                // PJT-4: a status flip drops the directory's cached answer.
                invalidator.Invalidate(db, ProjectDirectory.Tag(row.Id));
                return row;
            },
            cancellationToken);
    }
}
