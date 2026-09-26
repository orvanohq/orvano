using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Orvano.Tests;

// Spec 0001: the .NET SDK runtime behind every generated service.
public class OrvanoClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sends_the_SDK_name_and_version_on_every_request() // covers: AC-11
    {
        var server = new FakeServer().ThenHealth();
        using var client = server.Client();

        await client.Health.GetAsync(Ct);

        Assert.Equal($"Orvano/{Version()}", FakeServer.Header(Assert.Single(server.Requests), "X-Orvano-SDK"));
    }

    [Fact]
    public async Task Sends_the_project_API_key_and_session_from_its_options() // covers: AC-4
    {
        var server = new FakeServer().ThenHealth();
        using var client = server.Client(o =>
        {
            o.Project = "p1";
            o.ApiKey = "test-server-key";
            o.Session = new MemorySessionStore { Token = "t" };
        });

        await client.Health.GetAsync(Ct);

        var request = Assert.Single(server.Requests);
        Assert.Equal(("p1", "test-server-key", "t"), (FakeServer.Header(request, "X-Orvano-Project"), FakeServer.Header(request, "X-Orvano-Key"), FakeServer.Header(request, "X-Orvano-Session")));
    }

    [Fact]
    public async Task Sends_no_credentials_it_was_not_given() // covers: AC-4
    {
        var server = new FakeServer().ThenHealth();
        using var client = server.Client(o => o.Session = new MemorySessionStore());

        await client.Health.GetAsync(Ct);

        var request = Assert.Single(server.Requests);
        Assert.Null(FakeServer.Header(request, "X-Orvano-Key"));
        Assert.Null(FakeServer.Header(request, "X-Orvano-Session"));
        Assert.Null(FakeServer.Header(request, "X-Orvano-Project"));
    }

    [Theory]
    [InlineData("https://orvano.example.com")]
    [InlineData("https://orvano.example.com/")]
    public async Task Joins_the_endpoint_and_the_operation_path_with_one_slash(string endpoint)
    {
        var server = new FakeServer().ThenHealth();
        using var client = server.Client(endpoint: endpoint);

        await client.Health.GetAsync(Ct);

        Assert.Equal("https://orvano.example.com/v1/health", Assert.Single(server.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public void Refuses_a_relative_endpoint()
    {
        Assert.Throws<ArgumentException>(() => new OrvanoClientOptions(new Uri("/orvano", UriKind.Relative)));
    }

    [Fact]
    public async Task Warns_once_per_client_when_the_server_runs_another_minor() // covers: AC-11
    {
        var server = new FakeServer().ThenHealth("0.99.0");
        var logger = new ListLogger();
        using var client = server.Client(o => o.Logger = logger);

        await client.Health.GetAsync(Ct);
        await client.Health.GetAsync(Ct);

        var (level, message) = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("The Orvano .NET SDK", message, StringComparison.Ordinal);
        Assert.Contains("runs 0.99.0", message, StringComparison.Ordinal);
        Assert.Contains("update the Orvano package to 0.99.x", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stays_quiet_when_only_the_patch_version_differs() // covers: AC-11
    {
        var patch = Version().Split('.') is [var major, var minor, ..] ? $"{major}.{minor}.99" : "0.0.99";
        var server = new FakeServer().ThenHealth(patch);
        var logger = new ListLogger();
        using var client = server.Client(o => o.Logger = logger);

        await client.Health.GetAsync(Ct);

        Assert.Empty(logger.Lines);
    }

    [Fact]
    public async Task A_mismatch_still_returns_the_result_without_a_logger() // covers: AC-11
    {
        var server = new FakeServer().ThenHealth("0.99.0");
        using var client = server.Client();

        var health = await client.Health.GetAsync(Ct);

        Assert.Equal("ok", health.Status);
    }

    [Theory]
    [InlineData(503)]
    [InlineData(429)]
    public async Task Retries_a_GET_after_a_503_or_429_honoring_Retry_After(int status) // covers: AC-14
    {
        var server = new FakeServer().ThenStatus(status, retryAfter: "0").ThenHealth();
        using var client = server.Client();

        var health = await client.Health.GetAsync(Ct);

        Assert.Equal("ok", health.Status);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task Gives_up_after_the_retry_limit_with_the_last_status() // covers: AC-14
    {
        var server = new FakeServer().ThenStatus(503, retryAfter: "0");
        using var client = server.Client(o => o.MaxRetries = 2);

        var error = await Assert.ThrowsAsync<OrvanoException>(() => client.Health.GetAsync(Ct));

        Assert.Equal(503, error.Status);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task Does_not_retry_other_failures() // covers: AC-14
    {
        var server = new FakeServer().ThenStatus(500);
        using var client = server.Client();

        await Assert.ThrowsAsync<OrvanoException>(() => client.Health.GetAsync(Ct));

        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Never_retries_a_POST_that_is_not_marked_idempotent() // covers: AC-14
    {
        var server = new FakeServer().ThenStatus(503, retryAfter: "0");
        using var client = server.Client();

        var error = await Assert.ThrowsAsync<OrvanoException>(() => InternalSend.PostAsync(client, "/v1/things", idempotent: false, Ct));

        Assert.Equal(503, error.Status);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Retries_a_POST_marked_idempotent() // covers: AC-14
    {
        var server = new FakeServer().ThenStatus(503, retryAfter: "0").ThenStatus(204);
        using var client = server.Client();

        await InternalSend.PostAsync(client, "/v1/things", idempotent: true, Ct);

        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task Times_out_a_call_that_takes_too_long() // covers: AC-14
    {
        var server = new FakeServer().ThenHang();
        using var client = server.Client(o => o.Timeout = TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(() => client.Health.GetAsync(Ct));
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_reported_as_a_timeout() // covers: AC-14
    {
        var server = new FakeServer().ThenHang();
        using var client = server.Client();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancel.CancelAfter(50);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Health.GetAsync(cancel.Token));

        Assert.IsNotType<TimeoutException>(error);
    }

    [Fact]
    public async Task Maps_a_problem_body_to_one_exception_with_its_code_detail_and_request_id() // covers: AC-6
    {
        var server = new FakeServer().Then(() => Problem(409, """{"type":"https://orvano.dev/errors/x","title":"Conflict","status":409,"detail":"Already there.","code":"user_already_exists","requestId":"body-id"}"""));
        using var client = server.Client();

        var error = await Assert.ThrowsAsync<OrvanoException>(() => client.Health.GetAsync(Ct));

        Assert.Equal((409, "user_already_exists", "Already there.", "body-id"), (error.Status, error.Code, error.Message, error.RequestId));
    }

    [Fact]
    public async Task Falls_back_to_the_title_when_a_problem_has_no_detail() // covers: AC-6
    {
        var server = new FakeServer().Then(() => Problem(404, """{"type":"https://orvano.dev/errors/not_found","title":"Not Found","status":404,"code":"not_found","requestId":"body-id"}"""));
        using var client = server.Client();

        var error = await Assert.ThrowsAsync<OrvanoException>(() => client.Health.GetAsync(Ct));

        Assert.Equal(("not_found", "Not Found"), (error.Code, error.Message));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Bad gateway</html>")]
    public async Task Uses_unknown_and_the_request_id_header_when_the_body_is_not_a_problem(string body) // covers: AC-6
    {
        var server = new FakeServer().Then(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(body) };
            response.Headers.Add("X-Request-Id", "header-id");
            return response;
        });
        using var client = server.Client();

        var error = await Assert.ThrowsAsync<OrvanoException>(() => client.Health.GetAsync(Ct));

        Assert.Equal((502, "unknown", "header-id"), (error.Status, error.Code, error.RequestId));
        Assert.Contains("502", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decodes_an_unknown_event_to_null_instead_of_throwing() // covers: AC-8
    {
        using var payload = JsonDocument.Parse("""{"a":1}""");

        Assert.Null(OrvanoEvents.Decode("nobody.knows", payload.RootElement));
    }

    [Fact]
    public void Exposes_the_generated_error_codes_as_constants() // covers: AC-6
    {
        Assert.Equal(("not_found", "internal_error"), (ErrorCode.NotFound, ErrorCode.InternalError));
    }

    private static HttpResponseMessage Problem(int status, string json) =>
        new((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") };

    /// <summary>The repo's VERSION, which the SDK's generated constant must match.</summary>
    private static string Version()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "VERSION");
            if (File.Exists(file)) return File.ReadAllText(file).Trim();
        }

        throw new InvalidOperationException("VERSION not found");
    }
}
