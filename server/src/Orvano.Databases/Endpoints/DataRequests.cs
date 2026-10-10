using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Orvano.Auth.Contracts;
using Orvano.Core.Http;
using Orvano.Databases.Application;
using Orvano.Databases.Contracts;
using Orvano.Platform.Contracts;
using Api = Orvano.Contract;

namespace Orvano.Databases.Endpoints;

/// <summary>A request that passed its caller checks: the project, and who acts.</summary>
internal sealed record DataCaller(string ProjectId, DataActor Actor);

/// <summary>
/// The caller checks of every data operation, in spec 0015 AC-22's order: the project (spec 0003 AC-4), the
/// credential (401), the scope (403), and for callers without a key the row access decision (403), all before the
/// database or table is looked up. Console operations (AC-24) check the session's member and role instead.
/// </summary>
internal static class DataRequests
{
    /// <summary>An operation only an API key may call (databases and tables): any other caller gets 401.</summary>
    public static async Task<(DataCaller? Caller, IResult? Refusal)> ServerKeyAsync(HttpContext http, string scope)
    {
        var (projectId, projectRefusal) = await ProjectAsync(http);
        if (projectId is null) return (null, projectRefusal);
        if (!http.Request.Headers.ContainsKey(OrvanoHeaders.ApiKey)) return (null, InvalidApiKey());

        var resolved = await http.RequestServices.GetRequiredService<IAppCallerResolver>().ResolveAsync(http, projectId, scope, http.RequestAborted);
        return resolved switch
        {
            AppCallerResult.Rejected rejected => (null, rejected.Problem),
            AppCallerResult.Resolved { Caller: AppCaller.ApiKey key } => (new DataCaller(projectId, DataActor.Key(key.KeyId)), null),
            _ => (null, InvalidApiKey()),
        };
    }

    /// <summary>
    /// A row operation (AC-22): an API key needs <paramref name="scope"/>; anyone else goes through
    /// <see cref="IRowAccess"/>, which runs before the database and table are looked up.
    /// </summary>
    public static async Task<(DataCaller? Caller, IResult? Refusal)> RowCallerAsync(
        HttpContext http, string scope, string database, string table, RowOperation operation)
    {
        var (projectId, projectRefusal) = await ProjectAsync(http);
        if (projectId is null) return (null, projectRefusal);

        var services = http.RequestServices;
        var resolved = await services.GetRequiredService<IAppCallerResolver>().ResolveAsync(http, projectId, scope, http.RequestAborted);
        if (resolved is AppCallerResult.Rejected rejected) return (null, rejected.Problem);
        var caller = ((AppCallerResult.Resolved)resolved).Caller;
        if (caller is AppCaller.ApiKey key) return (new DataCaller(projectId, DataActor.Key(key.KeyId)), null);

        var decision = await services.GetRequiredService<IRowAccess>().DecideAsync(caller, projectId, database, table, operation, http.RequestAborted);
        if (decision != RowAccessDecision.Allow) return (null, ApiMapping.Problem(http, Failure.AccessDenied));
        return caller is AppCaller.AppUser user
            ? (new DataCaller(projectId, DataActor.AppUser(user.UserId)), null)
            : (null, ApiMapping.Problem(http, Failure.AccessDenied));
    }

    /// <summary>
    /// A console operation (AC-24): the signed in console user must be a member of the project's org (else 404, so
    /// existence never leaks), the project servable, and a change needs an owner or developer (403 before any lookup).
    /// </summary>
    public static async Task<(DataCaller? Caller, IResult? Refusal)> ConsoleAsync(HttpContext http, bool change)
    {
        if (http.Request.Headers[OrvanoHeaders.Project] is not [{ Length: > 0 } projectId])
            return (null, ApiMapping.Problem(http, Failure.ProjectHeaderMissing));

        var userId = ConsoleUser.Get(http);
        var services = http.RequestServices;
        if (await services.GetRequiredService<IConsoleAccess>().GetProjectRoleAsync(userId, projectId, http.RequestAborted) is not { } role)
            return (null, ApiMapping.Problem(http, Failure.ProjectNotFound));
        if (change && role is not (OrgRole.Owner or OrgRole.Developer)) return (null, ApiMapping.Problem(http, Failure.Forbidden));

        var lookup = await services.GetRequiredService<IProjectDirectory>().GetServableAsync(projectId, http.RequestAborted);
        return lookup switch
        {
            ProjectLookup.Servable => (new DataCaller(projectId, DataActor.ConsoleUser(userId)), null),
            ProjectLookup.NotReady => (null, ApiMapping.Problem(http, Failure.ProjectNotReady)),
            _ => (null, ApiMapping.Problem(http, Failure.ProjectNotFound)),
        };
    }

    /// <summary>
    /// The database a call names (AC-1, AC-2): 404 <c>database_not_found</c> for a slug of no database in the project,
    /// 409 <c>database_not_ready</c> unless <paramref name="mustBeActive"/> is false.
    /// </summary>
    public static async Task<Outcome<DatabaseRef>> DatabaseAsync(HttpContext http, string projectId, string slug, bool mustBeActive = true)
    {
        var database = await http.RequestServices.GetRequiredService<DatabaseDirectory>().FindAsync(projectId, slug, http.RequestAborted);
        if (database is null) return Failure.DatabaseNotFound;
        if (mustBeActive && database.State != DatabaseState.Active) return Failure.DatabaseNotReady;
        return database;
    }

    /// <summary>
    /// <c>X-Orvano-Project</c>: 400 when missing, 404 or 409 when not servable (spec 0003, AC-4), and 403
    /// <c>origin_not_allowed</c> when a browser's <c>Origin</c> is none of the project's web platforms.
    /// </summary>
    private static async Task<(string? ProjectId, IResult? Refusal)> ProjectAsync(HttpContext http)
    {
        if (http.Request.Headers[OrvanoHeaders.Project] is not [{ Length: > 0 } projectId])
            return (null, ApiMapping.Problem(http, Failure.ProjectHeaderMissing));

        var services = http.RequestServices;
        switch (await services.GetRequiredService<IProjectDirectory>().GetServableAsync(projectId, http.RequestAborted))
        {
            case ProjectLookup.Servable:
                break;
            case ProjectLookup.NotReady:
                return (null, ApiMapping.Problem(http, Failure.ProjectNotReady));
            default:
                return (null, ApiMapping.Problem(http, Failure.ProjectNotFound));
        }

        if (http.Request.Headers.Origin is [{ } origin]
            && !await services.GetRequiredService<IWebOriginPolicy>().AllowsAsync(projectId, origin, http.RequestAborted))
        {
            return (null, ApiProblem.Result(StatusCodes.Status403Forbidden, Api.ErrorCode.OriginNotAllowed, "This origin is not a web platform of the project."));
        }

        return (projectId, null);
    }

    private static IResult InvalidApiKey() =>
        ApiProblem.Result(StatusCodes.Status401Unauthorized, Api.ErrorCode.InvalidApiKey, "Send a valid API key of this project as X-Orvano-Key.");
}
