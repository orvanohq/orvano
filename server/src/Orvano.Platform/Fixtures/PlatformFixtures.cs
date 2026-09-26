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

/// <summary>
/// Seeds the shared scenarios' projects and API keys in the <c>Test</c> environment only (spec 0003, build task 6),
/// through the same code the console uses. The first fixture console user plays the install's first account: an
/// install admin who owns the <c>Fixtures</c> org the projects live in. Seeding twice changes nothing.
/// </summary>
internal static class PlatformFixtures
{
    public const string OrgName = "Fixtures";

    public static Task SeedAsync(
        PlatformStore store, Guid owner, IReadOnlyList<FixtureProject> projects, IReadOnlyList<FixtureApiKey> keys, ILogger logger, CancellationToken ct) =>
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

            logger.LogInformation("Seeded {Projects} fixture project(s) and {Keys} fixture API key(s)", projects.Count, keys.Count);
            return new Done();
        }, ct);
}
