using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Access;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Web;

// Spec 0002 ERR-4: anything unhandled becomes a ProblemDetails with a generic message and the trace ID, never the
// exception text; a server side cancellation is a 503 timeout.
public sealed class ErrorHandlingTests(PostgresFixture postgres) : IDisposable
{
    private const string SecretDetail = "connection string Password=hunter2";
    private readonly ThrowingServer _factory = new(OrvanoApiFactory.DefaultSettings(postgres));

    public void Dispose() => _factory.Dispose();

    [Fact, Trait("Spec", "0002:ERR-4")]
    public async Task Unhandled_exception_returns_500_problem_without_the_exception_text()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/test/throw", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain("hunter2");
        body.ShouldNotContain("InvalidOperationException");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("internal_error");
        problem.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact, Trait("Spec", "0002:ERR-4")]
    public async Task Server_side_cancellation_returns_503_timeout_when_the_caller_is_still_there()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/test/timeout", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("code").GetString().ShouldBe("timeout");
    }

    /// <summary>Adds two failing endpoints to the real pipeline, inside its exception handler.</summary>
    private sealed class ThrowingServer(IReadOnlyDictionary<string, string> settings) : OrvanoApiFactory(settings)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ThrowingEndpoints>());
        }
    }

    // Endpoints, since only endpoints answer after the access layer (spec 0012 EPT-3); public, so no credential is
    // needed. They go on the host's own route builder (the WebApplication, handed on in the builder's properties).
    private sealed class ThrowingEndpoints : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            IEndpointRouteBuilder routes = app.Properties.Values.OfType<IEndpointRouteBuilder>().Single();
            routes.Map("/test/throw", (RequestDelegate)(_ => throw new InvalidOperationException(SecretDetail))).AllowPublicAccess();
            routes.Map("/test/timeout", (RequestDelegate)(_ => throw new OperationCanceledException("query timed out"))).AllowPublicAccess();
        };
    }
}
