using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Platform.Application;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;

namespace Orvano.Platform.Fixtures;

/// <summary>A project to seed: its fixed ID and name.</summary>
internal sealed record FixtureProject(string Id, string Name);

/// <summary>An API key to seed: its project, its known secret, and its scopes (wire values).</summary>
internal sealed record FixtureApiKey(string Project, string Secret, IReadOnlyList<string> Scopes);

/// <summary>A platform to seed: its project, type, and identifier (a web host pattern for <c>web</c>).</summary>
internal sealed record FixturePlatform(string Project, PlatformType Type, string Identifier);

/// <summary>
/// Seeds the shared scenarios' projects, API keys, and platforms in the <c>Test</c> environment only (spec 0003, build task 6),
/// through the same code the console uses. The first fixture console user plays the install's first account: an
/// install admin who owns the <c>Fixtures</c> org the projects live in. Seeding twice changes nothing.
/// </summary>
internal static class PlatformFixtures
{
    public const string OrgName = "Fixtures";

    public static Task SeedAsync(
        PlatformStore store, Guid owner, IReadOnlyList<FixtureProject> projects, IReadOnlyList<FixtureApiKey> keys,
        IReadOnlyList<FixturePlatform> platforms, ILogger logger, CancellationToken ct) =>
        store.WriteAsync<Done>(async (uow, ct) =>
        {
            var db = uow.Db;
            var actor = Actor.User(owner);
            var now = await uow.NowAsync(ct);

            if (!await db.InstallAdmins.AnyAsync(ct))
            {
                db.InstallAdmins.Add(new InstallAdminRow { UserId = owner, CreatedAt = now });
                await db.SaveChangesAsync(ct);
            }

            var active = Statuses.Wire(OrgStatus.Active);
            var org = await db.Orgs.SingleOrDefaultAsync(o => o.CreatedByUserId == owner && o.Name == OrgName && o.Status == active, ct)
                ?? await OrgService.AddOrgAsync(uow, OrgName, owner, actor, ct);

            foreach (var project in projects)
            {
                if (await db.Projects.AnyAsync(p => p.Id == project.Id, ct)) continue;
                await ProjectService.AddProjectAsync(uow, project.Id, org.Id, project.Name, owner, actor, ct);
            }

            foreach (var key in keys)
            {
                if (!ApiKeySecret.TryParse(key.Secret, out var secret)) throw new InvalidOperationException("A fixture API key secret has the wrong shape.");
                var hash = secret.Hash;
                if (await db.ApiKeys.AnyAsync(k => k.SecretHash == hash, ct)) continue;
                await ApiKeyService.AddKeyAsync(uow, key.Project, "Fixture key", secret, [.. key.Scopes], null, owner, actor, now, ct);
            }

            foreach (var platform in platforms)
            {
                if (!PlatformIdentifiers.TryNormalize(platform.Type, platform.Identifier, out var identifier, out var problem))
                    throw new InvalidOperationException($"A fixture platform identifier is not valid: {problem}");
                var type = PlatformIdentifiers.Wire(platform.Type);
                if (await db.Platforms.AnyAsync(p => p.ProjectId == platform.Project && p.Type == type && p.Identifier == identifier, ct)) continue;
                db.Platforms.Add(new PlatformRow
                {
                    Id = Guid.CreateVersion7(),
                    ProjectId = platform.Project,
                    Type = type,
                    Name = "Fixture " + type,
                    Identifier = identifier,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                await db.SaveChangesAsync(ct);
            }

            logger.LogInformation("Seeded {Projects} fixture project(s), {Keys} API key(s), and {Platforms} platform(s)", projects.Count, keys.Count, platforms.Count);
            return new Done();
        }, ct);
}
