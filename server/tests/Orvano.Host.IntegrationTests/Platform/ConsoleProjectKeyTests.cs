using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Projects;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 PJT-1, PJT-3, AKY-1 to AKY-3, AKY-9 to AKY-11, SDK-1, ACX-1, ACX-2, AC-9, AC-10, AC-13, AC-15.
public sealed class ConsoleProjectKeyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<ConsoleTestHost> _hosts = [];
    private readonly FakeParticipant _participant = new();
    private string _database = string.Empty;
    private ConsoleTestHost _host = null!;
    private ConsoleSession _owner = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.CreateMigratedDatabaseAsync("keys", Token);
        _host = Start();
        _owner = await _host.CreateOwnerAsync();
    }

    public ValueTask DisposeAsync()
    {
        foreach (ConsoleTestHost host in _hosts)
        {
            host.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private ConsoleTestHost Start(Dictionary<string, string>? settings = null)
    {
        var host = new ConsoleTestHost(postgres, _database, settings: settings, services: services =>
        {
            services.AddScoped<IProjectLifecycleParticipant>(provider =>
            {
                _participant.Services = provider;
                return _participant;
            });
        });
        _hosts.Add(host);
        return host;
    }

    private static string KeysPath(Guid projectId) => $"/v1/console/projects/{projectId}/keys";

    private static string Current(Guid projectId) => $"/v1/projects/{projectId}/keys/current";

    private Task<int> ExecuteAsync(string sql) => _host.WithDatabaseAsync(db => db.Database.ExecuteSqlRawAsync(sql, Token));

    // A second operator, a member of nothing, with a session made the way sign in makes one.
    private async Task<ConsoleSession> StrangerAsync()
    {
        DateTimeOffset now = _host.Clock.GetUtcNow();
        await _host.WithDatabaseAsync(async db =>
        {
            db.Operators.Add(new Operator
            {
                Email = "stranger@example.com",
                EmailNormalized = "stranger@example.com",
                Name = "Stranger",
                PasswordHash = (await db.Operators.Select(x => x.PasswordHash).FirstAsync(Token)),
                PasswordChangedAt = now,
            });
            return await db.SaveChangesAsync(Token);
        });
        return await _host.SignInAsync("stranger@example.com") with { OrganizationId = Guid.Empty };
    }

    // Organizations and membership

    [Fact, Trait("Spec", "0017:ORG-3"), Trait("Spec", "0017:AC-9")]
    public async Task Organization_get_answers_the_organization_with_allowed_actions_for_a_member()
    {
        using HttpResponseMessage response = await _host.GetAsync($"/v1/console/organizations/{_owner.OrganizationId}", _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull();
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("name").GetString().ShouldBe("Acme");
        body.GetProperty("allowedActions").EnumerateArray().Select(x => x.GetString()).ShouldContain("org.read");
    }

    [Fact, Trait("Spec", "0012:CPL-3"), Trait("Spec", "0017:AC-9")]
    public async Task Every_organization_and_project_route_answers_404_when_the_operator_has_no_membership()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        ConsoleSession stranger = await StrangerAsync();

        foreach ((HttpMethod method, string path, string? body) in new (HttpMethod, string, string?)[]
        {
            (HttpMethod.Get, $"/v1/console/organizations/{_owner.OrganizationId}", null),
            (HttpMethod.Get, $"/v1/console/organizations/{_owner.OrganizationId}/projects", null),
            (HttpMethod.Post, $"/v1/console/organizations/{_owner.OrganizationId}/projects", """{"name":"x"}"""),
            (HttpMethod.Get, $"/v1/console/projects/{projectId}", null),
            (HttpMethod.Get, KeysPath(projectId), null),
            (HttpMethod.Post, KeysPath(projectId), """{"type":"secret","name":"x","scopes":["storage.read"]}"""),
        })
        {
            using HttpResponseMessage response = await _host.SendAsync(method, path, body, stranger);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.NotFound);
        }
    }

    [Fact, Trait("Spec", "0017:ACX-1"), Trait("Spec", "0012:RSV-5")]
    public async Task Console_routes_answer_401_when_no_session_is_sent()
    {
        using HttpResponseMessage response = await _host.GetAsync($"/v1/console/organizations/{_owner.OrganizationId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Projects

    [Fact, Trait("Spec", "0017:PJT-1"), Trait("Spec", "0011:TXN-1"), Trait("Spec", "0017:AC-10"), Trait("Spec", "0017:AC-22")]
    public async Task Project_create_runs_the_participants_while_creating_and_answers_201_active()
    {
        using HttpResponseMessage response = await _host.PostAsync(
            $"/v1/console/organizations/{_owner.OrganizationId}/projects",
            """{"name":"  Shop ","allowedOrigins":["HTTPS://App.Example.com","https://app.example.com","http://localhost:3000"]}""",
            _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("name").GetString().ShouldBe("Shop");
        body.GetProperty("status").GetString().ShouldBe("active");
        body.GetProperty("allowedOrigins").EnumerateArray().Select(x => x.GetString())
            .ShouldBe(["https://app.example.com", "http://localhost:3000"]);
        body.GetProperty("services").GetArrayLength().ShouldBe(0);
        Guid projectId = body.GetProperty("id").GetGuid();
        _participant.Seen.ShouldHaveSingleItem().ShouldBe((projectId, ProjectStatus.Creating));
        _host.Audit.Events[^1].Action.ShouldBe("project.created");
        _host.Audit.Events[^1].ProjectId.ShouldBe(projectId);
    }

    [Theory, Trait("Spec", "0017:PJT-6")]
    [InlineData("https://app.example.com/path")]
    [InlineData("https://*.example.com")]
    [InlineData("ftp://app.example.com")]
    [InlineData("app.example.com")]
    [InlineData("https://user@app.example.com")]
    public async Task Project_create_answers_invalid_origin_naming_the_index_when_an_origin_is_not_exact(string origin)
    {
        using HttpResponseMessage response = await _host.PostAsync(
            $"/v1/console/organizations/{_owner.OrganizationId}/projects",
            JsonSerializer.Serialize(new { name = "Shop", allowedOrigins = new[] { "https://ok.example.com", origin } }),
            _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement error = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("errors")[0];
        error.GetProperty("code").GetString().ShouldBe(FieldCodes.InvalidOrigin);
        error.GetProperty("field").GetString().ShouldBe("allowedOrigins[1]");
    }

    [Fact, Trait("Spec", "0002:TST-4")]
    public async Task Project_and_key_create_answer_validation_failed_when_a_field_is_missing()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        using HttpResponseMessage project = await _host.PostAsync($"/v1/console/organizations/{_owner.OrganizationId}/projects", "{}", _owner);
        (await ConsoleTestHost.ErrorCodeAsync(project)).ShouldBe(ErrorCodes.ValidationFailed);

        using HttpResponseMessage key = await _host.PostAsync(KeysPath(projectId), "{}", _owner);

        (await ConsoleTestHost.ErrorCodeAsync(key)).ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact, Trait("Spec", "0017:PJT-5"), Trait("Spec", "0017:AC-10")]
    public async Task Project_read_and_list_answer_the_project_and_query_escapes_wildcards()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner, "100% Shop");
        await _host.CreateProjectAsync(_owner, "Other");

        using HttpResponseMessage one = await _host.GetAsync($"/v1/console/projects/{projectId}", _owner);
        (await ConsoleTestHost.ReadJsonAsync(one)).RootElement.GetProperty("name").GetString().ShouldBe("100% Shop");

        using HttpResponseMessage all = await _host.GetAsync($"/v1/console/organizations/{_owner.OrganizationId}/projects", _owner);
        (await ConsoleTestHost.ReadJsonAsync(all)).RootElement.GetProperty("items").GetArrayLength().ShouldBe(2);

        using HttpResponseMessage found = await _host.GetAsync($"/v1/console/organizations/{_owner.OrganizationId}/projects?query=0%25%20sh", _owner);
        (await ConsoleTestHost.ReadJsonAsync(found)).RootElement.GetProperty("items").GetArrayLength().ShouldBe(1);

        using HttpResponseMessage none = await _host.GetAsync($"/v1/console/organizations/{_owner.OrganizationId}/projects?query=_", _owner);
        (await ConsoleTestHost.ReadJsonAsync(none)).RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact, Trait("Spec", "0017:PJT-3"), Trait("Spec", "0011:LIF-4"), Trait("Spec", "0017:AC-10")]
    public async Task Directory_finds_only_active_projects()
    {
        Guid active = await _host.CreateProjectAsync(_owner);
        await ExecuteAsync("""
            INSERT INTO platform.project (id, organization_id, name, status, created_at, updated_at)
            SELECT gen_random_uuid(), organization_id, 'Half made', 'creating', now(), now() FROM platform.project LIMIT 1
            """);
        Guid creating = await _host.WithDatabaseAsync(db => db.Projects.Where(x => x.Name == "Half made").Select(x => x.Id).SingleAsync(Token));

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IProjectDirectory directory = scope.ServiceProvider.GetRequiredService<IProjectDirectory>();

        (await directory.FindActiveAsync(active, Token)).ShouldNotBeNull().OrganizationId.ShouldBe(_owner.OrganizationId);
        (await directory.FindActiveAsync(creating, Token)).ShouldBeNull();
        (await directory.FindActiveAsync(Guid.CreateVersion7(), Token)).ShouldBeNull();
    }

    // Keys

    [Fact, Trait("Spec", "0017:AKY-2"), Trait("Spec", "0017:AKY-3"), Trait("Spec", "0017:AC-13"), Trait("Spec", "0017:AC-22")]
    public async Task Key_create_answers_the_full_key_once_and_stores_only_its_hash_prefix_and_last_4()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);

        using HttpResponseMessage response = await _host.PostAsync(
            KeysPath(projectId), """{"type":"secret","name":" Worker ","scopes":["storage.read","databases.read"],"expiresInDays":30}""", _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        string key = body.GetProperty("key").GetString()!;
        key.ShouldMatch("^orv_sk_[A-Za-z0-9_-]{43}$");
        body.GetProperty("prefix").GetString().ShouldBe(key[..15]);
        body.GetProperty("last4").GetString().ShouldBe(key[^4..]);
        body.GetProperty("name").GetString().ShouldBe("Worker");
        body.GetProperty("status").GetString().ShouldBe("active");
        body.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).ShouldBe(["databases.read", "storage.read"]);

        await _host.WithDatabaseAsync(async db =>
        {
            ApiKey row = await db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name]).SingleAsync(Token);
            row.Hash.ShouldBe(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
            row.Prefix.ShouldBe(key[..15]);
            row.Last4.ShouldBe(key[^4..]);
            row.CreatedByOperatorId.ShouldBe(_owner.OperatorId);
            row.ExpiresAt.ShouldBe(_host.Clock.GetUtcNow().AddDays(30));
            return true;
        });

        using HttpResponseMessage list = await _host.GetAsync(KeysPath(projectId), _owner);
        string listed = await list.Content.ReadAsStringAsync(Token);
        listed.ShouldNotContain(key);
        listed.ShouldNotContain("\"key\"");
        _host.Audit.Events[^1].Action.ShouldBe("api_key.created");
        _host.Audit.Events[^1].Metadata.ShouldBeNull();
        _host.Logs.Lines.ShouldNotContain(line => line.Contains(key, StringComparison.Ordinal));
    }

    [Fact, Trait("Spec", "0017:AKY-1"), Trait("Spec", "0017:AC-13")]
    public async Task Key_create_gives_a_publishable_key_every_client_scope_when_scopes_are_left_out()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);

        using HttpResponseMessage response = await _host.PostAsync(KeysPath(projectId), """{"type":"publishable","name":"Web"}""", _owner);

        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("key").GetString().ShouldStartWith("orv_pk_");
        body.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).ShouldBe(Scopes.Publishable, ignoreOrder: true);
    }

    [Theory, Trait("Spec", "0017:AKY-1"), Trait("Spec", "0012:SCP-3"), Trait("Spec", "0012:SCP-4"), Trait("Spec", "0017:AC-13")]
    [InlineData("""{"type":"secret","name":"x"}""")]
    [InlineData("""{"type":"other","name":"x"}""")]
    [InlineData("""{"type":"secret","name":"x","scopes":[]}""")]
    [InlineData("""{"type":"publishable","name":"x","scopes":[]}""")]
    [InlineData("""{"type":"secret","name":"x","scopes":["client.auth"]}""")]
    [InlineData("""{"type":"publishable","name":"x","scopes":["storage.read"]}""")]
    [InlineData("""{"type":"secret","name":"x","scopes":["users.impersonate"]}""")]
    [InlineData("""{"type":"secret","name":"x","scopes":["storage.read"],"expiresInDays":0}""")]
    public async Task Key_create_answers_validation_failed_when_its_scopes_or_expiry_are_refused(string body)
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);

        using HttpResponseMessage response = await _host.PostAsync(KeysPath(projectId), body, _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact, Trait("Spec", "0017:AKY-5")]
    public async Task Key_create_answers_409_when_the_project_is_not_active()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        await ExecuteAsync("UPDATE platform.project SET status = 'deleting', deletion_requested_at = now()");

        using HttpResponseMessage response = await _host.PostAsync(KeysPath(projectId), """{"type":"publishable","name":"Web"}""", _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("project_not_active");
    }

    [Fact, Trait("Spec", "0017:AKY-5"), Trait("Spec", "0017:AC-13")]
    public async Task Key_list_answers_newest_first_and_pages()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        foreach (string name in new[] { "One", "Two", "Three" })
        {
            using HttpResponseMessage created = await _host.PostAsync(KeysPath(projectId), JsonSerializer.Serialize(new { type = "publishable", name }), _owner);
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            _host.Clock.Advance(TimeSpan.FromMilliseconds(2));
            await Task.Delay(2, Token); // uuid v7's clock is the wall clock
        }

        using HttpResponseMessage first = await _host.GetAsync($"{KeysPath(projectId)}?limit=2", _owner);
        JsonElement page = (await ConsoleTestHost.ReadJsonAsync(first)).RootElement;
        page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ShouldBe(["Three", "Two"]);

        string cursor = page.GetProperty("nextCursor").GetString()!;
        using HttpResponseMessage second = await _host.GetAsync($"{KeysPath(projectId)}?limit=2&cursor={cursor}", _owner);
        JsonElement next = (await ConsoleTestHost.ReadJsonAsync(second)).RootElement;
        next.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ShouldBe(["One"]);
        next.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // keys.current

    [Fact, Trait("Spec", "0017:SDK-1"), Trait("Spec", "0017:AC-15")]
    public async Task Current_key_answers_the_project_type_and_scopes_for_each_key_type()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string secret = await _host.CreateKeyAsync(_owner, projectId);
        string publishable = await _host.CreateKeyAsync(_owner, projectId, "publishable");

        using HttpResponseMessage server = await _host.GetAsync(Current(projectId), key: secret);
        server.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(server)).RootElement;
        body.GetProperty("projectId").GetGuid().ShouldBe(projectId);
        body.GetProperty("type").GetString().ShouldBe("secret");
        body.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).ShouldBe(["storage.read"]);

        using HttpResponseMessage client = await _host.GetAsync(Current(projectId), key: publishable, origin: "https://app.example.com");
        client.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ConsoleTestHost.ReadJsonAsync(client)).RootElement.GetProperty("type").GetString().ShouldBe("publishable");
    }

    [Fact, Trait("Spec", "0017:AKY-9"), Trait("Spec", "0012:PRJ-2"), Trait("Spec", "0017:AC-15")]
    public async Task Current_key_answers_401_for_each_key_that_must_not_pass()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        Guid otherProject = await _host.CreateProjectAsync(_owner, "Other");
        string secret = await _host.CreateKeyAsync(_owner, projectId);
        string unknown = secret[..^4] + "AAAA";

        foreach ((Guid route, string? key) in new (Guid, string?)[]
        {
            (otherProject, secret),
            (projectId, unknown),
            (projectId, "orv_sk_short"),
            (projectId, "not a key at all"),
            (projectId, null),
        })
        {
            using HttpResponseMessage response = await _host.GetAsync(Current(route), key: key);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.Unauthorized);
        }
    }

    [Theory, Trait("Spec", "0017:AKY-9"), Trait("Spec", "0017:AC-15")]
    [InlineData("UPDATE platform.api_key SET revoked_at = now()")]
    [InlineData("UPDATE platform.project SET status = 'deleting', deletion_requested_at = now()")]
    [InlineData("UPDATE platform.project SET status = 'creating'")]
    public async Task Current_key_answers_401_on_the_next_request_when_the_key_is_revoked_or_the_project_not_active(string sql)
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string secret = await _host.CreateKeyAsync(_owner, projectId);
        (await _host.GetAsync(Current(projectId), key: secret)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ExecuteAsync(sql);

        using HttpResponseMessage response = await _host.GetAsync(Current(projectId), key: secret);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Spec", "0017:AKY-9"), Trait("Spec", "0017:AC-15")]
    public async Task Current_key_answers_401_when_the_key_expired()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        using HttpResponseMessage created = await _host.PostAsync(
            KeysPath(projectId), """{"type":"secret","name":"x","scopes":["storage.read"],"expiresInDays":1}""", _owner);
        string key = (await ConsoleTestHost.ReadJsonAsync(created)).RootElement.GetProperty("key").GetString()!;

        _host.Clock.Advance(TimeSpan.FromDays(1));

        using HttpResponseMessage response = await _host.GetAsync(Current(projectId), key: key);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Spec", "0017:AKY-10"), Trait("Spec", "0006:KEY-1"), Trait("Spec", "0017:AC-15")]
    public async Task Current_key_answers_401_for_a_secret_key_with_an_origin_header_unless_the_operator_allows_it()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string secret = await _host.CreateKeyAsync(_owner, projectId);

        using HttpResponseMessage refused = await _host.GetAsync(Current(projectId), key: secret, origin: "https://app.example.com");
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        ConsoleTestHost allowing = Start(new() { ["Keys:AllowSecretKeyFromBrowser"] = "true" });
        using HttpResponseMessage allowed = await allowing.GetAsync(Current(projectId), key: secret, origin: "https://app.example.com");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact, Trait("Spec", "0017:AKY-10"), Trait("Spec", "0006:KEY-2"), Trait("Spec", "0017:AC-15")]
    public async Task Current_key_answers_401_for_a_secret_key_with_a_bearer_token()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string secret = await _host.CreateKeyAsync(_owner, projectId);

        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Get, Current(projectId), origin: null, key: secret, authorization: "Bearer abc");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Spec", "0017:AKY-10"), Trait("Spec", "0012:RSV-3"), Trait("Spec", "0017:AC-15")]
    public async Task Operator_route_answers_401_when_a_key_comes_with_the_session()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string secret = await _host.CreateKeyAsync(_owner, projectId);

        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Get, "/v1/console/session", session: _owner, key: secret);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Spec", "0017:SES-4"), Trait("Spec", "0017:AC-15")]
    public async Task Current_key_never_reads_the_session_cookie()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);

        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Get, Current(projectId), session: _owner, origin: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Spec", "0006:KEY-4"), Trait("Spec", "0017:AKY-9")]
    public async Task Current_key_answers_403_insufficient_scope_when_a_stored_scope_is_outside_the_ceiling()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string secret = await _host.CreateKeyAsync(_owner, projectId);
        await ExecuteAsync("""
            ALTER TABLE platform.api_key DROP CONSTRAINT ck_api_key_scopes_secret;
            UPDATE platform.api_key SET scopes = ARRAY['users.impersonate'];
            """);

        using HttpResponseMessage response = await _host.GetAsync(Current(projectId), key: secret);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.InsufficientScope);
    }

    [Fact, Trait("Spec", "0017:AKY-11"), Trait("Spec", "0006:KEY-3")]
    public async Task Last_used_at_is_written_at_most_once_a_minute_and_leaves_updated_at_alone()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        string secret = await _host.CreateKeyAsync(_owner, projectId);
        DateTimeOffset created = _host.Clock.GetUtcNow();

        (await _host.GetAsync(Current(projectId), key: secret)).Dispose();
        (await ReadAsync()).ShouldBe((created, created));

        _host.Clock.Advance(TimeSpan.FromSeconds(30));
        (await _host.GetAsync(Current(projectId), key: secret)).Dispose();
        (await ReadAsync()).ShouldBe((created, created));

        _host.Clock.Advance(TimeSpan.FromSeconds(30));
        (await _host.GetAsync(Current(projectId), key: secret)).Dispose();
        (await ReadAsync()).ShouldBe((created + TimeSpan.FromMinutes(1), created));

        Task<(DateTimeOffset?, DateTimeOffset)> ReadAsync() => _host.WithDatabaseAsync(async db =>
        {
            ApiKey row = await db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name]).AsNoTracking().SingleAsync(Token);
            return (row.LastUsedAt, row.UpdatedAt);
        });
    }

    // Filters

    [Fact, Trait("Spec", "0002:DAT-2a"), Trait("Spec", "0017:ACX-5")]
    public async Task Project_filter_throws_rather_than_reads_when_no_project_is_set()
    {
        Guid projectId = await _host.CreateProjectAsync(_owner);
        await _host.CreateKeyAsync(_owner, projectId);

        await Should.ThrowAsync<InvalidOperationException>(() => _host.WithDatabaseAsync(db => db.ApiKeys.CountAsync(Token)));
    }

    private sealed class FakeParticipant : IProjectLifecycleParticipant
    {
        public IServiceProvider Services { get; set; } = null!;

        public List<(Guid Id, ProjectStatus Status)> Seen { get; } = [];

        public async Task CreateAsync(Guid projectId, CancellationToken cancellationToken)
        {
            PlatformDbContext db = Services.GetRequiredService<PlatformDbContext>();
            ProjectStatus status = await db.Projects.AsNoTracking().Where(x => x.Id == projectId).Select(x => x.Status).SingleAsync(cancellationToken);
            Seen.Add((projectId, status));
        }

        public Task TeardownAsync(Guid projectId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
