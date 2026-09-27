using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;
using Orvano.Core.Http;
using static Orvano.Auth.Endpoints.ApiMapping;
using Api = Orvano.Contract;

namespace Orvano.Auth.Endpoints;

/// <summary>
/// The <c>keys</c> service (AC-20): a project's JWKS and discovery document, with no credentials and a five minute
/// public cache. The project in the path must be servable, so <c>console</c> is 404.
/// </summary>
internal static class KeysEndpoints
{
    private const string Cache = "public, max-age=300";

    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapGet(Api.KeysOperations.GetJwks.Route, async (HttpContext http, SigningKeys keys, CancellationToken ct) =>
        {
            var rows = await keys.ReadPublicAsync(PublicRequests.Project(http), ct);
            http.Response.Headers.CacheControl = Cache;
            return TypedResults.Ok(new Api.Jwks([.. rows.Select(Jwk)]));
        })
            .WithName(Api.KeysOperations.GetJwks.Id)
            .RequireProjectInPath();

        v1.MapGet(Api.KeysOperations.GetOpenIdConfiguration.Route, (HttpContext http, PublicUrl publicUrl) =>
        {
            var issuer = AccessTokenClaims.IssuerFor(publicUrl.Origin, PublicRequests.Project(http));
            http.Response.Headers.CacheControl = Cache;
            return TypedResults.Ok(new Api.OpenIdConfiguration(
                Issuer: issuer,
                JwksUri: issuer + "/.well-known/jwks.json",
                IdTokenSigningAlgValuesSupported: [AccessTokenClaims.Algorithm],
                SubjectTypesSupported: ["public"],
                ResponseTypesSupported: ["token"]));
        })
            .WithName(Api.KeysOperations.GetOpenIdConfiguration.Id)
            .RequireProjectInPath();
    }
}
