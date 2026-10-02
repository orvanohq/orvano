using System.Net;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 build task 7 over HTTP against the real binary: the console's twins of the users email operations, for
// every role. AC-21, AC-23.
public class ConsoleUsersEmailTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth/callback";

    [Fact]
    public async Task Owners_and_developers_manage_verification_and_send_links_while_viewers_get_403()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        var developer = AuthApi.OtherConsoleUsers[0];
        var viewer = AuthApi.OtherConsoleUsers[1];
        await api.GrantAsync(developer, "developer");
        await api.GrantAsync(viewer, "viewer");
        using var created = await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/users", new { email = "ada@x.com", password = "correct horse battery" });
        var id = created.Body.GetProperty("id").GetString();

        using var unverified = await api.AsConsoleAsync(HttpMethod.Get, "/v1/console/project/users?emailVerified=false", account: viewer);
        Assert.Single(unverified.Body.GetProperty("items").EnumerateArray());

        using var mark = await api.AsConsoleAsync(HttpMethod.Put, $"/v1/console/project/users/{id}/email-verification", new { verified = true });
        Assert.True(mark.Body.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.user.updated' AND payload->'actor'->>'type' = 'user'"));

        using var reset = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/project/users/{id}/recovery", new { redirectUrl = Redirect }, account: developer);
        Assert.Equal(HttpStatusCode.Accepted, reset.Status);
        Assert.Equal("recovery", (await api.LatestEmailAsync("ada@x.com"))!.Type);

        using var change = await api.AsConsoleAsync(HttpMethod.Put, $"/v1/console/project/users/{id}/email", new { email = "ada.king@x.com", emailVerified = true }, account: developer);
        Assert.Equal("ada.king@x.com", change.Body.GetProperty("email").GetString());
        Assert.True(change.Body.GetProperty("emailVerified").GetBoolean());

        using var unmark = await api.AsConsoleAsync(HttpMethod.Put, $"/v1/console/project/users/{id}/email-verification", new { verified = false });
        using var verify = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/project/users/{id}/verification", new { redirectUrl = Redirect });
        Assert.Equal(HttpStatusCode.Accepted, verify.Status);
        Assert.Equal("verification", (await api.LatestEmailAsync("ada.king@x.com"))!.Type);

        foreach (var (method, path, body) in new (HttpMethod, string, object)[]
        {
            (HttpMethod.Put, "email-verification", new { verified = true }),
            (HttpMethod.Post, "verification", new { redirectUrl = Redirect }),
            (HttpMethod.Post, "recovery", new { redirectUrl = Redirect }),
            (HttpMethod.Put, "email", new { email = "eve@x.com" }),
        })
        {
            using var refused = await api.AsConsoleAsync(method, $"/v1/console/project/users/{id}/{path}", body, account: viewer);
            Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), (refused.Status, refused.Code));
        }
    }
}
