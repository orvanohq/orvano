using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orvano.Contract;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Messaging;

// Spec 0009, slice 3, over HTTP against the real binary in the Test environment (every response is also checked
// against the contract): a project's email templates, their preview, and a test email that arrives in Mailpit.
public class EmailTemplateApiTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string Project = "emailproject0003";
    private const string Path = "/v1/console/project/email/templates";
    private const string Recovery = Path + "/recovery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // AC-8, AC-9, AC-11, AC-12: edit, preview, save, test, reset.
    [Fact]
    public async Task A_developer_edits_previews_saves_tests_and_resets_a_template()
    {
        var owner = NewEmail("owner");
        await using var server = await StartAsync(owner);
        using var http = server.Api.Http();

        using var catalog = await SendAsync(http, HttpMethod.Get, Path, null, owner);
        Assert.Equal(HttpStatusCode.OK, catalog.Status);
        var templates = catalog.Body.GetProperty("templates").EnumerateArray().ToList();
        Assert.Equal(["verification", "recovery", "magic_link", "email_code", "security_alert"], templates.Select(t => t.GetProperty("kind").GetString()));
        Assert.Equal(["Email verification", "Password reset", "Magic link", "Email code", "Security alert"], templates.Select(t => t.GetProperty("name").GetString()));
        Assert.All(templates, t => Assert.False(t.GetProperty("isCustom").GetBoolean()));
        Assert.All(templates, t => Assert.Equal(JsonValueKind.Null, t.GetProperty("updatedAt").ValueKind));

        using var original = await SendAsync(http, HttpMethod.Get, Recovery, null, owner);
        Assert.Equal(HttpStatusCode.OK, original.Status);
        Assert.Equal("Reset your password for {{ project.name }}", original.Body.GetProperty("subject").GetString());
        Assert.Equal("en", original.Body.GetProperty("locale").GetString());
        Assert.False(original.Body.GetProperty("isCustom").GetBoolean());
        Assert.Contains("{{ action_url }}", original.Body.GetProperty("html").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(JsonValueKind.Null, original.Body.GetProperty("text").ValueKind);
        // The samples: the real project name and the caller's own email, the rest from the catalog.
        var variables = original.Body.GetProperty("variables").EnumerateArray().ToDictionary(v => v.GetProperty("name").GetString()!, v => v.GetProperty("sample").GetString());
        Assert.Equal(["project.name", "user.email", "user.name", "action_url", "expires_in_minutes"], variables.Keys);
        Assert.Equal(("Tom & <Jerry>", owner, "", "https://example.com/auth/confirm?token=sample", "60"),
            (variables["project.name"], variables["user.email"], variables["user.name"], variables["action_url"], variables["expires_in_minutes"]));
        using var code = await SendAsync(http, HttpMethod.Get, Path + "/email_code", null, owner);
        Assert.Contains(code.Body.GetProperty("variables").EnumerateArray(), v => v.GetProperty("name").GetString() == "code" && v.GetProperty("sample").GetString() == "428613");

        var edited = Template("  Reset it, {{ project.name }}  ", "<h1>{{ project.name }}</h1><p><a href=\"{{ action_url }}\">Reset</a> {{ user.email }}</p>", "  ");
        using var preview = await SendAsync(http, HttpMethod.Post, Recovery + "/preview", edited, owner);
        Assert.True(preview.Status == HttpStatusCode.OK, preview.Document?.RootElement.ToString());
        Assert.Equal("Reset it, Tom & <Jerry>", preview.Body.GetProperty("subject").GetString());
        Assert.Equal(
            $"<h1>Tom &amp; &lt;Jerry&gt;</h1><p><a href=\"https://example.com/auth/confirm?token=sample\">Reset</a> {owner}</p>",
            preview.Body.GetProperty("html").GetString());
        Assert.Contains("Tom & <Jerry>", preview.Body.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("Reset (https://example.com/auth/confirm?token=sample)", preview.Body.GetProperty("text").GetString(), StringComparison.Ordinal);
        // A preview stores nothing.
        Assert.Equal(0L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.messaging_email_templates"));

        using var saved = await SendAsync(http, HttpMethod.Put, Recovery, edited, owner);
        Assert.True(saved.Status == HttpStatusCode.OK, saved.Document?.RootElement.ToString());
        Assert.True(saved.Body.GetProperty("isCustom").GetBoolean());
        Assert.Equal("Reset it, {{ project.name }}", saved.Body.GetProperty("subject").GetString());
        Assert.Equal(JsonValueKind.Null, saved.Body.GetProperty("text").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, saved.Body.GetProperty("updatedAt").ValueKind);
        using var read = await SendAsync(http, HttpMethod.Get, Recovery, null, owner);
        Assert.Equal(saved.Body.GetProperty("html").GetString(), read.Body.GetProperty("html").GetString());
        Assert.Equal(saved.Body.GetProperty("updatedAt").GetString(), read.Body.GetProperty("updatedAt").GetString());
        using var listed = await SendAsync(http, HttpMethod.Get, Path, null, owner);
        Assert.Equal([false, true, false, false, false], listed.Body.GetProperty("templates").EnumerateArray().Select(t => t.GetProperty("isCustom").GetBoolean()));

        // Last write wins: a second save replaces the first and names only what changed.
        using var again = await SendAsync(http, HttpMethod.Put, Recovery, Template("Reset it, {{ project.name }}", "<p>{{ action_url }}</p>", "Go: {{ action_url }}"), owner);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(1L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.messaging_email_templates"));
        var payloads = await ScalarAsync<string[]>(server, "SELECT array_agg(payload::text ORDER BY id) FROM orvano.events WHERE type = 'messaging.template.updated' AND project_id = @p AND subject = 'recovery'", ("p", Project));
        Assert.Equal(2, payloads.Length);
        Assert.Contains("\"kind\": \"recovery\"", payloads[0], StringComparison.Ordinal);
        Assert.Contains("\"changed\": [\"subject\", \"html\", \"text\"]", payloads[0], StringComparison.Ordinal);
        Assert.Contains("\"changed\": [\"html\", \"text\"]", payloads[1], StringComparison.Ordinal);
        Assert.DoesNotContain("action_url", payloads[1], StringComparison.Ordinal);
        Assert.DoesNotContain("Reset it", payloads[0], StringComparison.Ordinal);

        // AC-12: no SMTP anywhere yet.
        await AssertProblemAsync(http, HttpMethod.Post, Recovery + "/test", edited, owner, HttpStatusCode.Conflict, ErrorCode.EmailNotConfigured);
        await SetSmtpAsync(server, Project);
        using var test = await SendAsync(http, HttpMethod.Post, Recovery + "/test", edited, owner);
        Assert.True(test.Status == HttpStatusCode.OK, test.Document?.RootElement.ToString());
        Assert.Equal(owner, test.Body.GetProperty("sentTo").GetString());
        var message = await mailpit.WaitForMessageToAsync(owner, Ct);
        Assert.Equal("Reset it, Tom & <Jerry>", message.GetProperty("Subject").GetString());
        Assert.Equal("hello@shop.test", message.GetProperty("From").GetProperty("Address").GetString());
        Assert.Contains("<h1>Tom &amp; &lt;Jerry&gt;</h1>", message.GetProperty("HTML").GetString(), StringComparison.Ordinal);
        Assert.Contains("Reset (https://example.com/auth/confirm?token=sample)", message.GetProperty("Text").GetString(), StringComparison.Ordinal);
        // A test stores nothing.
        Assert.Equal(0L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.messaging_emails"));

        // Reset, twice; the second has nothing to delete and still answers 204.
        using var reset = await SendAsync(http, HttpMethod.Delete, Recovery, null, owner);
        using var resetAgain = await SendAsync(http, HttpMethod.Delete, Recovery, null, owner);
        Assert.Equal(HttpStatusCode.NoContent, reset.Status);
        Assert.Equal(HttpStatusCode.NoContent, resetAgain.Status);
        using var back = await SendAsync(http, HttpMethod.Get, Recovery, null, owner);
        Assert.False(back.Body.GetProperty("isCustom").GetBoolean());
        Assert.Equal(original.Body.GetProperty("html").GetString(), back.Body.GetProperty("html").GetString());
        Assert.Equal(1L, await ScalarAsync<long>(server, "SELECT count(*) FROM orvano.events WHERE type = 'messaging.template.reset' AND subject = 'recovery'"));
    }

    // AC-12: with no SMTP of its own, a test goes through the install's.
    [Fact]
    public async Task A_test_email_goes_through_the_install_SMTP_when_the_project_has_none()
    {
        var owner = NewEmail("owner");
        await using var server = await StartAsync(owner);
        using var http = server.Api.Http();
        await SetSmtpAsync(server, "console");

        using var test = await SendAsync(http, HttpMethod.Post, Path + "/email_code/test", Template("Code {{ code }}", "<p>{{ code }} for {{ expires_in_minutes }} minutes</p>", null), owner);

        Assert.True(test.Status == HttpStatusCode.OK, test.Document?.RootElement.ToString());
        var message = await mailpit.WaitForMessageToAsync(owner, Ct);
        Assert.Equal("Code 428613", message.GetProperty("Subject").GetString());
        Assert.Equal("428613 for 10 minutes", message.GetProperty("Text").GetString()!.Trim());
    }

    // AC-10: the body, first its sizes (400), then the template's own rules (422).
    [Fact]
    public async Task An_invalid_template_is_refused_on_update_preview_and_test()
    {
        var owner = NewEmail("owner");
        await using var server = await StartAsync(owner);
        using var http = server.Api.Http();
        var cases = new (Dictionary<string, object?> Body, HttpStatusCode Status, string Code, string Detail)[]
        {
            (Template(" ", "<p>x</p>", null), HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, "subject: Enter a subject."),
            (Template(new string('s', 256), "<p>x</p>", null), HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, "subject: Use at most 255 characters."),
            (Template("Reset", new string('h', 102_401), null), HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, "html: Use at most 100 KB."),
            (Template("Reset", "<p>x</p>", new string('t', 102_401)), HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, "text: Use at most 100 KB."),
            (Template("Reset", "<p>\n\n\n{{ action_ur }}</p>", null), HttpStatusCode.UnprocessableEntity, ErrorCode.TemplateInvalid, "html: line 4: unknown variable action_ur"),
            (Template("Reset {{ user.emial }}", "<p>x</p>", null), HttpStatusCode.UnprocessableEntity, ErrorCode.TemplateInvalid, "subject: line 1: unknown variable user.emial"),
            (Template("Reset", "<p>x</p>", "a\n{% if %}"), HttpStatusCode.UnprocessableEntity, ErrorCode.TemplateInvalid, "text: line 2: Invalid 'if' tag"),
            (Template("Reset", "{{ user.name | raw }}", null), HttpStatusCode.UnprocessableEntity, ErrorCode.TemplateInvalid, "html: line 1: unknown filter raw"),
            (Template("Reset", "{% include 'footer' %}", null), HttpStatusCode.UnprocessableEntity, ErrorCode.TemplateInvalid, "html: line 1: include is not allowed"),
            (Template("Reset", "{% render 'footer' %}", null), HttpStatusCode.UnprocessableEntity, ErrorCode.TemplateInvalid, "html: line 1: render is not allowed"),
            (Template("Reset", "{% for i in (1..100001) %}x{% endfor %}", null), HttpStatusCode.UnprocessableEntity, ErrorCode.TemplateInvalid, "html: rendering takes more than 100,000 steps"),
            (Template("Reset", "{% assign s = 'ab' %}{% for i in (1..30) %}{% assign s = s | append: s %}{% endfor %}", null), HttpStatusCode.UnprocessableEntity, ErrorCode.TemplateInvalid, "html: a value or the output grows past 1 MB"),
        };

        foreach (var (body, status, code, detail) in cases)
        {
            foreach (var (method, url) in new[] { (HttpMethod.Put, Recovery), (HttpMethod.Post, Recovery + "/preview"), (HttpMethod.Post, Recovery + "/test") })
            {
                using var reply = await SendAsync(http, method, url, body, owner);
                Assert.True(status == reply.Status, $"{method} {url}: expected {status}, got {reply.Status}: {reply.Document?.RootElement}");
                Assert.Equal(code, reply.Body.GetProperty("code").GetString());
                Assert.Equal(detail, reply.Body.GetProperty("detail").GetString());
            }
        }

        Assert.Equal(0L, await ScalarAsync<long>(server, "SELECT (SELECT count(*) FROM orvano.messaging_email_templates) + (SELECT count(*) FROM orvano.events WHERE type LIKE 'messaging.template.%')"));
    }

    // AC-24: the project, the template, the role, the body, the project's state, then the SMTP.
    [Fact]
    public async Task Checks_answer_in_order_project_template_role_body_then_state()
    {
        var owner = NewEmail("owner");
        var viewer = NewEmail("viewer");
        var stranger = NewEmail("stranger");
        await using var server = await StartAsync(owner, viewer, stranger);
        using var http = server.Api.Http();
        await TestDatabase.ExecuteAsync(server.Database.Superuser,
            """
            INSERT INTO orvano.platform_memberships (org_id, user_id, role)
            SELECT p.org_id, u.id, 'viewer' FROM orvano.platform_projects p, orvano.auth_users u
            WHERE p.id = @project AND u.project_id = 'console' AND u.email = @email
            """, ("project", Project), ("email", viewer));
        var good = Template("Reset", "<p>{{ action_url }}</p>", null);
        var invalid = Template("", "<p>x</p>", null);
        var writes = new[] { (HttpMethod.Put, ""), (HttpMethod.Delete, ""), (HttpMethod.Post, "/preview"), (HttpMethod.Post, "/test") };

        // Not a member: 404 on everything, before the template is looked at.
        await AssertProblemAsync(http, HttpMethod.Get, Path, null, stranger, HttpStatusCode.NotFound, ErrorCode.ProjectNotFound);
        await AssertProblemAsync(http, HttpMethod.Get, Path + "/nope", null, stranger, HttpStatusCode.NotFound, ErrorCode.ProjectNotFound);
        await AssertProblemAsync(http, HttpMethod.Put, Recovery, invalid, stranger, HttpStatusCode.NotFound, ErrorCode.ProjectNotFound);
        await AssertProblemAsync(http, HttpMethod.Get, Recovery, null, owner, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, project: "");

        // An unknown template: 404 not_found, before the role. The console's own invite is not one of the four.
        foreach (var kind in new[] { "nope", "console_invitation", "Recovery" })
        {
            await AssertProblemAsync(http, HttpMethod.Get, $"{Path}/{kind}", null, owner, HttpStatusCode.NotFound, ErrorCode.NotFound);
            foreach (var (method, suffix) in writes)
                await AssertProblemAsync(http, method, $"{Path}/{kind}{suffix}", method == HttpMethod.Delete ? null : invalid, viewer, HttpStatusCode.NotFound, ErrorCode.NotFound);
        }

        // A viewer reads, and gets 403 on every change before the body is looked at.
        using var catalog = await SendAsync(http, HttpMethod.Get, Path, null, viewer);
        using var read = await SendAsync(http, HttpMethod.Get, Recovery, null, viewer);
        Assert.Equal(HttpStatusCode.OK, catalog.Status);
        Assert.Equal(HttpStatusCode.OK, read.Status);
        foreach (var (method, suffix) in writes)
            await AssertProblemAsync(http, method, Recovery + suffix, method == HttpMethod.Delete ? null : invalid, viewer, HttpStatusCode.Forbidden, ErrorCode.Forbidden);

        // A project that is not active: reads and preview still work, writes and tests answer 409 after the body check.
        await TestDatabase.ExecuteAsync(server.Database.Superuser,
            "UPDATE orvano.platform_projects SET status = 'provisioning' WHERE id = @p", ("p", Project));
        using var stillReads = await SendAsync(http, HttpMethod.Get, Recovery, null, owner);
        using var stillPreviews = await SendAsync(http, HttpMethod.Post, Recovery + "/preview", good, owner);
        Assert.Equal(HttpStatusCode.OK, stillReads.Status);
        Assert.Equal(HttpStatusCode.OK, stillPreviews.Status);
        await AssertProblemAsync(http, HttpMethod.Put, Recovery, invalid, owner, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);
        await AssertProblemAsync(http, HttpMethod.Put, Recovery, good, owner, HttpStatusCode.Conflict, ErrorCode.ProjectNotReady);
        await AssertProblemAsync(http, HttpMethod.Delete, Recovery, null, owner, HttpStatusCode.Conflict, ErrorCode.ProjectNotReady);
        // Not ready comes before not configured.
        await AssertProblemAsync(http, HttpMethod.Post, Recovery + "/test", good, owner, HttpStatusCode.Conflict, ErrorCode.ProjectNotReady);
    }

    // AC-25: previews have their own, larger limit, and a refused body still counts.
    [Fact]
    public async Task The_301st_preview_in_5_minutes_and_the_31st_test_in_15_are_rate_limited()
    {
        var owner = NewEmail("owner");
        await using var server = await StartAsync(owner);
        using var http = server.Api.Http();
        var invalid = Template("", "<p>x</p>", null);

        for (var i = 0; i < 300; i++)
            await AssertProblemAsync(http, HttpMethod.Post, Recovery + "/preview", invalid, owner, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, await StatusAsync(http, Recovery + "/preview", invalid, owner));

        // Test sends have their own limit, still untouched.
        for (var i = 0; i < 30; i++)
            await AssertProblemAsync(http, HttpMethod.Post, Recovery + "/test", invalid, owner, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, await StatusAsync(http, Recovery + "/test", invalid, owner));

        static async Task<HttpStatusCode> StatusAsync(HttpClient http, string url, object body, string session)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            await ConsoleSignIn.AuthorizeAsync(http, request, session, Ct);
            request.Headers.Add("X-Orvano-Project", Project);
            using var response = await http.SendAsync(request, Ct);
            Assert.Equal(response.StatusCode == HttpStatusCode.TooManyRequests, response.Headers.Contains("Retry-After"));
            return response.StatusCode;
        }
    }

    private static Dictionary<string, object?> Template(string subject, string html, string? text) =>
        new() { ["subject"] = subject, ["html"] = html, ["text"] = text };

    /// <summary>Points <paramref name="projectId"/> at Mailpit, as a saved row with no sign in.</summary>
    private Task SetSmtpAsync(Server server, string projectId) => TestDatabase.ExecuteAsync(server.Database.Superuser,
        """
        INSERT INTO orvano.messaging_smtp_settings (project_id, host, port, security, from_email, updated_by_user_id)
        VALUES (@project, @host, @port, 'none', 'hello@shop.test', gen_random_uuid())
        """, ("project", projectId), ("host", mailpit.Host), ("port", mailpit.SmtpPort));

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

    /// <summary>The api over a fresh database, with the first session owning an active project whose name needs encoding.</summary>
    private async Task<Server> StartAsync(params string[] sessions)
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var fixtures = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"orvano-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(fixtures, $"""
            {ConsoleSignIn.Fixtures(sessions)}projects:
              - id: {Project}
                name: "Tom & <Jerry>"

            """, Ct);

        var api = OrvanoProcess.Start(["api"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["ORVANO_TEST_FIXTURES"] = fixtures,
            ["ORVANO_SMTP_ALLOW_PRIVATE_HOSTS"] = "true",
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
