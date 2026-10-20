using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;
using Orvano.SharedKernel.Tests.Data;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Signals;

// Spec 0013 build plan step 2: a signal joins its transaction (SND-1 to SND-8), against PostgreSQL 18. The sample
// database is this class's own, so its LISTEN hears only these tests.
public sealed class TransactionSignalTests(SampleDatabase db) : IClassFixture<SampleDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "0013:AC-2"), Trait("Spec", "0013:SND-2"), Trait("Spec", "0013:SND-4")]
    public async Task Commit_sends_one_notification_per_kind_with_distinct_values_and_nothing_before()
    {
        await using SignalListenerProbe probe = await SignalListenerProbe.StartAsync(db.DataSource, Token);
        await using SampleContext context = db.CreateContext();
        List<string> beforeCommit = [];

        await context.InTransactionAsync(
            async token =>
            {
                TransactionSignals.Enlist(context, SignalKind.Cache, ["a", "b"]);
                TransactionSignals.Enlist(context, SignalKind.Session, ["s1"]);
                TransactionSignals.Enlist(context, SignalKind.Cache, ["b", "c", "a"]);
                TransactionSignals.Enlist(context, SignalKind.Job, []);
                await context.Gadgets.Where(x => x.Name == "none").ExecuteDeleteAsync(token);
                beforeCommit = await probe.TakeAsync(token);
            },
            Token);

        beforeCommit.ShouldBeEmpty();
        (await probe.TakeAsync(Token)).ShouldBe([
            """{"v":1,"k":"cache","d":["a","b","c"]}""",
            """{"v":1,"k":"session","d":["s1"]}""",
            """{"v":1,"k":"job","d":[]}""",
        ]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-2"), Trait("Spec", "0013:SND-3")]
    public async Task Rollback_sends_nothing_also_after_an_ExecuteUpdate_in_the_transaction()
    {
        Guid projectId = await db.AddProjectAsync();
        await using SignalListenerProbe probe = await SignalListenerProbe.StartAsync(db.DataSource, Token);
        await using SampleContext context = db.CreateContext();

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(Token);
            await context.Gadgets.Where(x => x.ProjectId == projectId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Name, "renamed"), Token);
            TransactionSignals.Enlist(context, SignalKind.Cache, ["rolled-back"]);
            await transaction.RollbackAsync(Token);
        });

        (await probe.TakeAsync(Token)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:AC-2")]
    public async Task Commit_sends_the_signal_when_the_change_was_raw_SQL()
    {
        Guid projectId = await db.AddProjectAsync();
        await using SignalListenerProbe probe = await SignalListenerProbe.StartAsync(db.DataSource, Token);
        await using SampleContext context = db.CreateContext();

        await context.InTransactionAsync(
            async token =>
            {
                await context.Database.ExecuteSqlAsync($"INSERT INTO sample.gadget (id, project_id, name) VALUES ({Guid.CreateVersion7()}, {projectId}, 'raw')", token);
                TransactionSignals.Enlist(context, SignalKind.Cache, [$"gadget:{projectId:N}"]);
            },
            Token);

        (await probe.TakeAsync(Token)).ShouldBe([$$"""{"v":1,"k":"cache","d":["gadget:{{projectId:N}}"]}"""]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-3"), Trait("Spec", "0013:SND-5")]
    public async Task Enlist_throws_naming_InTransactionAsync_when_the_context_has_no_transaction()
    {
        await using SampleContext context = db.CreateContext();

        InvalidOperationException error = Should.Throw<InvalidOperationException>(
            () => TransactionSignals.Enlist(context, SignalKind.Cache, ["t"]));

        error.Message.ShouldContain("InTransactionAsync");
    }

    [Fact]
    [Trait("Spec", "0013:AC-3"), Trait("Spec", "0013:SND-5")]
    public async Task Enlist_throws_naming_the_platform_database_when_the_context_has_no_Orvano_conventions()
    {
        await using var context = new PlainContext(new DbContextOptionsBuilder<PlainContext>().UseNpgsql(db.DataSource).Options);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(Token);

        InvalidOperationException error = Should.Throw<InvalidOperationException>(
            () => TransactionSignals.Enlist(context, SignalKind.Cache, ["t"]));

        error.Message.ShouldContain("Only platform database contexts send signals");
    }

    [Theory]
    [Trait("Spec", "0013:BUS-2")]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("tab\there")]
    [InlineData("café")]
    public async Task Enlist_throws_and_adds_nothing_when_a_value_is_not_printable_ASCII(string bad)
    {
        await using SignalListenerProbe probe = await SignalListenerProbe.StartAsync(db.DataSource, Token);
        await using SampleContext context = db.CreateContext();

        await context.InTransactionAsync(
            token =>
            {
                Should.Throw<ArgumentException>(() => TransactionSignals.Enlist(context, SignalKind.Cache, ["good", bad]));
                Should.Throw<ArgumentException>(() => TransactionSignals.Enlist(context, SignalKind.Cache, [new string('x', 201)]));
                TransactionSignals.Enlist(context, SignalKind.Cache, [new string('x', 200)]).ShouldBeTrue();
                return Task.CompletedTask;
            },
            Token);

        (await probe.TakeAsync(Token)).ShouldBe([$$"""{"v":1,"k":"cache","d":["{{new string('x', 200)}}"]}"""]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-3"), Trait("Spec", "0013:SND-8")]
    public async Task Synchronous_commit_throws_before_committing_when_signals_are_enlisted()
    {
        Guid projectId = await db.AddProjectAsync();
        await using SignalListenerProbe probe = await SignalListenerProbe.StartAsync(db.DataSource, Token);
        await using SampleContext context = db.CreateContext();

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(Token);
            await context.Database.ExecuteSqlAsync($"INSERT INTO sample.gadget (id, project_id, name) VALUES ({Guid.CreateVersion7()}, {projectId}, 'sync')", Token);
            TransactionSignals.Enlist(context, SignalKind.Cache, ["sync"]);

            InvalidOperationException error = Should.Throw<InvalidOperationException>(() => transaction.Commit());

            error.Message.ShouldContain("CommitAsync");
        });

        (await probe.TakeAsync(Token)).ShouldBeEmpty();
        (await context.Gadgets.AnyAsync(x => x.ProjectId == projectId, Token)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0013:AC-3"), Trait("Spec", "0013:SND-6")]
    public async Task InTransactionAsync_throws_when_the_context_already_has_a_transaction()
    {
        await using SampleContext context = db.CreateContext();

        await context.InTransactionAsync(
            async token => await Should.ThrowAsync<InvalidOperationException>(
                () => context.InTransactionAsync(_ => Task.CompletedTask, token)),
            Token);
    }

    [Fact]
    [Trait("Spec", "0013:AC-4"), Trait("Spec", "0013:SND-3"), Trait("Spec", "0013:SND-6")]
    public async Task Retry_on_the_same_connection_sends_only_the_values_of_the_attempt_that_commits()
    {
        await using SignalListenerProbe probe = await SignalListenerProbe.StartAsync(db.DataSource, Token);
        await using SampleContext context = db.CreateContext();
        await context.Database.OpenConnectionAsync(Token); // EF keeps a connection the caller opened, so both attempts share it
        List<int> backends = [];
        int attempts = 0;

        await context.InTransactionAsync(
            token =>
            {
                backends.Add(((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
                if (++attempts == 1)
                {
                    TransactionSignals.Enlist(context, SignalKind.Cache, ["a"]);
                    throw new NpgsqlException("forced transient failure", new TimeoutException());
                }

                TransactionSignals.Enlist(context, SignalKind.Cache, ["b"]);
                return Task.CompletedTask;
            },
            Token);

        attempts.ShouldBe(2);
        backends.Distinct().Count().ShouldBe(1);
        (await probe.TakeAsync(Token)).ShouldBe(["""{"v":1,"k":"cache","d":["b"]}"""]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-4"), Trait("Spec", "0013:SND-6")]
    public async Task Work_that_throws_is_rolled_back_and_leaves_no_batch_for_the_next_transaction()
    {
        await using SignalListenerProbe probe = await SignalListenerProbe.StartAsync(db.DataSource, Token);
        await using SampleContext context = db.CreateContext();

        await Should.ThrowAsync<InvalidOperationException>(() => context.InTransactionAsync(
            _ =>
            {
                TransactionSignals.Enlist(context, SignalKind.Cache, ["lost"]);
                throw new InvalidOperationException("work failed");
            },
            Token));
        context.Database.CurrentTransaction.ShouldBeNull();
        TransactionSignals.Enlisted(context, SignalKind.Cache).ShouldBeEmpty();

        await context.InTransactionAsync(_ => Task.CompletedTask, Token);

        (await probe.TakeAsync(Token)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:AC-2"), Trait("Spec", "0013:SND-1"), Trait("Spec", "0013:SND-2")]
    public async Task Each_module_context_sends_its_own_batch_on_its_own_commit_when_two_are_used_in_one_request()
    {
        await using SignalListenerProbe probe = await SignalListenerProbe.StartAsync(db.DataSource, Token);
        await using SampleContext first = db.CreateContext();
        await using var second = new OtherModuleContext(SampleContext.Options<OtherModuleContext>("other", db.DataSource, db.Clock));
        List<string> afterSecondCommit = [];
        IReadOnlyList<string> firstStillHolds = [];

        await first.InTransactionAsync(
            async token =>
            {
                TransactionSignals.Enlist(first, SignalKind.Cache, ["first"]);
                await second.InTransactionAsync(
                    _ =>
                    {
                        TransactionSignals.Enlist(second, SignalKind.Cache, ["second"]);
                        TransactionSignals.Enlist(second, SignalKind.Session, ["s"]);
                        return Task.CompletedTask;
                    },
                    token);
                afterSecondCommit = await probe.TakeAsync(token);
                firstStillHolds = [.. TransactionSignals.Enlisted(first, SignalKind.Cache)];
            },
            Token);

        afterSecondCommit.ShouldBe([
            """{"v":1,"k":"cache","d":["second"]}""",
            """{"v":1,"k":"session","d":["s"]}""",
        ]);
        firstStillHolds.ShouldBe(["first"]);
        (await probe.TakeAsync(Token)).ShouldBe(["""{"v":1,"k":"cache","d":["first"]}"""]);
    }

    [Fact]
    [Trait("Spec", "0013:SND-3")]
    public async Task Commit_stands_once_and_later_actions_still_run_when_an_after_commit_action_throws()
    {
        await using SignalListenerProbe probe = await SignalListenerProbe.StartAsync(db.DataSource, Token);
        await using SampleContext context = db.CreateContext();
        int attempts = 0;
        bool laterRan = false;

        await context.InTransactionAsync(
            _ =>
            {
                attempts++;
                TransactionSignals.Enlist(context, SignalKind.Cache, ["kept"]);
                TransactionSignals.AddAfterCommit(context, _ => throw new InvalidOperationException("action failed"));
                TransactionSignals.AddAfterCommit(context, _ =>
                {
                    laterRan = true;
                    return ValueTask.CompletedTask;
                });
                return Task.CompletedTask;
            },
            Token);

        attempts.ShouldBe(1); // a throwing action is not a failed commit, so the work is not run again
        laterRan.ShouldBeTrue();
        (await probe.TakeAsync(Token)).ShouldBe(["""{"v":1,"k":"cache","d":["kept"]}"""]);
    }

    [Fact]
    [Trait("Spec", "0013:SND-6")]
    public async Task Retry_clears_the_change_tracker_before_the_second_attempt()
    {
        Guid projectId = await db.AddProjectAsync();
        await using SampleContext context = db.CreateContext();
        int attempts = 0;

        await context.InTransactionAsync(
            async token =>
            {
                context.ChangeTracker.Entries().ShouldBeEmpty();
                context.Gadgets.Add(new Gadget { ProjectId = projectId, Code = $"retry{attempts}", Name = $"retry{attempts}" });
                if (++attempts == 1)
                {
                    throw new NpgsqlException("forced transient failure", new TimeoutException());
                }

                await context.SaveChangesAsync(token);
            },
            Token);

        (await context.Gadgets.CountAsync(x => x.ProjectId == projectId, Token)).ShouldBe(1);
    }

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options);

    // A second module's platform context, on its own schema and its own connection.
    private sealed class OtherModuleContext(DbContextOptions<OtherModuleContext> options) : DbContext(options);
}
