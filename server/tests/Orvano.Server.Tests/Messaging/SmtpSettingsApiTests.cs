using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Orvano.Contract;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Messaging;

// Spec 0009, slice 1, over HTTP against the real binary in the Test environment (every response is also checked
// against the contract): a project's SMTP settings, and a test email that arrives in Mailpit.
public class SmtpSettingsApiTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string Project = "emailproject0001";
    private const string Path = "/v1/console/project/email/smtp";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_developer_saves_project_SMTP_and_a_test_email_arrives()
    {
        var owner = NewEmail("owner");
        await using var server = await StartAsync(allowPrivateHosts: true, owner);
        using var http = server.Api.Http();

        using var before = await SendAsync(http, HttpMethod.Get, Path, null, owner);
        Assert.Equal(HttpStatusCode.OK, before.Status);
        Assert.Equal("none", before.Body.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, before.Body.GetProperty("settings").ValueKind);
        Assert.Equal(JsonValueKind.Null, before.Body.GetProperty("installSender").ValueKind);

        var settings = Mailpit(fromName: "Shop", replyTo: "support@shop.test");
        using var saved = await SendAsync(http, HttpMethod.Put, Path, settings, owner);
        Assert.True(saved.Status == HttpStatusCode.OK, saved.Document?.RootElement.ToString());
        Assert.False(saved.Body.GetProperty("hasPassword").GetBoolean());

        using var after = await SendAsync(http, HttpMethod.Get, Path, null, owner);
        Assert.Equal("project", after.Body.GetProperty("source").GetString());
        Assert.Equal(mailpit.Host, after.Body.GetProperty("settings").GetProperty("host").GetString());
        Assert.Equal("hello@shop.test", after.Body.GetProperty("settings").GetProperty("fromEmail").GetString());

        using var test = await SendAsync(http, HttpMethod.Post, Path + "/test", settings, owner);
        Assert.True(test.Status == HttpStatusCode.OK, test.Document?.RootElement.ToString());
        Assert.Equal(owner, test.Body.GetProperty("sentTo").GetString());

        // AC-6: multipart, from the configured sender, to the caller's own console email, naming the project.
        var message = await mailpit.WaitForMessageToAsync(owner, Ct);
        Assert.Equal("Test email from Orvano", message.GetProperty("Subject").GetString());
        Assert.Equal("hello@shop.test", message.GetProperty("From").GetProperty("Address").GetString());
        Assert.Equal("Shop", message.GetProperty("From").GetProperty("Name").GetString());
        Assert.Equal("support@shop.test", message.GetProperty("ReplyTo")[0].GetProperty("Address").GetString());
        Assert.Contains("the project Email project", message.GetProperty("Text").GetString(), StringComparison.Ordinal);
        Assert.Contains("the project Email project", message.GetProperty("HTML").GetString(), StringComparison.Ordinal);
        var headers = await mailpit.HeadersAsync(message.GetProperty("ID").GetString()!, Ct);
        Assert.Equal("auto-generated", headers.GetProperty("Auto-Submitted")[0].GetString());
        Assert.EndsWith("@localhost>", headers.GetProperty("Message-Id")[0].GetString(), StringComparison.Ordinal);

        // A test stores nothing, and the save wrote one event that names no host.
        Assert.Equal(0L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.messaging_emails"));
        Assert.Equal(1L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.events WHERE type = 'messaging.smtp.updated' AND project_id = @p", ("p", Project)));
        var payload = await ScalarAsync<string>(server, "SELECT payload::text FROM orvano.events WHERE type = 'messaging.smtp.updated'");
        Assert.DoesNotContain(mailpit.Host, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("shop.test", payload, StringComparison.Ordinal);
        Assert.Contains("\"changed\"", payload, StringComparison.Ordinal);

        // AC-5: stop using them, twice; the second has nothing to delete and still answers 204.
        using var deleted = await SendAsync(http, HttpMethod.Delete, Path, null, owner);
        using var again = await SendAsync(http, HttpMethod.Delete, Path, null, owner);
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal(HttpStatusCode.NoContent, again.Status);
        using var gone = await SendAsync(http, HttpMethod.Get, Path, null, owner);
        Assert.Equal("none", gone.Body.GetProperty("source").GetString());
        Assert.Equal(1L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.events WHERE type = 'messaging.smtp.deleted'"));
    }

    // AC-6, AC-26: a test never retries, so a 4xx reply is a rejection too, shown with its code; the logs keep neither
    // the sender nor the subject.
    [Fact]
    public async Task A_refused_test_is_not_retried_and_answers_the_reply_code()
    {
        var owner = NewEmail("owner");
        await using var server = await StartAsync(allowPrivateHosts: true, owner);
        using var http = server.Api.Http();
        try
        {
            foreach (var (sender, recipient, reply) in new[] { ((int?)451, (int?)null, "451"), (null, 550, "550") })
            {
                await mailpit.SetChaosAsync(sender, recipient, Ct);
                using var test = await SendAsync(http, HttpMethod.Post, Path + "/test", Mailpit(), owner);
                Assert.Equal(HttpStatusCode.BadGateway, test.Status);
                Assert.Equal(ErrorCode.SmtpRejected, test.Body.GetProperty("code").GetString());
                Assert.Contains(reply, test.Body.GetProperty("detail").GetString(), StringComparison.Ordinal);
            }
        }
        finally
        {
            await mailpit.SetChaosAsync(senderCode: null, recipientCode: null, Ct);
        }

        Assert.Equal(0, await mailpit.CountToAsync(owner, Ct));
        Assert.Equal(0L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.messaging_emails"));
        Assert.Contains("smtp_rejected", server.Api.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("hello@shop.test", server.Api.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Test email from Orvano", server.Api.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_password_is_sealed_never_returned_and_reused_only_for_the_same_server()
    {
        var owner = NewEmail("owner");
        await using var server = await StartAsync(allowPrivateHosts: true, owner);
        using var http = server.Api.Http();
        const string password = "correct horse smtp";
        var settings = new Dictionary<string, object?>
        {
            ["host"] = "smtp.example.com",
            ["port"] = 587,
            ["security"] = "starttls",
            ["username"] = "apikey",
            ["password"] = password,
            ["fromEmail"] = "hello@shop.test",
            ["fromName"] = null,
            ["replyTo"] = null,
        };

        using var saved = await SendAsync(http, HttpMethod.Put, Path, settings, owner);
        Assert.True(saved.Status == HttpStatusCode.OK, saved.Document?.RootElement.ToString());
        Assert.True(saved.Body.GetProperty("hasPassword").GetBoolean());
        Assert.DoesNotContain(password, saved.Body.GetRawText(), StringComparison.Ordinal);
        using var read = await SendAsync(http, HttpMethod.Get, Path, null, owner);
        Assert.DoesNotContain(password, read.Body.GetRawText(), StringComparison.Ordinal);

        var blob = await ScalarAsync<byte[]>(server, "SELECT password_ciphertext FROM orvano.messaging_smtp_settings WHERE project_id = @p", ("p", Project));
        Assert.DoesNotContain(password, Encoding.Latin1.GetString(blob), StringComparison.Ordinal);
        Assert.DoesNotContain(password, await ScalarAsync<string>(server, "SELECT string_agg(payload::text, '') FROM orvano.events"), StringComparison.Ordinal);
        Assert.DoesNotContain(password, server.Api.Output, StringComparison.Ordinal);

        // AC-1: null keeps it for the same host, port, and username...
        settings["password"] = null;
        settings["fromName"] = "Shop";
        using var kept = await SendAsync(http, HttpMethod.Put, Path, settings, owner);
        Assert.Equal(HttpStatusCode.OK, kept.Status);
        Assert.True(kept.Body.GetProperty("hasPassword").GetBoolean());
        Assert.Equal(blob, await ScalarAsync<byte[]>(server, "SELECT password_ciphertext FROM orvano.messaging_smtp_settings WHERE project_id = @p", ("p", Project)));

        // ...and never travels to another server, on update or on test.
        settings["host"] = "evil.example.net";
        foreach (var (method, url) in new[] { (HttpMethod.Put, Path), (HttpMethod.Post, Path + "/test") })
        {
            using var refused = await SendAsync(http, method, url, settings, owner);
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal(ErrorCode.InvalidRequest, refused.Body.GetProperty("code").GetString());
            Assert.Equal("password: Enter the password again when you change the host, port, or username.", refused.Body.GetProperty("detail").GetString());
        }

        // A username without a password clears it.
        settings["host"] = "smtp.example.com";
        settings["username"] = null;
        using var cleared = await SendAsync(http, HttpMethod.Put, Path, settings, owner);
        Assert.Equal(HttpStatusCode.OK, cleared.Status);
        Assert.False(cleared.Body.GetProperty("hasPassword").GetBoolean());
    }

    [Fact]
    public async Task A_private_host_is_refused_for_a_project_on_update_and_on_test()
    {
        var owner = NewEmail("owner");
        await using var server = await StartAsync(allowPrivateHosts: false, owner);
        using var http = server.Api.Http();

        foreach (var host in new[] { "127.0.0.1", "10.0.0.5", "169.254.169.254", "::1", "::ffff:10.0.0.5", "localhost" })
        {
            using var update = await SendAsync(http, HttpMethod.Put, Path, Mailpit(host: host), owner);
            Assert.True(update.Status == HttpStatusCode.BadRequest, $"{host}: {update.Status}");
            Assert.Equal(ErrorCode.SmtpHostNotAllowed, update.Body.GetProperty("code").GetString());

            using var test = await SendAsync(http, HttpMethod.Post, Path + "/test", Mailpit(host: host), owner);
            Assert.True(test.Status == HttpStatusCode.BadRequest, $"{host}: {test.Status}");
            Assert.Equal(ErrorCode.SmtpHostNotAllowed, test.Body.GetProperty("code").GetString());
        }

        Assert.Equal(0L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.messaging_smtp_settings"));
        Assert.Equal(0, await mailpit.CountToAsync(owner, Ct));

        // A name that doesn't resolve yet can be saved; sending through it then fails as unreachable.
        using var unresolved = await SendAsync(http, HttpMethod.Put, Path, Mailpit(host: "smtp.does-not-exist.invalid"), owner);
        Assert.Equal(HttpStatusCode.OK, unresolved.Status);
        using var unreachable = await SendAsync(http, HttpMethod.Post, Path + "/test", Mailpit(host: "smtp.does-not-exist.invalid"), owner);
        Assert.Equal(HttpStatusCode.BadGateway, unreachable.Status);
        Assert.Equal(ErrorCode.SmtpUnreachable, unreachable.Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Checks_answer_in_order_project_role_body_then_state()
    {
        var owner = NewEmail("owner");
        var viewer = NewEmail("viewer");
        var stranger = NewEmail("stranger");
        await using var server = await StartAsync(allowPrivateHosts: true, owner, viewer, stranger);
        using var http = server.Api.Http();
        await TestDatabase.ExecuteAsync(server.Database.Superuser,
            """
            INSERT INTO orvano.platform_memberships (org_id, user_id, role)
            SELECT p.org_id, u.id, 'viewer' FROM orvano.platform_projects p, orvano.auth_users u
            WHERE p.id = @project AND u.project_id = 'console' AND u.email = @email
            """, ("project", Project), ("email", viewer));
        var invalid = Mailpit(host: "");

        // Not a member: 404 on everything, so the project's existence never leaks.
        await AssertProblemAsync(http, HttpMethod.Get, Path, null, stranger, HttpStatusCode.NotFound, ErrorCode.ProjectNotFound);
        await AssertProblemAsync(http, HttpMethod.Put, Path, invalid, stranger, HttpStatusCode.NotFound, ErrorCode.ProjectNotFound);
        await AssertProblemAsync(http, HttpMethod.Get, Path, null, owner, HttpStatusCode.NotFound, ErrorCode.ProjectNotFound, project: "console");
        await AssertProblemAsync(http, HttpMethod.Get, Path, null, owner, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, project: "");

        // A viewer reads, and gets 403 on every change before the body is looked at.
        using var read = await SendAsync(http, HttpMethod.Get, Path, null, viewer);
        Assert.Equal(HttpStatusCode.OK, read.Status);
        await AssertProblemAsync(http, HttpMethod.Put, Path, invalid, viewer, HttpStatusCode.Forbidden, ErrorCode.Forbidden);
        await AssertProblemAsync(http, HttpMethod.Delete, Path, null, viewer, HttpStatusCode.Forbidden, ErrorCode.Forbidden);
        await AssertProblemAsync(http, HttpMethod.Post, Path + "/test", invalid, viewer, HttpStatusCode.Forbidden, ErrorCode.Forbidden);

        // The body, with the field named first.
        using var bad = await SendAsync(http, HttpMethod.Put, Path, invalid, owner);
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.StartsWith("host: ", bad.Body.GetProperty("detail").GetString(), StringComparison.Ordinal);
        using var plain = await SendAsync(http, HttpMethod.Put, Path, Mailpit(username: "someone", password: "secret"), owner);
        Assert.Equal("username: A username needs STARTTLS or TLS.", plain.Body.GetProperty("detail").GetString());

        // Nothing listens on a free port: the failure is a safe sentence, never an exception message.
        var closed = Mailpit(host: "127.0.0.1", port: OrvanoProcess.FreePort());
        using var unreachable = await SendAsync(http, HttpMethod.Post, Path + "/test", closed, owner);
        Assert.Equal(HttpStatusCode.BadGateway, unreachable.Status);
        Assert.Equal(ErrorCode.SmtpUnreachable, unreachable.Body.GetProperty("code").GetString());
        Assert.Equal("Couldn't connect to the SMTP server.", unreachable.Body.GetProperty("detail").GetString());

        // A project that is not active: reads still work, writes and tests answer 409 after the body check.
        await TestDatabase.ExecuteAsync(server.Database.Superuser,
            "UPDATE orvano.platform_projects SET status = 'provisioning' WHERE id = @p", ("p", Project));
        using var stillReads = await SendAsync(http, HttpMethod.Get, Path, null, owner);
        Assert.Equal(HttpStatusCode.OK, stillReads.Status);
        await AssertProblemAsync(http, HttpMethod.Put, Path, invalid, owner, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);
        await AssertProblemAsync(http, HttpMethod.Put, Path, Mailpit(), owner, HttpStatusCode.Conflict, ErrorCode.ProjectNotReady);
        await AssertProblemAsync(http, HttpMethod.Delete, Path, null, owner, HttpStatusCode.Conflict, ErrorCode.ProjectNotReady);
        await AssertProblemAsync(http, HttpMethod.Post, Path + "/test", Mailpit(), owner, HttpStatusCode.Conflict, ErrorCode.ProjectNotReady);
    }

    [Fact]
    public async Task The_31st_test_in_15_minutes_is_rate_limited()
    {
        var owner = NewEmail("owner");
        await using var server = await StartAsync(allowPrivateHosts: true, owner);
        using var http = server.Api.Http();
        var invalid = Mailpit(host: "");

        // Every attempt that passed the role check counts, also one the body check refuses (AC-25).
        for (var i = 0; i < 30; i++)
            await AssertProblemAsync(http, HttpMethod.Post, Path + "/test", invalid, owner, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);

        using var request = new HttpRequestMessage(HttpMethod.Post, Path + "/test") { Content = JsonContent.Create(invalid) };
        await ConsoleSignIn.AuthorizeAsync(http, request, owner, Ct);
        request.Headers.Add("X-Orvano-Project", Project);
        using var response = await http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task A_bad_email_setting_stops_the_role_from_starting()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();

        await using var realtime = OrvanoProcess.Start(["realtime"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ORVANO_SMTP_ALLOW_PRIVATE_HOSTS"] = "yes",
        }, listen: true);
        await realtime.WaitForExitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, realtime.ExitCode);
        Assert.Contains("ORVANO_SMTP_ALLOW_PRIVATE_HOSTS must be true or false", realtime.Output, StringComparison.Ordinal);
    }

    private Dictionary<string, object?> Mailpit(
        string? host = null, int? port = null, string? username = null, string? password = null, string? fromName = null, string? replyTo = null) => new()
        {
            ["host"] = host ?? mailpit.Host,
            ["port"] = port ?? mailpit.SmtpPort,
            ["security"] = "none",
            ["username"] = username,
            ["password"] = password,
            ["fromEmail"] = "hello@shop.test",
            ["fromName"] = fromName,
            ["replyTo"] = replyTo,
        };

    // Unique per test, so one Mailpit serves every test of the class.
    private static string NewEmail(string who) => $"{who}-{Guid.NewGuid():N}@console.test";

    private sealed record Server(OrvanoProcess Api, TestDatabase Database) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Api.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed record Reply(HttpStatusCode Status, JsonDocument? Document) : IDisposable
    {
        public JsonElement Body => Document!.RootElement;

        public void Dispose() => Document?.Dispose();
    }

    /// <summary>The api over a fresh database, with the first session owning an active project.</summary>
    private async Task<Server> StartAsync(bool allowPrivateHosts, params string[] sessions)
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var fixtures = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"orvano-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(fixtures, $"""
            {ConsoleSignIn.Fixtures(sessions)}projects:
              - id: {Project}
                name: Email project

            """, Ct);

        var api = OrvanoProcess.Start(["api"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["ORVANO_TEST_FIXTURES"] = fixtures,
            ["ORVANO_SMTP_ALLOW_PRIVATE_HOSTS"] = allowPrivateHosts ? "true" : "false",
        }, listen: true);
        try
        {
            await api.WaitUntilListeningAsync();
            // No worker runs here, so the fixture project is made active by hand.
            await TestDatabase.ExecuteAsync(database.Superuser, "UPDATE orvano.platform_projects SET status = 'active' WHERE id = @p", ("p", Project));
            return new Server(api, database);
        }
        catch
        {
            await api.DisposeAsync();
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task<Reply> SendAsync(HttpClient http, HttpMethod method, string url, object? body, string session, string? project = Project)
    {
        using var request = new HttpRequestMessage(method, url);
        await ConsoleSignIn.AuthorizeAsync(http, request, session, Ct);
        if (!string.IsNullOrEmpty(project)) request.Headers.Add("X-Orvano-Project", project);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return new Reply(response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text) : null);
    }

    private static async Task AssertProblemAsync(
        HttpClient http, HttpMethod method, string url, object? body, string session, HttpStatusCode status, string code, string? project = Project)
    {
        using var reply = await SendAsync(http, method, url, body, session, project);
        Assert.True(status == reply.Status, $"{method} {url}: expected {status}, got {reply.Status}: {reply.Document?.RootElement}");
        Assert.Equal(code, reply.Body.GetProperty("code").GetString());
    }

    private static Task<T> ScalarAsync<T>(Server server, string sql, params (string Name, object Value)[] parameters) =>
        TestDatabase.ScalarAsync<T>(server.Database.Superuser, sql, parameters);
}
