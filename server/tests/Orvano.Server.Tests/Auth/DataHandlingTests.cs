using System.Net;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0004's data handling over HTTP against the real binary: after a whole account lifecycle, no event payload,
// problem details body, or log line carries personal data or a secret. AC-33, AC-34.
public class DataHandlingTests(PostgresFixture postgres)
{
    private const string Email = "ada.lovelace@x.com";
    private const string Name = "Ada Lovelace";
    private const string NewName = "Ada King";
    private const string Password = "correct horse battery";
    private const string NewPassword = "analytical engine notes";
    private const string ClientIp = "203.0.113.77";
    private const string ClientAgent = "LeakCheck/1.0";

    [Fact]
    public async Task No_event_problem_body_or_log_line_carries_personal_data_or_a_secret()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var client = new Dictionary<string, string> { ["X-Orvano-Client-IP"] = ClientIp, ["X-Orvano-Client-UA"] = ClientAgent };
        var problems = new List<string>();
        var secrets = new List<string>();

        // Every write that produces an auth event, plus the refusals that answer with a problem body.
        using var signUp = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = Email, password = Password, name = Name }, headers: client);
        Assert.Equal(HttpStatusCode.Created, signUp.Status);
        var userId = AuthApi.UserId(signUp);
        secrets.Add(AuthApi.AccessToken(signUp));
        secrets.Add(AuthApi.RefreshToken(signUp));

        problems.Add(await ProblemAsync(api.SignUpAsync(Email.ToUpperInvariant()), HttpStatusCode.Conflict));
        problems.Add(await ProblemAsync(api.SignInAsync(Email, "not the password"), HttpStatusCode.Unauthorized));
        problems.Add(await ProblemAsync(api.SignInAsync("nobody@x.com", Password), HttpStatusCode.Unauthorized));

        using var signIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/password", new { email = Email, password = Password }, headers: client);
        var access = AuthApi.AccessToken(signIn);
        secrets.Add(access);
        using var refreshed = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/refresh", new { refreshToken = AuthApi.RefreshToken(signIn) }, headers: client);
        access = refreshed.Body.GetProperty("accessToken").GetString()!;
        secrets.Add(access);
        secrets.Add(refreshed.Body.GetProperty("refreshToken").GetString()!);

        using var updated = await api.SendAsync(HttpMethod.Patch, "/v1/account", new { name = NewName, metadata = new { plan = "pro" } }, bearer: access);
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        problems.Add(await ProblemAsync(
            api.SendAsync(HttpMethod.Put, "/v1/account/password", new { currentPassword = "not the password", newPassword = NewPassword }, bearer: access),
            HttpStatusCode.Unauthorized));
        using var changed = await api.SendAsync(HttpMethod.Put, "/v1/account/password", new { currentPassword = Password, newPassword = NewPassword }, bearer: access);
        Assert.Equal(HttpStatusCode.NoContent, changed.Status);

        using var searched = await api.AsServerAsync(HttpMethod.Get, $"/v1/users?email={Uri.EscapeDataString(Email)}");
        Assert.Equal(userId, searched.Body.GetProperty("items")[0].GetProperty("id").GetString());
        using var blocked = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{userId}/block");
        problems.Add(await ProblemAsync(api.SignInAsync(Email, NewPassword), HttpStatusCode.Forbidden));
        using var unblocked = await api.AsServerAsync(HttpMethod.Post, $"/v1/users/{userId}/unblock");
        using var rotated = await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/auth/keys/rotate");
        Assert.Equal(HttpStatusCode.OK, rotated.Status);

        using var again = await api.SignInAsync(Email, NewPassword);
        problems.Add(await ProblemAsync(
            api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { password = "not the password" }, bearer: AuthApi.AccessToken(again)),
            HttpStatusCode.Unauthorized));
        using var deleted = await api.SendAsync(HttpMethod.Post, "/v1/account/delete", new { password = NewPassword }, bearer: AuthApi.AccessToken(again));
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        var events = await TestDatabase.ScalarAsync<string[]>(api.Database.Superuser,
            "SELECT array_agg(type || ' ' || payload::text ORDER BY id) FROM orvano.events WHERE type LIKE 'auth.%'");
        var logs = api.Process.Output;

        // The lifecycle wrote every auth event type, so the checks below read real payloads.
        Assert.Equal(
            ["auth.key.rotated", "auth.password.changed", "auth.session.created", "auth.session.ended", "auth.user.blocked",
             "auth.user.created", "auth.user.deleted", "auth.user.unblocked", "auth.user.updated"],
            events.Select(e => e.Split(' ')[0]).Distinct().Order());

        string[] personal = [Email, Email.ToUpperInvariant(), Uri.EscapeDataString(Email), Name, NewName, ClientIp, ClientAgent];
        string[] secret = [Password, NewPassword, "not the password", AuthApi.ServerKey, "orv_rt_", "$argon2id$", .. secrets];
        foreach (var value in personal.Concat(secret))
        {
            Assert.All(events, e => Assert.DoesNotContain(value, e, StringComparison.OrdinalIgnoreCase));
            Assert.All(problems, p => Assert.DoesNotContain(value, p, StringComparison.OrdinalIgnoreCase));
        }

        // AC-34 names what no log line may hold: a password, token, hash, key, or email.
        foreach (var value in secret.Concat([Email, Email.ToUpperInvariant(), Uri.EscapeDataString(Email)]))
        {
            Assert.DoesNotContain(value, logs, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Awaits a refused call, checks its status, and returns its problem details body as text.</summary>
    private static async Task<string> ProblemAsync(Task<Reply> call, HttpStatusCode status)
    {
        using var reply = await call;
        Assert.Equal(status, reply.Status);
        return reply.Body.GetRawText();
    }
}
