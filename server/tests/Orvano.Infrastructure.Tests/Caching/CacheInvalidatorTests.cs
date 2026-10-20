using System.Data.Common;
using System.Diagnostics.Metrics;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.Infrastructure.Caching;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;
using Orvano.Testing;

using Shouldly;

using Xunit;

namespace Orvano.Infrastructure.Tests.Caching;

// Spec 0013 build plan step 4 and 6: the writer's own removal after the commit (CAC-3, SND-3, AC-6), against
// PostgreSQL 18 on a database of this class's own.
public sealed class CacheInvalidatorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Sent = "orvano.infrastructure.signals.sent";

    private NpgsqlDataSource _dataSource = null!;
    private ServiceProvider _services = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HybridCache Cache => _services.GetRequiredKeyedService<HybridCache>(SignalAwareHybridCache.InnerKey);

    private ICacheInvalidator Invalidator => _services.GetRequiredService<ICacheInvalidator>();

    public async ValueTask InitializeAsync()
    {
        string database = await postgres.CreateDatabaseAsync("invalidator", Token);
        _dataSource = NpgsqlDataSource.Create(postgres.ConnectionString(database));
        _services = new ServiceCollection()
            .AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .AddLogging()
            .AddSingleton(_dataSource)
            .AddInfrastructure()
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _dataSource.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:CAC-3"), Trait("Spec", "0013:AC-6")]
    public async Task Tags_are_removed_after_the_commit_and_before_CommitAsync_returns_never_before_it()
    {
        await SeedAsync("t1", "kept");
        await using ProbeContext db = Context();
        int seenInside = 0;

        await db.InTransactionAsync(
            async token =>
            {
                Invalidator.Invalidate(db, "t1");
                seenInside = await ReadAsync("t1", 99);
            },
            Token);

        seenInside.ShouldBe(1);
        (await ReadAsync("t1", 2)).ShouldBe(2);
        (await ReadAsync("kept", 2)).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-6")]
    public async Task Rollback_removes_nothing()
    {
        await SeedAsync("t2");
        await using ProbeContext db = Context();

        await Should.ThrowAsync<InvalidOperationException>(() => db.InTransactionAsync(
            _ =>
            {
                Invalidator.Invalidate(db, "t2");
                throw new InvalidOperationException("roll back");
            },
            Token));

        (await ReadAsync("t2", 2)).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-4"), Trait("Spec", "0013:SND-6"), Trait("Spec", "0013:LSN-7")]
    public async Task Retry_removes_only_the_tags_of_the_attempt_that_commits_and_counts_one_sent_when_the_first_attempt_fails()
    {
        await SeedAsync("first-try", "second-try");
        using var meters = new MeterRecorder(_services.GetRequiredService<IMeterFactory>());
        await using ProbeContext db = Context();
        int attempts = 0;

        await db.InTransactionAsync(
            _ =>
            {
                if (++attempts == 1)
                {
                    Invalidator.Invalidate(db, "first-try");
                    throw new NpgsqlException("forced transient failure", new TimeoutException());
                }

                Invalidator.Invalidate(db, "second-try");
                return Task.CompletedTask;
            },
            Token);

        attempts.ShouldBe(2);
        (await ReadAsync("first-try", 2)).ShouldBe(1);
        (await ReadAsync("second-try", 2)).ShouldBe(2);
        meters.Sum(Sent).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:SND-3"), Trait("Spec", "0013:AC-6")]
    public async Task Tags_are_removed_when_the_commit_fails_with_an_unknown_outcome()
    {
        await SeedAsync("t3");
        await using var db = new ProbeContext(new DbContextOptionsBuilder<ProbeContext>()
            .AddInterceptors(new KillBackendOnCommit(_dataSource))
            .UseOrvanoConventions("probe", _services, npgsql => npgsql.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d)))
            .Options);

        await using (IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(Token))
        {
            Invalidator.Invalidate(db, "t3");
            await Should.ThrowAsync<Exception>(() => transaction.CommitAsync(Token));
        }

        (await ReadAsync("t3", 2)).ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "0013:SND-7"), Trait("Spec", "0013:AC-5")]
    public async Task Every_entry_is_removed_when_the_batch_collapses_to_the_star()
    {
        await SeedAsync("other");
        await using ProbeContext db = Context();

        await db.InTransactionAsync(
            _ =>
            {
                Invalidator.Invalidate(db, Enumerable.Range(0, 60).Select(i => $"tag:{i:D32}"));
                return Task.CompletedTask;
            },
            Token);

        (await ReadAsync("other", 2)).ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "0013:LSN-7"), Trait("Spec", "0013:AC-13")]
    public async Task Sent_counts_one_per_kind_for_a_committed_transaction_and_nothing_for_a_rolled_back_one()
    {
        using var meters = new MeterRecorder(_services.GetRequiredService<IMeterFactory>());
        await using ProbeContext db = Context();
        ISignalBus bus = _services.GetRequiredService<ISignalBus>();

        await db.InTransactionAsync(
            _ =>
            {
                Invalidator.Invalidate(db, "a", "b");
                Invalidator.Invalidate(db, "c");
                bus.Enlist(db, SignalKind.Session, ["s"]);
                return Task.CompletedTask;
            },
            Token);
        await Should.ThrowAsync<InvalidOperationException>(() => db.InTransactionAsync(
            _ =>
            {
                Invalidator.Invalidate(db, "d");
                throw new InvalidOperationException("roll back");
            },
            Token));

        meters.Sum(Sent).ShouldBe(2);
        meters.Sum(Sent, "kind", "cache").ShouldBe(1);
        meters.Sum(Sent, "kind", "session").ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-3")]
    public async Task Invalidate_throws_and_sends_nothing_when_there_is_no_transaction()
    {
        await SeedAsync("t4");
        await using ProbeContext db = Context();

        Should.Throw<InvalidOperationException>(() => Invalidator.Invalidate(db, "t4")).Message.ShouldContain("InTransactionAsync");

        (await ReadAsync("t4", 2)).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:BUS-2"), Trait("Spec", "0013:AC-6")]
    public async Task Bus_enlist_throws_and_enlists_nothing_when_the_kind_is_cache()
    {
        await using ProbeContext db = Context();
        ISignalBus bus = _services.GetRequiredService<ISignalBus>();
        IReadOnlyList<string> enlisted = ["not checked"];

        await db.InTransactionAsync(
            _ =>
            {
                Should.Throw<ArgumentException>(() => bus.Enlist(db, SignalKind.Cache, ["t5"])).Message.ShouldContain("ICacheInvalidator");
                enlisted = [.. TransactionSignals.Enlisted(db, SignalKind.Cache)];
                return Task.CompletedTask;
            },
            Token);

        enlisted.ShouldBeEmpty();
    }

    private ProbeContext Context() =>
        new(new DbContextOptionsBuilder<ProbeContext>().UseOrvanoConventions("probe", _services).Options);

    private async Task SeedAsync(params string[] tags)
    {
        foreach (string tag in tags)
        {
            await Cache.SetAsync($"entry:{tag}", 1, tags: [tag], cancellationToken: Token);
        }
    }

    private ValueTask<int> ReadAsync(string tag, int fresh) =>
        Cache.GetOrCreateAsync($"entry:{tag}", _ => ValueTask.FromResult(fresh), cancellationToken: Token);

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options);

    /// <summary>Kills the committing connection's backend just before COMMIT is sent, so the commit's outcome is unknown.</summary>
    private sealed class KillBackendOnCommit(NpgsqlDataSource dataSource) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            int backend = ((NpgsqlConnection)transaction.Connection!).ProcessID;
            await using NpgsqlCommand command = dataSource.CreateCommand("SELECT pg_terminate_backend($1, 5000)");
            command.Parameters.AddWithValue(backend);
            await command.ExecuteScalarAsync(cancellationToken);
            return result;
        }
    }
}
