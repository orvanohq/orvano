using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    [Trait("Spec", "0012:PRJ-4")]
    public async Task Failed_resolver_answers_unauthorized_and_skips_the_rest_when_an_earlier_resolver_resolved()
    {
        bool ran = false;
        var resolving = new FixedResolver(new CallerResolution.Resolved(Secret), order: 100);
        var failing = new FixedResolver(new CallerResolution.Failed("expired token"), order: 200);
        var later = new FixedResolver(new CallerResolution.NotPresent(), order: 300);
        await using WebApplication app = await StartAsync(
            services => services.AddSingleton<ICallerResolver>(later).AddSingleton<ICallerResolver>(failing).AddSingleton<ICallerResolver>(resolving),
            app => app.MapGet("/counted", () => ran = true).RequireScope(Scopes.StorageWrite));

        (await CodeAsync(app, "/counted")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized")); // the earlier caller is dropped
        failing.Calls.ShouldHaveSingleItem().Resolved.ShouldBeSameAs(Secret);
        later.Calls.ShouldBeEmpty();
        ran.ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-3")]
    public async Task Later_registered_resolver_type_wins_a_tie_when_both_use_the_api_key_order()
    {
        await using WebApplication app = await StartAsync(services => services
            .AddCallerResolver<TieGuestResolver>()
            .AddCallerResolver<TieSecretResolver>());

        (await CodeAsync(app, "/storage")).ShouldBe((HttpStatusCode.OK, null)); // the secret key, registered second, wins
        (await CodeAsync(app, $"/p/{Project}/whoami")).ShouldBe((HttpStatusCode.Forbidden, "forbidden")); // so no guest is left
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-2")]
    public async Task Resolver_that_resolves_a_null_caller_fails_the_request()
    {
        await using WebApplication app = await StartAsync(services =>
            services.AddSingleton<ICallerResolver>(new FixedResolver(new CallerResolution.Resolved(null!))));

        await Should.ThrowAsync<InvalidOperationException>(() => GetAsync(app, "/storage"));
    }

    [Theory]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:PRJ-2")]
    [Trait("Spec", "0012:RSV-2")]
    [InlineData("not-a-uuid", null)]
    [InlineData("0192f7a4-0000-7000-8000-00000000000", null)] // one digit short
    [InlineData("00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000000")]
    [InlineData("0192F7A4-0000-7000-8000-000000000001", null)] // the caller's project, upper case
    [InlineData("0192f7a4000070008000000000000001", null)] // without hyphens
    [InlineData("%7B0192f7a4-0000-7000-8000-000000000001%7D", null)] // in braces
    [InlineData("%200192f7a4-0000-7000-8000-000000000001%20", null)] // with spaces
    public async Task Caller_answers_unauthorized_when_the_route_project_is_malformed_empty_or_not_canonical(
        string projectId,
        string? routeProjectId)
    {
        var resolver = new FixedResolver(Secret);
        await using WebApplication app = await StartAsync(services => services.AddSingleton<ICallerResolver>(resolver));

        (await CodeAsync(app, $"/p/{projectId}/storage")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
        resolver.Calls.ShouldHaveSingleItem().RouteProjectId.ShouldBe(routeProjectId is null ? null : Guid.Parse(routeProjectId));
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:CPL-3")]
    public async Task Operator_membership_is_read_once_when_a_group_and_its_endpoint_each_need_a_console_permission()
    {
        var memberships = new OwnerOfOrganization();
        await using WebApplication app = await StartAsync(
            services => services.AddSingleton<ICallerResolver>(new FixedResolver(Operator)).AddSingleton<IMembershipLookup>(memberships),
            app => app.MapGroup("/console/projects/{projectId}").RequireConsolePermission(ConsolePermission.ProjectRead)
                .MapGet("/rows", (IProjectContext project) => project.Id.ToString()).RequireConsolePermission(ConsolePermission.DataRead));

        using HttpResponseMessage response = await GetAsync(app, $"/console/projects/{Project}/rows");

        (await response.Content.ReadAsStringAsync(Token)).ShouldBe(Project.ToString());
        memberships.Lookups.ShouldBe(2); // the project's organization, then the role, once for both permissions
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-2")]
    public async Task Failed_reason_is_logged_cut_to_100_characters_when_the_resolver_gives_a_longer_one()
    {
        var logs = new LogCapture(LogLevel.Information);
        await using WebApplication app = await StartAsync(services => services
            .AddSingleton<ICallerResolver>(new FixedResolver(new CallerResolution.Failed(new string('x', 500))))
            .AddSingleton<ILoggerProvider>(logs));

        (await CodeAsync(app, "/storage")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));

        string logged = logs.Messages.Where(message => message.StartsWith("Credential refused by", StringComparison.Ordinal)).ShouldHaveSingleItem();
        logged.ShouldEndWith(new string('x', 100) + "…");
        logged.ShouldNotContain(new string('x', 101));
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:RSV-4")]
    public async Task Group_route_project_is_checked_like_an_endpoint_route_project_when_the_group_prefix_holds_it()
    {
        var spy = new FixedResolver(new CallerResolution.NotPresent(), order: 300);
        await using WebApplication app = await StartAsync(
            services => services.AddCallerResolver<SampleKeyResolver>().AddSingleton<ICallerResolver>(spy),
            app => app.MapGroup("/g/{projectId}").RequireClientApi(Scopes.ClientDatabases)
                .MapGet("/whoami", (ICallerContext caller, IProjectContext project) => Describe(caller.Current, project)));

        using HttpResponseMessage own = await GetAsync(app, $"/g/{Project}/whoami", $"guest:{Project}");

        (await own.Content.ReadAsStringAsync(Token)).Split(' ')[2].ShouldBe(Project.ToString());
        spy.Calls.ShouldHaveSingleItem().RouteProjectId.ShouldBe(Project);
        (await CodeAsync(app, $"/g/{OtherProject}/whoami", $"guest:{Project}")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
        (await CodeAsync(app, "/g/not-a-uuid/whoami", $"guest:{Project}")).ShouldBe((HttpStatusCode.Unauthorized, "unauthorized"));
    }

    [Theory]
    [Trait("Spec", "0012:AC-9")]
    [Trait("Spec", "0012:DEN-5")]
    [InlineData("{ not json")]
    [InlineData("")]
    public async Task Request_with_no_credential_answers_unauthorized_before_the_body_when_the_body_is_bad(string body)
    {
        await using WebApplication app = await StartAsync(
            services => services.AddCallerResolver<SampleKeyResolver>(),
            app => app.MapPost("/p/{projectId}/rows", (Row row) => row.Name).RequireClientApi(Scopes.ClientDatabases));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/p/{Project}/rows", UriKind.Relative))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        using HttpResponseMessage response = await app.GetTestClient().SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("code").GetString().ShouldBe("unauthorized");
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

    private static async Task<WebApplication> StartAsync(Action<IServiceCollection> configure, Action<WebApplication>? map = null)
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
        map?.Invoke(app);
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

    private sealed record Row(string Name);

    // Two resolver types at the same order, so the tie is decided by AddCallerResolver's registration order.
    private sealed class TieGuestResolver : ICallerResolver
    {
        public int Order => CallerResolverOrder.ApiKey;

        public ValueTask<CallerResolution> ResolveAsync(CallerResolutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<CallerResolution>(new CallerResolution.Resolved(Guest));
    }

    private sealed class TieSecretResolver : ICallerResolver
    {
        public int Order => CallerResolverOrder.ConsoleSession;

        public ValueTask<CallerResolution> ResolveAsync(CallerResolutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<CallerResolution>(new CallerResolution.Resolved(Secret));
    }

    private sealed class OwnerOfOrganization : IMembershipLookup
    {
        private int _lookups;

        public int Lookups => _lookups;

        public Task<string?> FindRoleAsync(Guid operatorId, Guid organizationId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _lookups);
            return Task.FromResult(organizationId == Organization ? "owner" : null);
        }

        public Task<Guid?> FindProjectOrganizationAsync(Guid projectId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _lookups);
            return Task.FromResult(projectId == Project ? Organization : (Guid?)null);
        }
    }
}
