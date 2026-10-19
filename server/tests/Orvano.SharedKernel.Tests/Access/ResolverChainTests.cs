using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Access;

using Shouldly;

using Xunit;

using static Orvano.SharedKernel.Tests.Access.Callers;

namespace Orvano.SharedKernel.Tests.Access;

// Spec 0012 build plan step 10: the resolver chain on a sample minimal API, with sample key and token resolvers.
public sealed class ResolverChainTests
{
    private static readonly Guid Organization = Guid.Parse("0192f7a4-0000-7000-8000-0000000000a1");
    private static readonly Guid OtherProject = Guid.Parse("0192f7a4-0000-7000-8000-000000000002");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-3")]
    public async Task Token_resolver_turns_the_keys_guest_into_an_end_user_keeping_the_keys_project_and_client_apis()
    {
        await using WebApplication app = await StartAsync(services => services.AddCallerResolver<SampleTokenResolver>().AddCallerResolver<SampleKeyResolver>());

        using HttpResponseMessage response = await GetAsync(app, $"/p/{Project}/whoami", $"guest:{Project}", $"Bearer {UserA}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldBe(
            $"EndUser {UserA} {Project} {string.Join(',', SampleKeyResolver.GuestApis.Order(StringComparer.Ordinal))}");
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public async Task Failed_token_answers_unauthorized_when_the_publishable_key_is_valid()
    {
        await using WebApplication app = await StartAsync(services => services.AddCallerResolver<SampleKeyResolver>().AddCallerResolver<SampleTokenResolver>());

        (await CodeAsync(app, $"/p/{Project}/whoami", $"guest:{Project}", "Bearer expired")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
        (await CodeAsync(app, $"/p/{Project}/whoami", key: null, $"Bearer {UserA}")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:PRJ-4")]
    public async Task Failed_resolver_stops_the_chain_when_a_later_resolver_would_resolve()
    {
        var failing = new FixedResolver(new CallerResolution.Failed("revoked key"), order: 100);
        var later = new FixedResolver(new CallerResolution.Resolved(Secret), order: 200);
        await using WebApplication app = await StartAsync(services => services.AddSingleton<ICallerResolver>(later).AddSingleton<ICallerResolver>(failing));

        using HttpResponseMessage response = await GetAsync(app, "/storage");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(Token)).ShouldNotContain("revoked key"); // logged, never sent
        later.Calls.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-3")]
    public async Task Resolvers_run_in_ascending_order_and_ties_in_registration_order_when_chained()
    {
        var second = new FixedResolver(new CallerResolution.Resolved(Secret), order: 100);
        var first = new FixedResolver(new CallerResolution.Resolved(Guest), order: 100);
        var last = new FixedResolver(new CallerResolution.NotPresent(), order: 200);
        var earliest = new FixedResolver(new CallerResolution.NotPresent(), order: 50);
        await using WebApplication app = await StartAsync(services => services
            .AddSingleton<ICallerResolver>(last)
            .AddSingleton<ICallerResolver>(first)
            .AddSingleton<ICallerResolver>(second)
            .AddSingleton<ICallerResolver>(earliest));

        (await GetAsync(app, "/storage")).StatusCode.ShouldBe(HttpStatusCode.OK); // the secret key passes RequireScope

        earliest.Calls.ShouldHaveSingleItem().Resolved.ShouldBeNull();
        first.Calls.ShouldHaveSingleItem().Resolved.ShouldBeNull();
        second.Calls.ShouldHaveSingleItem().Resolved.ShouldBeSameAs(Guest);
        last.Calls.ShouldHaveSingleItem().Resolved.ShouldBeSameAs(Secret);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-4")]
    public async Task Caller_gets_the_route_project_when_its_proven_project_matches()
    {
        await using WebApplication app = await StartAsync(services => services.AddCallerResolver<SampleKeyResolver>());

        using HttpResponseMessage response = await GetAsync(app, $"/p/{Project}/whoami", $"guest:{Project}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).Split(' ')[2].ShouldBe(Project.ToString()); // "Guest <no user> <project> ..."
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:PRJ-2")]
    public async Task Caller_whose_project_differs_from_the_route_answers_unauthorized()
    {
        await using WebApplication app = await StartAsync(services => services.AddCallerResolver<SampleKeyResolver>());

        (await CodeAsync(app, $"/p/{OtherProject}/whoami", $"guest:{Project}")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
        (await CodeAsync(app, $"/p/{OtherProject}/storage", $"secret:{Project}")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
        (await CodeAsync(app, "/p/not-a-uuid/whoami", $"guest:{Project}")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-2")]
    public async Task Resolver_sees_the_route_project_as_a_uuid_or_null_when_called()
    {
        var resolver = new FixedResolver(new CallerResolution.NotPresent());
        await using WebApplication app = await StartAsync(services => services.AddSingleton<ICallerResolver>(resolver));

        await GetAsync(app, $"/p/{Project}/whoami");
        await GetAsync(app, "/p/not-a-uuid/whoami");
        await GetAsync(app, "/storage");

        resolver.Calls.Select(call => call.RouteProjectId).ShouldBe([Project, null, null]);
        resolver.Calls.ShouldAllBe(call => call.Http != null);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-4")]
    public async Task Caller_has_no_project_when_the_route_has_none()
    {
        await using WebApplication app = await StartAsync(services => services.AddSingleton<ICallerResolver>(new FixedResolver(Secret)));

        using HttpResponseMessage response = await GetAsync(app, "/storage");

        (await response.Content.ReadAsStringAsync(Token)).ShouldBe("no project");
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-4")]
    public async Task Operator_gets_the_route_project_only_through_the_membership_check_when_on_a_project_route()
    {
        var memberships = new OwnerOfOrganization();
        await using WebApplication app = await StartAsync(services => services
            .AddSingleton<ICallerResolver>(new FixedResolver(Operator))
            .AddSingleton<IMembershipLookup>(memberships));

        using HttpResponseMessage member = await GetAsync(app, $"/console/projects/{Project}");
        using HttpResponseMessage outsider = await GetAsync(app, $"/console/projects/{OtherProject}");

        (await member.Content.ReadAsStringAsync(Token)).ShouldBe(Project.ToString());
        outsider.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-5")]
    public async Task Declared_endpoint_answers_unauthorized_when_no_resolver_resolves_a_caller()
    {
        await using WebApplication app = await StartAsync(services => services
            .AddCallerResolver<SampleKeyResolver>()
            .AddSingleton<IMembershipLookup>(new OwnerOfOrganization()));

        (await CodeAsync(app, "/storage")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
        (await CodeAsync(app, $"/p/{Project}/whoami")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
        (await CodeAsync(app, $"/console/projects/{Project}")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:AC-13")]
    public async Task Declared_endpoint_answers_unauthorized_when_no_resolver_is_registered()
    {
        await using WebApplication app = await StartAsync(_ => { });

        (await CodeAsync(app, "/storage")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:CLR-3")]
    public async Task Resolver_that_returns_a_system_caller_fails_the_request()
    {
        await using WebApplication app = await StartAsync(services => services.AddSingleton<ICallerResolver>(new FixedResolver(Caller.System)));

        await Should.ThrowAsync<InvalidOperationException>(() => GetAsync(app, "/storage"));
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-1")]
    public void Add_caller_resolver_registers_a_type_once_and_scoped_when_called_twice()
    {
        ServiceDescriptor descriptor = new ServiceCollection()
            .AddCallerResolver<SampleKeyResolver>()
            .AddCallerResolver<SampleKeyResolver>()
            .ShouldHaveSingleItem();

        descriptor.ServiceType.ShouldBe(typeof(ICallerResolver));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    private static async Task<WebApplication> StartAsync(Action<IServiceCollection> configure)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext();
        builder.Services.AddProblemDetails();
        configure(builder.Services);

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAccessRequirements();
        app.MapGet("/p/{projectId}/whoami", (ICallerContext caller, IProjectContext project) => Describe(caller.Current, project))
            .RequireClientApi(Scopes.ClientDatabases);
        app.MapGet("/p/{projectId}/storage", () => "ok").RequireScope(Scopes.StorageWrite);
        app.MapGet("/storage", (IProjectContext project) => TryProject(project) is { } id ? id.ToString() : "no project")
            .RequireScope(Scopes.StorageWrite);
        app.MapGet("/console/projects/{projectId}", (IProjectContext project) => project.Id.ToString())
            .RequireConsolePermission(ConsolePermission.DataRead);
        await app.StartAsync(Token);
        return app;
    }

    private static string Describe(Caller caller, IProjectContext project) =>
        $"{caller.Kind} {caller.UserId} {project.Id} {string.Join(',', caller.Scopes.Order(StringComparer.Ordinal))}";

    private static Guid? TryProject(IProjectContext project)
    {
        try
        {
            return project.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path, string? key = null, string? authorization = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (key is not null)
        {
            request.Headers.Add("X-Key", key);
        }

        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        HttpResponseMessage response = await app.GetTestClient().SendAsync(request, Token);
        await response.Content.LoadIntoBufferAsync(Token);
        return response;
    }

    private static async Task<(HttpStatusCode Status, string? Code)> CodeAsync(
        WebApplication app,
        string path,
        string? key = null,
        string? authorization = null)
    {
        using HttpResponseMessage response = await GetAsync(app, path, key, authorization);
        string body = await response.Content.ReadAsStringAsync(Token);
        return (response.StatusCode, response.IsSuccessStatusCode ? null : JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private sealed class OwnerOfOrganization : IMembershipLookup
    {
        public Task<string?> FindRoleAsync(Guid operatorId, Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(organizationId == Organization ? "owner" : null);

        public Task<Guid?> FindProjectOrganizationAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult(projectId == Project ? Organization : (Guid?)null);
    }
}
