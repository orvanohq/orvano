using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.Modules.Platform;
using Orvano.Modules.Platform.Setup;
using Orvano.SharedKernel.Api;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0016 STP-1 to STP-8, AC-6, AC-7: the setup token issued at start, by `orvano admin setup-token`, and checked by
// POST /v1/console/setup/token. Each test gets its own migrated database; time comes from ManualClock, the repo's fake
// TimeProvider (server/AGENTS.md), so expiry is tested without waiting.
public sealed partial class SetupTokenTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string PublicUrl = "http://localhost:8080";
    private static readonly Uri CheckPath = new("/v1/console/setup/token", UriKind.Relative);

    private readonly ConsoleBuild _console = new();
    private readonly List<OrvanoApiFactory> _factories = [];
    private readonly ManualClock _clock = new();
    private string _database = string.Empty;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await postgres.CreateMigratedDatabaseAsync("setup", Token);

    public ValueTask DisposeAsync()
    {
        foreach (OrvanoApiFactory factory in _factories)
        {
            factory.Dispose();
        }

        _console.Dispose();
        return ValueTask.CompletedTask;
    }

    // Start (STP-3, STP-4)

    [Fact, Trait("Spec", "0016:STP-1"), Trait("Spec", "0016:STP-3"), Trait("Spec", "0016:STP-4"), Trait("Spec", "0016:AC-6")]
    public async Task Start_issues_a_token_stores_only_its_hash_and_prints_the_banner_when_setup_is_open()
    {
        (_, string output) = await StartAsync();

        string token = TokenIn(output);
        token.ShouldMatch("^orv_setup_[A-Za-z0-9_-]{43}$");
        output.ShouldBe(
            $"{Environment.NewLine}Finish setting up Orvano: {PublicUrl}/console/setup#token={token}{Environment.NewLine}"
            + $"This link works until setup is done or 2026-10-20T12:00:00Z. Make a new one with: orvano admin setup-token{Environment.NewLine}"
            + Environment.NewLine);
        (await HashAsync()).ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        (await ExpiresAtAsync()).ShouldBe(_clock.GetUtcNow() + TimeSpan.FromHours(24));
    }

    [Fact, Trait("Spec", "0016:STP-3"), Trait("Spec", "0016:STP-4"), Trait("Spec", "0016:AC-6")]
    public async Task Start_keeps_a_valid_token_and_prints_the_short_banner_when_one_exists()
    {
        (HttpClient first, string firstOutput) = await StartAsync();
        byte[]? hash = await HashAsync();
        _clock.Advance(TimeSpan.FromHours(23));

        (_, string output) = await StartAsync();

        output.ShouldBe($"Orvano setup is waiting. Make a setup link with: orvano admin setup-token{Environment.NewLine}");
        (await HashAsync()).ShouldBe(hash);
        (await CheckAsync(first, TokenIn(firstOutput))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Spec", "0016:STP-3"), Trait("Spec", "0016:AC-6")]
    public async Task Start_issues_a_new_token_when_the_old_one_expired()
    {
        (_, string firstOutput) = await StartAsync();
        _clock.Advance(TimeSpan.FromHours(24));

        (HttpClient client, string output) = await StartAsync();

        string token = TokenIn(output);
        token.ShouldNotBe(TokenIn(firstOutput));
        (await HashAsync()).ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        (await CheckAsync(client, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Spec", "0016:STP-3"), Trait("Spec", "0016:AC-6")]
    public async Task Start_issues_and_prints_nothing_when_setup_is_complete()
    {
        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");

        (_, string output) = await StartAsync();

        output.ShouldBeEmpty();
        (await HashAsync()).ShouldBeNull();
    }

    [Fact, Trait("Spec", "0016:STP-3")]
    public async Task Start_issues_and_prints_nothing_in_the_worker_role()
    {
        (_, string output) = await StartAsync(roles: "worker");

        output.ShouldBeEmpty();
        (await HashAsync()).ShouldBeNull();
    }

    [Fact, Trait("Spec", "0016:STP-3")]
    public async Task Start_carries_on_without_a_banner_when_the_instance_table_is_missing()
    {
        await ExecuteAsync("DROP TABLE platform.instance");

        (HttpClient client, string output) = await StartAsync();

        output.ShouldBeEmpty();
        using HttpResponseMessage live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Token);
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact, Trait("Spec", "0016:STP-3")]
    public async Task Concurrent_issues_at_start_make_at_most_one_token()
    {
        (_, string output) = await StartAsync(roles: "worker"); // no hook, so the row has no token
        output.ShouldBeEmpty();
        OrvanoApiFactory factory = _factories[^1];

        IssuedSetupToken?[] issued = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<SetupTokens>().IssueIfNoneValidAsync(Token);
        }));

        IssuedSetupToken winner = issued.OfType<IssuedSetupToken>().ShouldHaveSingleItem();
        (await HashAsync()).ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(winner.Token)));
    }

    // The check (STP-6 to STP-8)

    [Fact, Trait("Spec", "0016:STP-6"), Trait("Spec", "0016:STP-7"), Trait("Spec", "0016:AC-7")]
    public async Task Check_answers_204_without_caching_or_using_the_token_up_when_it_is_current()
    {
        (HttpClient client, string output) = await StartAsync();
        byte[]? hash = await HashAsync();

        using HttpResponseMessage first = await CheckAsync(client, TokenIn(output));
        using HttpResponseMessage second = await CheckAsync(client, TokenIn(output));

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        first.Headers.CacheControl?.NoStore.ShouldBeTrue();
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await HashAsync()).ShouldBe(hash);
    }

    [Fact, Trait("Spec", "0016:STP-6")]
    public async Task Check_answers_204_when_the_token_has_surrounding_white_space()
    {
        (HttpClient client, string output) = await StartAsync();

        using HttpResponseMessage response = await CheckAsync(client, $"  {TokenIn(output)}\n");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory, Trait("Spec", "0016:STP-6"), Trait("Spec", "0016:STP-7"), Trait("Spec", "0016:AC-7")]
    [InlineData("wrong")]
    [InlineData("expired")]
    [InlineData("replaced")]
    [InlineData("none issued")]
    public async Task Check_answers_401_setup_token_invalid_alike_when_the_token_is(string reason)
    {
        (HttpClient client, string output) = await StartAsync();
        string token = TokenIn(output);
        switch (reason)
        {
            case "wrong":
                token = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');
                break;
            case "expired":
                _clock.Advance(TimeSpan.FromHours(24));
                break;
            case "replaced":
                (await PlatformSetup.IssueLinkAsync(_factories[^1].Services, Token)).ShouldNotBeNull();
                break;
            case "none issued":
                await ExecuteAsync("UPDATE platform.instance SET setup_token_hash = NULL, setup_token_expires_at = NULL");
                break;
        }

        using HttpResponseMessage response = await CheckAsync(client, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using JsonDocument problem = await ProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(PlatformErrorCodes.SetupTokenInvalid);
        problem.RootElement.GetProperty("detail").GetString()!.ShouldNotContain(token);
    }

    [Fact, Trait("Spec", "0016:STP-7"), Trait("Spec", "0016:AC-7")]
    public async Task Check_answers_409_setup_complete_after_setup_even_with_the_last_token()
    {
        (HttpClient client, string output) = await StartAsync();
        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");

        using HttpResponseMessage response = await CheckAsync(client, TokenIn(output));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using JsonDocument problem = await ProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(PlatformErrorCodes.SetupComplete);
    }

    [Theory, Trait("Spec", "0016:STP-7"), Trait("Spec", "0014:PRB-3")]
    [InlineData("{}")]
    [InlineData("""{"token":null}""")]
    [InlineData("""{"token":"   "}""")]
    [InlineData("""{"token":"orv_setup_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")]
    public async Task Check_answers_400_validation_failed_when_the_body_is_missing_or_malformed(string body)
    {
        (HttpClient client, _) = await StartAsync();

        using HttpResponseMessage response = await PostAsync(client, body, PublicUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using JsonDocument problem = await ProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.ValidationFailed);
        problem.RootElement.GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("token");
    }

    [Theory, Trait("Spec", "0016:STP-7"), Trait("Spec", "0006:CON-2"), Trait("Spec", "0016:AC-7")]
    [InlineData("http://evil.example")]
    [InlineData("http://localhost")]
    [InlineData("https://localhost:8080")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Check_answers_403_before_reading_the_body_when_the_origin_is_not_the_console(string? origin)
    {
        (HttpClient client, string output) = await StartAsync();

        using HttpResponseMessage valid = await PostAsync(client, Body(TokenIn(output)), origin);
        using HttpResponseMessage malformed = await PostAsync(client, "{", origin);

        valid.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        malformed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using JsonDocument problem = await ProblemAsync(valid);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.Forbidden);
    }

    [Fact, Trait("Spec", "0006:CON-2")]
    public async Task Check_accepts_the_console_origin_setting_over_the_public_url()
    {
        (HttpClient client, string output) = await StartAsync(consoleOrigin: "http://localhost:5173");

        using HttpResponseMessage fromConsole = await PostAsync(client, Body(TokenIn(output)), "http://localhost:5173");
        using HttpResponseMessage fromPublicUrl = await PostAsync(client, Body(TokenIn(output)), PublicUrl);

        fromConsole.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        fromPublicUrl.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Spec", "0006:CON-2")]
    public async Task Check_answers_403_to_every_origin_when_no_console_address_is_set()
    {
        (HttpClient client, string output) = await StartAsync(publicUrl: null);

        output.ShouldContain(" /console/setup#token=orv_setup_"); // the link is the path alone
        using HttpResponseMessage response = await PostAsync(client, Body(TokenIn(output)), PublicUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Spec", "0016:STP-8"), Trait("Spec", "0016:AC-7")]
    public async Task Check_answers_429_with_retry_after_on_the_eleventh_check_in_a_minute()
    {
        (HttpClient client, string output) = await StartAsync();

        for (int i = 0; i < 10; i++)
        {
            using HttpResponseMessage allowed = await CheckAsync(client, i % 2 == 0 ? TokenIn(output) : "wrong");
            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        using HttpResponseMessage limited = await CheckAsync(client, TokenIn(output));

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter?.Delta.ShouldNotBeNull();
        using JsonDocument problem = await ProblemAsync(limited);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.RateLimited);
    }

    [Fact, Trait("Spec", "0016:STP-7")]
    public async Task Check_answers_404_in_the_worker_role()
    {
        (HttpClient client, _) = await StartAsync(roles: "worker");

        using HttpResponseMessage response = await CheckAsync(client, "orv_setup_x");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // `orvano admin setup-token` (STP-5), as the real process

    [Fact, Trait("Spec", "0016:STP-5"), Trait("Spec", "0016:AC-6")]
    public async Task Admin_setup_token_prints_only_a_new_link_each_time_and_replaces_the_previous_token()
    {
        OrvanoCli.Result first = await AdminSetupTokenAsync();
        OrvanoCli.Result second = await AdminSetupTokenAsync();

        first.ExitCode.ShouldBe(0, first.Error);
        second.ExitCode.ShouldBe(0, second.Error);
        first.Output.ShouldMatch($"^{Regex.Escape(PublicUrl)}/console/setup#token=orv_setup_[A-Za-z0-9_-]{{43}}\\r?\\n$");
        string token = TokenIn(second.Output);
        token.ShouldNotBe(TokenIn(first.Output));
        (await HashAsync()).ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        (HttpClient client, _) = await StartAsync();
        (await CheckAsync(client, TokenIn(first.Output))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CheckAsync(client, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Spec", "0016:STP-5"), Trait("Spec", "0016:AC-6")]
    public async Task Admin_setup_token_exits_3_and_issues_nothing_when_setup_is_complete()
    {
        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");

        OrvanoCli.Result result = await AdminSetupTokenAsync();

        result.ExitCode.ShouldBe(3);
        result.Output.ShouldBeEmpty();
        result.Error.Trim().ShouldBe("Setup is already complete.");
        (await HashAsync()).ShouldBeNull();
    }

    [Fact, Trait("Spec", "0016:STP-5")]
    public async Task Admin_setup_token_exits_1_when_the_database_is_not_migrated()
    {
        string empty = await postgres.CreateDatabaseAsync("setup-empty", Token);

        OrvanoCli.Result result = await AdminSetupTokenAsync(empty);

        result.ExitCode.ShouldBe(1);
        result.Output.ShouldBeEmpty();
        result.Error.ShouldContain("Could not make a setup link");
    }

    private async Task<(HttpClient Client, string Output)> StartAsync(
        string? roles = null, string? publicUrl = PublicUrl, string? consoleOrigin = null)
    {
        Dictionary<string, string> settings = OrvanoApiFactory.DefaultSettings(postgres);
        settings["Database:Name"] = _database;
        if (roles is not null)
        {
            settings["ROLES"] = roles;
        }

        if (publicUrl is not null)
        {
            settings["PUBLIC_URL"] = publicUrl;
        }

        if (consoleOrigin is not null)
        {
            settings["CONSOLE_ORIGIN"] = consoleOrigin;
        }

        var output = new StringWriter();
        var factory = new OrvanoApiFactory(settings, _console.ContentRoot, services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton(new SetupOutput(output));
        });
        _factories.Add(factory);
        HttpClient client = factory.CreateClient(new() { AllowAutoRedirect = false });
        return (client, output.ToString());
    }

    private Task<OrvanoCli.Result> AdminSetupTokenAsync(string? database = null)
    {
        var environment = postgres.DatabaseEnvironment.ToDictionary();
        environment["ORVANO_DATABASE__NAME"] = database ?? _database;
        environment["ORVANO_PUBLIC_URL"] = PublicUrl;
        return OrvanoCli.RunAsync(["admin", "setup-token"], environment, Token);
    }

    private static string TokenIn(string output) => TokenPattern().Match(output) is { Success: true } match
        ? match.Groups["token"].Value
        : throw new InvalidOperationException($"No setup link in: {output}");

    private static string Body(string token) => JsonSerializer.Serialize(new { token });

    private static Task<HttpResponseMessage> CheckAsync(HttpClient client, string token) => PostAsync(client, Body(token), PublicUrl);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string body, string? origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CheckPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return await client.SendAsync(request, Token);
    }

    private static async Task<JsonDocument> ProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
    }

    private async Task<byte[]?> HashAsync() => await ScalarAsync("SELECT setup_token_hash FROM platform.instance") as byte[];

    private async Task<DateTimeOffset?> ExpiresAtAsync() =>
        await ScalarAsync("SELECT setup_token_expires_at FROM platform.instance") is DateTime at
            ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))
            : null;

    private async Task ExecuteAsync(string sql)
    {
        await using var data = NpgsqlDataSource.Create(postgres.ConnectionString(_database));
        await using NpgsqlCommand command = data.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(Token);
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var data = NpgsqlDataSource.Create(postgres.ConnectionString(_database));
        await using NpgsqlCommand command = data.CreateCommand(sql);
        object? value = await command.ExecuteScalarAsync(Token);
        return value is DBNull ? null : value;
    }

    [GeneratedRegex("#token=(?<token>orv_setup_[A-Za-z0-9_-]+)")]
    private static partial Regex TokenPattern();
}
