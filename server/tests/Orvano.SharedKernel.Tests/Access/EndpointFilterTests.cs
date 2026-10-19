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

// Spec 0012 build plan steps 4 to 6: the endpoint filters on a sample minimal API, with a fake membership lookup.
public sealed class EndpointFilterTests
{
    private static readonly Guid Organization = Guid.Parse("0192f7a4-0000-7000-8000-0000000000a1");
    private static readonly Guid OtherOrganization = Guid.Parse("0192f7a4-0000-7000-8000-0000000000a2");
    private static readonly Guid OtherProject = Guid.Parse("0192f7a4-0000-7000-8000-000000000002");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_scope_passes_a_secret_key_holding_the_scope_when_called() =>
        (await CallAsync(Caller.SecretKey(Project, [Scopes.StorageWrite]), "/server/storage")).Status.ShouldBe(HttpStatusCode.OK);

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_scope_answers_insufficient_scope_when_the_key_lacks_it()
    {
        (HttpStatusCode status, string? code) = await CallAsync(Caller.SecretKey(Project, [Scopes.StorageRead]), "/server/storage");

        status.ShouldBe(HttpStatusCode.Forbidden);
        code.ShouldBe("insufficient_scope");
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_scope_refuses_a_stored_scope_outside_the_ceiling_when_used()
    {
        (HttpStatusCode status, string? code) = await CallAsync(
            Caller.SecretKey(Project, [Scopes.StorageWrite, "client.storage"]), "/server/storage");

        status.ShouldBe(HttpStatusCode.Forbidden);
        code.ShouldBe("insufficient_scope");
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_scope_checks_a_run_keys_scopes_the_same_way_when_called()
    {
        (await CallAsync(Caller.RunKey(Project, [Scopes.StorageWrite], null), "/server/storage")).Status.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(Caller.RunKey(Project, [Scopes.UsersImpersonate, Scopes.StorageWrite], null), "/server/storage"))
            .Code.ShouldBe("insufficient_scope");
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_scope_refuses_end_users_and_operators_when_they_call_a_server_route()
    {
        (await CallAsync(A, "/server/storage")).Code.ShouldBe("insufficient_scope");
        (await CallAsync(Operator, "/server/storage")).Code.ShouldBe("insufficient_scope");
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_client_api_passes_guests_and_end_users_when_the_api_is_on()
    {
        (await CallAsync(Guest, "/client/storage")).Status.ShouldBe(HttpStatusCode.OK);
        (await CallAsync(A, "/client/storage")).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_client_api_answers_forbidden_when_the_publishable_key_has_the_api_off()
    {
        (HttpStatusCode status, string? code) = await CallAsync(Caller.Guest(Project, [Scopes.ClientAuth]), "/client/storage");

        status.ShouldBe(HttpStatusCode.Forbidden);
        code.ShouldBe("forbidden");
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_client_api_answers_insufficient_scope_when_a_stored_scope_is_outside_the_ceiling() =>
        (await CallAsync(Caller.Guest(Project, [Scopes.ClientStorage, Scopes.DatabasesWrite]), "/client/storage"))
            .Code.ShouldBe("insufficient_scope");

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_client_api_refuses_a_secret_key_when_it_calls_a_client_route() =>
        (await CallAsync(Secret, "/client/storage")).Code.ShouldBe("forbidden");

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_passes_an_owner_and_sets_the_project_when_the_project_is_in_the_organization()
    {
        HttpResponseMessage response = await SendAsync(Operator, $"/console/orgs/{Organization}/projects/{Project}", new FakeMemberships("owner"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldBe(Project.ToString());
    }

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_finds_the_organization_from_the_project_when_only_the_project_is_in_the_route() =>
        (await CallAsync(Operator, $"/console/projects/{Project}", new FakeMemberships("owner"))).Status.ShouldBe(HttpStatusCode.OK);

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_answers_not_found_when_the_operator_is_not_a_member()
    {
        (HttpStatusCode status, string? code) = await CallAsync(Operator, $"/console/orgs/{Organization}", new FakeMemberships(null));

        status.ShouldBe(HttpStatusCode.NotFound);
        code.ShouldBe("not_found");
    }

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_answers_forbidden_when_the_members_role_lacks_the_permission()
    {
        (HttpStatusCode status, string? code) = await CallAsync(Operator, $"/console/orgs/{Organization}", new FakeMemberships("viewer"));

        status.ShouldBe(HttpStatusCode.Forbidden);
        code.ShouldBe("forbidden");
    }

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_answers_not_found_when_the_project_is_outside_the_organization()
    {
        var memberships = new FakeMemberships("owner") { ProjectOrganizations = { [OtherProject] = OtherOrganization } };

        (await CallAsync(Operator, $"/console/orgs/{Organization}/projects/{OtherProject}", memberships)).Status
            .ShouldBe(HttpStatusCode.NotFound);
        (await CallAsync(Operator, $"/console/projects/{Guid.CreateVersion7()}", memberships)).Status
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_answers_not_found_when_the_caller_is_not_an_operator() =>
        (await CallAsync(Secret, $"/console/orgs/{Organization}", new FakeMemberships("owner"))).Status.ShouldBe(HttpStatusCode.NotFound);

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public async Task Project_context_throws_when_read_before_the_access_layer_set_it()
    {
        await using ServiceProvider services = new ServiceCollection().AddAccessContext().BuildServiceProvider();
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IProjectContext>().Id);
        Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ICallerContext>().Current);
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_scope_answers_insufficient_scope_when_a_guest_or_system_caller_calls_a_server_route()
    {
        (await CallAsync(Guest, "/server/storage")).Code.ShouldBe("insufficient_scope");
        (await CallAsync(Caller.System, "/server/storage")).Code.ShouldBe("insufficient_scope");
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_client_api_refuses_a_run_key_and_an_operator_when_they_call_a_client_route()
    {
        (await CallAsync(Caller.RunKey(Project, [Scopes.StorageWrite], UserA), "/client/storage")).Code.ShouldBe("forbidden");
        (await CallAsync(Operator, "/client/storage")).Code.ShouldBe("forbidden");
    }

    [Fact]
    [Trait("Spec", "0012:AC-5")]
    public async Task Denial_is_a_problem_json_body_with_the_status_and_a_safe_message_when_refused()
    {
        using HttpResponseMessage response = await SendAsync(Caller.SecretKey(Project, []), "/server/storage", new FakeMemberships(null));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        body.RootElement.GetProperty("status").GetInt32().ShouldBe(403);
        body.RootElement.GetProperty("detail").GetString().ShouldBe(AccessErrors.InsufficientScope.Message);
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_scope_throws_when_the_endpoint_declares_a_scope_outside_the_secret_catalog()
    {
        await using WebApplication app = WebApplication.CreateSlimBuilder().Build();

        Should.Throw<ArgumentException>(() => app.MapGet("/x", () => "ok").RequireScope(Scopes.ClientStorage));
        Should.Throw<ArgumentException>(() => app.MapGet("/y", () => "ok").RequireScope(Scopes.UsersImpersonate));
    }

    [Fact]
    [Trait("Spec", "0012:AC-8")]
    public async Task Require_client_api_throws_when_the_endpoint_declares_a_scope_outside_the_client_catalog()
    {
        await using WebApplication app = WebApplication.CreateSlimBuilder().Build();

        Should.Throw<ArgumentException>(() => app.MapGet("/x", () => "ok").RequireClientApi(Scopes.StorageRead));
    }

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_answers_forbidden_when_a_member_lacks_the_permission_on_a_project_route()
    {
        (HttpStatusCode status, string? code) = await CallAsync(Operator, $"/console/projects/{Project}", new FakeMemberships("viewer"));

        status.ShouldBe(HttpStatusCode.Forbidden);
        code.ShouldBe("forbidden");
    }

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_answers_not_found_when_a_route_id_is_not_a_uuid()
    {
        (await CallAsync(Operator, "/console/orgs/not-a-uuid", new FakeMemberships("owner"))).Code.ShouldBe("not_found");
        (await CallAsync(Operator, "/console/projects/not-a-uuid", new FakeMemberships("owner"))).Code.ShouldBe("not_found");
    }

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_answers_not_found_when_the_operator_is_a_member_of_another_organization_only()
    {
        var memberships = new FakeMemberships("owner") { ProjectOrganizations = { [OtherProject] = OtherOrganization } };

        (await CallAsync(Operator, $"/console/orgs/{OtherOrganization}", memberships)).Code.ShouldBe("not_found");
        (await CallAsync(Operator, $"/console/projects/{OtherProject}", memberships)).Code.ShouldBe("not_found");
    }

    [Fact]
    [Trait("Spec", "0012:AC-11")]
    public async Task Console_permission_throws_when_the_route_has_no_organization_or_project() =>
        await Should.ThrowAsync<InvalidOperationException>(() => SendAsync(Operator, "/console/nowhere", new FakeMemberships("owner")));

    private static async Task<(HttpStatusCode Status, string? Code)> CallAsync(
        Caller caller,
        string path,
        FakeMemberships? memberships = null)
    {
        using HttpResponseMessage response = await SendAsync(caller, path, memberships ?? new FakeMemberships(null));
        string body = await response.Content.ReadAsStringAsync(Token);
        string? code = response.IsSuccessStatusCode ? null : JsonDocument.Parse(body).RootElement.GetProperty("code").GetString();
        return (response.StatusCode, code);
    }

    private static async Task<HttpResponseMessage> SendAsync(Caller caller, string path, FakeMemberships memberships)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAccessContext();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<IMembershipLookup>(memberships);

        await using WebApplication app = builder.Build();
        // Stands in for the credential resolvers of features 13 and 14.
        app.Use((http, next) =>
        {
            http.RequestServices.GetRequiredService<AccessContext>().Establish(caller, null);
            return next(http);
        });
        app.MapGet("/server/storage", () => "ok").RequireScope(Scopes.StorageWrite);
        app.MapGet("/client/storage", () => "ok").RequireClientApi(Scopes.ClientStorage);
        app.MapGet("/console/orgs/{organizationId}", () => "ok").RequireConsolePermission(ConsolePermission.OrgRead);
        app.MapGet("/console/orgs/{organizationId}/projects/{projectId}", (IProjectContext project) => project.Id.ToString())
            .RequireConsolePermission(ConsolePermission.ProjectRead);
        app.MapGet("/console/projects/{projectId}", (IProjectContext project) => project.Id.ToString())
            .RequireConsolePermission(ConsolePermission.DataRead);
        app.MapGet("/console/nowhere", () => "ok").RequireConsolePermission(ConsolePermission.OrgRead);
        await app.StartAsync(Token);

        HttpResponseMessage response = await app.GetTestClient().GetAsync(new Uri(path, UriKind.Relative), Token);
        await response.Content.LoadIntoBufferAsync(Token);
        return response;
    }

    private sealed class FakeMemberships(string? role) : IMembershipLookup
    {
        public Dictionary<Guid, Guid> ProjectOrganizations { get; } = new() { [Project] = Organization };

        public Task<string?> FindRoleAsync(Guid operatorId, Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(organizationId == Organization ? role : null);

        public Task<Guid?> FindProjectOrganizationAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult(ProjectOrganizations.TryGetValue(projectId, out Guid organization) ? organization : (Guid?)null);
    }
}
