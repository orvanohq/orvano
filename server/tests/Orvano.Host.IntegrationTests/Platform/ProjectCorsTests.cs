using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Orvano.SharedKernel.Api;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 PJT-6, spec 0007 COR-1 to COR-3: the per project policy on project routes, which browser conformance needs
// (SDK-4). Build plan step 17 adds changing the origins and their cache invalidation (AC-11).
public sealed class ProjectCorsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Allowed = "http://localhost:63315";
    private const string Other = "http://localhost:3000";

    private ConsoleTestHost _host = null!;
    private ConsoleSession _owner = null!;
    private Guid _projectId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = new ConsoleTestHost(postgres, await postgres.CreateMigratedDatabaseAsync("cors", Token));
        _owner = await _host.CreateOwnerAsync();
        _projectId = await CreateProjectAsync([Allowed]);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string Current(Guid projectId) => $"/v1/projects/{projectId}/keys/current";

    private async Task<Guid> CreateProjectAsync(string[] allowedOrigins)
    {
        using HttpResponseMessage response = await _host.PostAsync(
            $"/v1/console/organizations/{_owner.OrganizationId}/projects",
            JsonSerializer.Serialize(new { name = "Shop", allowedOrigins }),
            _owner);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> PreflightAsync(string path, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", $"{OrvanoHeaders.Key},{OrvanoHeaders.ApiVersion}");
        return await _host.Client.SendAsync(request, Token);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : null;

    [Fact, Trait("Spec", "0017:PJT-6"), Trait("Spec", "0007:COR-1"), Trait("Spec", "0007:COR-3"), Trait("Spec", "0017:AC-11")]
    public async Task Preflight_passes_with_no_key_when_the_origin_is_allowed()
    {
        using HttpResponseMessage response = await PreflightAsync(Current(_projectId), Allowed);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Header(response, "Access-Control-Allow-Origin").ShouldBe(Allowed);
        Header(response, "Access-Control-Allow-Methods").ShouldNotBeNull().ShouldContain("GET");
        Header(response, "Access-Control-Allow-Headers").ShouldNotBeNull().ShouldContain(OrvanoHeaders.Key, Case.Insensitive);
        Header(response, "Access-Control-Max-Age").ShouldBe("600");
        Header(response, "Access-Control-Allow-Credentials").ShouldBeNull();
        Header(response, "Vary").ShouldBe("Origin");
    }

    [Fact, Trait("Spec", "0017:PJT-6"), Trait("Spec", "0007:COR-2")]
    public async Task Preflight_gets_no_cors_headers_when_the_origin_is_not_on_the_list()
    {
        using HttpResponseMessage response = await PreflightAsync(Current(_projectId), Other);

        Header(response, "Access-Control-Allow-Origin").ShouldBeNull();
        Header(response, "Access-Control-Allow-Headers").ShouldBeNull();
    }

    [Fact, Trait("Spec", "0017:PJT-6"), Trait("Spec", "0017:PJT-3")]
    public async Task Preflight_gets_no_cors_headers_when_the_project_is_unknown()
    {
        using HttpResponseMessage response = await PreflightAsync(Current(Guid.NewGuid()), Allowed);

        Header(response, "Access-Control-Allow-Origin").ShouldBeNull();
    }

    [Fact, Trait("Spec", "0017:PJT-6"), Trait("Spec", "0011:LIF-4")]
    public async Task Preflight_gets_no_cors_headers_when_the_project_is_not_active()
    {
        Guid projectId = await CreateProjectAsync([Allowed]);
        await _host.WithDatabaseAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE platform.project SET status = 'deleting', deletion_requested_at = now() WHERE id = {projectId}", Token));

        using HttpResponseMessage response = await PreflightAsync(Current(projectId), Allowed);

        Header(response, "Access-Control-Allow-Origin").ShouldBeNull();
    }

    [Fact, Trait("Spec", "0017:PJT-6"), Trait("Spec", "0007:COR-2")]
    public async Task Preflight_gets_no_cors_headers_when_the_project_allows_no_origins()
    {
        Guid projectId = await CreateProjectAsync([]);

        using HttpResponseMessage response = await PreflightAsync(Current(projectId), Allowed);

        Header(response, "Access-Control-Allow-Origin").ShouldBeNull();
    }

    [Fact, Trait("Spec", "0017:PJT-6"), Trait("Spec", "0007:COR-2"), Trait("Spec", "0007:COR-3")]
    public async Task Answer_carries_the_exact_origin_and_exposed_headers_when_the_origin_is_allowed()
    {
        string key = await _host.CreateKeyAsync(_owner, _projectId, "publishable");

        using HttpResponseMessage response = await _host.GetAsync(Current(_projectId), key: key, origin: Allowed);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Header(response, "Access-Control-Allow-Origin").ShouldBe(Allowed);
        Header(response, "Access-Control-Expose-Headers").ShouldNotBeNull().ShouldContain(OrvanoHeaders.RequestId, Case.Insensitive);
        Header(response, "Vary").ShouldBe("Origin");
    }

    [Fact, Trait("Spec", "0017:SDK-3"), Trait("Spec", "0007:COR-2")]
    public async Task Refused_key_stays_readable_by_the_browser_when_the_origin_is_allowed()
    {
        using HttpResponseMessage response = await _host.GetAsync(Current(_projectId), key: "not-a-real-key", origin: Allowed);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        Header(response, "Access-Control-Allow-Origin").ShouldBe(Allowed);
    }

    [Fact, Trait("Spec", "0007:COR-2")]
    public async Task Answer_varies_by_origin_when_the_request_has_no_origin()
    {
        string key = await _host.CreateKeyAsync(_owner, _projectId);

        using HttpResponseMessage response = await _host.GetAsync(Current(_projectId), key: key);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Header(response, "Vary").ShouldBe("Origin");
        Header(response, "Access-Control-Allow-Origin").ShouldBeNull();
    }

    [Fact, Trait("Spec", "0007:COR-4")]
    public async Task Console_routes_send_no_cors_headers_for_a_project_origin()
    {
        using HttpResponseMessage response = await _host.GetAsync($"/v1/console/projects/{_projectId}", _owner, origin: Allowed);

        Header(response, "Access-Control-Allow-Origin").ShouldBeNull();
    }
}
