using System.Net;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0004 build task 5 over HTTP against the real binary: the signed in user changes their name, metadata, and
// password, and deletes themselves. AC-13, AC-14, AC-15, AC-30, AC-33.
public class SelfServiceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_user_changes_their_name_and_metadata_and_only_real_changes_write_an_event()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var bearer = AuthApi.AccessToken(signUp);

        using var both = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { name = "Ada", metadata = new { plan = "pro", seats = 3 } }, bearer: bearer);
        using var nothing = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { }, bearer: bearer);
        using var same = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { name = "Ada" }, bearer: bearer);
        using var metadataOnly = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { metadata = new { plan = "team" } }, bearer: bearer);
        using var cleared = await api.SendAsync(HttpMethod.Patch, "/v1/account", JsonDocument.Parse("""{"name":null}""").RootElement, bearer: bearer);
        using var me = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: bearer);

        Assert.Equal(HttpStatusCode.OK, both.Status);
        Assert.Equal("Ada", both.Body.GetProperty("name").GetString());
        Assert.Equal(3, both.Body.GetProperty("metadata").GetProperty("seats").GetInt32());
        Assert.Equal("Ada", metadataOnly.Body.GetProperty("name").GetString()); // a field left out stays
        Assert.Equal("team", metadataOnly.Body.GetProperty("metadata").GetProperty("plan").GetString());
        Assert.False(metadataOnly.Body.GetProperty("metadata").TryGetProperty("seats", out _)); // replaced, not merged
        Assert.Equal(JsonValueKind.Null, cleared.Body.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, me.Body.GetProperty("name").ValueKind);

        var changes = await TestDatabase.ScalarAsync<string[]>(api.Database.Superuser,
            "SELECT array_agg(payload->>'changed' ORDER BY id) FROM orvano.events WHERE type = 'auth.user.updated'");
        Assert.Equal(["[\"name\", \"metadata\"]", "[\"metadata\"]", "[\"name\"]"], changes);
        var payloads = await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT string_agg(payload::text, ' ') FROM orvano.events WHERE type = 'auth.user.updated'");
        Assert.DoesNotContain("Ada", payloads, StringComparison.Ordinal);
        Assert.DoesNotContain("team", payloads, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_update_refuses_a_long_name_metadata_that_is_not_an_object_or_too_big()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var bearer = AuthApi.AccessToken(signUp);

        using var longName = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { name = new string('a', 257) }, bearer: bearer);
        using var list = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { metadata = new[] { 1, 2 } }, bearer: bearer);
        using var nullMetadata = await api.SendAsync(HttpMethod.Patch, "/v1/account", JsonDocument.Parse("""{"metadata":null}""").RootElement, bearer: bearer);
        using var big = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { metadata = new { blob = new string('x', 16 * 1024) } }, bearer: bearer);
        using var wrongType = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { name = 5 }, bearer: bearer);
        using var justFits = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { name = new string('a', 256), metadata = new { blob = new string('x', 16 * 1024 - 11) } }, bearer: bearer);

        foreach (var refused in new[] { longName, list, nullMetadata, big, wrongType })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("invalid_request", refused.Code);
        }

        Assert.Equal(HttpStatusCode.OK, justFits.Status);
    }

    [Fact]
    public async Task A_password_change_ends_every_other_session_and_keeps_the_callers()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var laptop = await api.SignUpAsync("ada@x.com");
        using var phone = await api.SignInAsync("ada@x.com");
        using var tablet = await api.SignInAsync("ada@x.com");
        using var phoneBefore = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(phone));
        var bearer = AuthApi.AccessToken(laptop);

        using var wrong = await Change(api, bearer, "wrong horse battery", "new horse battery staple");
        using var weak = await Change(api, bearer, "correct horse battery", "short");
        using var changed = await Change(api, bearer, "correct horse battery", "new horse battery staple");
        using var laptopAfter = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: bearer);
        using var laptopRefresh = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(laptop) });
        using var phoneAfter = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(phone));
        using var tabletRefresh = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(tablet) });
        using var oldPassword = await api.SignInAsync("ada@x.com");
        using var newPassword = await api.SignInAsync("ada@x.com", "new horse battery staple");

        Assert.Equal("invalid_credentials", wrong.Code);
        Assert.Equal("invalid_password", weak.Code);
        Assert.Equal(HttpStatusCode.NoContent, changed.Status);
        Assert.Equal(HttpStatusCode.OK, laptopAfter.Status);
        Assert.Equal(HttpStatusCode.OK, laptopRefresh.Status);
        Assert.Equal(HttpStatusCode.OK, phoneBefore.Status);
        Assert.Equal("invalid_token", phoneAfter.Code);
        Assert.Equal("invalid_refresh_token", tabletRefresh.Code);
        Assert.Equal("invalid_credentials", oldPassword.Code);
        Assert.Equal(HttpStatusCode.Created, newPassword.Status);
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.password.changed'"));
        Assert.Equal(2L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.session.ended' AND payload->>'reason' = 'password_changed'"));
    }

    [Fact]
    public async Task A_user_deletes_themselves_with_their_password_and_every_token_stops()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var laptop = await api.SignUpAsync("ada@x.com");
        using var phone = await api.SignInAsync("ada@x.com");
        using var phoneBefore = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(phone));
        var userId = Guid.Parse(AuthApi.UserId(laptop));

        using var wrong = await api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { password = "wrong horse battery" }, bearer: AuthApi.AccessToken(laptop));
        using var deleted = await api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { password = "correct horse battery" }, bearer: AuthApi.AccessToken(laptop));
        using var phoneAfter = await api.SendAsync(HttpMethod.Get, "/v1/account", bearer: AuthApi.AccessToken(phone));
        using var signIn = await api.SignInAsync("ada@x.com");
        using var again = await api.SignUpAsync("ada@x.com");

        Assert.Equal("invalid_credentials", wrong.Code);
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal(HttpStatusCode.OK, phoneBefore.Status);
        Assert.Equal("invalid_token", phoneAfter.Code);
        Assert.Equal("invalid_credentials", signIn.Code);
        Assert.Equal(HttpStatusCode.Created, again.Status);
        foreach (var table in new[] { "auth_users WHERE id", "auth_passwords WHERE user_id", "auth_sessions WHERE user_id" })
            Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, $"SELECT count(*) FROM orvano.{table} = @id", ("id", userId)));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.deleted' AND subject = @id::text AND payload->'actor'->>'type' = 'user'", ("id", userId)));
    }

    [Fact]
    public async Task The_11th_password_check_of_one_user_within_15_minutes_is_limited()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var bearer = AuthApi.AccessToken(signUp);

        for (var i = 0; i < 5; i++)
        {
            using var change = await Change(api, bearer, "wrong horse battery", "new horse battery staple");
            using var delete = await api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { password = "wrong horse battery" }, bearer: bearer);
            Assert.Equal("invalid_credentials", change.Code);
            Assert.Equal("invalid_credentials", delete.Code);
        }

        using var limited = await Change(api, bearer, "correct horse battery", "new horse battery staple");
        using var other = await api.SignUpAsync("grace@x.com");
        using var otherUser = await Change(api, AuthApi.AccessToken(other), "correct horse battery", "new horse battery staple");

        Assert.Equal((HttpStatusCode)429, limited.Status);
        Assert.Equal("rate_limited", limited.Code);
        Assert.Equal(HttpStatusCode.NoContent, otherUser.Status);
    }

    private static Task<Reply> Change(AuthApi api, string bearer, string current, string next) =>
        api.SendAsync(HttpMethod.Put, "/v1/account/password", new { currentPassword = current, newPassword = next }, bearer: bearer);
}
