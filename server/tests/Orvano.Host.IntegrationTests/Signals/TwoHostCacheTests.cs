using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Signals;

// Spec 0013 build plan step 5: two servers on one platform database, the path spec 0012 AC-10 reuses for the label
// cache. A write on host A invalidates a tag; A's entry is gone when the commit returns, B's within 5 seconds.
public sealed class TwoHostCacheTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeSpan Arrival = TimeSpan.FromSeconds(5); // spec 0013 AC-7

    private OrvanoApiFactory _a = null!;
    private OrvanoApiFactory _b = null!;
    private TestDatabase _database = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string> settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "twohost", Token);
        _database = TestDatabase.For(postgres, settings);
        await _database.ExecuteAsync("CREATE TABLE probe_value (id text PRIMARY KEY, value text NOT NULL)", Token);
        _a = new OrvanoApiFactory(settings);
        _b = new OrvanoApiFactory(settings);
        await _a.WaitUntilConnectedAsync(Token);
        await _b.WaitUntilConnectedAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _a.DisposeAsync();
        await _b.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:AC-6"), Trait("Spec", "0013:AC-7"), Trait("Spec", "0013:CAC-3"), Trait("Spec", "0013:CAC-4")]
    public async Task A_committed_invalidation_on_host_A_shows_the_new_value_on_A_at_once_and_on_B_within_5_seconds()
    {
        await _database.ExecuteAsync("INSERT INTO probe_value VALUES ('label', 'old')", Token);
        (await ReadAsync(_a, "label")).ShouldBe("old");
        (await ReadAsync(_b, "label")).ShouldBe("old");

        await using (var db = ProbeContext.On(_a.Services))
        {
            ICacheInvalidator invalidator = _a.Services.GetRequiredService<ICacheInvalidator>();
            await db.InTransactionAsync(
                async token =>
                {
                    await _database.ExecuteAsync("UPDATE probe_value SET value = 'new' WHERE id = 'label'", token);
                    invalidator.Invalidate(db, "probe:label");
                },
                Token);
        }

        (await ReadAsync(_a, "label")).ShouldBe("new");
        await Eventually.TrueAsync(async () => await ReadAsync(_b, "label") == "new", Arrival, "host B kept the old value", Token);
    }

    [Fact]
    [Trait("Spec", "0013:AC-5"), Trait("Spec", "0013:SND-7")]
    public async Task An_oversize_batch_clears_every_entry_on_the_other_host()
    {
        await _database.ExecuteAsync("INSERT INTO probe_value VALUES ('bystander', 'old')", Token);
        (await ReadAsync(_b, "bystander")).ShouldBe("old");
        await _database.ExecuteAsync("UPDATE probe_value SET value = 'new' WHERE id = 'bystander'", Token);

        await using (var db = ProbeContext.On(_a.Services))
        {
            ICacheInvalidator invalidator = _a.Services.GetRequiredService<ICacheInvalidator>();
            await db.InTransactionAsync(
                _ =>
                {
                    invalidator.Invalidate(db, Enumerable.Range(0, 60).Select(i => $"tag:{i:D32}"));
                    return Task.CompletedTask;
                },
                Token);
        }

        await Eventually.TrueAsync(async () => await ReadAsync(_b, "bystander") == "new", Arrival, "host B was not cleared", Token);
    }

    [Fact]
    [Trait("Spec", "0013:AC-5"), Trait("Spec", "0013:LSN-6")]
    public async Task A_malformed_or_unknown_payload_is_ignored_and_the_next_good_one_applies()
    {
        await _database.ExecuteAsync("INSERT INTO probe_value VALUES ('after-garbage', 'old')", Token);
        (await ReadAsync(_b, "after-garbage")).ShouldBe("old");
        await _database.ExecuteAsync("UPDATE probe_value SET value = 'new' WHERE id = 'after-garbage'", Token);

        await _database.NotifyAsync("not json", Token);
        await _database.NotifyAsync("""{"v":2,"k":"cache","d":["probe:after-garbage"]}""", Token);
        await _database.NotifyAsync("""{"v":1,"k":"later","d":["probe:after-garbage"]}""", Token);
        await _database.NotifyAsync("""{"v":1,"k":"cache","d":[]}""", Token);
        await Task.Delay(300, Token);
        (await ReadAsync(_b, "after-garbage")).ShouldBe("old");

        await _database.NotifyAsync("""{"v":1,"k":"cache","d":["probe:after-garbage"]}""", Token);

        await Eventually.TrueAsync(async () => await ReadAsync(_b, "after-garbage") == "new", Arrival, "the good signal did not apply", Token);
        _b.Services.GetRequiredService<ISignalConnectionState>().IsConnected.ShouldBeTrue();
    }

    // Reads through the host's own HybridCache, as a module would, tagged "probe:<id>".
    private async Task<string> ReadAsync(OrvanoApiFactory host, string id) =>
        await host.Services.GetRequiredService<HybridCache>().GetOrCreateAsync(
            $"probe:{id}",
            async token =>
            {
                await using Npgsql.NpgsqlCommand command = _database.DataSource.CreateCommand("SELECT value FROM probe_value WHERE id = $1");
                command.Parameters.AddWithValue(id);
                return (string)(await command.ExecuteScalarAsync(token))!;
            },
            tags: [$"probe:{id}"],
            cancellationToken: Token);
}
