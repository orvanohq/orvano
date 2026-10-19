using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Access;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Web;

// Spec 0012 EPT-1 and spec 0007 COR-1: the real host runs CORS, then the access layer, after routing. The host maps
// no endpoint with a requirement yet, so these tests add one, plus a CORS policy, the way later features will.
public sealed class AccessPipelineTests(PostgresFixture postgres) : IDisposable
{
    private const string Origin = "https://app.example.test";
    private const string Declared = "/test/storage";

    private readonly ConsoleBuild _console = new();
    private OrvanoApiFactory? _factory;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _factory?.Dispose();
        _console.Dispose();
    }

    [Fact, Trait("Spec", "0012:AC-13"), Trait("Spec", "0012:EPT-1")]
    public async Task Declared_endpoint_answers_unauthorized_when_the_request_has_no_credential()
    {
        using HttpResponseMessage response = await Client().GetAsync(new Uri(Declared, UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).ShouldBe("unauthorized");
    }

    [Fact, Trait("Spec", "0012:AC-13"), Trait("Spec", "0012:EPT-1")]
    public async Task Wrong_method_answers_not_found_when_routing_would_answer_405()
    {
        using HttpResponseMessage response = await Client().PostAsync(new Uri("/health/live", UriKind.Relative), null, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(response)).ShouldBe("not_found"); // the access layer's answer, so it runs after routing
    }

    [Fact, Trait("Spec", "0012:AC-13"), Trait("Spec", "0012:EPT-3")]
    public async Task Request_that_matches_no_endpoint_answers_an_empty_not_found()
    {
        using HttpResponseMessage response = await Client().GetAsync(new Uri("/test/missing", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentLength.ShouldBe(0);
    }

    [Fact, Trait("Spec", "0007:COR-1"), Trait("Spec", "0012:EPT-1")]
    public async Task Preflight_on_a_declared_endpoint_answers_204_with_cors_headers_when_it_sends_no_credential()
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, new Uri(Declared, UriKind.Relative));
        preflight.Headers.Add("Origin", Origin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");

        using HttpResponseMessage response = await Client().SendAsync(preflight, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Origin]);
    }

    private HttpClient Client()
    {
        _factory = new OrvanoApiFactory(OrvanoApiFactory.DefaultSettings(postgres), _console.ContentRoot);
        WebApplicationFactory<Program> withEndpoint = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, DeclaredEndpoint>();
            services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.WithOrigins(Origin).WithMethods("GET").WithHeaders("Authorization")));
        }));
        return withEndpoint.CreateClient();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("code").GetString();

    // Maps a declared endpoint on the host's own route builder (the WebApplication, which it hands on in the builder's
    // properties) after the host configured its pipeline, so it routes through the host's UseRouting, UseCors, and
    // access layer, and the startup check sees it.
    private sealed class DeclaredEndpoint : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            IEndpointRouteBuilder routes = app.Properties.Values.OfType<IEndpointRouteBuilder>().Single();
            routes.MapGet(Declared, () => "ran").RequireScope(Scopes.StorageRead);
        };
    }
}
