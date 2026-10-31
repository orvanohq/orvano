using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Setup;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Audit;
using Orvano.SharedKernel.Data;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 BST-1 to BST-4, AC-17: `orvano admin bootstrap-project`. Most tests call what the command calls, on a host with
// a recording audit sink; the last ones run the real process, for its exit codes and its standard output.
public sealed class BootstrapProjectTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string OwnerEmail = "conformance@example.com";
    private const string BrowserOrigin = "http://localhost:63315";

    private string _database = string.Empty;
    private ConsoleTestHost _host = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.CreateMigratedDatabaseAsync("bootstrap", Token);
        _host = new ConsoleTestHost(postgres, _database);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string Current(Guid projectId) => $"/v1/projects/{projectId}/keys/current";

    private Task<Result<BootstrappedProject>> BootstrapAsync(
        string email = OwnerEmail, string organization = "Conformance", string project = "Run", string[]? origins = null) =>
        PlatformSetup.BootstrapProjectAsync(_host.Services, new BootstrapProjectRequest(email, organization, project, origins ?? []), Token);

    private async Task<BootstrappedProject> BootstrappedAsync(string organization = "Conformance", string[]? origins = null)
    {
        Result<BootstrappedProject> result = await BootstrapAsync(organization: organization, origins: origins);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Fact, Trait("Spec", "0017:BST-2"), Trait("Spec", "0017:AC-17")]
    public async Task Bootstrap_completes_open_setup_with_an_owner_and_clears_the_token()
    {
        await _host.IssueSetupTokenAsync();

        BootstrappedProject made = await BootstrappedAsync();

        await _host.WithDatabaseAsync(async db =>
        {
            Instance instance = await db.Instances.AsNoTracking().SingleAsync(Token);
            instance.SetupCompletedAt.ShouldNotBeNull();
            instance.SetupTokenHash.ShouldBeNull();
            instance.SetupTokenExpiresAt.ShouldBeNull();

            Operator owner = await db.Operators.AsNoTracking().SingleAsync(Token);
            owner.Email.ShouldBe(OwnerEmail);
            owner.Name.ShouldBe("Owner");
            owner.PasswordHash.ShouldNotBeNullOrEmpty();

            Membership membership = await db.Memberships.AsNoTracking().SingleAsync(Token);
            membership.OperatorId.ShouldBe(owner.Id);
            membership.OrganizationId.ShouldBe(made.OrganizationId);
            membership.Role.ShouldBe(RolePermissions.Owner);

            (await db.OperatorSessions.AnyAsync(Token)).ShouldBeFalse();
            return true;
        });

        // The owner form then answers as it does after any finished setup.
        using HttpResponseMessage form = await _host.PostAsync("/v1/console/setup/owner", ConsoleTestHost.OwnerBody("orv_setup_any"));
        form.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact, Trait("Spec", "0017:BST-4"), Trait("Spec", "0017:AC-17")]
    public async Task Bootstrap_makes_an_active_project_and_two_keys_with_full_ceilings_and_no_creator()
    {
        BootstrappedProject made = await BootstrappedAsync(origins: [BrowserOrigin]);

        made.AllowedOrigins.ShouldBe([BrowserOrigin]);
        made.PublishableKey.ShouldStartWith("orv_pk_");
        made.SecretKey.ShouldStartWith("orv_sk_");
        await _host.WithDatabaseAsync(async db =>
        {
            Project project = await db.Projects.AsNoTracking().SingleAsync(Token);
            project.Id.ShouldBe(made.ProjectId);
            project.OrganizationId.ShouldBe(made.OrganizationId);
            project.Name.ShouldBe("Run");
            project.Status.ShouldBe(ProjectStatus.Active);

            List<ApiKey> keys = await db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name]).AsNoTracking().ToListAsync(Token);
            keys.Count.ShouldBe(2);
            keys.ShouldAllBe(x => x.ProjectId == made.ProjectId && x.CreatedByOperatorId == null && x.ExpiresAt == null);
            keys.Single(x => x.Type == KeyType.Publishable).Scopes.ShouldBe(Scopes.Publishable, ignoreOrder: true);
            keys.Single(x => x.Type == KeyType.Secret).Scopes.ShouldBe(Scopes.Secret, ignoreOrder: true);
            return true;
        });

        using HttpResponseMessage client = await _host.GetAsync(Current(made.ProjectId), key: made.PublishableKey);
        client.StatusCode.ShouldBe(HttpStatusCode.OK);
        using HttpResponseMessage server = await _host.GetAsync(Current(made.ProjectId), key: made.SecretKey);
        server.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ConsoleTestHost.ReadJsonAsync(server)).RootElement.GetProperty("type").GetString().ShouldBe("secret");
    }

    [Fact, Trait("Spec", "0017:BST-4"), Trait("Spec", "0017:AUS-3"), Trait("Spec", "0017:AC-22")]
    public async Task Bootstrap_records_each_action_as_the_system_with_no_address()
    {
        BootstrappedProject made = await BootstrappedAsync();

        IReadOnlyList<AuditEvent> events = _host.Audit.Events;
        events.Select(x => x.Action).ShouldBe(
            ["operator.created", "organization.created", "project.created", "api_key.created", "api_key.created"]);
        events.ShouldAllBe(x => x.ActorKind == AuditActorKind.System && x.ActorId == null && x.Ip == null && x.UserAgent == null);
        events.Where(x => x.Action == "api_key.created").ShouldAllBe(x => x.ProjectId == made.ProjectId);
    }

    [Fact, Trait("Spec", "0017:BST-3"), Trait("Spec", "0017:AC-17")]
    public async Task Bootstrap_adds_a_new_project_to_the_same_organization_when_setup_is_complete()
    {
        BootstrappedProject first = await BootstrappedAsync();
        BootstrappedProject second = await BootstrappedAsync();

        second.OrganizationId.ShouldBe(first.OrganizationId);
        second.ProjectId.ShouldNotBe(first.ProjectId);
        second.SecretKey.ShouldNotBe(first.SecretKey);
        (await _host.WithDatabaseAsync(db => db.Operators.CountAsync(Token))).ShouldBe(1);
    }

    [Fact, Trait("Spec", "0017:BST-3")]
    public async Task Bootstrap_creates_an_organization_with_owner_membership_when_the_owner_has_none_of_that_name()
    {
        ConsoleSession owner = await _host.CreateOwnerAsync();

        Result<BootstrappedProject> result = await BootstrapAsync(ConsoleTestHost.OwnerEmail.ToUpperInvariant(), "Another");

        result.IsSuccess.ShouldBeTrue();
        result.Value.OrganizationId.ShouldNotBe(owner.OrganizationId);
        Membership membership = await _host.WithDatabaseAsync(
            db => db.Memberships.AsNoTracking().SingleAsync(x => x.OrganizationId == result.Value.OrganizationId, Token));
        membership.OperatorId.ShouldBe(owner.OperatorId);
        membership.Role.ShouldBe(RolePermissions.Owner);
    }

    [Fact, Trait("Spec", "0017:BST-3")]
    public async Task Bootstrap_fails_and_writes_nothing_when_setup_is_complete_and_the_email_has_no_operator()
    {
        await _host.CreateOwnerAsync();
        int organizations = await _host.WithDatabaseAsync(db => db.Organizations.CountAsync(Token));

        Result<BootstrappedProject> result = await BootstrapAsync("nobody@example.com");

        result.IsSuccess.ShouldBeFalse();
        (await _host.WithDatabaseAsync(db => db.Organizations.CountAsync(Token))).ShouldBe(organizations);
        (await _host.WithDatabaseAsync(db => db.Projects.AnyAsync(Token))).ShouldBeFalse();
    }

    [Theory, Trait("Spec", "0017:BST-2"), Trait("Spec", "0017:PJT-6"), Trait("Spec", "0002:TST-4")]
    [InlineData("not-an-email", "Conformance", "Run", "http://localhost:3000")]
    [InlineData(OwnerEmail, " ", "Run", "http://localhost:3000")]
    [InlineData(OwnerEmail, "Conformance", "", "http://localhost:3000")]
    [InlineData(OwnerEmail, "Conformance", "Run", "http://localhost:3000/path")]
    [InlineData(OwnerEmail, "Conformance", "Run", "https://*.example.com")]
    public async Task Bootstrap_leaves_setup_open_when_an_argument_is_not_valid(string email, string organization, string project, string origin)
    {
        Result<BootstrappedProject> result = await BootstrapAsync(email, organization, project, [origin]);

        result.IsSuccess.ShouldBeFalse();
        Instance instance = await _host.WithDatabaseAsync(db => db.Instances.AsNoTracking().SingleAsync(Token));
        instance.SetupCompletedAt.ShouldBeNull();
        (await _host.WithDatabaseAsync(db => db.Operators.AnyAsync(Token))).ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0017:BST-2"), Trait("Spec", "0017:OWN-3")]
    public async Task Bootstrap_gives_one_owner_and_two_projects_when_two_runs_race_for_open_setup()
    {
        Result<BootstrappedProject>[] results = await Task.WhenAll(BootstrapAsync(), BootstrapAsync());

        results.ShouldAllBe(x => x.IsSuccess);
        results[0].Value!.ProjectId.ShouldNotBe(results[1].Value!.ProjectId);
        results[0].Value!.OrganizationId.ShouldBe(results[1].Value!.OrganizationId);
        (await _host.WithDatabaseAsync(db => db.Operators.CountAsync(Token))).ShouldBe(1);
    }

    [Fact, Trait("Spec", "0017:BST-1"), Trait("Spec", "0017:AC-17")]
    public void No_route_reaches_the_bootstrap_command()
    {
        IEnumerable<string> routes = _host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(x => x.RoutePattern.RawText ?? string.Empty);

        routes.ShouldNotContain(x => x.Contains("bootstrap", StringComparison.OrdinalIgnoreCase));
    }

    // The real process

    private Task<OrvanoCli.Result> RunAsync(params string[] args)
    {
        var environment = postgres.DatabaseEnvironment.ToDictionary();
        environment["ORVANO_DATABASE__NAME"] = _database;
        return OrvanoCli.RunAsync(["admin", "bootstrap-project", .. args], environment, Token);
    }

    [Fact, Trait("Spec", "0017:BST-1"), Trait("Spec", "0017:BST-4"), Trait("Spec", "0017:AC-17")]
    public async Task Command_prints_one_json_object_and_exits_0()
    {
        OrvanoCli.Result result = await RunAsync(
            "--owner-email", OwnerEmail, "--organization", "Conformance", "--project", "Run",
            "--origin", BrowserOrigin, "--origin", "HTTP://LOCALHOST:5173");

        result.ExitCode.ShouldBe(0, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        root.EnumerateObject().Select(x => x.Name).ShouldBe(["organizationId", "projectId", "publishableKey", "secretKey", "allowedOrigins"]);
        root.GetProperty("organizationId").GetGuid().ShouldNotBe(Guid.Empty);
        Guid projectId = root.GetProperty("projectId").GetGuid();
        root.GetProperty("allowedOrigins").EnumerateArray().Select(x => x.GetString()).ShouldBe([BrowserOrigin, "http://localhost:5173"]);

        string key = root.GetProperty("secretKey").GetString()!;
        result.Error.ShouldNotContain(key);
        using HttpResponseMessage response = await _host.GetAsync(Current(projectId), key: key);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact, Trait("Spec", "0017:BST-3"), Trait("Spec", "0017:BST-4")]
    public async Task Command_exits_1_with_the_reason_when_the_email_has_no_operator()
    {
        await _host.CreateOwnerAsync();

        OrvanoCli.Result result = await RunAsync("--owner-email", "nobody@example.com", "--organization", "Conformance", "--project", "Run");

        result.ExitCode.ShouldBe(1);
        result.Output.ShouldBeEmpty();
        result.Error.ShouldContain("no operator has that email");
        result.Error.ShouldNotContain("Unhandled exception");
    }

    [Fact, Trait("Spec", "0017:BST-1"), Trait("Spec", "0017:BST-4")]
    public async Task Command_exits_1_when_a_required_option_is_missing()
    {
        OrvanoCli.Result result = await RunAsync("--owner-email", OwnerEmail);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("--organization");
        (await _host.WithDatabaseAsync(db => db.Operators.AnyAsync(Token))).ShouldBeFalse();
    }
}
