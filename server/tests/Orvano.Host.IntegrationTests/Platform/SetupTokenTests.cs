using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

using Npgsql;

using Orvano.Host.IntegrationTests.Cli;
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
    private readonly RecordedLogs _logs = new();
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
    public async Task Start_warns_and_prints_nothing_when_the_instance_row_is_missing()
    {
        await ExecuteAsync("DELETE FROM platform.instance");

        (_, string output) = await StartAsync();

        output.ShouldBeEmpty();
        _logs.Lines.ShouldContain(line => line.StartsWith("Warning", StringComparison.Ordinal) && line.Contains("does not exist yet", StringComparison.Ordinal));
    }

    [Fact, Trait("Spec", "0016:STP-3")]
    public async Task Start_issues_a_token_when_the_row_has_a_hash_without_an_expiry()
    {
        await ExecuteAsync("UPDATE platform.instance SET setup_token_hash = '\\x00'");

        (HttpClient client, string output) = await StartAsync();

        (await CheckAsync(client, TokenIn(output))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Spec", "0016:STP-3"), Trait("Spec", "0016:AC-6")]
    public async Task Start_leaves_a_leftover_token_alone_and_prints_nothing_when_setup_is_complete()
    {
        await StartAsync();
        byte[]? hash = await HashAsync();
        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");

        (_, string output) = await StartAsync();

        output.ShouldBeEmpty();
        (await HashAsync()).ShouldBe(hash);
    }

    [Fact, Trait("Spec", "0016:STP-2")]
    public async Task Issue_moves_updated_at_to_the_clock_and_keeps_created_at()
    {
        object? createdAt = await ScalarAsync("SELECT created_at FROM platform.instance");

        await StartAsync();

        (await TimeAsync("updated_at")).ShouldBe(_clock.GetUtcNow());
        (await ScalarAsync("SELECT created_at FROM platform.instance")).ShouldBe(createdAt);
    }

    [Theory, Trait("Spec", "0016:STP-4")]
    [InlineData("https://orvano.example/", "https://orvano.example/console/setup#token=")]
    [InlineData("https://orvano.example/base/", "https://orvano.example/base/console/setup#token=")]
    [InlineData("HTTP://LOCALHOST:8080", "http://localhost:8080/console/setup#token=")]
    public async Task Banner_builds_the_link_without_a_double_slash_when_the_public_url_is(string publicUrl, string link)
    {
        (_, string output) = await StartAsync(publicUrl: publicUrl);

        output.ShouldContain($"Finish setting up Orvano: {link}orv_setup_");
    }

    [Fact, Trait("Spec", "0016:STP-4")]
    public void Banner_states_the_expiry_in_UTC_when_the_time_has_an_offset()
    {
        var output = new StringWriter();

        new SetupOutput(output).WriteBanner("link", new DateTimeOffset(2026, 10, 20, 14, 30, 5, TimeSpan.FromHours(2)));

        output.ToString().ShouldContain("This link works until setup is done or 2026-10-20T12:30:05Z.");
    }

    [Fact, Trait("Spec", "0016:STP-4"), Trait("Spec", "0006:SCR-5")]
    public async Task Logs_never_hold_the_token_during_start_a_replaced_token_and_each_check()
    {
        (HttpClient client, string output) = await StartAsync();
        string first = TokenIn(output);
        (await CheckAsync(client, first)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        SetupLink? link = await PlatformSetup.IssueLinkAsync(_factories[^1].Services, Token);
        (await CheckAsync(client, first)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        _logs.Lines.ShouldNotBeEmpty();
        _logs.Lines.ShouldNotContain(line => line.Contains(first, StringComparison.Ordinal));
        _logs.Lines.ShouldNotContain(line => line.Contains(TokenIn(link!.Url), StringComparison.Ordinal));
    }

    [Fact, Trait("Spec", "0016:STP-3")]
    public async Task Concurrent_starts_print_one_banner_and_short_banners_for_the_rest()
    {
        (HttpClient Client, string Output)[] starts = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Task.Run(() => StartAsync(), Token)));

        string[] outputs = [.. starts.Select(start => start.Output)];
        outputs.Count(output => output.Contains("#token=", StringComparison.Ordinal)).ShouldBe(1, string.Join('|', outputs));
        outputs.Count(output => output.StartsWith("Orvano setup is waiting.", StringComparison.Ordinal)).ShouldBe(3, string.Join('|', outputs));
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

    [Fact, Trait("Spec", "0016:STP-6"), Trait("Spec", "0016:STP-1")]
    public async Task Check_answers_204_in_the_last_second_before_the_token_expires()
    {
        (HttpClient client, string output) = await StartAsync();
        _clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));

        using HttpResponseMessage response = await CheckAsync(client, TokenIn(output));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory, Trait("Spec", "0016:STP-6"), Trait("Spec", "0016:STP-7")]
    [InlineData("hash without an expiry")]
    [InlineData("expiry without a hash")]
    [InlineData("no row")]
    public async Task Check_answers_401_setup_token_invalid_when_the_row_is_not_whole(string state)
    {
        (HttpClient client, string output) = await StartAsync();
        await ExecuteAsync(state switch
        {
            "hash without an expiry" => "UPDATE platform.instance SET setup_token_expires_at = NULL",
            "expiry without a hash" => "UPDATE platform.instance SET setup_token_hash = NULL",
            _ => "DELETE FROM platform.instance",
        });

        using HttpResponseMessage response = await CheckAsync(client, TokenIn(output));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using JsonDocument problem = await ProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(PlatformErrorCodes.SetupTokenInvalid);
    }

    [Theory, Trait("Spec", "0016:STP-6"), Trait("Spec", "0016:STP-7")]
    [InlineData("\u0000")]
    [InlineData("orv_setup_\u0645\u0631\u062d\u0628\u0627")]
    [InlineData("\ud83d\ude00")]
    [InlineData("%00'; DROP TABLE platform.instance; --")]
    public async Task Check_answers_401_and_changes_nothing_when_the_token_is_not_token_shaped(string token)
    {
        (HttpClient client, _) = await StartAsync();
        byte[]? hash = await HashAsync();

        using HttpResponseMessage response = await CheckAsync(client, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await HashAsync()).ShouldBe(hash);
    }

    [Fact, Trait("Spec", "0016:STP-7")]
    public async Task Check_takes_a_100_character_token_and_refuses_101_as_too_long()
    {
        (HttpClient client, _) = await StartAsync();

        using HttpResponseMessage hundred = await CheckAsync(client, new string('a', 100));
        using HttpResponseMessage hundredAndOne = await CheckAsync(client, new string('a', 101));

        hundred.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        hundredAndOne.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using JsonDocument problem = await ProblemAsync(hundredAndOne);
        problem.RootElement.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe(FieldCodes.TooLong);
    }

    [Fact, Trait("Spec", "0016:STP-7"), Trait("Spec", "0014:HDR-5")]
    public async Task Check_sends_no_store_on_each_refusal()
    {
        (HttpClient client, string output) = await StartAsync();

        using HttpResponseMessage wrong = await CheckAsync(client, "orv_setup_wrong");
        using HttpResponseMessage invalid = await PostAsync(client, "{}", PublicUrl);
        using HttpResponseMessage foreign = await PostAsync(client, Body(TokenIn(output)), "http://evil.example");
        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");
        using HttpResponseMessage complete = await CheckAsync(client, TokenIn(output));

        foreach (HttpResponseMessage response in new[] { wrong, invalid, foreign, complete })
        {
            response.Headers.CacheControl?.NoStore.ShouldBeTrue(response.StatusCode.ToString());
        }
    }

    [Fact, Trait("Spec", "0016:STP-7")]
    public async Task Check_answers_503_unavailable_when_the_platform_schema_is_missing()
    {
        (HttpClient client, string output) = await StartAsync();
        await ExecuteAsync("DROP SCHEMA platform CASCADE");

        using HttpResponseMessage response = await CheckAsync(client, TokenIn(output));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using JsonDocument problem = await ProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.Unavailable);
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

    [Theory, Trait("Spec", "0016:STP-7"), Trait("Spec", "0014:PRB-3")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("\"orv_setup_x\"")]
    [InlineData("""{"token":123}""")]
    [InlineData("""{"token":{}}""")]
    [InlineData("""{"token":"\ud800"}""")]
    public async Task Check_answers_400_invalid_request_when_the_body_cannot_be_read(string body)
    {
        (HttpClient client, _) = await StartAsync();

        using HttpResponseMessage response = await PostAsync(client, body, PublicUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using JsonDocument problem = await ProblemAsync(response);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.InvalidRequest);
    }

    [Fact, Trait("Spec", "0016:STP-7"), Trait("Spec", "0014:PRB-3")]
    public async Task Check_answers_415_when_the_body_is_not_json()
    {
        (HttpClient client, _) = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, CheckPath)
        {
            Content = new StringContent(Body("orv_setup_x"), Encoding.UTF8, "text/plain"),
        };
        request.Headers.TryAddWithoutValidation("Origin", PublicUrl);

        using HttpResponseMessage response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await ProblemAsync(response)).Dispose();
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

    [Theory, Trait("Spec", "0006:CON-2")]
    [InlineData("http://localhost:8081")]
    [InlineData("http://localhost:8080/path")]
    [InlineData("http://localhost:8080?x=1")]
    [InlineData("http://user@localhost:8080")]
    [InlineData("http://localhost:8080, http://localhost:8080")]
    [InlineData("http://localhost.evil.example:8080")]
    [InlineData("localhost:8080")]
    [InlineData("ftp://localhost:8080")]
    public async Task Check_answers_403_when_the_origin_only_looks_like_the_console(string origin)
    {
        (HttpClient client, string output) = await StartAsync();

        using HttpResponseMessage response = await PostAsync(client, Body(TokenIn(output)), origin);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory, Trait("Spec", "0006:CON-2")]
    [InlineData("http://localhost:8080", "HTTP://LOCALHOST:8080")]
    [InlineData("https://orvano.example", "https://orvano.example:443")]
    [InlineData("https://orvano.example:443", "https://orvano.example")]
    [InlineData("https://orvano.example/base/", "https://orvano.example")]
    public async Task Check_compares_scheme_host_and_port_when_the_origin_is_the_console(string publicUrl, string origin)
    {
        (HttpClient client, string output) = await StartAsync(publicUrl: publicUrl);

        using HttpResponseMessage response = await PostAsync(client, Body(TokenIn(output)), origin);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Spec", "0006:CON-2")]
    public async Task Check_answers_403_when_the_request_has_two_origin_headers()
    {
        (HttpClient client, string output) = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, CheckPath)
        {
            Content = new StringContent(Body(TokenIn(output)), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Origin", [PublicUrl, PublicUrl]);

        using HttpResponseMessage response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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

    [Fact, Trait("Spec", "0016:STP-8")]
    public async Task Check_limits_each_client_address_on_its_own()
    {
        await StartAsync();
        OrvanoApiFactory factory = _factories[^1];

        await SpendTheLimitAsync(factory, from: "203.0.113.1");

        (await CheckFromAsync(factory, "203.0.113.1")).ShouldBe(StatusCodes.Status429TooManyRequests);
        (await CheckFromAsync(factory, "203.0.113.2")).ShouldBe(StatusCodes.Status401Unauthorized);
    }

    [Fact, Trait("Spec", "0016:STP-8"), Trait("Spec", "0006:NET-1")]
    public async Task Check_counts_forged_forwarded_addresses_as_one_client_when_no_proxy_is_trusted()
    {
        await StartAsync();
        OrvanoApiFactory factory = _factories[^1];

        for (int i = 0; i < 10; i++)
        {
            (await CheckFromAsync(factory, "203.0.113.1", forwardedFor: $"198.51.100.{i}")).ShouldBe(StatusCodes.Status401Unauthorized);
        }

        (await CheckFromAsync(factory, "203.0.113.1", forwardedFor: "198.51.100.200")).ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    [Fact, Trait("Spec", "0016:STP-8"), Trait("Spec", "0006:NET-1")]
    public async Task Check_limits_by_the_forwarded_address_when_a_trusted_proxy_sends_it()
    {
        const string caddy = "10.20.30.40";
        await StartAsync(trustedProxies: caddy);
        OrvanoApiFactory factory = _factories[^1];

        await SpendTheLimitAsync(factory, from: caddy, forwardedFor: "203.0.113.1");

        (await CheckFromAsync(factory, caddy, forwardedFor: "203.0.113.1")).ShouldBe(StatusCodes.Status429TooManyRequests);
        (await CheckFromAsync(factory, caddy, forwardedFor: "203.0.113.2")).ShouldBe(StatusCodes.Status401Unauthorized);
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

    [Fact, Trait("Spec", "0016:STP-5")]
    public async Task Admin_setup_token_exits_1_when_the_instance_row_is_missing()
    {
        await ExecuteAsync("DELETE FROM platform.instance");

        OrvanoCli.Result result = await AdminSetupTokenAsync();

        result.ExitCode.ShouldBe(1);
        result.Output.ShouldBeEmpty();
        result.Error.ShouldContain("Run `orvano migrate` first");
    }

    [Fact, Trait("Spec", "0016:STP-5")]
    public async Task Admin_setup_token_exits_1_naming_the_variable_and_keeps_the_token_when_the_public_url_is_bad()
    {
        OrvanoCli.Result first = await AdminSetupTokenAsync();
        byte[]? hash = await HashAsync();

        OrvanoCli.Result result = await AdminSetupTokenAsync(publicUrl: "ftp://orvano.example");

        first.ExitCode.ShouldBe(0, first.Error);
        result.ExitCode.ShouldBe(1);
        result.Output.ShouldBeEmpty();
        result.Error.ShouldContain("ORVANO_PUBLIC_URL");
        (await HashAsync()).ShouldBe(hash);
    }

    [Fact, Trait("Spec", "0016:STP-5"), Trait("Spec", "0002:CFG-3")]
    public async Task Admin_setup_token_reads_the_same_configuration_as_serve_when_the_password_is_in_a_file()
    {
        string password = Path.Combine(_console.ContentRoot, "database-password");
        await File.WriteAllTextAsync(password, PostgresFixture.Password, Token);
        var environment = postgres.DatabaseEnvironment.ToDictionary();
        environment["ORVANO_DATABASE__NAME"] = _database;
        environment.Remove("ORVANO_DATABASE__PASSWORD");
        environment["ORVANO_DATABASE__PASSWORD_FILE"] = password;

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["admin", "setup-token"], environment, Token);

        result.ExitCode.ShouldBe(0, result.Error);
        result.Output.ShouldContain("#token=orv_setup_");
    }

    // Takes about a minute: the EF Core retry backs off six times before it gives up.
    [Fact, Trait("Spec", "0016:STP-5")]
    public async Task Admin_setup_token_exits_1_without_a_stack_trace_when_the_database_is_unreachable()
    {
        var environment = postgres.DatabaseEnvironment.ToDictionary();
        environment["ORVANO_DATABASE__HOST"] = "127.0.0.1";
        environment["ORVANO_DATABASE__PORT"] = Ports.Free().ToString(CultureInfo.InvariantCulture);

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["admin", "setup-token"], environment, Token, timeLimit: TimeSpan.FromMinutes(3));

        result.ExitCode.ShouldBe(1);
        result.Output.ShouldBeEmpty();
        result.Error.ShouldContain("Could not make a setup link");
        result.Error.ShouldNotContain("Unhandled exception");
        result.Error.ShouldNotContain(PostgresFixture.Password);
    }

    // The real process: the banner goes through the real standard output, next to the real JSON logs (STP-4)

    [Fact, Trait("Spec", "0016:STP-4"), Trait("Spec", "0016:AC-6"), Trait("Spec", "0016:AC-7")]
    public async Task Serve_prints_the_banner_whole_and_writes_the_token_nowhere_else()
    {
        int port = Ports.Free();
        string url = $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}";
        var environment = postgres.DatabaseEnvironment.ToDictionary();
        environment["ORVANO_DATABASE__NAME"] = _database;
        environment["ORVANO_ROOT_KEY"] = TestRootKey.Create();
        environment["ORVANO_PUBLIC_URL"] = url;
        environment["ASPNETCORE_HTTP_PORTS"] = port.ToString(CultureInfo.InvariantCulture);
        var lines = new ConcurrentQueue<string>();
        using Process server = Process.Start(OrvanoCli.StartInfo(["serve"], environment)) ?? throw new InvalidOperationException("serve did not start");
        server.OutputDataReceived += (_, line) => lines.Enqueue(line.Data ?? string.Empty);
        server.ErrorDataReceived += (_, line) => lines.Enqueue(line.Data ?? string.Empty);
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();
        string token;
        try
        {
#pragma warning disable RS0030 // PRF-3, OUT-1: one client for one test's process, so there is no pool to protect
            using var client = new HttpClient { BaseAddress = new Uri(url) };
#pragma warning restore RS0030
            await WaitUntilReadyAsync(client, server);
            token = TokenIn(string.Join('\n', lines));

            (await PostAsync(client, Body(token), url)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await PostAsync(client, Body("orv_setup_wrong"), url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync(Token);
        }

        string everything = string.Join('\n', lines);
        Regex.Count(everything, Regex.Escape(token)).ShouldBe(1, everything);
        everything.ShouldContain(
            $"\n\nFinish setting up Orvano: {url}/console/setup#token={token}\nThis link works until setup is done or ", Case.Sensitive, everything);
    }

    // No outbound calls (TEL-1)

    [Fact, Trait("Spec", "0016:TEL-1"), Trait("Spec", "0016:AC-14")]
    public async Task Serve_calls_nothing_outside_during_start_and_the_setup_flow_when_no_otlp_endpoint_is_set()
    {
        var outbound = new OutboundCalls();

        (HttpClient client, string output) = await StartAsync(outbound: outbound);
        (await client.GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CheckAsync(client, TokenIn(output))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CheckAsync(client, "orv_setup_wrong")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        outbound.Sent.ShouldBeEmpty();
    }

    [Fact, Trait("Spec", "0016:TEL-1")]
    public async Task Outbound_calls_records_a_send_from_the_hosts_http_client_factory()
    {
        var outbound = new OutboundCalls();
        await StartAsync(outbound: outbound);
        using HttpClient http = _factories[^1].Services.GetRequiredService<IHttpClientFactory>().CreateClient();

        await Should.ThrowAsync<InvalidOperationException>(() => http.GetAsync(new Uri("https://orvano.example/update-check"), Token));

        outbound.Sent.ShouldBe([new Uri("https://orvano.example/update-check")]);
    }

    private async Task<(HttpClient Client, string Output)> StartAsync(
        string? roles = null, string? publicUrl = PublicUrl, string? consoleOrigin = null, OutboundCalls? outbound = null,
        string? trustedProxies = null)
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

        if (trustedProxies is not null)
        {
            settings["TRUSTED_PROXIES"] = trustedProxies;
        }

        if (outbound is not null)
        {
            settings["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;
        }

        var output = new StringWriter();
        var factory = new OrvanoApiFactory(settings, _console.ContentRoot, services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton(new SetupOutput(output));
            services.AddSingleton<ILoggerProvider>(_logs);
            if (outbound is not null)
            {
                services.AddHttpClient();
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(outbound);
            }
        });
        lock (_factories)
        {
            _factories.Add(factory);
        }

        HttpClient client = factory.CreateClient(new() { AllowAutoRedirect = false });
        return (client, output.ToString());
    }

    private Task<OrvanoCli.Result> AdminSetupTokenAsync(string? database = null, string publicUrl = PublicUrl)
    {
        var environment = postgres.DatabaseEnvironment.ToDictionary();
        environment["ORVANO_DATABASE__NAME"] = database ?? _database;
        environment["ORVANO_PUBLIC_URL"] = publicUrl;
        return OrvanoCli.RunAsync(["admin", "setup-token"], environment, Token);
    }

    private static async Task WaitUntilReadyAsync(HttpClient client, Process server)
    {
        for (int attempt = 0; attempt < 120 && !server.HasExited; attempt++)
        {
            try
            {
                using HttpResponseMessage ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
                if (ready.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(250, Token);
        }

        throw new InvalidOperationException("serve never answered /health/ready");
    }

    private static async Task SpendTheLimitAsync(OrvanoApiFactory factory, string from, string? forwardedFor = null)
    {
        for (int i = 0; i < 10; i++)
        {
            (await CheckFromAsync(factory, from, forwardedFor)).ShouldBe(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>A wrong token from <paramref name="from"/>, which only the test server can set (spec 0016 STP-8).</summary>
    private static async Task<int> CheckFromAsync(OrvanoApiFactory factory, string from, string? forwardedFor = null)
    {
        HttpContext context = await factory.Server.SendAsync(http =>
        {
            http.Request.Method = HttpMethods.Post;
            http.Request.Path = CheckPath.OriginalString;
            http.Request.ContentType = "application/json";
            http.Request.Headers.Origin = PublicUrl;
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Body("orv_setup_wrong")));
            http.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());
            http.Connection.RemoteIpAddress = IPAddress.Parse(from);
            if (forwardedFor is not null)
            {
                http.Request.Headers["X-Forwarded-For"] = forwardedFor;
            }
        }, Token);
        return context.Response.StatusCode;
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

    private Task<DateTimeOffset?> ExpiresAtAsync() => TimeAsync("setup_token_expires_at");

    private async Task<DateTimeOffset?> TimeAsync(string column) =>
        await ScalarAsync($"SELECT {column} FROM platform.instance") is DateTime at
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

    /// <summary>The test server says a request it did not make from an <see cref="HttpClient"/> has no body.</summary>
    private sealed class HasBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    /// <summary>Keeps every line that reaches <c>ILogger</c>, so a test can show what never did (spec 0016 STP-4).</summary>
    private sealed class RecordedLogs : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _lines.Enqueue($"{logLevel} {formatter(state, exception)} {exception}");

        public void Dispose()
        {
        }
    }

    /// <summary>Stands in for every factory made client's real handler: a send is recorded and fails (spec 0016 TEL-1).</summary>
    private sealed class OutboundCalls : IHttpMessageHandlerBuilderFilter
    {
        private readonly ConcurrentQueue<Uri?> _sent = new();

        public IReadOnlyCollection<Uri?> Sent => _sent;

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.PrimaryHandler = new FailingHandler(_sent);
        };

        private sealed class FailingHandler(ConcurrentQueue<Uri?> sent) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                sent.Enqueue(request.RequestUri);
                throw new InvalidOperationException($"Orvano tried to call {request.RequestUri}; spec 0016 TEL-1 allows no outbound call.");
            }
        }
    }
}
