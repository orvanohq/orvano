using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Keys;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Console;

/// <summary>API keys (spec 0017 AKY-1 to AKY-8).</summary>
internal static class ApiKeyEndpoints
{
    private static readonly Error NotActive =
        Error.Conflict(PlatformErrorCodes.ApiKeyNotActive, "The key is revoked or expired, so it can't be regenerated.");

    public static RouteGroupBuilder MapApiKeys(this RouteGroupBuilder console)
    {
        console.MapGet("/projects/{projectId:guid}/keys", ListAsync)
            .RequireConsolePermission(ConsolePermission.KeysRead)
            .WithName("Platform_ListApiKeys")
            .WithSummary("List a project's API keys.")
            .WithDescription("Lists every key, newest first, with its status. Never holds a key itself.")
            .WithErrors(ErrorCodes.ValidationFailed, ErrorCodes.InvalidCursor)
            .WithPaging();

        console.MapPost("/projects/{projectId:guid}/keys", CreateAsync)
            .RequireConsolePermission(ConsolePermission.KeysWrite)
            .RequireRecentReauth()
            .WithName("Platform_CreateApiKey")
            .WithSummary("Create an API key.")
            .WithDescription("Answers 201 with the full key, exactly once. Only its hash, prefix, and last 4 characters are "
                + "stored. A secret key needs at least one scope; a publishable key defaults to every client scope.")
            .WithErrors(ErrorCodes.ValidationFailed, PlatformErrorCodes.ProjectNotActive, PlatformErrorCodes.ReauthRequired)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        console.MapPatch("/projects/{projectId:guid}/keys/{keyId:guid}", RenameAsync)
            .RequireConsolePermission(ConsolePermission.KeysWrite)
            .WithName("Platform_RenameApiKey")
            .WithSummary("Rename an API key.")
            .WithDescription("Changes only the name; a key's type, scopes, and expiry never change.")
            .WithErrors(ErrorCodes.ValidationFailed, PlatformErrorCodes.ProjectNotActive)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        console.MapPost("/projects/{projectId:guid}/keys/{keyId:guid}/revoke", RevokeAsync)
            .RequireConsolePermission(ConsolePermission.KeysWrite)
            .RequireRecentReauth()
            .WithName("Platform_RevokeApiKey")
            .WithSummary("Revoke an API key.")
            .WithDescription("The key stops working on its next request. Revoking it again changes nothing.")
            .WithErrors(PlatformErrorCodes.ReauthRequired);

        console.MapPost("/projects/{projectId:guid}/keys/{keyId:guid}/regenerate", RegenerateAsync)
            .RequireConsolePermission(ConsolePermission.KeysWrite)
            .RequireRecentReauth()
            .WithName("Platform_RegenerateApiKey")
            .WithSummary("Replace an API key.")
            .WithDescription("Revokes the key and answers 201 with a new one of the same type, name, scopes, and expiry. "
                + "The old key stops at once.")
            .WithErrors(PlatformErrorCodes.ApiKeyNotActive, PlatformErrorCodes.ProjectNotActive, PlatformErrorCodes.ReauthRequired)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return console;
    }

    internal static async Task<Results<Ok<Page<ApiKeyResponse>>, ErrorProblemResult>> ListAsync(
        Guid projectId,
        [AsParameters] PageQuery page,
        PlatformDbContext db,
        IMembershipLookup memberships,
        ICallerContext caller,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        string cursorQuery = CursorCodec.Query(http.Request);
        IQueryable<ApiKey> rows = db.ApiKeys.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (page.Cursor is { } cursor)
        {
            Result<CursorKeys> keys = CursorCodec.Decode(cursor, cursorQuery, CursorKey.Uuid);
            if (!keys.IsSuccess)
            {
                return keys.Error.ToProblem();
            }

            Guid before = keys.Value.GetUuid(0);
            rows = rows.Where(x => x.Id.CompareTo(before) < 0);
        }

        int limit = page.Size;
        List<ApiKey> found = await rows.OrderByDescending(x => x.Id).Take(limit + 1).ToListAsync(cancellationToken);
        Guid organizationId = await OrganizationOfAsync(db, projectId, cancellationToken);
        string? role = await memberships.FindRoleAsync(caller.Current.OperatorId!.Value, organizationId, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        return TypedResults.Ok(Page.From<ApiKey, ApiKeyResponse>(
            found,
            limit,
            cursorQuery,
            row => new JsonArray(row.Id.ToString("D")),
            row => ToResponse(row, role, now)));
    }

    internal static async Task<Results<Created<CreatedApiKeyResponse>, ErrorProblemResult>> CreateAsync(
        Guid projectId,
        CreateApiKeyRequest request,
        PlatformDbContext db,
        IMembershipLookup memberships,
        ConsoleAudit audit,
        ICallerContext caller,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        KeyType type = request.Type == "secret" ? KeyType.Secret : KeyType.Publishable;
        Result<string[]> scopes = ScopesFor(type, request.Scopes);
        if (!scopes.IsSuccess)
        {
            return scopes.Error.ToProblem();
        }

        if (!await IsActiveAsync(db, projectId, cancellationToken))
        {
            return PlatformErrors.ProjectNotActive.ToProblem();
        }

        DateTimeOffset now = clock.GetUtcNow();
        Guid operatorId = caller.Current.OperatorId!.Value;
        NewApiKeyToken token = ApiKeyToken.Create(type);
        ApiKey key = NewKey(projectId, type, request.Name.Trim(), token, scopes.Value, operatorId, request.ExpiresInDays is { } days ? now.AddDays(days) : null);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);

        Guid organizationId = await OrganizationOfAsync(db, projectId, cancellationToken);
        await audit.RecordAsync(http, AuditActions.ApiKeyCreated, operatorId, "api_key", key.Id, organizationId, projectId);
        string? role = await memberships.FindRoleAsync(operatorId, organizationId, cancellationToken);
        return TypedResults.Created((string?)null, ToCreated(key, token, role, now));
    }

    internal static async Task<Results<Ok<ApiKeyResponse>, ErrorProblemResult>> RenameAsync(
        Guid projectId,
        Guid keyId,
        RenameApiKeyRequest request,
        PlatformDbContext db,
        IMembershipLookup memberships,
        ICallerContext caller,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!await IsActiveAsync(db, projectId, cancellationToken))
        {
            return PlatformErrors.ProjectNotActive.ToProblem();
        }

        // AKY-8: a label, so last write wins, and ExecuteUpdate needs no version: last_used_at moves xmin all the time.
        DateTimeOffset now = clock.GetUtcNow();
        string name = request.Name.Trim();
        int changed = await db.ApiKeys
            .Where(x => x.ProjectId == projectId && x.Id == keyId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Name, name).SetProperty(x => x.UpdatedAt, now), cancellationToken);
        if (changed == 0)
        {
            return AccessErrors.NotFound.ToProblem();
        }

        ApiKey key = await db.ApiKeys.AsNoTracking().SingleAsync(x => x.ProjectId == projectId && x.Id == keyId, cancellationToken);
        Guid organizationId = await OrganizationOfAsync(db, projectId, cancellationToken);
        string? role = await memberships.FindRoleAsync(caller.Current.OperatorId!.Value, organizationId, cancellationToken);
        return TypedResults.Ok(ToResponse(key, role, now));
    }

    internal static async Task<Results<Ok<ApiKeyResponse>, ErrorProblemResult>> RevokeAsync(
        Guid projectId,
        Guid keyId,
        PlatformDbContext db,
        IMembershipLookup memberships,
        ConsoleAudit audit,
        ICallerContext caller,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // AKY-6: the condition makes a second revoke change nothing, and it works at any project status (AKY-5).
        DateTimeOffset now = clock.GetUtcNow();
        int revoked = await db.ApiKeys
            .Where(x => x.ProjectId == projectId && x.Id == keyId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.UpdatedAt, now), cancellationToken);
        ApiKey? key = await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == keyId, cancellationToken);
        if (key is null)
        {
            return AccessErrors.NotFound.ToProblem();
        }

        Guid operatorId = caller.Current.OperatorId!.Value;
        Guid organizationId = await OrganizationOfAsync(db, projectId, cancellationToken);
        if (revoked > 0)
        {
            await audit.RecordAsync(http, AuditActions.ApiKeyRevoked, operatorId, "api_key", keyId, organizationId, projectId);
        }

        string? role = await memberships.FindRoleAsync(operatorId, organizationId, cancellationToken);
        return TypedResults.Ok(ToResponse(key, role, now));
    }

    internal static async Task<Results<Created<CreatedApiKeyResponse>, ErrorProblemResult>> RegenerateAsync(
        Guid projectId,
        Guid keyId,
        PlatformDbContext db,
        IMembershipLookup memberships,
        ConsoleAudit audit,
        ICallerContext caller,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!await IsActiveAsync(db, projectId, cancellationToken))
        {
            return PlatformErrors.ProjectNotActive.ToProblem();
        }

        ApiKey? current = await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == keyId, cancellationToken);
        if (current is null)
        {
            return AccessErrors.NotFound.ToProblem();
        }

        Guid operatorId = caller.Current.OperatorId!.Value;
        NewApiKeyToken token = ApiKeyToken.Create(current.Type);
        ApiKey key = NewKey(projectId, current.Type, current.Name, token, current.Scopes, operatorId, current.ExpiresAt);
        DateTimeOffset now = clock.GetUtcNow();

        // AKY-7: of two at once, the row lock lets one through and the other changes no row.
        bool replaced = await db.InTransactionAsync(
            async work =>
            {
                int revoked = await db.ApiKeys
                    .Where(x => x.ProjectId == projectId && x.Id == keyId && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.UpdatedAt, now), work);
                if (revoked == 0)
                {
                    return false;
                }

                db.ApiKeys.Add(key);
                await db.SaveChangesAsync(work);
                return true;
            },
            cancellationToken);
        if (!replaced)
        {
            return NotActive.ToProblem();
        }

        Guid organizationId = await OrganizationOfAsync(db, projectId, cancellationToken);
        await audit.RecordAsync(http, AuditActions.ApiKeyRegenerated, operatorId, "api_key", key.Id, organizationId, projectId);
        string? role = await memberships.FindRoleAsync(operatorId, organizationId, cancellationToken);
        return TypedResults.Created((string?)null, ToCreated(key, token, role, now));
    }

    // AKY-1: only an omitted list asks for the default, and only a publishable key has one.
    private static Result<string[]> ScopesFor(KeyType type, IReadOnlyList<string>? requested)
    {
        if (requested is null && type == KeyType.Publishable)
        {
            return Scopes.CeilingFor(type).Order(StringComparer.Ordinal).ToArray();
        }

        return requested is { Count: > 0 }
            ? Scopes.Validate(type, requested)
            : ConsoleFields.Invalid(Scopes.Field, FieldCodes.Required, "Choose at least one scope.");
    }

    private static ApiKey NewKey(
        Guid projectId, KeyType type, string name, NewApiKeyToken token, string[] scopes, Guid operatorId, DateTimeOffset? expiresAt) => new()
        {
            ProjectId = projectId,
            Type = type,
            Name = name,
            Hash = token.Hash,
            Prefix = token.Prefix,
            Last4 = token.Last4,
            Scopes = scopes,
            CreatedByOperatorId = operatorId,
            ExpiresAt = expiresAt,
        };

    private static async Task<bool> IsActiveAsync(PlatformDbContext db, Guid projectId, CancellationToken cancellationToken) =>
        await db.Projects.AsNoTracking().AnyAsync(x => x.Id == projectId && x.Status == ProjectStatus.Active, cancellationToken);

    private static Task<Guid> OrganizationOfAsync(PlatformDbContext db, Guid projectId, CancellationToken cancellationToken) =>
        db.Projects.AsNoTracking().Where(x => x.Id == projectId).Select(x => x.OrganizationId).SingleAsync(cancellationToken);

    private static CreatedApiKeyResponse ToCreated(ApiKey key, NewApiKeyToken token, string? role, DateTimeOffset now)
    {
        ApiKeyResponse response = ToResponse(key, role, now);
        return new CreatedApiKeyResponse(
            response.Id,
            response.ProjectId,
            response.Type,
            response.Name,
            response.Prefix,
            response.Last4,
            response.Scopes,
            response.Status,
            response.LastUsedAt,
            response.ExpiresAt,
            response.RevokedAt,
            response.CreatedAt,
            response.AllowedActions,
            token.Key);
    }

    private static ApiKeyResponse ToResponse(ApiKey key, string? role, DateTimeOffset now) => new(
        key.Id,
        key.ProjectId,
        key.Type,
        key.Name,
        key.Prefix,
        key.Last4,
        key.Scopes,
        StatusOf(key, now),
        key.LastUsedAt,
        key.ExpiresAt,
        key.RevokedAt,
        key.CreatedAt,
        ConsolePermissions.Allowed(role, ConsolePermissions.OnKey));

    private static ApiKeyStatus StatusOf(ApiKey key, DateTimeOffset now)
    {
        if (key.RevokedAt is not null)
        {
            return ApiKeyStatus.Revoked;
        }

        return key.ExpiresAt is { } expiresAt && expiresAt <= now ? ApiKeyStatus.Expired : ApiKeyStatus.Active;
    }
}
