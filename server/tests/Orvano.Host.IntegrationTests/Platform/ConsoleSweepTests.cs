using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0006 TEN-5, spec 0017 SES-7, AC-7, AC-25: sweeps over every console endpoint the host maps, so a new route is in
// them with no change here. Operator A owns organization A and project A; operator B owns B's.
public sealed partial class ConsoleSweepTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ConsoleTestHost _host = null!;
    private ConsoleSession _ownerA = null!;
    private ConsoleSession _ownerB = null!;
    private Guid _organizationB;
    private Guid _projectA;
    private Guid _projectB;
    private Guid _keyIdA;
    private string _keyA = string.Empty;
    private string _keyB = string.Empty;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = new ConsoleTestHost(postgres, await postgres.CreateMigratedDatabaseAsync("sweep", Token));
        _ownerA = await _host.CreateOwnerAsync();
        _ownerB = await _host.AddOperatorAsync();
        using HttpResponseMessage organization = await _host.PostAsync("/v1/console/organizations", """{"name":"Beta"}""", _ownerB);
        _organizationB = (await ConsoleTestHost.ReadJsonAsync(organization)).RootElement.GetProperty("id").GetGuid();
        _projectA = await _host.CreateProjectAsync(_ownerA);
        _projectB = await _host.CreateProjectAsync(_ownerB, organizationId: _organizationB);
        _keyA = await _host.CreateKeyAsync(_ownerA, _projectA);
        _keyB = await _host.CreateKeyAsync(_ownerB, _projectB);
        _keyIdA = await _host.WithDatabaseAsync(db =>
            db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name]).Where(x => x.ProjectId == _projectA).Select(x => x.Id).SingleAsync(Token));
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private ConsoleRoute[] ConsoleRoutes =>
    [
        .. _host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/v1/console", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(method => method != "OPTIONS")
                .Select(method => new ConsoleRoute(new HttpMethod(method), endpoint.RoutePattern.RawText!, endpoint))),
    ];

    private string Fill(string route) => RouteValue().Replace(route, match => match.Groups[1].Value switch
    {
        "organizationId" => _ownerA.OrganizationId.ToString(),
        "projectId" => _projectA.ToString(),
        "keyId" => _keyIdA.ToString(),
        var name => throw new InvalidOperationException($"The sweep has no value for {{{name}}}; add one."),
    });

    [Fact, Trait("Spec", "0017:SES-7"), Trait("Spec", "0017:ACX-4"), Trait("Spec", "0006:CON-2"), Trait("Spec", "0017:AC-7")]
    public async Task Every_state_changing_console_endpoint_refuses_a_missing_or_wrong_token_and_a_foreign_or_missing_origin()
    {
        ConsoleRoute[] swept = [.. ConsoleRoutes.Where(route => AccessDeclaration.ChangesState(route.Method.Method))];
        swept.Length.ShouldBeGreaterThan(10);

        foreach ((HttpMethod method, string route, RouteEndpoint endpoint) in swept)
        {
            string path = Fill(route);
            var sends = new List<(ConsoleSession? Session, string? Origin, string? Csrf)>
            {
                (null, "http://evil.example", null),
                (null, null, null),
            };
            if (!AccessDeclaration.IsPublic(endpoint))
            {
                sends =
                [
                    (_ownerA with { CsrfToken = null! }, ConsoleTestHost.PublicUrl, null),
                    (_ownerA, ConsoleTestHost.PublicUrl, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
                    (_ownerA, "http://evil.example", null),
                    (_ownerA, null, null),
                ];
            }

            foreach ((ConsoleSession? session, string? origin, string? csrf) in sends)
            {
                // The body is invalid on purpose: a 400 here would mean binding ran first.
                using HttpResponseMessage response = await _host.SendAsync(method, path, "{", session, origin, csrf);
                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}, origin {origin}");
                (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.Forbidden, $"{method} {route}");
            }
        }
    }

    [Fact, Trait("Spec", "0006:TEN-5"), Trait("Spec", "0012:CPL-3"), Trait("Spec", "0017:AC-9"), Trait("Spec", "0017:AC-25")]
    public async Task Every_organization_and_project_route_answers_404_to_an_operator_of_another_organization()
    {
        ConsoleRoute[] swept =
        [
            .. ConsoleRoutes.Where(route => route.Route.Contains("{organizationId", StringComparison.Ordinal) || route.Route.Contains("{projectId", StringComparison.Ordinal)),
        ];
        swept.Length.ShouldBeGreaterThan(12);

        foreach ((HttpMethod method, string route, _) in swept)
        {
            using HttpResponseMessage response = await _host.SendAsync(method, Fill(route), "{}", _ownerB);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {route}");
            (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.NotFound, $"{method} {route}");
        }

        (await StatusOfAsync(_keyA, _projectA)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact, Trait("Spec", "0006:TEN-5"), Trait("Spec", "0017:AC-25")]
    public async Task A_key_id_of_project_A_answers_404_on_project_Bs_key_routes_and_changes_nothing()
    {
        string keys = $"/v1/console/projects/{_projectB}/keys/{_keyIdA}";
        foreach ((HttpMethod method, string path, string body) in new[]
        {
            (HttpMethod.Patch, keys, """{"name":"Taken"}"""),
            (HttpMethod.Post, $"{keys}/revoke", "{}"),
            (HttpMethod.Post, $"{keys}/regenerate", "{}"),
        })
        {
            using HttpResponseMessage response = await _host.SendAsync(method, path, body, _ownerB);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }

        (await StatusOfAsync(_keyA, _projectA)).ShouldBe(HttpStatusCode.OK);
        (await _host.WithDatabaseAsync(db => db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name]).Where(x => x.Id == _keyIdA).Select(x => x.Name).SingleAsync(Token)))
            .ShouldBe("Worker");
    }

    [Fact, Trait("Spec", "0006:TEN-5"), Trait("Spec", "0012:PRJ-2"), Trait("Spec", "0017:AC-25")]
    public async Task A_key_of_project_A_answers_401_on_project_B()
    {
        (await StatusOfAsync(_keyA, _projectB)).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusOfAsync(_keyB, _projectA)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Spec", "0006:TEN-5"), Trait("Spec", "0017:AKY-10"), Trait("Spec", "0017:AC-25")]
    public async Task A_key_reaches_no_console_route_that_needs_an_operator()
    {
        foreach ((HttpMethod method, string route, RouteEndpoint endpoint) in ConsoleRoutes.Where(route => !AccessDeclaration.IsPublic(route.Endpoint)))
        {
            using HttpResponseMessage response = await _host.SendAsync(method, Fill(route), "{}", key: _keyA);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
        }
    }

    private async Task<HttpStatusCode> StatusOfAsync(string key, Guid projectId)
    {
        using HttpResponseMessage response = await _host.GetAsync($"/v1/projects/{projectId}/keys/current", key: key);
        return response.StatusCode;
    }

    [GeneratedRegex(@"\{(\w+)(?::\w+)?\}")]
    private static partial Regex RouteValue();

    private sealed record ConsoleRoute(HttpMethod Method, string Route, RouteEndpoint Endpoint);
}
