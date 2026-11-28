using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Orvano.IntegrationTests;

/// <summary>
/// <c>orvano</c> serves the console export at <c>/console</c> (spec 0003, AC-1 to AC-4). The cases follow the order
/// of the spec's HTTP surface table, the same order as <c>console/tests/serve-export.spec.ts</c>.
/// Postgres is never contacted: serving files needs no database.
/// </summary>
public sealed class ConsoleFilesTests : IDisposable
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=orvano;Username=orvano;Password=unused";
    private const string IndexHtml = "<!doctype html><title>Projects · Orvano</title>";
    private const string MembersHtml = "<!doctype html><title>Members · Orvano</title>";
    private const string NotFoundHtml = "<!doctype html><title>Page not found · Orvano</title>";

    private static readonly string[] SecurityHeaders =
        ["X-Content-Type-Options", "Referrer-Policy", "X-Frame-Options", "Content-Security-Policy"];

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("orvano-console-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // ---- Any path, index.html missing → 503 problem ----------------------------------------------------------

    [Theory]
    [InlineData("GET", "/console")]
    [InlineData("GET", "/console/")]
    [InlineData("GET", "/console/members/")]
    [InlineData("POST", "/console/")]
    public async Task Missing_files_answer_503_console_not_built(string method, string path)
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancel);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        AssertSecurityHeaders(response);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        var body = json.RootElement;
        Assert.Equal("about:blank", body.GetProperty("type").GetString());
        Assert.Equal("Console not built", body.GetProperty("title").GetString());
        Assert.Equal(503, body.GetProperty("status").GetInt32());
        Assert.Equal("console_not_built", body.GetProperty("code").GetString());
        Assert.Contains("pnpm --filter @orvano/console build", body.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_files_leave_other_paths_alone_and_log_one_warning_naming_the_folder()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var health = await client.GetAsync("/healthz", Cancel);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        AssertNoConsoleHeaders(health);
        var warning = Assert.Single(factory.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("console", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_root.FullName, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_folder_answers_503_and_starts_serving_once_it_is_built()
    {
        var root = Path.Combine(_root.FullName, "not-yet");
        await using var factory = CreateFactory(root);
        using var client = CreateClient(factory);

        using (var before = await client.GetAsync("/console/", Cancel))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, before.StatusCode);
        }

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), IndexHtml, Cancel);

        using var after = await client.GetAsync("/console/", Cancel);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    // ---- Built console -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Other_methods_answer_405_with_allow(string method)
    {
        WriteFixture();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/console/"), Cancel);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET", "HEAD"], response.Content.Headers.Allow.Order(StringComparer.Ordinal));
        AssertNoCache(response);
        AssertSecurityHeaders(response);
    }

    [Theory]
    [InlineData("/console", HttpStatusCode.PermanentRedirect, "/console/")]
    [InlineData("/console?x=1", HttpStatusCode.PermanentRedirect, "/console/?x=1")]
    [InlineData("/console/members", HttpStatusCode.MovedPermanently, "/console/members/")]
    [InlineData("/console/members?project=prj_x", HttpStatusCode.MovedPermanently, "/console/members/?project=prj_x")]
    public async Task Paths_without_their_slash_redirect(string path, HttpStatusCode status, string location)
    {
        WriteFixture();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(path, Cancel);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.OriginalString);
        AssertNoCache(response);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Hashed_assets_are_cached_for_a_year()
    {
        WriteFixture();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/console/_next/static/app.js", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        AssertSecurityHeaders(response);
    }

    [Theory]
    [InlineData("/console/", "text/html", IndexHtml)]
    [InlineData("/console/members/", "text/html", MembersHtml)]
    [InlineData("/console/project/database/?project=prj_x", "text/html", IndexHtml)]
    [InlineData("/console/index.txt", "text/plain", "route data")]
    public async Task Pages_and_files_are_served_with_no_cache(string path, string mediaType, string body)
    {
        WriteFixture();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(path, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(body, await response.Content.ReadAsStringAsync(Cancel));
        AssertNoCache(response);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Head_is_served_without_a_body()
    {
        WriteFixture();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/console/"), Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Cancel));
    }

    [Theory]
    [InlineData("/console/nope")]
    [InlineData("/console/nope/")]
    [InlineData("/console/members/extra.bin")]
    public async Task Unknown_paths_answer_404_with_the_not_found_page(string path)
    {
        WriteFixture();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(path, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(NotFoundHtml, await response.Content.ReadAsStringAsync(Cancel));
        AssertNoCache(response);
        AssertSecurityHeaders(response);
    }

    [Theory]
    [InlineData("/consolefoo")]
    [InlineData("/healthz")]
    public async Task Paths_outside_the_branch_get_no_console_headers(string path)
    {
        WriteFixture();
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(path, Cancel);

        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoConsoleHeaders(response);
    }

    public void Dispose() => _root.Delete(recursive: true);

    private OrvanoAppFactory CreateFactory(string? root = null) =>
        new(UnreachableDatabase, consoleRootPath: root ?? _root.FullName);

    private static HttpClient CreateClient(OrvanoAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private void WriteFixture()
    {
        Write("index.html", IndexHtml);
        Write("index.txt", "route data");
        Write("404.html", NotFoundHtml);
        Write("members/index.html", MembersHtml);
        Write("project/database/index.html", IndexHtml);
        Write("_next/static/app.js", "console.log(1);");
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void AssertNoCache(HttpResponseMessage response) =>
        Assert.True(response.Headers.CacheControl?.NoCache, $"Expected Cache-Control: no-cache, got '{response.Headers.CacheControl}'.");

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
    }

    private static void AssertNoConsoleHeaders(HttpResponseMessage response)
    {
        foreach (var name in SecurityHeaders)
        {
            Assert.Null(Header(response, name));
        }
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : null;
}
