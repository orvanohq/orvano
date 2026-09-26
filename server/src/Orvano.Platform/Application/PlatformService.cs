using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Core.Paging;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;

namespace Orvano.Platform.Application;

/// <summary>Platform use cases (AC-13): the web hosts and app IDs allowed to use a project.</summary>
internal sealed class PlatformService(PlatformStore store, ILogger<PlatformService> logger)
{
    private const string UniqueIndex = "platform_platforms_identifier_key";

    public Task<Outcome<Page<PlatformRow>>> ListAsync(Guid userId, string? projectId, string? cursor, int? limit, CancellationToken ct) =>
        store.ReadAsync<Outcome<Page<PlatformRow>>>(async (db, ct) =>
        {
            if (PageCursor.Limit(limit) is not { } size) return Failure.Invalid($"limit must be 1 to {PageCursor.MaxLimit}.");
            var (project, _, failure) = await ProjectService.FindAsync(db, userId, projectId, ConsoleAction.View, ct);
            if (failure is not null) return failure;

            var query = db.Platforms.Where(p => p.ProjectId == project!.Id);
            if (cursor is not null)
            {
                if (!PageCursor.TryDecode(cursor, out var after) || !Guid.TryParse(after.Id, out var afterId)) return Failure.InvalidCursor;
                query = query.Where(p => p.CreatedAt > after.CreatedAt || p.CreatedAt == after.CreatedAt && p.Id.CompareTo(afterId) > 0);
            }

            var rows = await query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Take(size + 1).AsNoTracking().ToListAsync(ct);
            var items = rows.Take(size).ToList();
            var next = rows.Count > size ? PageCursor.Encode(new PagePosition(items[^1].CreatedAt, items[^1].Id.ToString())) : null;
            return new Page<PlatformRow>(items, next);
        }, ct);

    public Task<Outcome<PlatformRow>> CreateAsync(Guid userId, string? projectId, PlatformType? type, string? name, string? identifier, CancellationToken ct) =>
        Unique(store.WriteAsync<PlatformRow>(async (uow, ct) =>
        {
            if (type is not { } platformType) return Failure.Invalid("Give a known platform type.");
            if (!Names.TryNormalize(name, out var platformName)) return Failure.Invalid($"A platform name is 1 to {Names.MaxLength} characters.");
            if (!PlatformIdentifiers.TryNormalize(platformType, identifier, out var normalized, out var problem)) return Failure.Invalid(problem);

            var (project, _, failure) = await ProjectService.FindAsync(uow.Db, userId, projectId, ConsoleAction.ManagePlatforms, ct);
            if (failure is not null) return failure;

            var now = await uow.NowAsync(ct);
            var platform = new PlatformRow
            {
                Id = Guid.CreateVersion7(),
                ProjectId = project!.Id,
                Type = PlatformIdentifiers.Wire(platformType),
                Name = platformName,
                Identifier = normalized,
                CreatedAt = now,
                UpdatedAt = now,
            };
            uow.Db.Platforms.Add(platform);
            await uow.Db.SaveChangesAsync(ct);
            await WriteEventAsync(uow, PlatformEvents.PlatformCreated, userId, platform, null, ct);
            logger.LogInformation("Console user {UserId} added platform {PlatformId} to project {ProjectId}", userId, platform.Id, project.Id);
            return platform;
        }, ct));

    public Task<Outcome<PlatformRow>> UpdateAsync(Guid userId, string? projectId, string platformId, string? name, string? identifier, CancellationToken ct) =>
        Unique(store.WriteAsync<PlatformRow>(async (uow, ct) =>
        {
            var (project, _, failure) = await ProjectService.FindAsync(uow.Db, userId, projectId, ConsoleAction.ManagePlatforms, ct);
            if (failure is not null) return failure;
            if (!Guid.TryParse(platformId, out var id)) return Failure.NotFound("platform");
            var platform = await uow.Db.Platforms.SingleOrDefaultAsync(p => p.Id == id && p.ProjectId == project!.Id, ct);
            if (platform is null) return Failure.NotFound("platform");

            var changed = new List<string>();
            if (name is not null)
            {
                if (!Names.TryNormalize(name, out var platformName)) return Failure.Invalid($"A platform name is 1 to {Names.MaxLength} characters.");
                if (platform.Name != platformName) changed.Add("name");
                platform.Name = platformName;
            }

            if (identifier is not null)
            {
                if (!PlatformIdentifiers.TryNormalize(PlatformIdentifiers.Parse(platform.Type), identifier, out var normalized, out var problem))
                    return Failure.Invalid(problem);
                if (platform.Identifier != normalized) changed.Add("identifier");
                platform.Identifier = normalized;
            }

            if (changed.Count > 0)
            {
                platform.UpdatedAt = await uow.NowAsync(ct);
                await uow.Db.SaveChangesAsync(ct);
                await WriteEventAsync(uow, PlatformEvents.PlatformUpdated, userId, platform, changed, ct);
                logger.LogInformation("Console user {UserId} changed platform {PlatformId} of project {ProjectId}", userId, platform.Id, platform.ProjectId);
            }

            return platform;
        }, ct));

    public Task<Outcome<Done>> DeleteAsync(Guid userId, string? projectId, string platformId, CancellationToken ct) =>
        store.WriteAsync<Done>(async (uow, ct) =>
        {
            var (project, _, failure) = await ProjectService.FindAsync(uow.Db, userId, projectId, ConsoleAction.ManagePlatforms, ct);
            if (failure is not null) return failure;
            if (!Guid.TryParse(platformId, out var id)) return Failure.NotFound("platform");
            var platform = await uow.Db.Platforms.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.ProjectId == project!.Id, ct);
            if (platform is null) return Failure.NotFound("platform");

            await uow.Db.Platforms.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            await WriteEventAsync(uow, PlatformEvents.PlatformDeleted, userId, platform, null, ct);
            logger.LogInformation("Console user {UserId} removed platform {PlatformId} from project {ProjectId}", userId, platform.Id, platform.ProjectId);
            return new Done();
        }, ct);

    /// <summary>The unique index on (project, type, lower(identifier)) turns a duplicate into a 400, not a 500.</summary>
    private static async Task<Outcome<PlatformRow>> Unique(Task<Outcome<PlatformRow>> write)
    {
        try
        {
            return await write;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueIndex })
        {
            return Failure.Invalid("This project already has a platform of that type with that identifier.");
        }
    }

    private static Task WriteEventAsync(UnitOfWork uow, string type, Guid userId, PlatformRow platform, IReadOnlyList<string>? changed, CancellationToken ct) =>
        PlatformEvents.WriteAsync(uow.Tx, type, Actor.User(userId), platform.Id.ToString(),
            new Dictionary<string, string> { ["projectId"] = platform.ProjectId, ["platformId"] = platform.Id.ToString() },
            platform.ProjectId, changed, ct);
}
