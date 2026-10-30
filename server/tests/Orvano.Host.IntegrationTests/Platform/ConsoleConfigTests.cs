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
        Dictionary<string, string> settings = OrvanoApiFactory.DefaultSettings(postgres);
        settings["Database:Name"] = _database;
        if (roles is not null)
        {
            settings["ROLES"] = roles;
        }

        _factory = new OrvanoApiFactory(settings, _console.ContentRoot);
        return _factory.CreateClient(new() { AllowAutoRedirect = false });
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
