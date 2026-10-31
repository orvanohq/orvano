using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Keys;

/// <summary>
/// The <c>Orvano-Key</c> header (spec 0017 AKY-9, AKY-10). Key rows and their project's status are read on every request
/// and never cached, so a revoke or a project delete holds on the next one (KEY-3).
/// </summary>
internal sealed class ApiKeyResolver(PlatformDbContext db, ApiKeyUsage usage, IOptions<KeysOptions> options, TimeProvider clock) : ICallerResolver
{
    private const string HeaderOrigin = "Origin";

    public int Order => CallerResolverOrder.ApiKey;

    public async ValueTask<CallerResolution> ResolveAsync(CallerResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        HttpRequest request = context.Http.Request;
        if (!request.Headers.TryGetValue(OrvanoHeaders.Key, out Microsoft.Extensions.Primitives.StringValues values))
        {
            return new CallerResolution.NotPresent();
        }

        // RSV-3: a key and a console session are independent credentials.
        if (context.Http.GetEndpoint() is { } endpoint && AccessDeclaration.RequiresOperator(endpoint))
        {
            return new CallerResolution.Failed("API key on an operator route");
        }

        string key = values.ToString();
        if (!ApiKeyToken.IsWellFormed(key))
        {
            return new CallerResolution.Failed("malformed key");
        }

        if (key.StartsWith(ApiKeyToken.SecretPrefix, StringComparison.Ordinal))
        {
            if (request.Headers.ContainsKey(OrvanoHeaders.Authorization))
            {
                return new CallerResolution.Failed("secret key with a bearer token"); // KEY-2
            }

            if (request.Headers.ContainsKey(HeaderOrigin) && !options.Value.AllowSecretKeyFromBrowser)
            {
                return new CallerResolution.Failed("secret key with an Origin header"); // KEY-1
            }
        }

        byte[] hash = ApiKeyToken.Hash(key);

        // Deviation DAT-2a: FLT-1's API key lookup by hash; no project context exists yet.
        var row = await (
                from apiKey in db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name]).AsNoTracking()
                join project in db.Projects.AsNoTracking() on apiKey.ProjectId equals project.Id
                where apiKey.Hash == hash
                select new { apiKey.Id, apiKey.ProjectId, apiKey.Type, apiKey.Scopes, apiKey.ExpiresAt, apiKey.RevokedAt, project.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return new CallerResolution.Failed("unknown key");
        }

        if (row.RevokedAt is not null)
        {
            return new CallerResolution.Failed($"revoked key {row.Id}");
        }

        if (row.ExpiresAt is { } expiresAt && expiresAt <= clock.GetUtcNow())
        {
            return new CallerResolution.Failed($"expired key {row.Id}");
        }

        if (row.Status != ProjectStatus.Active)
        {
            return new CallerResolution.Failed($"key {row.Id} of a project that isn't active");
        }

        await usage.RecordAsync(db, row.Id, cancellationToken);
        return new CallerResolution.Resolved(
            row.Type == KeyType.Secret ? Caller.SecretKey(row.ProjectId, row.Scopes) : Caller.Guest(row.ProjectId, row.Scopes));
    }
}
