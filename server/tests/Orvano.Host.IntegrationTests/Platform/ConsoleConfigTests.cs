using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Api;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0016 FRS-1, AC-5, and the data model: platform.instance is made with its one row by migrate, and
// GET /v1/console/config answers setupRequired from it. Each test gets its own migrated database.
public sealed class ConsoleConfigTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly ConsoleBuild _console = new();
    private OrvanoApiFactory? _factory;
    private string _database = string.Empty;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await postgres.CreateMigratedDatabaseAsync("platform", Token);

    public ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        _console.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact, Trait("Spec", "0016:FRS-1"), Trait("Spec", "0016:AC-5")]
    public async Task Config_answers_setup_required_without_caching_when_the_install_is_new()
    {
        using HttpResponseMessage response = await Client().GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldBe("""{"setupRequired":true}""");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    [Fact, Trait("Spec", "0016:FRS-1"), Trait("Spec", "0016:AC-5")]
    public async Task Config_answers_setup_not_required_when_setup_is_complete()
    {
        // Until feature 13 completes setup for real, the fixture sets the column (spec 0016 Value sourcing).
        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");

        using HttpResponseMessage response = await Client().GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token);

        (await response.Content.ReadAsStringAsync(Token)).ShouldBe("""{"setupRequired":false}""");
    }

    // Review R-1: only a finished setup is kept, because setup never reopens (spec 0016 State transitions).
    [Fact, Trait("Spec", "0016:FRS-1")]
    public async Task Config_answers_false_on_the_next_read_when_setup_completes_after_a_true()
    {
        HttpClient client = Client();
        (await ReadConfigAsync(client)).ShouldBe("""{"setupRequired":true}""");
        (await ReadConfigAsync(client)).ShouldBe("""{"setupRequired":true}""");

        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");

        (await ReadConfigAsync(client)).ShouldBe("""{"setupRequired":false}""");
    }

    // Feature 13 sets the column in the transaction that makes the owner (STP-6), with no call into this process.
    [Fact, Trait("Spec", "0016:FRS-1"), Trait("Spec", "0016:STP-6")]
    public async Task Config_answers_true_until_the_transaction_that_completes_setup_commits()
    {
        HttpClient client = Client();
        await using var data = NpgsqlDataSource.Create(postgres.ConnectionString(_database));
        await using NpgsqlConnection connection = await data.OpenConnectionAsync(Token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(Token);
        await using var complete = new NpgsqlCommand("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1", connection, transaction);
        await complete.ExecuteNonQueryAsync(Token);

        (await ReadConfigAsync(client)).ShouldBe("""{"setupRequired":true}""");

        await transaction.CommitAsync(Token);

        (await ReadConfigAsync(client)).ShouldBe("""{"setupRequired":false}""");
    }

    [Fact, Trait("Spec", "0016:FRS-1")]
    public async Task Config_keeps_false_without_the_database_when_it_has_read_a_finished_setup()
    {
        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");
        HttpClient client = Client();
        (await ReadConfigAsync(client)).ShouldBe("""{"setupRequired":false}""");

        await ExecuteAsync("DROP SCHEMA platform CASCADE");

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldBe("""{"setupRequired":false}""");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    [Fact, Trait("Spec", "0016:FRS-1")]
    public async Task Config_keeps_nothing_from_a_failed_read()
    {
        HttpClient client = Client();
        await ExecuteAsync("ALTER SCHEMA platform RENAME TO platform_away");

        using (HttpResponseMessage failed = await client.GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token))
        {
            failed.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        await ExecuteAsync("ALTER SCHEMA platform_away RENAME TO platform");
        (await ReadConfigAsync(client)).ShouldBe("""{"setupRequired":true}""");

        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");
        (await ReadConfigAsync(client)).ShouldBe("""{"setupRequired":false}""");
    }

    // More than one api replica may run, and nothing tells one that another saw setup finish.
    [Fact, Trait("Spec", "0016:FRS-1")]
    public async Task Config_finds_a_finished_setup_for_itself_on_each_replica()
    {
        HttpClient first = Client();
        using OrvanoApiFactory other = Factory();
        using HttpClient second = other.CreateClient(new() { AllowAutoRedirect = false });
        (await ReadConfigAsync(first)).ShouldBe("""{"setupRequired":true}""");
        (await ReadConfigAsync(second)).ShouldBe("""{"setupRequired":true}""");
        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");
        (await ReadConfigAsync(first)).ShouldBe("""{"setupRequired":false}""");

        // Nothing real clears the column; here it shows which replica still asks the database.
        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = NULL WHERE id = 1");
        (await ReadConfigAsync(first)).ShouldBe("""{"setupRequired":false}""");
        (await ReadConfigAsync(second)).ShouldBe("""{"setupRequired":true}""");

        await ExecuteAsync("UPDATE platform.instance SET setup_completed_at = now() WHERE id = 1");
        (await ReadConfigAsync(second)).ShouldBe("""{"setupRequired":false}""");
    }

    [Theory, Trait("Spec", "0016:FRS-1"), Trait("Spec", "0012:EPT-5")]
    [InlineData("Orvano-Key", "orv_pk_not_a_real_key")]
    [InlineData("Authorization", "Bearer not-a-token")]
    public async Task Config_answers_200_when_the_caller_sends_a_bad_credential(string header, string value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/console/config", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(header, value);

        using HttpResponseMessage response = await Client().SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact, Trait("Spec", "0016:FRS-1")]
    public async Task Config_answers_setup_required_when_the_instance_row_is_missing()
    {
        await ExecuteAsync("DELETE FROM platform.instance");

        using HttpResponseMessage response = await Client().GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token);

        (await response.Content.ReadAsStringAsync(Token)).ShouldBe("""{"setupRequired":true}""");
    }

    // STP-8's limit is on the check alone: a boot loads the config, so a reload must never be refused.
    [Fact, Trait("Spec", "0016:STP-8"), Trait("Spec", "0016:FRS-2")]
    public async Task Config_answers_200_past_the_token_checks_limit()
    {
        HttpClient client = Client();

        for (int i = 0; i < 12; i++)
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact, Trait("Spec", "0016:FRS-1")]
    public async Task Config_answers_503_unavailable_when_the_platform_schema_is_missing()
    {
        HttpClient client = Client();
        await ExecuteAsync("DROP SCHEMA platform CASCADE");

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        problem.RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.Unavailable);
    }

    [Fact, Trait("Spec", "0016:FRS-1")]
    public async Task Config_answers_404_in_the_worker_role()
    {
        using HttpResponseMessage response = await Client(roles: "worker").GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Spec", "0011:IDS-1"), Trait("Spec", "0002:DAT-10")]
    public async Task Migrate_creates_the_one_instance_row_with_setup_open_and_no_pending_model_changes()
    {
        // Read before a server starts: an api start issues a setup token (spec 0016 STP-3).
        long open = (long)(await ScalarAsync(
            "SELECT count(*) FROM platform.instance WHERE id = 1 AND setup_token_hash IS NULL AND setup_token_expires_at IS NULL "
            + "AND setup_completed_at IS NULL AND created_at IS NOT NULL"))!;

        Client();
        await using (AsyncServiceScope scope = _factory!.Services.CreateAsyncScope())
        {
            PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            db.Database.HasPendingModelChanges().ShouldBeFalse();
            (await db.Database.GetPendingMigrationsAsync(Token)).ShouldBeEmpty();
        }

        (await ScalarAsync("SELECT count(*) FROM platform.instance")).ShouldBe(1L);
        open.ShouldBe(1L);
    }

    [Fact, Trait("Spec", "0011:IDS-1")]
    public async Task Instance_refuses_a_second_row()
    {
        PostgresException error = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync("INSERT INTO platform.instance (id, created_at, updated_at) VALUES (2, now(), now())"));

        error.ConstraintName.ShouldBe("ck_instance_id");
    }

    [Fact, Trait("Spec", "0016:AC-9")]
    public async Task Migrate_again_keeps_the_instance_row_and_its_created_at()
    {
        object? before = await ScalarAsync("SELECT created_at FROM platform.instance");
        var environment = postgres.DatabaseEnvironment.ToDictionary();
        environment["ORVANO_DATABASE__NAME"] = _database;

        OrvanoCli.Result migrate = await OrvanoCli.RunAsync(["migrate"], environment, Token);

        migrate.ExitCode.ShouldBe(0, migrate.Error);
        (await ScalarAsync("SELECT created_at FROM platform.instance")).ShouldBe(before);
    }

    private HttpClient Client(string? roles = null)
    {
        _factory = Factory(roles);
        return _factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    private OrvanoApiFactory Factory(string? roles = null)
    {
        Dictionary<string, string> settings = OrvanoApiFactory.DefaultSettings(postgres);
        settings["Database:Name"] = _database;
        if (roles is not null)
        {
            settings["ROLES"] = roles;
        }

        return new OrvanoApiFactory(settings, _console.ContentRoot);
    }

    private static async Task<string> ReadConfigAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/console/config", UriKind.Relative), Token);
        return await response.Content.ReadAsStringAsync(Token);
    }

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
        return await command.ExecuteScalarAsync(Token);
    }
}
