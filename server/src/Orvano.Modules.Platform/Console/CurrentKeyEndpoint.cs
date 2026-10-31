using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform.Console;

/// <summary>
/// <c>GET /v1/projects/{projectId}/keys/current</c> (spec 0017 SDK-1): a key proves itself, reading nothing but the caller.
/// </summary>
internal static class CurrentKeyEndpoint
{
    public static RouteGroupBuilder MapCurrentKey(this RouteGroupBuilder project)
    {
        // Out of the public contract until milestone 2 adds its scenarios: the runners must merge first (SDK-5), and a
        // contract operation with no live scenario fails CNF-9. XTN-2 still needs its SDK name and errors at startup.
        project.MapGet("/keys/current", Get)
            .RequireProjectKey()
            .WithSdk("keys", "current")
            .WithErrors()
            .WithMeter("platform.keys.read")
            .ExcludeFromDescription()
            .WithName("Platform_GetCurrentKey")
            .WithSummary("Read the key this request carries.")
            .WithDescription("Answers the key's project, type, and scopes, so an SDK can prove its key works.");
        return project;
    }

    internal static Ok<CurrentKeyResponse> Get(ICallerContext caller)
    {
        Caller current = caller.Current;
        return TypedResults.Ok(new CurrentKeyResponse(
            current.ProjectId!.Value,
            current.Kind == CallerKind.SecretKey ? KeyType.Secret : KeyType.Publishable,
            [.. current.Scopes.Order(StringComparer.Ordinal)]));
    }
}
