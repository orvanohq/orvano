using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Console;
using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Projects;

namespace Orvano.Modules.Platform.Projects;

/// <summary>Removes a project that's <c>deleting</c> (spec 0011 LIF-2, spec 0017 PJT-8). The project is the job's own.</summary>
public sealed record ProjectTeardown : IJob
{
    public static string Kind => "project.teardown";
}

/// <summary>
/// Spec 0017 PJT-8: cancels the project's jobs, calls each participant's teardown in reverse <c>MigrationOrder</c>, which is
/// the order modules register in, then deletes the row, whose cascade takes <c>project_settings</c> and <c>api_key</c>.
/// Every step tolerates running twice, so a retried job resumes.
/// </summary>
internal sealed class ProjectTeardownHandler(
    PlatformDbContext db, IJobQueue jobs, IEnumerable<IProjectLifecycleParticipant> participants, ConsoleAudit audit) : IJobHandler<ProjectTeardown>
{
    public async Task HandleAsync(ProjectTeardown job, JobContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Guid projectId = context.ProjectId!.Value; // RequiresProject
        var project = await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.OrganizationId, x.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            return; // a run before this one finished
        }

        if (project.Status != ProjectStatus.Deleting)
        {
            throw new PermanentJobFailureException($"Project {projectId} is {project.Status}, not deleting.");
        }

        await jobs.CancelProjectJobsAsync(projectId, context.Id, cancellationToken);
        foreach (IProjectLifecycleParticipant participant in participants.Reverse())
        {
            await participant.TeardownAsync(projectId, cancellationToken);
        }

        await db.Projects.Where(x => x.Id == projectId).ExecuteDeleteAsync(cancellationToken);
        await audit.RecordSystemAsync(AuditActions.ProjectDeleted, "project", projectId, project.OrganizationId, projectId);
    }
}

/// <summary>The platform module's job payloads (spec 0002 JSN-1).</summary>
[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ProjectTeardown))]
internal sealed partial class PlatformJsonContext : JsonSerializerContext;
