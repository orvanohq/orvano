using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Access;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Web;

// Spec 0012 EPT-4 (2): the real host, with every role on, maps no endpoint that leaves its access undeclared. The
// startup check refuses such a host too; this test names the offenders in CI before a deploy.
public sealed class EndpointAccessTests(PostgresFixture postgres) : IDisposable
{
    private readonly ConsoleBuild _console = new();
    private OrvanoApiFactory? _factory;

    public void Dispose()
    {
        _factory?.Dispose();
        _console.Dispose();
    }

    [Fact, Trait("Spec", "0012:AC-13")]
    public void Every_endpoint_declares_a_requirement_or_public_access_when_every_role_is_on()
    {
        Dictionary<string, string> settings = OrvanoApiFactory.DefaultSettings(postgres);
        settings["ROLES"] = "api,worker";
        _factory = new OrvanoApiFactory(settings, _console.ContentRoot);
        using HttpClient client = _factory.CreateClient(); // starts the host

        IReadOnlyList<Endpoint> endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        endpoints.ShouldNotBeEmpty();
        endpoints
            .Select(endpoint => AccessDeclaration.Problem(endpoint) is { } problem ? $"{AccessDeclaration.Name(endpoint)}: {problem}" : null)
            .OfType<string>()
            .ShouldBeEmpty();
        endpoints.Where(AccessDeclaration.IsPublic).Select(AccessDeclaration.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["/", "/console", "/console/{**path}", "/health/live", "/health/ready", "/v1/console/config", "/v1/console/session", "/v1/console/setup/owner", "/v1/console/setup/token", "/v1/health"]); // EPT-5, spec 0014 HLT-1, spec 0016 FRS-1, STP-7
    }

    [Fact, Trait("Spec", "0012:AC-13"), Trait("Spec", "0012:EPT-5")]
    public async Task Health_answers_200_when_the_probe_sends_a_bad_bearer_token()
    {
        _factory = new OrvanoApiFactory(OrvanoApiFactory.DefaultSettings(postgres), _console.ContentRoot);
        using HttpClient client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer not-a-token");

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
