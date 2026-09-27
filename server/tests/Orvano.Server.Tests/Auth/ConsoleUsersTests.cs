using System.Net;
using System.Text.Json;
using Orvano.Auth.Jobs;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0004 build task 9 over HTTP against the real binary: the console's Users page and signing keys panel for the
// project in X-Orvano-Project, with the roles (AC-22, AC-29).
public class ConsoleUsersTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_owner_manages_a_projects_users_and_their_sessions_from_the_console()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var user = AuthApi.UserId(signUp);

        using var created = await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/users", new { email = "grace@x.com", password = "correct horse battery" });
        using var listed = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/users?email=ADA");
        using var sessions = await api.AsConsoleAsync(HttpMethod.Get, $"/v1/console/project/users/{user}/sessions");
        using var blocked = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/project/users/{user}/block");
        using var unblocked = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/project/users/{user}/unblock");
        using var deleted = await api.AsConsoleAsync(HttpMethod.Delete, $"/v1/console/project/users/{created.Body.GetProperty("id").GetString()}");
        using var elsewhere = await api.AsConsoleAsync(HttpMethod.Get, $"/v1/console/project/users/{user}", project: AuthApi.OtherProject);

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal([user], listed.Body.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetString()));
        Assert.Single(sessions.Body.GetProperty("items").EnumerateArray());
        Assert.Equal("blocked", blocked.Body.GetProperty("status").GetString());
        Assert.Equal("active", unblocked.Body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal("user_not_found", elsewhere.Code); // the owner's other project has no such user
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.blocked' AND payload->'actor'->>'type' = 'user'"));
    }

    [Fact]
    public async Task Viewers_read_developers_change_users_only_owners_rotate_and_strangers_see_nothing()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var user = AuthApi.UserId(signUp);
        var (developer, viewer, stranger) = (AuthApi.OtherConsoleUsers[0], AuthApi.OtherConsoleUsers[1], AuthApi.OtherConsoleUsers[2]);
        await api.GrantAsync(developer, "developer");
        await api.GrantAsync(viewer, "viewer");

        using var viewerList = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/users", account: viewer);
        using var viewerKeys = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/auth/keys", account: viewer);
        using var viewerBlock = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/project/users/{user}/block", account: viewer);
        using var developerBlock = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/project/users/{user}/block", account: developer);
        using var developerRotate = await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/auth/keys/rotate", account: developer);
        using var strangerList = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/users", account: stranger);
        using var noProject = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/users", project: null);
        using var console = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/users", project: "console");

        Assert.Equal(HttpStatusCode.OK, viewerList.Status);
        Assert.Equal(HttpStatusCode.OK, viewerKeys.Status);
        Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), (viewerBlock.Status, viewerBlock.Code));
        Assert.Equal("blocked", developerBlock.Body.GetProperty("status").GetString());
        Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), (developerRotate.Status, developerRotate.Code));
        Assert.Equal("project_not_found", strangerList.Code);
        Assert.Equal("invalid_request", noProject.Code);
        Assert.Equal("project_not_found", console.Code);
    }

    [Fact]
    public async Task Rotation_signs_with_a_new_key_keeps_the_old_one_for_24_hours_then_drops_it_from_the_jwks()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var before = await api.SignUpAsync("ada@x.com");
        var oldKid = Kid(AuthApi.AccessToken(before));

        using var rotated = await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/auth/keys/rotate");
        using var after = await api.SignInAsync("ada@x.com");
        using var oldTokenStillWorks = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(before));
        using var jwks = await api.SendAsync(HttpMethod.Get, $"/v1/projects/{AuthApi.Project}/.well-known/jwks.json", project: null);

        var keys = rotated.Body.GetProperty("keys").EnumerateArray().ToList();
        Assert.Equal(["active", "retiring"], keys.Select(k => k.GetProperty("status").GetString()));
        Assert.Equal(oldKid, keys[1].GetProperty("id").GetString());
        Assert.InRange(keys[1].GetProperty("retireAfter").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24.1));
        Assert.Equal(JsonValueKind.Null, keys[0].GetProperty("retireAfter").ValueKind);
        Assert.Equal(keys[0].GetProperty("id").GetString(), Kid(AuthApi.AccessToken(after)));
        Assert.Equal(HttpStatusCode.OK, oldTokenStillWorks.Status);
        Assert.Equal(2, jwks.Body.GetProperty("keys").GetArrayLength());
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.key.rotated' AND payload->>'previousKeyId' = @kid", ("kid", oldKid)));

        await TestDatabase.ExecuteAsync(api.Database.Superuser, "UPDATE orvano.auth_signing_keys SET retire_after = now() - interval '1 second' WHERE status = 'retiring'");
        await using (var app = Npgsql.NpgsqlDataSource.Create(api.Database.AppUrl))
            await AuthRetention.RunAsync(app, Ct);
        using var later = await api.SendAsync(HttpMethod.Get, $"/v1/projects/{AuthApi.Project}/.well-known/jwks.json", project: null);

        Assert.Equal([keys[0].GetProperty("id").GetString()], later.Body.GetProperty("keys").EnumerateArray().Select(k => k.GetProperty("kid").GetString()));
    }

    private static string Kid(string token) =>
        JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(token.Split('.')[0])).RootElement.GetProperty("kid").GetString()!;
}
