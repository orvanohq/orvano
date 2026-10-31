using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Access;

// Spec 0017 ACX-1 to ACX-4: the two new requirement kinds, a failure's code, and the public console endpoint check.
public sealed class ConsoleAccessTests
{
    private static readonly Guid ProjectId = Guid.CreateVersion7();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact, Trait("Spec", "0017:ACX-2")]
    public async Task Startup_fails_when_RequireProjectKey_has_no_project_route_value()
    {
        AccessDeclarationException error = await StartupErrorAsync(app => app.MapGet("/keys/current", () => "ok").RequireProjectKey());

        error.Message.ShouldContain("/keys/current");
        error.Message.ShouldContain("RequireProjectKey needs a '{projectId}' route value");
    }

    [Fact, Trait("Spec", "0017:ACX-4"), Trait("Spec", "0017:AC-7")]
    public async Task Startup_fails_when_a_public_console_endpoint_changes_state_with_no_origin_check()
    {
        AccessDeclarationException error = await StartupErrorAsync(app =>
            app.MapPost(ApiPaths.Console + "/open", () => "ok").AllowPublicAccess());

        error.Message.ShouldContain("/v1/console/open");
        error.Message.ShouldContain("RequireConsoleOrigin");
    }

    [Fact, Trait("Spec", "0017:ACX-4")]
    public void Declaration_accepts_a_public_console_read_and_a_public_write_with_the_origin_check()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        using WebApplication app = builder.Build();
        app.MapGet(ApiPaths.Console + "/config", () => "ok").AllowPublicAccess();
        app.MapPost(ApiPaths.Console + "/session", () => "ok").AllowPublicAccess().RequireConsoleOrigin();
        app.MapPost("/outside", () => "ok").AllowPublicAccess();

        ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Select(AccessDeclaration.Problem)
            .ShouldAllBe(problem => problem == null);
    }

    [Fact, Trait("Spec", "0017:ACX-2"), Trait("Spec", "0014:XTN-1")]
    public void Audience_of_a_project_key_route_is_both_client_and_server()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        using WebApplication app = builder.Build();
        app.MapGet("/p/{projectId}/keys/current", () => "ok").RequireProjectKey();

        Endpoint endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).Single();

        AccessDeclaration.Audience(endpoint).ShouldBe([Audiences.Client, Audiences.Server]);
    }

    [Fact, Trait("Spec", "0017:SES-4"), Trait("Spec", "0017:AKY-10")]
    public void Requires_operator_is_true_only_for_the_operator_requirements()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        using WebApplication app = builder.Build();
        app.MapGet("/a", () => "ok").RequireOperator();
        app.MapGet("/o/{organizationId}", () => "ok").RequireConsolePermission(ConsolePermission.OrgRead);
        app.MapGet("/p/{projectId}", () => "ok").RequireProjectKey();
        app.MapGet("/s", () => "ok").RequireScope(Scopes.StorageRead);

        ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Select(AccessDeclaration.RequiresOperator)
            .ShouldBe([true, true, false, false]);
    }

    [Fact, Trait("Spec", "0017:ACX-3")]
    public void Failed_accepts_only_the_two_credential_codes()
    {
        new CallerResolution.Failed("reason").Code.ShouldBe(ErrorCodes.Unauthorized);
        new CallerResolution.Failed("reason", ErrorCodes.SessionExpired).Code.ShouldBe(ErrorCodes.SessionExpired);
        Should.Throw<ArgumentException>(() => new CallerResolution.Failed("reason", ErrorCodes.Forbidden));
    }

    public static TheoryData<string, HttpStatusCode> Operators => new()
    {
        { "operator", HttpStatusCode.OK },
        { "guest", HttpStatusCode.Forbidden },
        { "secret", HttpStatusCode.Forbidden },
    };

    [Theory, MemberData(nameof(Operators)), Trait("Spec", "0017:ACX-1")]
    public async Task RequireOperator_admits_only_operators(string kind, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await SendAsync(Make(kind), "/account");

        response.StatusCode.ShouldBe(expected);
    }

    public static TheoryData<string, HttpStatusCode> Keys => new()
    {
        { "guest", HttpStatusCode.OK },
        { "end_user", HttpStatusCode.OK },
        { "secret", HttpStatusCode.OK },
        { "run", HttpStatusCode.Forbidden },
    };

    [Theory, MemberData(nameof(Keys)), Trait("Spec", "0017:ACX-2")]
    public async Task RequireProjectKey_admits_every_key_of_the_project_but_a_run_key(string kind, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await SendAsync(Make(kind), $"/projects/{ProjectId}/keys/current");

        response.StatusCode.ShouldBe(expected);
    }

    [Fact, Trait("Spec", "0017:ACX-2"), Trait("Spec", "0012:PRJ-2")]
    public async Task RequireProjectKey_answers_401_when_the_key_is_of_another_project()
    {
        using HttpResponseMessage response = await SendAsync(Caller.SecretKey(Guid.CreateVersion7(), [Scopes.StorageRead]), $"/projects/{ProjectId}/keys/current");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Spec", "0006:KEY-4")]
    public async Task RequireProjectKey_answers_insufficient_scope_when_a_scope_is_outside_the_ceiling()
    {
        using HttpResponseMessage response = await SendAsync(Caller.Guest(ProjectId, [Scopes.StorageRead]), $"/projects/{ProjectId}/keys/current");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(ErrorCodes.InsufficientScope);
    }

    [Fact, Trait("Spec", "0017:ACX-3"), Trait("Spec", "0012:RSV-5")]
    public async Task Failed_with_session_expired_answers_that_code_with_a_401()
    {
        using HttpResponseMessage response = await SendAsync(new CallerResolution.Failed("ended session", ErrorCodes.SessionExpired), "/account");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(ErrorCodes.SessionExpired);
    }

    private static Caller Make(string kind) => kind switch
    {
        "operator" => Caller.Operator(Guid.CreateVersion7()),
        "guest" => Caller.Guest(ProjectId, [Scopes.ClientAuth]),
        "end_user" => Caller.EndUser(ProjectId, Guid.CreateVersion7(), [], [Scopes.ClientAuth]),
        "secret" => Caller.SecretKey(ProjectId, [Scopes.StorageRead]),
        "run" => Caller.RunKey(ProjectId, [Scopes.StorageRead], null),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static Task<HttpResponseMessage> SendAsync(Caller caller, string path) =>
        SendAsync(new CallerResolution.Resolved(caller), path);

    private static async Task<HttpResponseMessage> SendAsync(CallerResolution answer, string path)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext();
        builder.Services.AddSingleton<ICallerResolver>(new FixedResolver(answer));
        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAccessRequirements();
        app.MapGet("/account", () => "ok").RequireOperator();
        app.MapGet("/projects/{projectId}/keys/current", () => "ok").RequireProjectKey();
        await app.StartAsync(Token);
        return await app.GetTestClient().GetAsync(new Uri(path, UriKind.Relative), Token);
    }

    private static async Task<AccessDeclarationException> StartupErrorAsync(Action<WebApplication> map)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext();
        await using WebApplication app = builder.Build();
        map(app);
        return await Should.ThrowAsync<AccessDeclarationException>(() => app.StartAsync(Token));
    }
}
