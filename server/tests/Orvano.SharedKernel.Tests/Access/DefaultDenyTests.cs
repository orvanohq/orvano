using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        AccessDeclarationException error = await StartupErrorAsync(app => app.MapGet("/forgotten", () => "ok"));

        error.Message.ShouldContain("/forgotten");
        error.Message.ShouldContain("declares no access");
    }

    [Fact]
    [Trait("Spec", "0017:SES-8")]
    public async Task Startup_fails_when_a_recent_password_check_is_asked_of_a_read_or_a_route_no_operator_passes()
    {
        (await StartupErrorAsync(app => app.MapGet("/read", () => "ok").RequireOperator().RequireRecentReauth()))
            .Message.ShouldContain("/read");
        (await StartupErrorAsync(app => app.MapPost("/open", () => "ok").AllowPublicAccess().RequireRecentReauth()))
            .Message.ShouldContain("/open");
        (await StartupErrorAsync(app => app.MapPost("/keyed", () => "ok").RequireScope(Scopes.StorageRead).RequireRecentReauth()))
            .Message.ShouldContain("RequireRecentReauth");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-3")]
    public async Task Startup_fails_when_an_endpoint_declares_a_requirement_and_public_access()
    {
        AccessDeclarationException error = await StartupErrorAsync(app =>
            app.MapGet("/both", () => "ok").RequireScope(Scopes.StorageRead).AllowPublicAccess());

        error.Message.ShouldContain("/both");
        error.Message.ShouldContain("both a requirement and AllowPublicAccess");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-3")]
    public async Task Startup_fails_when_an_endpoint_mixes_a_scope_and_a_console_permission()
    {
        AccessDeclarationException error = await StartupErrorAsync(app => app.MapGet("/p/{projectId}/mixed", () => "ok")
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
        AccessDeclarationException error = await StartupErrorAsync(app =>
            app.MapGroup("/server").RequireScope(Scopes.StorageRead).MapGet("/open", () => "ok").AllowPublicAccess());

        error.Message.ShouldContain("/server/open");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-4")]
    public async Task Startup_names_every_broken_endpoint_in_one_error()
    {
        AccessDeclarationException error = await StartupErrorAsync(app =>
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
        AccessDeclarationException error = await StartupErrorAsync(app =>
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
    [Trait("Spec", "0012:EPT-3")]
    public async Task Request_that_matches_no_endpoint_answers_not_found_when_a_later_branch_would_answer_it()
    {
        bool ran = false;
        await using WebApplication app = await StartAsync(new FixedResolver(new CallerResolution.NotPresent()), app =>
        {
            app.MapGet("/open", () => "ok").AllowPublicAccess();
            ((IApplicationBuilder)app).Map(new PathString("/branch"), branch => branch.Run(http =>
            {
                ran = true;
                return http.Response.WriteAsync("open to everyone", http.RequestAborted);
            }));
        });

        using HttpResponseMessage response = await GetAsync(app, "/branch");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentLength.ShouldBe(0);
        ran.ShouldBeFalse();
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

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-3")]
    public async Task Startup_fails_when_an_endpoint_mixes_a_scope_and_a_client_api()
    {
        AccessDeclarationException error = await StartupErrorAsync(app => app.MapGet("/p/{projectId}/rows", () => "ok")
            .RequireScope(Scopes.DatabasesRead)
            .RequireClientApi(Scopes.ClientDatabases));

        error.Message.ShouldContain("/p/{projectId}/rows");
        error.Message.ShouldContain("mixes requirement kinds");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-3")]
    public async Task Startup_fails_when_a_group_requirement_and_an_endpoint_requirement_are_different_kinds()
    {
        AccessDeclarationException error = await StartupErrorAsync(app =>
            app.MapGroup("/p/{projectId}").RequireScope(Scopes.DatabasesRead).MapGet("/rows", () => "ok").RequireClientApi(Scopes.ClientDatabases));

        error.Message.ShouldContain("/p/{projectId}/rows");
        error.Message.ShouldContain("mixes requirement kinds");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-3")]
    public async Task Startup_fails_when_a_public_group_holds_an_endpoint_with_a_requirement()
    {
        AccessDeclarationException error = await StartupErrorAsync(app =>
            app.MapGroup("/open").AllowPublicAccess().MapGet("/secret", () => "ok").RequireScope(Scopes.StorageRead));

        error.Message.ShouldContain("/open/secret");
        error.Message.ShouldContain("both a requirement and AllowPublicAccess");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-5")]
    public async Task Public_group_covers_each_endpoint_in_the_group_when_called()
    {
        var resolver = new FixedResolver(Secret);
        await using WebApplication app = await StartAsync(resolver, app =>
        {
            RouteGroupBuilder group = app.MapGroup("/health").AllowPublicAccess();
            group.MapGet("/live", () => "ok");
            group.MapGet("/ready", () => "ok");
        });

        (await CodeAsync(app, "/health/live")).ShouldBe((HttpStatusCode.OK, null));
        (await CodeAsync(app, "/health/ready")).ShouldBe((HttpStatusCode.OK, null));
        resolver.Calls.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-5")]
    public async Task Public_endpoint_ignores_a_bad_key_and_an_expired_bearer_token_when_the_sample_resolvers_would_refuse_them()
    {
        bool ran = false;
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext().AddCallerResolver<SampleKeyResolver>().AddCallerResolver<SampleTokenResolver>();
        builder.Services.AddProblemDetails();
        await using WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAccessRequirements();
        app.MapGet("/health/live", () => ran = true).AllowPublicAccess();
        app.MapGet("/storage", () => "ok").RequireScope(Scopes.StorageWrite);
        await app.StartAsync(Token);

        using HttpResponseMessage open = await SendWithCredentialsAsync(app, "/health/live");
        using HttpResponseMessage declared = await SendWithCredentialsAsync(app, "/storage");

        open.StatusCode.ShouldBe(HttpStatusCode.OK);
        ran.ShouldBeTrue();
        declared.StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // the control: the same credentials are refused elsewhere
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-5")]
    public async Task Public_endpoint_answers_when_the_pipeline_has_no_access_layer()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext();
        await using WebApplication app = builder.Build();
        app.MapGet("/health/live", () => "ok").AllowPublicAccess();
        await app.StartAsync(Token);

        (await CodeAsync(app, "/health/live")).ShouldBe((HttpStatusCode.OK, null));
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-4")]
    public async Task Endpoint_mapped_after_startup_answers_not_found_and_logs_an_error_naming_it_when_it_declares_nothing()
    {
        bool ran = false;
        var logs = new LogCapture(LogLevel.Error);
        await using WebApplication app = await StartAsync(
            new FixedResolver(Secret),
            app => app.MapGet("/open", () => "ok").AllowPublicAccess(),
            services => services.AddSingleton<ILoggerProvider>(logs));
        app.MapGet("/late", () => ran = true); // after the startup check ran

        (await CodeAsync(app, "/late")).ShouldBe((HttpStatusCode.NotFound, "not_found"));
        ran.ShouldBeFalse();
        logs.Messages.ShouldHaveSingleItem().ShouldContain("/late");
    }

    [Fact]
    [Trait("Spec", "0012:AC-13")]
    [Trait("Spec", "0012:EPT-1")]
    public async Task Wrong_method_answers_not_found_without_an_error_log_when_routing_rejects_it()
    {
        var logs = new LogCapture(LogLevel.Error);
        await using WebApplication app = await StartAsync(
            new FixedResolver(Secret),
            app => app.MapGet("/storage", () => "ok").RequireScope(Scopes.StorageRead),
            services => services.AddSingleton<ILoggerProvider>(logs));

        using HttpResponseMessage response = await app.GetTestClient().PostAsync(new Uri("/storage", UriKind.Relative), null, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        logs.Messages.ShouldBeEmpty();
    }

    private static async Task<AccessDeclarationException> StartupErrorAsync(Action<WebApplication> map)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext();
        await using WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAccessRequirements();
        map(app);

        return await Should.ThrowAsync<AccessDeclarationException>(() => app.StartAsync(Token));
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
    private static async Task<HttpResponseMessage> SendWithCredentialsAsync(WebApplication app, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-Key", "bad");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer expired");
        request.Headers.Add("Cookie", "session=junk");
        return await app.GetTestClient().SendAsync(request, Token);
    }
}
