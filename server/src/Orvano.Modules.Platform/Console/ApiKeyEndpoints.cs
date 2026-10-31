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

namespace Orvano.Modules.Platform.Console;

/// <summary>API keys (spec 0017 AKY-1 to AKY-5). Revoke, regenerate, and rename come with build plan step 15.</summary>
internal static class ApiKeyEndpoints
{
    private static readonly Error ProjectNotActive =
        Error.Conflict(PlatformErrorCodes.ProjectNotActive, "The project is being created or deleted, so keys can't change now.");

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
            .WithName("Platform_CreateApiKey")
            .WithSummary("Create an API key.")
            .WithDescription("Answers 201 with the full key, exactly once. Only its hash, prefix, and last 4 characters are "
                + "stored. A secret key needs at least one scope; a publishable key defaults to every client scope.")
            .WithErrors(ErrorCodes.ValidationFailed, PlatformErrorCodes.ProjectNotActive)
            .ProducesValidationProblem()
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
        string? role = await RoleAsync(db, memberships, caller, projectId, cancellationToken);
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

        ProjectStatus status = await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => x.Status)
            .SingleAsync(cancellationToken);
        if (status != ProjectStatus.Active)
        {
            return ProjectNotActive.ToProblem();
        }

        DateTimeOffset now = clock.GetUtcNow();
        Guid operatorId = caller.Current.OperatorId!.Value;
        NewApiKeyToken token = ApiKeyToken.Create(type);
        var key = new ApiKey
        {
            ProjectId = projectId,
            Type = type,
            Name = request.Name.Trim(),
            Hash = token.Hash,
            Prefix = token.Prefix,
            Last4 = token.Last4,
            Scopes = scopes.Value,
            CreatedByOperatorId = operatorId,
            ExpiresAt = request.ExpiresInDays is { } days ? now.AddDays(days) : null,
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);

        string? role = await RoleAsync(db, memberships, caller, projectId, cancellationToken);
        Guid organizationId = await db.Projects.AsNoTracking().Where(x => x.Id == projectId).Select(x => x.OrganizationId).SingleAsync(cancellationToken);
        await audit.RecordAsync(http, AuditActions.ApiKeyCreated, operatorId, "api_key", key.Id, organizationId, projectId);
        ApiKeyResponse response = ToResponse(key, role, now);
        return TypedResults.Created((string?)null, new CreatedApiKeyResponse(
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
            token.Key));
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

    private static async Task<string?> RoleAsync(
        PlatformDbContext db, IMembershipLookup memberships, ICallerContext caller, Guid projectId, CancellationToken cancellationToken)
    {
        Guid organizationId = await db.Projects.AsNoTracking().Where(x => x.Id == projectId).Select(x => x.OrganizationId).SingleAsync(cancellationToken);
        return await memberships.FindRoleAsync(caller.Current.OperatorId!.Value, organizationId, cancellationToken);
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
