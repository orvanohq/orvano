using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Data;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 AKY-5 to AKY-8, AC-14: revoke, regenerate, rename, and a key's status.
public sealed class ConsoleKeyLifecycleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _database = string.Empty;
    private ConsoleTestHost _host = null!;
    private ConsoleSession _owner = null!;
    private Guid _projectId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.CreateMigratedDatabaseAsync("key_lifecycle", Token);
        _host = new ConsoleTestHost(postgres, _database);
        _owner = await _host.CreateOwnerAsync();
        _projectId = await _host.CreateProjectAsync(_owner);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private string Keys => $"/v1/console/projects/{_projectId}/keys";

    private string Current => $"/v1/projects/{_projectId}/keys/current";

    private async Task<(Guid Id, string Key)> CreateKeyAsync(string body = """{"type":"secret","name":"Worker","scopes":["storage.write","storage.read"],"expiresInDays":30}""")
    {
        using HttpResponseMessage response = await _host.PostAsync(Keys, body, _owner);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement created = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        return (created.GetProperty("id").GetGuid(), created.GetProperty("key").GetString()!);
    }

    private Task<HttpResponseMessage> PostAsync(Guid keyId, string action) => _host.PostAsync($"{Keys}/{keyId}/{action}", "{}", _owner);

    private Task<int> ExecuteAsync(string sql) => _host.WithDatabaseAsync(db => db.Database.ExecuteSqlRawAsync(sql, Token));

    private async Task<HttpStatusCode> StatusOfAsync(string key)
    {
        using HttpResponseMessage response = await _host.GetAsync(Current, key: key);
        return response.StatusCode;
    }

    // Revoke

    [Fact, Trait("Spec", "0017:AKY-6"), Trait("Spec", "0017:AC-14"), Trait("Spec", "0017:AC-22")]
    public async Task Revoke_answers_200_stops_the_key_at_once_and_a_second_revoke_changes_nothing()
    {
        (Guid id, string key) = await CreateKeyAsync();
        (await StatusOfAsync(key)).ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage first = await PostAsync(id, "revoke");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement revoked = (await ConsoleTestHost.ReadJsonAsync(first)).RootElement;
        revoked.GetProperty("status").GetString().ShouldBe("revoked");
        (await first.Content.ReadAsStringAsync(Token)).ShouldNotContain("\"key\"");
        (await StatusOfAsync(key)).ShouldBe(HttpStatusCode.Unauthorized);

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage second = await PostAsync(id, "revoke");

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ConsoleTestHost.ReadJsonAsync(second)).RootElement.GetProperty("revokedAt").GetDateTimeOffset()
            .ShouldBe(revoked.GetProperty("revokedAt").GetDateTimeOffset());
        _host.Audit.Events.Count(x => x.Action == "api_key.revoked").ShouldBe(1);
    }

    [Fact, Trait("Spec", "0017:AKY-9"), Trait("Spec", "0006:KEY-3"), Trait("Spec", "0017:AC-14")]
    public async Task Revoke_on_one_host_applies_on_the_next_request_on_another()
    {
        (Guid id, string key) = await CreateKeyAsync();
        using var other = new ConsoleTestHost(postgres, _database, _host.Clock);
        using (HttpResponseMessage before = await other.GetAsync(Current, key: key))
        {
            before.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await PostAsync(id, "revoke")).Dispose();

        using HttpResponseMessage after = await other.GetAsync(Current, key: key);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Spec", "0017:AKY-5")]
    public async Task Revoke_and_list_work_when_the_project_is_being_deleted()
    {
        (Guid id, _) = await CreateKeyAsync();
        await ExecuteAsync("UPDATE platform.project SET status = 'deleting', deletion_requested_at = now()");

        using HttpResponseMessage revoke = await PostAsync(id, "revoke");
        using HttpResponseMessage list = await _host.GetAsync(Keys, _owner);

        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Regenerate

    [Fact, Trait("Spec", "0017:AKY-7"), Trait("Spec", "0017:AC-14"), Trait("Spec", "0017:AC-22")]
    public async Task Regenerate_replaces_the_key_with_the_same_type_name_scopes_and_expiry()
    {
        (Guid id, string key) = await CreateKeyAsync();

        using HttpResponseMessage response = await PostAsync(id, "regenerate");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        string replacement = body.GetProperty("key").GetString()!;
        replacement.ShouldNotBe(key);
        body.GetProperty("id").GetGuid().ShouldNotBe(id);
        body.GetProperty("type").GetString().ShouldBe("secret");
        body.GetProperty("name").GetString().ShouldBe("Worker");
        body.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).ShouldBe(["storage.read", "storage.write"]);
        await _host.WithDatabaseAsync(async db =>
        {
            List<ApiKey> rows = await db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name]).AsNoTracking().ToListAsync(Token);
            rows.Count.ShouldBe(2);
            rows.Single(x => x.Id == id).RevokedAt.ShouldNotBeNull();
            rows.Single(x => x.Id != id).ExpiresAt.ShouldBe(rows.Single(x => x.Id == id).ExpiresAt);
            return true;
        });
        (await StatusOfAsync(key)).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusOfAsync(replacement)).ShouldBe(HttpStatusCode.OK);
        _host.Audit.Events[^1].Action.ShouldBe("api_key.regenerated");
        _host.Logs.Lines.ShouldNotContain(line => line.Contains(replacement, StringComparison.Ordinal));
    }

    [Theory, Trait("Spec", "0017:AKY-7"), Trait("Spec", "0017:AC-14")]
    [InlineData("UPDATE platform.api_key SET revoked_at = now()")]
    [InlineData("UPDATE platform.api_key SET expires_at = '2020-01-01'")]
    public async Task Regenerate_answers_409_when_the_key_is_not_active(string sql)
    {
        (Guid id, _) = await CreateKeyAsync();
        await ExecuteAsync(sql);

        using HttpResponseMessage response = await PostAsync(id, "regenerate");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("api_key_not_active");
    }

    [Fact, Trait("Spec", "0017:AKY-7"), Trait("Spec", "0017:AC-14")]
    public async Task Two_regenerates_at_once_mint_one_key()
    {
        (Guid id, _) = await CreateKeyAsync();

        HttpResponseMessage[] responses = await Task.WhenAll(PostAsync(id, "regenerate"), PostAsync(id, "regenerate"));

        responses.Select(x => x.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true);
        (await _host.WithDatabaseAsync(db => db.ApiKeys.IgnoreQueryFilters([ProjectFilter.Name]).CountAsync(Token))).ShouldBe(2);
    }

    [Theory, Trait("Spec", "0017:AKY-5")]
    [InlineData("regenerate")]
    [InlineData("rename")]
    public async Task Regenerate_and_rename_answer_409_when_the_project_is_not_active(string action)
    {
        (Guid id, _) = await CreateKeyAsync();
        await ExecuteAsync("UPDATE platform.project SET status = 'deleting', deletion_requested_at = now()");

        using HttpResponseMessage response = action == "rename"
            ? await _host.SendAsync(HttpMethod.Patch, $"{Keys}/{id}", """{"name":"New"}""", _owner)
            : await PostAsync(id, action);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("project_not_active");
    }

    // Rename and status

    [Fact, Trait("Spec", "0017:AKY-8"), Trait("Spec", "0017:AC-14")]
    public async Task Rename_changes_only_the_name_and_takes_no_version()
    {
        (Guid id, string key) = await CreateKeyAsync();

        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Patch, $"{Keys}/{id}", """{"name":"  New name "}""", _owner, ifMatch: "stale");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldBeNull();
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("name").GetString().ShouldBe("New name");
        body.GetProperty("scopes").GetArrayLength().ShouldBe(2);
        body.TryGetProperty("version", out _).ShouldBeFalse();
        (await StatusOfAsync(key)).ShouldBe(HttpStatusCode.OK);
    }

    [Theory, Trait("Spec", "0002:TST-4"), Trait("Spec", "0017:AKY-8")]
    [InlineData("{}")]
    [InlineData("""{"name":"   "}""")]
    [InlineData("""{"name":"x","scopes":[]}""")]
    public async Task Rename_answers_400_when_the_name_is_missing_or_another_field_is_sent(string body)
    {
        (Guid id, _) = await CreateKeyAsync();

        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Patch, $"{Keys}/{id}", body, _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory, Trait("Spec", "0017:AKY-6"), Trait("Spec", "0017:AKY-7"), Trait("Spec", "0017:AKY-8")]
    [InlineData("revoke")]
    [InlineData("regenerate")]
    public async Task Revoke_and_regenerate_answer_404_when_the_key_is_unknown(string action)
    {
        using HttpResponseMessage response = await PostAsync(Guid.CreateVersion7(), action);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Spec", "0017:AKY-8")]
    public async Task Rename_answers_404_when_the_key_is_unknown()
    {
        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Patch, $"{Keys}/{Guid.CreateVersion7()}", """{"name":"x"}""", _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Spec", "0017:AKY-5"), Trait("Spec", "0017:AC-14")]
    public async Task List_derives_each_status_from_the_clock_and_the_dates()
    {
        (Guid revoked, _) = await CreateKeyAsync();
        (await PostAsync(revoked, "revoke")).Dispose();
        (Guid expiring, _) = await CreateKeyAsync("""{"type":"secret","name":"Soon","scopes":["storage.read"],"expiresInDays":1}""");
        (Guid lasting, _) = await CreateKeyAsync("""{"type":"publishable","name":"Web"}""");
        await ExecuteAsync("UPDATE platform.api_key SET expires_at = '2020-01-01' WHERE name = 'Soon'");

        using HttpResponseMessage response = await _host.GetAsync(Keys, _owner);

        var statuses = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetGuid(), x => x.GetProperty("status").GetString());
        statuses[revoked].ShouldBe("revoked");
        statuses[expiring].ShouldBe("expired");
        statuses[lasting].ShouldBe("active");
    }
}
