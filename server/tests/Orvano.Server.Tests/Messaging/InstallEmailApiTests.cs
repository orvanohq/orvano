using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orvano.Contract;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Messaging;

// Spec 0009, slice 2, over HTTP against the real api and worker in the Test environment (every response is also
// checked against the contract): the install's SMTP settings, an invite that arrives by email, and the email log.
public class InstallEmailApiTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string Project = "emailproject0002";
    private const string Smtp = "/v1/console/install/smtp";
    private const string Emails = "/v1/console/install/emails";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_install_admin_sets_SMTP_and_an_invite_arrives_by_email_and_shows_in_the_log()
    {
        var admin = NewEmail("admin");
        var invited = NewEmail("invited");
        // Private hosts stay refused for projects: the install's own SMTP is exempt (AC-7).
        await using var server = await StartAsync(allowPrivateHosts: false, admin);
        using var http = server.Api.Http();

        using var none = await SendAsync(http, HttpMethod.Get, Smtp, null, admin);
        Assert.Equal(HttpStatusCode.OK, none.Status);
        Assert.Equal(JsonValueKind.Null, none.Body.GetProperty("settings").ValueKind);

        // With no install SMTP an invitation is created and not emailed.
        using var org = await SendAsync(http, HttpMethod.Post, "/v1/console/orgs", new { name = "Acme" }, admin);
        var invitations = $"/v1/console/orgs/{org.Body.GetProperty("id").GetString()}/invitations";
        using var plain = await SendAsync(http, HttpMethod.Post, invitations, new { email = NewEmail("plain"), role = "viewer" }, admin);
        Assert.Equal(HttpStatusCode.Created, plain.Status);
        Assert.False(plain.Body.GetProperty("emailed").GetBoolean());
        Assert.Equal(0L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.messaging_emails"));

        var settings = Mailpit();
        using var test = await SendAsync(http, HttpMethod.Post, Smtp + "/test", settings, admin);
        Assert.True(test.Status == HttpStatusCode.OK, test.Document?.RootElement.ToString());
        Assert.Equal(admin, test.Body.GetProperty("sentTo").GetString());
        Assert.Contains("this Orvano server", (await mailpit.WaitForMessageToAsync(admin, Ct)).GetProperty("Text").GetString(), StringComparison.Ordinal);

        using var saved = await SendAsync(http, HttpMethod.Put, Smtp, settings, admin);
        Assert.True(saved.Status == HttpStatusCode.OK, saved.Document?.RootElement.ToString());
        using var read = await SendAsync(http, HttpMethod.Get, Smtp, null, admin);
        Assert.Equal(mailpit.Host, read.Body.GetProperty("settings").GetProperty("host").GetString());
        Assert.Equal(1L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.events WHERE type = 'messaging.smtp.updated' AND project_id = 'console'"));

        // A project without its own settings now sends through the install's.
        using var project = await SendAsync(http, HttpMethod.Get, "/v1/console/project/email/smtp", null, admin, Project);
        Assert.Equal("install", project.Body.GetProperty("source").GetString());
        Assert.Equal("orvano@install.test", project.Body.GetProperty("installSender").GetProperty("email").GetString());

        // AC-23: the invite is queued with the invitation, and the worker sends the very link the dialog shows.
        using var created = await SendAsync(http, HttpMethod.Post, invitations, new { email = invited, role = "developer" }, admin);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.True(created.Body.GetProperty("emailed").GetBoolean());
        var url = created.Body.GetProperty("url").GetString()!;
        var message = await mailpit.WaitForMessageToAsync(invited, Ct);
        Assert.EndsWith("invited you to join Acme on Orvano", message.GetProperty("Subject").GetString(), StringComparison.Ordinal);
        Assert.Contains(url, message.GetProperty("Text").GetString(), StringComparison.Ordinal);
        Assert.Contains("as a developer", message.GetProperty("Text").GetString(), StringComparison.Ordinal);

        // AC-21: the console's own log, newest first, with the masked recipient only.
        var entry = await Eventually.ReturnsAsync(async () =>
        {
            using var log = await SendAsync(http, HttpMethod.Get, Emails, null, admin);
            Assert.Equal(HttpStatusCode.OK, log.Status);
            return log.Body.GetProperty("items")[0].Clone();
        }, e => e.GetProperty("status").GetString() == "sent", TimeSpan.FromSeconds(20), "the invite email to be sent");
        Assert.Equal("console_invitation", entry.GetProperty("template").GetString());
        Assert.Equal($"{invited[0]}***@console.test", entry.GetProperty("recipient").GetString());
        Assert.Equal("install", entry.GetProperty("smtpSource").GetString());
        Assert.Equal(1, entry.GetProperty("attempts").GetInt32());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("errorCode").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, entry.GetProperty("completedAt").ValueKind);

        // AC-26: nothing stored or logged names the recipient or the link.
        var token = url[(url.IndexOf('#', StringComparison.Ordinal) + 1)..];
        var stored = await ScalarAsync<string>(server,
            "SELECT (SELECT string_agg(payload::text, '') FROM orvano.jobs) || (SELECT string_agg(payload::text, '') FROM orvano.events)");
        foreach (var secret in new[] { invited, token })
        {
            Assert.DoesNotContain(secret, stored, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, server.Api.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, server.Worker.Output, StringComparison.Ordinal);
        }

        // The project's own log is empty: invites belong to the console.
        using var projectLog = await SendAsync(http, HttpMethod.Get, "/v1/console/project/emails", null, admin, Project);
        Assert.Equal(HttpStatusCode.OK, projectLog.Status);
        Assert.Equal(0, projectLog.Body.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, projectLog.Body.GetProperty("nextCursor").ValueKind);

        // Stop using it, twice; then invitations are no longer emailed.
        using var deleted = await SendAsync(http, HttpMethod.Delete, Smtp, null, admin);
        using var again = await SendAsync(http, HttpMethod.Delete, Smtp, null, admin);
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (deleted.Status, again.Status));
        using var after = await SendAsync(http, HttpMethod.Post, invitations, new { email = NewEmail("later"), role = "viewer" }, admin);
        Assert.False(after.Body.GetProperty("emailed").GetBoolean());
    }

    [Fact]
    public async Task The_log_pages_newest_first_and_refuses_a_bad_cursor_or_limit()
    {
        var admin = NewEmail("admin");
        await using var server = await StartAsync(allowPrivateHosts: true, admin);
        using var http = server.Api.Http();
        await TestDatabase.ExecuteAsync(server.Database.Superuser,
            """
            INSERT INTO orvano.messaging_emails (id, project_id, template, recipient_masked, status, error_code, completed_at, created_at)
            SELECT gen_random_uuid(), @project, 'recovery', n || '***@x.test', 'failed', 'smtp_rejected', now(), now() - make_interval(mins => n)
            FROM generate_series(1, 5) AS n
            """, ("project", Project));
        const string log = "/v1/console/project/emails";

        using var first = await SendAsync(http, HttpMethod.Get, log + "?limit=2", null, admin, Project);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(["1***@x.test", "2***@x.test"], [.. first.Body.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("recipient").GetString())]);
        Assert.Equal("smtp_rejected", first.Body.GetProperty("items")[0].GetProperty("errorCode").GetString());
        var cursor = first.Body.GetProperty("nextCursor").GetString()!;

        using var second = await SendAsync(http, HttpMethod.Get, $"{log}?limit=2&cursor={Uri.EscapeDataString(cursor)}", null, admin, Project);
        Assert.Equal(["3***@x.test", "4***@x.test"], [.. second.Body.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("recipient").GetString())]);
        using var last = await SendAsync(http, HttpMethod.Get, $"{log}?limit=2&cursor={Uri.EscapeDataString(second.Body.GetProperty("nextCursor").GetString()!)}", null, admin, Project);
        Assert.Equal(1, last.Body.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, last.Body.GetProperty("nextCursor").ValueKind);

        await AssertProblemAsync(http, HttpMethod.Get, log + "?cursor=nope", null, admin, HttpStatusCode.BadRequest, ErrorCode.InvalidCursor, Project);
        await AssertProblemAsync(http, HttpMethod.Get, log + "?limit=0", null, admin, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, Project);
        await AssertProblemAsync(http, HttpMethod.Get, log, null, admin, HttpStatusCode.NotFound, ErrorCode.ProjectNotFound, "nosuchproject");
    }

    // AC-7, AC-21, AC-25: install admins only, then the rate limit, then the body.
    [Fact]
    public async Task Anyone_but_an_install_admin_gets_403_and_the_31st_test_is_rate_limited()
    {
        var admin = NewEmail("admin");
        var other = NewEmail("other");
        await using var server = await StartAsync(allowPrivateHosts: true, admin, other);
        using var http = server.Api.Http();
        var invalid = Mailpit(host: "");

        await AssertProblemAsync(http, HttpMethod.Get, Smtp, null, other, HttpStatusCode.Forbidden, ErrorCode.Forbidden);
        await AssertProblemAsync(http, HttpMethod.Put, Smtp, invalid, other, HttpStatusCode.Forbidden, ErrorCode.Forbidden);
        await AssertProblemAsync(http, HttpMethod.Delete, Smtp, null, other, HttpStatusCode.Forbidden, ErrorCode.Forbidden);
        await AssertProblemAsync(http, HttpMethod.Post, Smtp + "/test", invalid, other, HttpStatusCode.Forbidden, ErrorCode.Forbidden);
        await AssertProblemAsync(http, HttpMethod.Get, Emails, null, other, HttpStatusCode.Forbidden, ErrorCode.Forbidden);

        using var bad = await SendAsync(http, HttpMethod.Put, Smtp, invalid, admin);
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.StartsWith("host: ", bad.Body.GetProperty("detail").GetString(), StringComparison.Ordinal);

        // AC-1's rule holds for the install too: a stored password never leaves for another host.
        var signedIn = Mailpit(host: "smtp.example.com", security: "starttls", username: "apikey", password: "install smtp secret");
        using var saved = await SendAsync(http, HttpMethod.Put, Smtp, signedIn, admin);
        Assert.True(saved.Status == HttpStatusCode.OK, saved.Document?.RootElement.ToString());
        Assert.True(saved.Body.GetProperty("hasPassword").GetBoolean());
        signedIn["password"] = null;
        signedIn["host"] = "evil.example.net";
        using var moved = await SendAsync(http, HttpMethod.Put, Smtp, signedIn, admin);
        Assert.Equal("password: Enter the password again when you change the host, port, or username.", moved.Body.GetProperty("detail").GetString());

        for (var i = 0; i < 30; i++)
            await AssertProblemAsync(http, HttpMethod.Post, Smtp + "/test", invalid, admin, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);
        await AssertProblemAsync(http, HttpMethod.Post, Smtp + "/test", invalid, admin, HttpStatusCode.TooManyRequests, ErrorCode.RateLimited);
    }

    private Dictionary<string, object?> Mailpit(string? host = null, string security = "none", string? username = null, string? password = null) => new()
    {
        ["host"] = host ?? mailpit.Host,
        ["port"] = mailpit.SmtpPort,
        ["security"] = security,
        ["username"] = username,
        ["password"] = password,
        ["fromEmail"] = "orvano@install.test",
        ["fromName"] = "Orvano",
        ["replyTo"] = null,
    };

    // Unique per test, so one Mailpit serves every test of the class.
    private static string NewEmail(string who) => $"{who}-{Guid.NewGuid():N}@console.test";

    private sealed record Server(OrvanoProcess Api, OrvanoProcess Worker, TestDatabase Database) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Api.DisposeAsync();
            await Worker.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed record Reply(HttpStatusCode Status, JsonDocument? Document) : IDisposable
    {
        public JsonElement Body => Document!.RootElement;

        public void Dispose() => Document?.Dispose();
    }

    /// <summary>The api and a worker over a fresh database; the first session is the install admin and owns a project.</summary>
    private async Task<Server> StartAsync(bool allowPrivateHosts, params string[] sessions)
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var fixtures = Path.Combine(Path.GetTempPath(), $"orvano-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(fixtures, $"""
            {ConsoleSignIn.Fixtures(sessions)}projects:
              - id: {Project}
                name: Email project

            """, Ct);

        var allow = allowPrivateHosts ? "true" : "false";
        var api = OrvanoProcess.Start(["api"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["ORVANO_TEST_FIXTURES"] = fixtures,
            ["ORVANO_SMTP_ALLOW_PRIVATE_HOSTS"] = allow,
        }, listen: true);
        var worker = OrvanoProcess.Start(["worker"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ORVANO_DB_ADMIN_URL"] = database.AdminUrl,
            ["ORVANO_SMTP_ALLOW_PRIVATE_HOSTS"] = allow,
        }, listen: true);
        try
        {
            await api.WaitUntilListeningAsync();
            await worker.WaitUntilListeningAsync();
            await Eventually.TrueAsync(
                async () => await TestDatabase.ScalarAsync<string>(database.Superuser, "SELECT status FROM orvano.platform_projects WHERE id = @p", ("p", Project)) == "active",
                TimeSpan.FromSeconds(30), "the fixture project to be provisioned");
            return new Server(api, worker, database);
        }
        catch
        {
            await api.DisposeAsync();
            await worker.DisposeAsync();
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task<Reply> SendAsync(HttpClient http, HttpMethod method, string url, object? body, string session, string? project = null)
    {
        using var request = new HttpRequestMessage(method, url);
        await ConsoleSignIn.AuthorizeAsync(http, request, session, Ct);
        if (project is not null) request.Headers.Add("X-Orvano-Project", project);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return new Reply(response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text) : null);
    }

    private static async Task AssertProblemAsync(
        HttpClient http, HttpMethod method, string url, object? body, string session, HttpStatusCode status, string code, string? project = null)
    {
        using var reply = await SendAsync(http, method, url, body, session, project);
        Assert.True(status == reply.Status, $"{method} {url}: expected {status}, got {reply.Status}: {reply.Document?.RootElement}");
        Assert.Equal(code, reply.Body.GetProperty("code").GetString());
    }

    private static Task<T> ScalarAsync<T>(Server server, string sql, params (string Name, object Value)[] parameters) =>
        TestDatabase.ScalarAsync<T>(server.Database.Superuser, sql, parameters);
}
