using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using Orvano.SharedKernel.Api;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Api;

// Spec 0014 AC-13, HLT-1 to HLT-5: the public health call any SDK can make, and the no redirect sweep (PTH-4).
public sealed class V1HealthTests(PostgresFixture postgres) : IDisposable
{
    private const string Origin = "https://app.example.test";

    private readonly ConsoleBuild _console = new();
    private OrvanoApiFactory? _factory;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _factory?.Dispose();
        _console.Dispose();
    }

    [Fact, Trait("Spec", "0014:AC-13"), Trait("Spec", "0014:HLT-2")]
    public async Task Health_answers_ok_with_the_supported_versions_and_nothing_else_when_ready()
    {
        using HttpResponseMessage response = await Client().GetAsync(new Uri("/v1/health", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldBe($$"""{"status":"ok","apiVersions":["{{ApiVersions.Current}}"]}""");
        response.Headers.GetValues(OrvanoHeaders.ApiVersion).ShouldBe([ApiVersions.Current]);
        response.Headers.Contains(OrvanoHeaders.RequestId).ShouldBeTrue();
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    [Theory, Trait("Spec", "0014:AC-13"), Trait("Spec", "0014:HLT-4"), Trait("Spec", "0012:EPT-5")]
    [InlineData("Orvano-Key", "orv_pk_not_a_real_key")]
    [InlineData("Authorization", "Bearer not-a-token")]
    public async Task Health_answers_200_when_the_caller_sends_a_bad_credential(string header, string value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/health", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(header, value);

        using HttpResponseMessage response = await Client().SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact, Trait("Spec", "0014:AC-13"), Trait("Spec", "0014:HLT-2")]
    public async Task Health_answers_503_unavailable_problem_when_a_ready_check_fails()
    {
        HttpClient client = Client(services: services =>
            services.AddHealthChecks().AddCheck("down", () => HealthCheckResult.Unhealthy("database down"), tags: ["ready"]));

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        string body = await response.Content.ReadAsStringAsync(Token);
        body.ShouldNotContain("database down");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.Unavailable);
        problem.RootElement.TryGetProperty("retryAfter", out _).ShouldBeFalse();
        response.Headers.Contains("Retry-After").ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0014:AC-13"), Trait("Spec", "0014:HLT-3")]
    public async Task Health_sends_any_origin_cors_without_credentials_when_a_browser_calls_it()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/health", UriKind.Relative));
        request.Headers.Add("Origin", Origin);

        using HttpResponseMessage response = await Client().SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["*"]);
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
        response.Headers.Vary.ShouldNotContain("Origin");
        string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers")).Split(',')
            .ShouldBe(OrvanoHeaders.CorsExposedHeaders, ignoreOrder: true);
    }

    [Fact, Trait("Spec", "0014:AC-4"), Trait("Spec", "0014:HLT-3")]
    public async Task Health_sends_any_origin_cors_with_the_problem_when_a_browser_sends_an_unsupported_version()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/health", UriKind.Relative));
        request.Headers.Add("Origin", Origin);
        request.Headers.Add(OrvanoHeaders.ApiVersion, "1999-01-01");

        using HttpResponseMessage response = await Client().SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["*"]);
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
        string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers")).Split(',')
            .ShouldBe(OrvanoHeaders.CorsExposedHeaders, ignoreOrder: true);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        problem.RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.UnsupportedApiVersion);
    }

    [Fact, Trait("Spec", "0014:AC-13"), Trait("Spec", "0014:HLT-3")]
    public async Task Health_preflight_answers_the_fixed_policy_without_a_credential()
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, new Uri("/v1/health", UriKind.Relative));
        preflight.Headers.Add("Origin", Origin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "orvano-key,orvano-api-version,orvano-client");

        using HttpResponseMessage response = await Client().SendAsync(preflight, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["*"]);
        response.Headers.GetValues("Access-Control-Allow-Methods").ShouldBe(["GET"]);
        response.Headers.GetValues("Access-Control-Max-Age").ShouldBe(["600"]);
        string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ShouldContain("orvano-key", Case.Insensitive);
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0014:HLT-1")]
    public async Task Health_answers_in_the_worker_role_too()
    {
        using HttpResponseMessage response = await Client(roles: "worker").GetAsync(new Uri("/v1/health", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact, Trait("Spec", "0014:AC-3"), Trait("Spec", "0014:PTH-4")]
    public async Task No_v1_endpoint_answers_a_redirect_when_every_role_is_on()
    {
        HttpClient client = Client(roles: "api,worker");
        RouteEndpoint[] api = [.. _factory!.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(ApiPaths.Prefix, StringComparison.Ordinal) == true)];

        api.ShouldNotBeEmpty();
        foreach (RouteEndpoint endpoint in api)
        {
            string path = string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part =>
                part is Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal ? literal.Content : Guid.CreateVersion7().ToString()))));
            foreach (string method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [HttpMethods.Get])
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), new Uri("/" + path, UriKind.Relative));
                using HttpResponseMessage response = await client.SendAsync(request, Token);

                ((int)response.StatusCode).ShouldNotBeInRange(300, 399, $"{method} /{path}");
            }
        }
    }

    private HttpClient Client(string? roles = null, Action<IServiceCollection>? services = null)
    {
        Dictionary<string, string> settings = OrvanoApiFactory.DefaultSettings(postgres);
        if (roles is not null)
        {
            settings["ROLES"] = roles;
        }

        _factory = new OrvanoApiFactory(settings, _console.ContentRoot, services);
        return _factory.CreateClient(new() { AllowAutoRedirect = false });
    }
}
