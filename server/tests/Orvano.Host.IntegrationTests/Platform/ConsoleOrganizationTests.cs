using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 ORG-1 to ORG-3, AC-9: organizations in full.
public sealed class ConsoleOrganizationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Path = "/v1/console/organizations";

    private ConsoleTestHost _host = null!;
    private ConsoleSession _owner = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = new ConsoleTestHost(postgres, await postgres.CreateMigratedDatabaseAsync("organizations", Token));
        _owner = await _host.CreateOwnerAsync();
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Guid> CreateAsync(string name, ConsoleSession? session = null)
    {
        using HttpResponseMessage response = await _host.PostAsync(Path, JsonSerializer.Serialize(new { name }), session ?? _owner);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<string?[]> NamesAsync(string query = "", ConsoleSession? session = null)
    {
        using HttpResponseMessage response = await _host.GetAsync(Path + query, session ?? _owner);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString())];
    }

    [Fact, Trait("Spec", "0017:ORG-1"), Trait("Spec", "0017:AC-9"), Trait("Spec", "0017:AC-22")]
    public async Task Create_answers_201_with_an_owner_membership()
    {
        using HttpResponseMessage response = await _host.PostAsync(Path, """{"name":"  Second "}""", _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("name").GetString().ShouldBe("Second");
        body.GetProperty("allowedActions").EnumerateArray().Select(x => x.GetString()).ShouldContain("org.write");
        Guid id = body.GetProperty("id").GetGuid();
        (await _host.WithDatabaseAsync(db => db.Memberships.SingleAsync(x => x.OrganizationId == id, Token))).Role.ShouldBe(RolePermissions.Owner);
        _host.Audit.Events[^1].Action.ShouldBe("organization.created");

        await _host.CreateProjectAsync(_owner, organizationId: id);
    }

    [Theory, Trait("Spec", "0002:TST-4")]
    [InlineData("{}")]
    [InlineData("""{"name":"   "}""")]
    public async Task Create_answers_validation_failed_when_the_name_is_missing(string body)
    {
        using HttpResponseMessage response = await _host.PostAsync(Path, body, _owner);

        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact, Trait("Spec", "0017:ORG-2"), Trait("Spec", "0017:AC-9")]
    public async Task List_answers_only_the_operators_organizations()
    {
        ConsoleSession stranger = await _host.AddOperatorAsync();
        await CreateAsync("Theirs", stranger);

        (await NamesAsync()).ShouldBe(["Acme"]);
        (await NamesAsync(session: stranger)).ShouldBe(["Theirs"]);
    }

    [Fact, Trait("Spec", "0017:ORG-2"), Trait("Spec", "0017:AC-9")]
    public async Task List_query_matches_a_contains_ignoring_case_and_escapes_wildcards()
    {
        await CreateAsync("100% Shop");

        (await NamesAsync("?query=0%25%20sH")).ShouldBe(["100% Shop"]);
        (await NamesAsync("?query=_")).ShouldBeEmpty();
        (await NamesAsync("?query=cm")).ShouldBe(["Acme"]);
    }

    [Fact, Trait("Spec", "0017:ORG-2"), Trait("Spec", "0014:PAG-1")]
    public async Task List_pages_by_id()
    {
        await CreateAsync("Second");
        await CreateAsync("Third");

        using HttpResponseMessage first = await _host.GetAsync($"{Path}?limit=2", _owner);
        JsonElement page = (await ConsoleTestHost.ReadJsonAsync(first)).RootElement;
        page.GetProperty("items").GetArrayLength().ShouldBe(2);

        using HttpResponseMessage second = await _host.GetAsync($"{Path}?limit=2&cursor={page.GetProperty("nextCursor").GetString()}", _owner);
        JsonElement next = (await ConsoleTestHost.ReadJsonAsync(second)).RootElement;
        next.GetProperty("items").GetArrayLength().ShouldBe(1);
        next.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact, Trait("Spec", "0017:ORG-2"), Trait("Spec", "0002:TST-4")]
    public async Task List_answers_validation_failed_when_the_query_is_too_long()
    {
        using HttpResponseMessage response = await _host.GetAsync($"{Path}?query={new string('a', 129)}", _owner);

        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact, Trait("Spec", "0017:ORG-3"), Trait("Spec", "0017:AC-9"), Trait("Spec", "0017:AC-22")]
    public async Task Patch_renames_the_organization_and_moves_its_version()
    {
        using HttpResponseMessage read = await _host.GetAsync($"{Path}/{_owner.OrganizationId}", _owner);
        string version = (await ConsoleTestHost.ReadJsonAsync(read)).RootElement.GetProperty("version").GetString()!;

        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Patch, $"{Path}/{_owner.OrganizationId}", """{"name":" Renamed "}""", _owner, ifMatch: version);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull();
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("name").GetString().ShouldBe("Renamed");
        body.GetProperty("version").GetString().ShouldNotBe(version);
        _host.Audit.Events[^1].Action.ShouldBe("organization.updated");
    }

    [Fact, Trait("Spec", "0017:ORG-3"), Trait("Spec", "0014:CNC-3")]
    public async Task Patch_answers_412_when_the_version_is_stale()
    {
        using HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Patch, $"{Path}/{_owner.OrganizationId}", """{"name":"Late"}""", _owner, ifMatch: "AAAAAAAAAAE");

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.VersionConflict);
    }

    [Theory, Trait("Spec", "0002:TST-4")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":"   "}""")]
    public async Task Patch_answers_400_when_the_name_is_blank(string body)
    {
        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Patch, $"{Path}/{_owner.OrganizationId}", body, _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ConsoleTestHost.FieldCodeAsync(response)).ShouldBe(FieldCodes.Required);
    }
}
