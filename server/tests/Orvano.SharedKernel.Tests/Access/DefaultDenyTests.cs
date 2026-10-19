using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Access;

using Shouldly;

using Xunit;

using static Orvano.SharedKernel.Tests.Access.Callers;

namespace Orvano.SharedKernel.Tests.Access;

// Spec 0012 build plan step 11: every endpoint declares its access, enforced at startup and at request time.
public sealed class DefaultDenyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-4")]
    public async Task Startup_fails_naming_the_endpoint_when_it_declares_nothing()
    {
        InvalidOperationException error = await StartupErrorAsync(app => app.MapGet("/forgotten", () => "ok"));

        error.Message.ShouldContain("/forgotten");
        error.Message.ShouldContain("declares no access");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-3")]
    public async Task Startup_fails_when_an_endpoint_declares_a_requirement_and_public_access()
    {
        InvalidOperationException error = await StartupErrorAsync(app =>
            app.MapGet("/both", () => "ok").RequireScope(Scopes.StorageRead).AllowPublicAccess());

        error.Message.ShouldContain("/both");
        error.Message.ShouldContain("both a requirement and AllowPublicAccess");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-3")]
    public async Task Startup_fails_when_an_endpoint_mixes_a_scope_and_a_console_permission()
    {
        InvalidOperationException error = await StartupErrorAsync(app => app.MapGet("/p/{projectId}/mixed", () => "ok")
            .RequireScope(Scopes.StorageRead)
            .RequireConsolePermission(ConsolePermission.DataRead));

        error.Message.ShouldContain("/p/{projectId}/mixed");
        error.Message.ShouldContain("mixes requirement kinds");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-3")]
    public async Task Startup_fails_when_a_group_requirement_meets_an_endpoint_marked_public()
    {
        InvalidOperationException error = await StartupErrorAsync(app =>
            app.MapGroup("/server").RequireScope(Scopes.StorageRead).MapGet("/open", () => "ok").AllowPublicAccess());

        error.Message.ShouldContain("/server/open");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-4")]
    public async Task Startup_names_every_broken_endpoint_in_one_error()
    {
        InvalidOperationException error = await StartupErrorAsync(app =>
        {
            app.MapGet("/one", () => "ok");
            app.MapGet("/two", () => "ok").AllowAnonymous();
            app.MapGet("/fine", () => "ok").AllowPublicAccess();
        });

        error.Message.ShouldContain("/one");
        error.Message.ShouldContain("/two"); // AllowAnonymous is not a declaration (EPT-3)
        error.Message.ShouldNotContain("/fine");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:CPL-3")]
    public async Task Startup_fails_when_a_group_console_permission_has_no_organization_or_project()
    {
        InvalidOperationException error = await StartupErrorAsync(app =>
            app.MapGroup("/console").RequireConsolePermission(ConsolePermission.OrgRead).MapGet("/nowhere", () => "ok"));

        error.Message.ShouldContain("/console/nowhere");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-2")]
    public async Task Group_requirement_covers_each_endpoint_in_the_group_when_called()
    {
        await using WebApplication app = await StartAsync(new FixedResolver(Caller.SecretKey(Project, [Scopes.StorageRead])), app =>
        {
            RouteGroupBuilder group = app.MapGroup("/server").RequireScope(Scopes.StorageRead);
            group.MapGet("/read", () => "ok");
            group.MapGet("/write", () => "ok").RequireScope(Scopes.StorageWrite); // group and endpoint combine, all must pass
        });

        (await CodeAsync(app, "/server/read")).ShouldBe((HttpStatusCode.OK, null));
        (await CodeAsync(app, "/server/write")).ShouldBe((HttpStatusCode.Forbidden, "insufficient_scope"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-2")]
    public async Task Group_requirement_fails_closed_when_the_pipeline_has_no_access_layer()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext();
        await using WebApplication app = builder.Build();
        app.MapGroup("/server").RequireScope(Scopes.StorageRead).MapGet("/read", () => "ok");
        await app.StartAsync(Token);

        await Should.ThrowAsync<InvalidOperationException>(
            () => app.GetTestClient().GetAsync(new Uri("/server/read", UriKind.Relative), Token));
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-5")]
    public async Task Public_endpoint_runs_with_no_caller_and_ignores_a_credential_that_would_fail()
    {
        var failing = new FixedResolver(new CallerResolution.Failed("bad bearer token"));
        await using WebApplication app = await StartAsync(failing, app => app.MapGet("/health/live", (ICallerContext caller) =>
        {
            try
            {
                _ = caller.Current;
                return "caller";
            }
            catch (InvalidOperationException)
            {
                return "no caller";
            }
        }).AllowPublicAccess());

        using HttpResponseMessage response = await GetAsync(app, "/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldBe("no caller");
        failing.Calls.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    public async Task Declared_endpoint_answers_unauthorized_when_the_request_has_no_credential()
    {
        await using WebApplication app = await StartAsync(new FixedResolver(new CallerResolution.NotPresent()), app =>
            app.MapGet("/client/files", () => "ok").RequireClientApi(Scopes.ClientStorage));

        (await CodeAsync(app, "/client/files")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-4")]
    public async Task Undeclared_endpoint_answers_not_found_and_never_runs_when_it_skipped_the_startup_check()
    {
        bool ran = false;
        await using WebApplication app = await StartAsync(new FixedResolver(Secret), app =>
        {
            app.MapGet("/late", () => ran = true);
            app.MapGet("/late-both", () => ran = true).RequireScope(Scopes.StorageRead).AllowPublicAccess();
        }, skipStartupCheck: true);

        (await CodeAsync(app, "/late")).ShouldBe((HttpStatusCode.NotFound, "not_found"));
        (await CodeAsync(app, "/late-both")).ShouldBe((HttpStatusCode.NotFound, "not_found"));
        ran.ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-1")]
    public async Task Request_that_matches_no_endpoint_gets_routings_not_found()
    {
        await using WebApplication app = await StartAsync(new FixedResolver(Secret), app =>
            app.MapGet("/open", () => "ok").AllowPublicAccess());

        using HttpResponseMessage response = await GetAsync(app, "/missing");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentLength.ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-1")]
    public async Task Wrong_method_on_an_endpoint_answers_not_found_when_routing_would_answer_405()
    {
        await using WebApplication app = await StartAsync(new FixedResolver(Secret), app =>
            app.MapGet("/open", () => "ok").AllowPublicAccess());

        using HttpResponseMessage response = await app.GetTestClient().PostAsync(new Uri("/open", UriKind.Relative), null, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-2")]
    public async Task Declarations_work_on_builders_other_than_a_route_handler_when_added()
    {
        await using WebApplication app = await StartAsync(new FixedResolver(Secret), app =>
        {
            app.MapHealthChecks("/health").AllowPublicAccess();
            app.Map("/raw", (HttpContext http) => http.Response.WriteAsync("ok", http.RequestAborted)).RequireScope(Scopes.StorageRead);
        }, services => services.AddHealthChecks());

        (await CodeAsync(app, "/health")).ShouldBe((HttpStatusCode.OK, null));
        (await CodeAsync(app, "/raw")).ShouldBe((HttpStatusCode.OK, null));
    }

    private static async Task<InvalidOperationException> StartupErrorAsync(Action<WebApplication> map)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext();
        await using WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAccessRequirements();
        map(app);

        return await Should.ThrowAsync<InvalidOperationException>(() => app.StartAsync(Token));
    }

    private static async Task<WebApplication> StartAsync(
        ICallerResolver resolver,
        Action<WebApplication> map,
        Action<IServiceCollection>? configure = null,
        bool skipStartupCheck = false)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(resolver);
        configure?.Invoke(builder.Services);
        if (skipStartupCheck)
        {
            // Stands in for an endpoint added after startup, which the startup check never saw.
            builder.Services.Remove(builder.Services.Single(service => service.ImplementationType == typeof(AccessEndpointCheck)));
        }

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAccessRequirements();
        map(app);
        await app.StartAsync(Token);
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path)
    {
        HttpResponseMessage response = await app.GetTestClient().GetAsync(new Uri(path, UriKind.Relative), Token);
        await response.Content.LoadIntoBufferAsync(Token);
        return response;
    }

    private static async Task<(HttpStatusCode Status, string? Code)> CodeAsync(WebApplication app, string path)
    {
        using HttpResponseMessage response = await GetAsync(app, path);
        string body = await response.Content.ReadAsStringAsync(Token);
        return (response.StatusCode, response.IsSuccessStatusCode ? null : JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }
}
