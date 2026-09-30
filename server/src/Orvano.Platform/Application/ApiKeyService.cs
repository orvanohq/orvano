using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Contract;
using Orvano.Core.Paging;
using Orvano.Platform.Data;
using Orvano.Platform.Domain;

namespace Orvano.Platform.Application;

/// <summary>A new key and its secret, which leaves the server only in this one response.</summary>
internal sealed record CreatedKey(ApiKeyRow Key, ApiKeySecret Secret);

/// <summary>API key use cases (AC-11, AC-12): list, create, delete. The database never sees a secret.</summary>
internal sealed class ApiKeyService(PlatformStore store, ILogger<ApiKeyService> logger)
{
    public Task<Outcome<Page<ApiKeyRow>>> ListAsync(Guid userId, string? projectId, string? cursor, int? limit, CancellationToken ct) =>
        store.ReadAsync<Outcome<Page<ApiKeyRow>>>(async (db, ct) =>
        {
            if (PageCursor.Limit(limit) is not { } size) return Failure.Invalid($"limit must be 1 to {PageCursor.MaxLimit}.");
            var (project, _, failure) = await ProjectService.FindAsync(db, userId, projectId, ConsoleAction.View, ct);
            if (failure is not null) return failure;

            var query = db.ApiKeys.Where(k => k.ProjectId == project!.Id);
            if (cursor is not null)
            {
                if (!PageCursor.TryDecode(cursor, out var after) || !Guid.TryParse(after.Id, out var afterId)) return Failure.InvalidCursor;
                query = query.Where(k => k.CreatedAt > after.CreatedAt || k.CreatedAt == after.CreatedAt && k.Id.CompareTo(afterId) > 0);
            }

            var rows = await query.OrderBy(k => k.CreatedAt).ThenBy(k => k.Id).Take(size + 1).AsNoTracking().ToListAsync(ct);
            var items = rows.Take(size).ToList();
            var next = rows.Count > size ? PageCursor.Encode(new PagePosition(items[^1].CreatedAt, items[^1].Id.ToString())) : null;
            return new Page<ApiKeyRow>(items, next);
        }, ct);

    public Task<Outcome<CreatedKey>> CreateAsync(
        Guid userId, string? projectId, string? name, IReadOnlyList<ApiKeyScope>? scopes, DateTimeOffset? expiresAt, CancellationToken ct) =>
        store.WriteAsync<CreatedKey>(async (uow, ct) =>
        {
            if (!Names.TryNormalize(name, out var keyName)) return Failure.Invalid($"A key name is 1 to {Names.MaxLength} characters.");
            if (!ApiKeyScopes.TryNormalize(scopes, out var wireScopes))
                return Failure.Invalid($"Give at least one scope, each one of: {string.Join(", ", ApiKeyScopes.Known.Order(StringComparer.Ordinal))}.");

            var (project, _, failure) = await ProjectService.FindForChangeAsync(uow, userId, projectId, ConsoleAction.CreateApiKey, ct);
            if (failure is not null) return failure;
            if (await ProjectService.LiveProjectFailureAsync(uow, project!.Id, ct) is { } frozen) return frozen;

            var now = await uow.NowAsync(ct);
            if (expiresAt is { } expiry && expiry <= now) return Failure.Invalid("expiresAt must be in the future.");

            var secret = ApiKeySecret.New();
            var key = await AddKeyAsync(uow, project!.Id, keyName, secret, wireScopes, expiresAt, userId, Actor.User(userId), now, ct);
            logger.LogInformation("Console user {UserId} created API key {KeyId} in project {ProjectId}", userId, key.Id, project.Id);
            return new CreatedKey(key, secret);
        }, ct);

    public Task<Outcome<Done>> DeleteAsync(Guid userId, string? projectId, string keyId, CancellationToken ct) =>
        store.WriteAsync<Done>(async (uow, ct) =>
        {
            var (project, role, failure) = await ProjectService.FindForChangeAsync(uow, userId, projectId, ConsoleAction.View, ct);
            if (failure is not null) return failure;
            if (!Guid.TryParse(keyId, out var id)) return Failure.NotFound("API key");

            var key = await uow.Db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Id == id && k.ProjectId == project!.Id, ct);
            if (key is null) return Failure.NotFound("API key");
            if (!ConsolePermissions.CanDeleteApiKey(role!.Value, key.CreatedByUserId == userId)) return Failure.Forbidden;

            await uow.Db.ApiKeys.Where(k => k.Id == id).ExecuteDeleteAsync(ct);
            await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.KeyDeleted, Actor.User(userId), key.Id.ToString(),
                new Dictionary<string, string> { ["projectId"] = key.ProjectId, ["keyId"] = key.Id.ToString() }, key.ProjectId, ct: ct);
            logger.LogInformation("Console user {UserId} deleted API key {KeyId} in project {ProjectId}", userId, key.Id, key.ProjectId);
            return new Done();
        }, ct);

    /// <summary>Stores the key's hash and prefix only, with its event (no secret, no hash in the payload).</summary>
    internal static async Task<ApiKeyRow> AddKeyAsync(
        UnitOfWork uow, string projectId, string name, ApiKeySecret secret, string[] scopes, DateTimeOffset? expiresAt,
        Guid createdBy, Actor actor, DateTimeOffset now, CancellationToken ct)
    {
        var key = new ApiKeyRow
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            Name = name,
            Prefix = secret.Prefix,
            SecretHash = secret.Hash,
            Scopes = scopes,
            ExpiresAt = expiresAt,
            CreatedByUserId = createdBy,
            CreatedAt = now,
        };
        uow.Db.ApiKeys.Add(key);
        await uow.Db.SaveChangesAsync(ct);
        await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.KeyCreated, actor, key.Id.ToString(),
            new Dictionary<string, string> { ["projectId"] = projectId, ["keyId"] = key.Id.ToString() }, projectId, ct: ct);
        return key;
    }
}
