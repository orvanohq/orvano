using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Core.Http;
using Orvano.Platform.Application;
using static Orvano.Platform.Endpoints.ApiMapping;
using Api = Orvano.Contract;

namespace Orvano.Platform.Endpoints;

/// <summary>
/// The console operations of row 7 (spec 0003), each a thin adapter: read the console user and the request, call a
/// use case, map the result. The host's console session check has already run for every <c>/v1/console</c> route.
/// Project scoped operations take the project from <c>X-Orvano-Project</c> (spec 0001).
/// </summary>
internal static class ConsoleEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        MapOrgs(v1);
        MapProjects(v1);
        MapApiKeys(v1);
        MapPlatforms(v1);
        MapInstall(v1);
    }

    private static void MapOrgs(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.ConsoleOrgsOperations.List.Route, async (HttpContext http, OrgService orgs, string? cursor, int? limit, CancellationToken ct) =>
            Ok(await orgs.ListAsync(User(http), cursor, limit, ct), page => new Api.OrgPage([.. page.Items.Select(Org)], page.NextCursor)))
            .WithName(Api.ConsoleOrgsOperations.List.Id);

        v1.MapPost(Api.ConsoleOrgsOperations.Create.Route, async (HttpContext http, OrgService orgs, Api.CreateOrgRequest request, CancellationToken ct) =>
            Created(await orgs.CreateAsync(User(http), request.Name, ct), Org))
            .WithName(Api.ConsoleOrgsOperations.Create.Id);

        v1.MapGet(Api.ConsoleOrgsOperations.Get.Route, async (HttpContext http, OrgService orgs, string orgId, CancellationToken ct) =>
            Guid.TryParse(orgId, out var id) ? Ok(await orgs.GetAsync(User(http), id, ct), Org) : Problem(Failure.OrgNotFound))
            .WithName(Api.ConsoleOrgsOperations.Get.Id);

        v1.MapPatch(Api.ConsoleOrgsOperations.Update.Route, async (HttpContext http, OrgService orgs, string orgId, Api.UpdateOrgRequest request, CancellationToken ct) =>
            Guid.TryParse(orgId, out var id) ? Ok(await orgs.RenameAsync(User(http), id, request.Name, ct), Org) : Problem(Failure.OrgNotFound))
            .WithName(Api.ConsoleOrgsOperations.Update.Id);

        v1.MapDelete(Api.ConsoleOrgsOperations.Delete.Route, async (HttpContext http, OrgService orgs, string orgId, CancellationToken ct) =>
            Guid.TryParse(orgId, out var id) ? Ok(await orgs.DeleteAsync(User(http), id, ct), Org) : Problem(Failure.OrgNotFound))
            .WithName(Api.ConsoleOrgsOperations.Delete.Id);

        v1.MapPost(Api.ConsoleOrgsOperations.Restore.Route, async (HttpContext http, OrgService orgs, string orgId, CancellationToken ct) =>
            Guid.TryParse(orgId, out var id) ? Ok(await orgs.RestoreAsync(User(http), id, ct), Org) : Problem(Failure.OrgNotFound))
            .WithName(Api.ConsoleOrgsOperations.Restore.Id);
    }

    private static void MapProjects(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.ConsoleProjectsOperations.List.Route, async (HttpContext http, ProjectService projects, string orgId, string? cursor, int? limit, CancellationToken ct) =>
            Guid.TryParse(orgId, out var id)
                ? Ok(await projects.ListAsync(User(http), id, cursor, limit, ct), page => new Api.ProjectPage([.. page.Items.Select(Project)], page.NextCursor))
                : Problem(Failure.OrgNotFound))
            .WithName(Api.ConsoleProjectsOperations.List.Id);

        v1.MapPost(Api.ConsoleProjectsOperations.Create.Route, async (HttpContext http, ProjectService projects, string orgId, Api.CreateProjectRequest request, CancellationToken ct) =>
            Guid.TryParse(orgId, out var id) ? Created(await projects.CreateAsync(User(http), id, request.Name, ct), Project) : Problem(Failure.OrgNotFound))
            .WithName(Api.ConsoleProjectsOperations.Create.Id);

        v1.MapGet(Api.ConsoleProjectsOperations.Get.Route, async (HttpContext http, ProjectService projects, CancellationToken ct) =>
            Ok(await projects.GetAsync(User(http), ProjectOf(http), ct), Project))
            .WithName(Api.ConsoleProjectsOperations.Get.Id);

        v1.MapPatch(Api.ConsoleProjectsOperations.Update.Route, async (HttpContext http, ProjectService projects, Api.UpdateProjectRequest request, CancellationToken ct) =>
            Ok(await projects.RenameAsync(User(http), ProjectOf(http), request.Name, ct), Project))
            .WithName(Api.ConsoleProjectsOperations.Update.Id);

        v1.MapDelete(Api.ConsoleProjectsOperations.Delete.Route, async (HttpContext http, ProjectService projects, CancellationToken ct) =>
            Ok(await projects.DeleteAsync(User(http), ProjectOf(http), ct), Project))
            .WithName(Api.ConsoleProjectsOperations.Delete.Id);

        v1.MapPost(Api.ConsoleProjectsOperations.Restore.Route, async (HttpContext http, ProjectService projects, CancellationToken ct) =>
            Ok(await projects.RestoreAsync(User(http), ProjectOf(http), ct), Project))
            .WithName(Api.ConsoleProjectsOperations.Restore.Id);

        v1.MapPost(Api.ConsoleProjectsOperations.RetryProvisioning.Route, async (HttpContext http, ProjectService projects, CancellationToken ct) =>
            Ok(await projects.RetryProvisioningAsync(User(http), ProjectOf(http), ct), Project))
            .WithName(Api.ConsoleProjectsOperations.RetryProvisioning.Id);

        v1.MapPost(Api.ConsoleProjectsOperations.RetryPurge.Route, async (HttpContext http, ProjectService projects, CancellationToken ct) =>
            Ok(await projects.RetryPurgeAsync(User(http), ProjectOf(http), ct), Project))
            .WithName(Api.ConsoleProjectsOperations.RetryPurge.Id);
    }

    private static void MapApiKeys(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.ConsoleApiKeysOperations.List.Route, async (HttpContext http, ApiKeyService keys, string? cursor, int? limit, CancellationToken ct) =>
            Ok(await keys.ListAsync(User(http), ProjectOf(http), cursor, limit, ct), page => new Api.ApiKeyPage([.. page.Items.Select(ApiKey)], page.NextCursor)))
            .WithName(Api.ConsoleApiKeysOperations.List.Id);

        v1.MapPost(Api.ConsoleApiKeysOperations.Create.Route, async (HttpContext http, ApiKeyService keys, Api.CreateApiKeyRequest request, CancellationToken ct) =>
            Created(await keys.CreateAsync(User(http), ProjectOf(http), request.Name, request.Scopes, request.ExpiresAt, ct), CreatedApiKey))
            .WithName(Api.ConsoleApiKeysOperations.Create.Id);

        v1.MapDelete(Api.ConsoleApiKeysOperations.Delete.Route, async (HttpContext http, ApiKeyService keys, string keyId, CancellationToken ct) =>
            NoContent(await keys.DeleteAsync(User(http), ProjectOf(http), keyId, ct)))
            .WithName(Api.ConsoleApiKeysOperations.Delete.Id);
    }

    private static void MapPlatforms(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.ConsolePlatformsOperations.List.Route, async (HttpContext http, PlatformService platforms, string? cursor, int? limit, CancellationToken ct) =>
            Ok(await platforms.ListAsync(User(http), ProjectOf(http), cursor, limit, ct), page => new Api.PlatformPage([.. page.Items.Select(ApiPlatform)], page.NextCursor)))
            .WithName(Api.ConsolePlatformsOperations.List.Id);

        v1.MapPost(Api.ConsolePlatformsOperations.Create.Route, async (HttpContext http, PlatformService platforms, Api.CreatePlatformRequest request, CancellationToken ct) =>
            Created(await platforms.CreateAsync(User(http), ProjectOf(http), ToPlatformType(request.Type), request.Name, request.Identifier, ct), ApiPlatform))
            .WithName(Api.ConsolePlatformsOperations.Create.Id);

        v1.MapPatch(Api.ConsolePlatformsOperations.Update.Route, async (HttpContext http, PlatformService platforms, string platformId, Api.UpdatePlatformRequest request, CancellationToken ct) =>
            Ok(await platforms.UpdateAsync(User(http), ProjectOf(http), platformId, request.Name, request.Identifier, ct), ApiPlatform))
            .WithName(Api.ConsolePlatformsOperations.Update.Id);

        v1.MapDelete(Api.ConsolePlatformsOperations.Delete.Route, async (HttpContext http, PlatformService platforms, string platformId, CancellationToken ct) =>
            NoContent(await platforms.DeleteAsync(User(http), ProjectOf(http), platformId, ct)))
            .WithName(Api.ConsolePlatformsOperations.Delete.Id);
    }

    private static void MapInstall(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.ConsoleInstallOperations.GetSettings.Route, async (HttpContext http, InstallService install, CancellationToken ct) =>
            Ok(await install.GetAsync(User(http), ct), InstallSettings))
            .WithName(Api.ConsoleInstallOperations.GetSettings.Id);

        v1.MapPatch(Api.ConsoleInstallOperations.UpdateSettings.Route, async (HttpContext http, InstallService install, Api.UpdateInstallSettingsRequest request, CancellationToken ct) =>
            request.ConsoleSignup switch
            {
                Api.ConsoleSignupMode.Invite => Ok(await install.UpdateAsync(User(http), InstallService.Invite, ct), InstallSettings),
                Api.ConsoleSignupMode.Open => Ok(await install.UpdateAsync(User(http), InstallService.Open, ct), InstallSettings),
                _ => Problem(Failure.Invalid("consoleSignup is invite or open.")),
            })
            .WithName(Api.ConsoleInstallOperations.UpdateSettings.Id);
    }

    private static Guid User(HttpContext http) => ConsoleUser.Get(http);

    private static string? ProjectOf(HttpContext http) =>
        http.Request.Headers[OrvanoHeaders.Project] is [{ Length: > 0 } project] ? project : null;
}
