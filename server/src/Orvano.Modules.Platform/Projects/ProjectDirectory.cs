using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Projects;

namespace Orvano.Modules.Platform.Projects;

/// <summary>Spec 0017 PJT-3: <c>active</c> projects, cached for 60 seconds at most (spec 0007 CCH-2, COR-1).</summary>
internal sealed class ProjectDirectory(PlatformDbContext db, HybridCache cache) : IProjectDirectory
{
    private static readonly HybridCacheEntryOptions Expiry = new() { Expiration = TimeSpan.FromSeconds(60), LocalCacheExpiration = TimeSpan.FromSeconds(60) };

    /// <summary>The tag every project status or origin change invalidates (PJT-4).</summary>
    public static string Tag(Guid projectId) => $"orvano:project:{projectId}";

    public ValueTask<ActiveProject?> FindActiveAsync(Guid projectId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            $"orvano:platform:project:{projectId}",
            (db, projectId),
            static async (state, token) =>
            {
                ProjectRow? row = await state.db.Projects.AsNoTracking()
                    .Where(x => x.Id == state.projectId && x.Status == ProjectStatus.Active)
                    .Select(x => new ProjectRow(x.OrganizationId))
                    .SingleOrDefaultAsync(token);
                if (row is null)
                {
                    return null;
                }

                // Deviation DAT-2a: FLT-1's read of project_settings, before any project context exists (spec 0017 PJT-3).
                string[] origins = await state.db.ProjectSettings.IgnoreQueryFilters([ProjectFilter.Name]).AsNoTracking()
                    .Where(x => x.ProjectId == state.projectId)
                    .Select(x => x.AllowedOrigins)
                    .SingleAsync(token);
                return new ActiveProject(state.projectId, row.OrganizationId, origins);
            },
            Expiry,
            [Tag(projectId), $"orvano:platform:cors:{projectId}"],
            cancellationToken);

    private sealed record ProjectRow(Guid OrganizationId);
}
