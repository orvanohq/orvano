using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Auth;
using Orvano.Auth.Jobs;
using Orvano.Core.Data;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Core.Secrets;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0012's background work: the auth.apple.revoke job run as the worker runs it, against the fake provider the api
// serves (AC-15), and the hourly retention sweep of OAuth flows and used ID tokens (AC-19).
public class OAuthJobsTests(PostgresFixture postgres)
{
    private const string Nonce = "nonce-0123456789abcdef";
    private const string RevokeKind = "auth.apple.revoke";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_revoke_job_posts_the_token_to_apple_for_the_client_id_it_was_issued_to()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var job = await DeletedAppleUserJobAsync(api);
        using var logs = new ListLoggerProvider();
        await using var services = WorkerServices(api, FakeProviderUrl(api), logs);

        await RunAsync(job, services, attempt: 1);

        using var revocations = await api.SendAsync(HttpMethod.Get, "/v1/test/oauth/apple/revocations");
        var revocation = Assert.Single(revocations.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(AuthApi.AppleBundleId, revocation.GetProperty("clientId").GetString());
        Assert.DoesNotContain(logs.Logger.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.DoesNotContain("fake-refresh", logs.FullText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_apple_retries_until_the_last_attempt_then_only_warns()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var job = await DeletedAppleUserJobAsync(api);
        using var logs = new ListLoggerProvider();
        // Nothing listens on port 9, so every call fails to connect.
        await using var services = WorkerServices(api, "http://127.0.0.1:9/v1/test/oauth", logs);

        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(job, services, attempt: 1));
        await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(job, services, attempt: job.MaxAttempts - 1));
        await RunAsync(job, services, attempt: job.MaxAttempts);

        var warning = Assert.Single(logs.Logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(AuthApi.Project, warning.FullText, StringComparison.Ordinal);
        Assert.Contains(job.IdentityId, warning.FullText, StringComparison.Ordinal);
        Assert.Equal(8, job.MaxAttempts);
        Assert.DoesNotContain("fake-refresh", logs.FullText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_project_without_apple_settings_ends_the_job_with_a_warning_and_no_call()
    {
        await using var api = await AuthApi.StartAsync(postgres, oauth: true);
        var job = await DeletedAppleUserJobAsync(api);
        using (var deleted = await api.AsConsoleAsync(HttpMethod.Delete, "/v1/console/project/auth/providers/apple"))
            Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        using var logs = new ListLoggerProvider();
        await using var services = WorkerServices(api, FakeProviderUrl(api), logs);

        await RunAsync(job, services, attempt: 1);

        var warning = Assert.Single(logs.Logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(job.IdentityId, warning.FullText, StringComparison.Ordinal);
        using var revocations = await api.SendAsync(HttpMethod.Get, "/v1/test/oauth/apple/revocations");
        Assert.Empty(revocations.Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Retention_deletes_expired_oauth_flows_and_used_id_tokens_past_one_batch_and_keeps_live_ones()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        await TestDatabase.ExecuteAsync(database.Superuser,
            """
            INSERT INTO orvano.auth_oauth_flows (id, project_id, provider, purpose, state_hash, redirect_url, code_challenge, expires_at)
            VALUES (gen_random_uuid(), 'shop', 'google', 'sign_in', sha256('old'::bytea), 'https://app.example.com/cb', repeat('a', 43), now() - interval '1 second'),
                   (gen_random_uuid(), 'shop', 'github', 'sign_in', sha256('new'::bytea), 'https://app.example.com/cb', repeat('b', 43), now() + interval '10 minutes');
            INSERT INTO orvano.auth_id_token_uses (token_hash, project_id, expires_at)
            SELECT sha256(('used-' || i)::bytea), 'shop', now() - interval '1 second' FROM generate_series(1, 1001) AS i;
            INSERT INTO orvano.auth_id_token_uses (token_hash, project_id, expires_at)
            VALUES (sha256('live'::bytea), 'shop', now() + interval '1 hour');
            """);
        await using var app = NpgsqlDataSource.Create(database.AppUrl);

        var (_, _, tokens) = await AuthRetention.RunAsync(app, Ct);

        Assert.Equal(1 + 1001, tokens);
        Assert.Equal(["github"], await TestDatabase.ScalarAsync<string[]>(database.Superuser, "SELECT array_agg(provider) FROM orvano.auth_oauth_flows"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(database.Superuser,
            "SELECT count(*) FROM orvano.auth_id_token_uses WHERE token_hash = sha256('live'::bytea)"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(database.Superuser, "SELECT count(*) FROM orvano.auth_id_token_uses"));
    }

    /// <summary>A queued <c>auth.apple.revoke</c> job, as the worker would claim it.</summary>
    private sealed record QueuedJob(long Id, string Queue, string Kind, string? ProjectId, string Payload, int MaxAttempts, string IdentityId);

    /// <summary>Signs a user in natively with Apple (so a refresh token is stored), deletes them as a server, and returns the queued job.</summary>
    private static async Task<QueuedJob> DeletedAppleUserJobAsync(AuthApi api)
    {
        var hashed = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Nonce)));
        using var minted = await api.SendAsync(HttpMethod.Post, "/v1/test/oauth/id-tokens",
            new { provider = "apple", aud = AuthApi.AppleBundleId, sub = "a-revoke", email = (string?)null, emailVerified = false, nonce = hashed, expiresIn = 600 });
        using var signedIn = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/id-token", new
        {
            provider = "apple",
            idToken = minted.Body.GetProperty("idToken").GetString(),
            nonce = Nonce,
            authorizationCode = minted.Body.GetProperty("authorizationCode").GetString(),
        });
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        using (var deleted = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{AuthApi.UserId(signedIn)}"))
            Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        await using var conn = await api.Database.Superuser.OpenConnectionAsync(Ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id, queue, kind, project_id, payload::text, max_attempts FROM orvano.jobs WHERE kind = @kind", conn);
        cmd.Parameters.AddWithValue("kind", RevokeKind);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        var payload = reader.GetString(4);
        using var doc = JsonDocument.Parse(payload);
        return new QueuedJob(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), payload,
            reader.GetInt32(5), doc.RootElement.GetProperty("identityId").GetString()!);
    }

    /// <summary>Runs the job through the handler the Auth module registers, as the worker would on that attempt.</summary>
    private static async Task RunAsync(QueuedJob job, IServiceProvider services, int attempt)
    {
        var work = new WorkRegistry();
        new AuthModule().RegisterWork(work);
        var handler = work.HandlerFor(RevokeKind) ?? throw new InvalidOperationException("No handler for the revoke job.");
        await handler(new JobContext(new ClaimedJob(job.Id, job.Queue, job.Kind, job.ProjectId, job.Payload, attempt, job.MaxAttempts), services), Ct);
    }

    private static string FakeProviderUrl(AuthApi api) => new Uri(api.Http.BaseAddress!, "/v1/test/oauth").AbsoluteUri;

    /// <summary>The Auth module's services as the worker builds them, with the provider endpoints under <paramref name="providerUrl"/>.</summary>
    private static ServiceProvider WorkerServices(AuthApi api, string providerUrl, ListLoggerProvider logs)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AuthModule.TestProviderSetting] = providerUrl })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddKeyedSingleton(OrvanoDb.App, (_, _) => api.Database.Track(NpgsqlDataSource.Create(api.Database.AppUrl)));
        services.AddSingleton(new SecretBox(MasterKeys.Parse(OrvanoProcess.MasterKeys)));
        new AuthModule().ConfigureServices(services, config);
        return services.BuildServiceProvider();
    }
}
