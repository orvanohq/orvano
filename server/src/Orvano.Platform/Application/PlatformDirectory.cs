using Microsoft.EntityFrameworkCore;
using Orvano.Platform.Contracts;
using Orvano.Platform.Domain;

namespace Orvano.Platform.Application;

/// <summary>
/// The module contracts other modules read through (spec 0003): project lookup (AC-4), API key checks (AC-5,
/// AC-12), browser origins (AC-13), and console roles (AC-9).
/// </summary>
internal sealed class PlatformDirectory(PlatformStore store, TimeProvider clock) : IProjectDirectory, IApiKeyVerifier, IWebOriginPolicy, IConsoleAccess
{
    public async Task<ProjectLookup> GetServableAsync(string projectId, CancellationToken ct)
    {
        var project = await GetAsync(projectId, ct);
        return project switch
        {
            { Kind: ProjectKind.App, Status: ProjectStatus.Active } => new ProjectLookup.Servable(project),
            { Kind: ProjectKind.App, Status: ProjectStatus.Provisioning or ProjectStatus.Failed } => new ProjectLookup.NotReady(),
            _ => new ProjectLookup.NotFound(),
        };
    }

    public Task<ProjectInfo?> GetAsync(string projectId, CancellationToken ct) =>
        string.IsNullOrEmpty(projectId)
            ? Task.FromResult<ProjectInfo?>(null)
            : store.ReadAsync(async (db, ct) =>
            {
                var row = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct);
                return row is null ? null : new ProjectInfo(row.Id, row.OrgId, row.Name, Statuses.Kind(row.Kind), Statuses.Project(row.Status), row.PurgeFailedAt);
            }, ct);

    public Task<ApiKeyVerification> VerifyAsync(string projectId, string secret, CancellationToken ct)
    {
        if (!ApiKeySecret.TryParse(secret, out var parsed) || string.IsNullOrEmpty(projectId)) return Task.FromResult(ApiKeyVerification.Invalid);

        return store.ReadAsync(async (db, ct) =>
        {
            var hash = parsed.Hash;
            var active = Statuses.Wire(ProjectStatus.Active);
            var app = Statuses.Wire(ProjectKind.App);
            var key = await (
                from k in db.ApiKeys
                join p in db.Projects on k.ProjectId equals p.Id
                where k.SecretHash == hash && p.Status == active && p.Kind == app
                select k).AsNoTracking().SingleOrDefaultAsync(ct);

            var now = clock.GetUtcNow();
            if (key is null || key.ProjectId != projectId || key.ExpiresAt is { } expiry && expiry <= now) return ApiKeyVerification.Invalid;

            if (key.LastUsedAt is null || key.LastUsedAt < now - ApiKeyUsage.Resolution)
            {
                var stale = now - ApiKeyUsage.Resolution;
                // Conditional, so concurrent calls write it at most once per resolution window.
                await db.ApiKeys.Where(k => k.Id == key.Id && (k.LastUsedAt == null || k.LastUsedAt < stale))
                    .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct);
            }

            return new ApiKeyVerification(true, key.Id, ApiKeyScopes.Parse(key.Scopes).Select(ApiKeyScopes.Wire).ToHashSet(StringComparer.Ordinal));
        }, ct);
    }

    public async Task<bool> AllowsAsync(string projectId, string origin, CancellationToken ct)
    {
        if (!WebOriginPattern.TryHostOf(origin, out _)) return false;

        var web = PlatformIdentifiers.Wire(PlatformType.Web);
        var identifiers = await store.ReadAsync((db, ct) =>
            db.Platforms.AsNoTracking().Where(p => p.ProjectId == projectId && p.Type == web).Select(p => p.Identifier).ToListAsync(ct), ct);
        return identifiers.Any(identifier => WebOriginPattern.TryParse(identifier, out var pattern, out _) && pattern.Matches(origin));
    }

    public async Task<bool> AllowsRedirectAsync(string projectId, Uri redirectUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(redirectUrl);
        if (!redirectUrl.IsAbsoluteUri || redirectUrl.UserInfo.Length > 0) return false;
        var host = redirectUrl.IdnHost.ToLowerInvariant();
        if (host.Length == 0 || host.EndsWith('.')) return false;

        var scheme = redirectUrl.Scheme;
        var https = scheme == Uri.UriSchemeHttps;
        var http = scheme == Uri.UriSchemeHttp;
        if (http && host is not ("localhost" or "127.0.0.1")) return false;

        var platforms = await store.ReadAsync((db, ct) =>
            db.Platforms.AsNoTracking().Where(p => p.ProjectId == projectId).Select(p => new { p.Type, p.Identifier }).ToListAsync(ct), ct);

        if (https || http)
        {
            if (redirectUrl.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4)) return false;
            var web = PlatformIdentifiers.Wire(PlatformType.Web);
            return platforms.Any(p => p.Type == web && WebOriginPattern.TryParse(p.Identifier, out var pattern, out _) && pattern.MatchesHost(host));
        }

        // A custom scheme names an app: its bundle ID or package name. Windows and Linux platforms have none.
        string[] apps = [PlatformIdentifiers.Wire(PlatformType.Ios), PlatformIdentifiers.Wire(PlatformType.Android), PlatformIdentifiers.Wire(PlatformType.Macos)];
        return platforms.Any(p => apps.Contains(p.Type) && string.Equals(p.Identifier, scheme, StringComparison.OrdinalIgnoreCase));
    }

    public Task<OrgRole?> GetOrgRoleAsync(Guid userId, Guid orgId, CancellationToken ct) =>
        store.ReadAsync(async (db, ct) => await db.RoleInAsync(userId, orgId, ct) is { } role ? Roles.Parse(role) : (OrgRole?)null, ct);

    public Task<OrgRole?> GetProjectRoleAsync(Guid userId, string projectId, CancellationToken ct) =>
        store.ReadAsync(async (db, ct) =>
        {
            var role = await (
                from p in db.Projects
                join m in db.Memberships on p.OrgId equals m.OrgId
                where p.Id == projectId && m.UserId == userId
                select m.Role).SingleOrDefaultAsync(ct);
            return role is null ? (OrgRole?)null : Roles.Parse(role);
        }, ct);
}
