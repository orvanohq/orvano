using System.Diagnostics.Metrics;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.Host.IntegrationTests.Signals;
using Orvano.Infrastructure.Jobs.Data;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Jobs;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Jobs;

// Spec 0013 build plan step 15: enqueue joins the caller's transaction, refuses before any I/O, and has a standalone
// form. One api host (no runner), so every job stays queued; a raw LISTEN connection hears the job signals.
public sealed class JobQueueTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string JobSignal = """{"v":1,"k":"job","d":[]}""";

    private readonly JobProbes _probes = new();
    private OrvanoApiFactory _host = null!;
    private TestDatabase _database = null!;
    private JobRows _rows = null!;
    private Dictionary<string, string> _settings = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IJobQueue Queue => _host.Services.GetRequiredService<IJobQueue>();

    public async ValueTask InitializeAsync()
    {
        _settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "enqueue", Token);
        _database = TestDatabase.For(postgres, _settings);
        _rows = _database.Rows();
        await _database.ExecuteAsync("CREATE TABLE probe_note (id uuid PRIMARY KEY)", Token);
        _host = JobHosts.Create(_settings, _probes, clock: null, roles: "api");
        await _host.WaitUntilConnectedAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:AC-16"), Trait("Spec", "0013:ENQ-1"), Trait("Spec", "0013:ENQ-4"), Trait("Spec", "0013:AC-27")]
    public async Task Enqueue_in_a_transaction_is_seen_with_its_change_only_after_the_commit_and_sends_one_job_signal()
    {
        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());
        var note = Guid.CreateVersion7();
        Guid? first = null;
        long seenBeforeCommit = -1;

        await using (var db = ProbeContext.On(_host.Services))
        {
            await db.InTransactionAsync(
                async token =>
                {
                    await db.Database.ExecuteSqlAsync($"INSERT INTO probe_note VALUES ({note})", token);
                    first = await Queue.EnqueueAsync(db, new ProbeJob(ProbeMode.Ok), null, token);
                    await Queue.EnqueueAsync(db, new ProbeJob(ProbeMode.Ok), null, token);
                    seenBeforeCommit = await _rows.CountAsync("true", token) + await NotesAsync(token);
                },
                Token);
        }

        seenBeforeCommit.ShouldBe(0);
        (await NotesAsync(Token)).ShouldBe(1);
        JobRows.Row row = (await _rows.FindAsync(first!.Value, Token)).ShouldNotBeNull();
        row.Status.ShouldBe("queued");
        row.Attempt.ShouldBe(0);
        row.MaxAttempts.ShouldBe(10);
        row.Kind.ShouldBe("test.probe");
        (await _rows.CountAsync("true", Token)).ShouldBe(2);
        (await tap.TakeAsync(Token)).ShouldBe([JobSignal]); // one per transaction, however many jobs (ENQ-4)
        meters.Sum("orvano.infrastructure.jobs.enqueued", "kind", "test.probe").ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "0013:AC-16"), Trait("Spec", "0013:ENQ-1"), Trait("Spec", "0013:ENQ-5"), Trait("Spec", "0013:AC-27")]
    public async Task Enqueue_in_a_rolled_back_transaction_leaves_no_job_no_signal_and_no_count()
    {
        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());

        await using (var db = ProbeContext.On(_host.Services))
        {
            await Should.ThrowAsync<InvalidOperationException>(() => db.InTransactionAsync(
                async token =>
                {
                    await db.Database.ExecuteSqlAsync($"INSERT INTO probe_note VALUES ({Guid.CreateVersion7()})", token);
                    await Queue.EnqueueAsync(db, new ProbeJob(ProbeMode.Ok), null, token);
                    throw new InvalidOperationException("the change fails");
                },
                Token));
        }

        (await _rows.CountAsync("true", Token)).ShouldBe(0);
        (await NotesAsync(Token)).ShouldBe(0);
        (await tap.TakeAsync(Token)).ShouldBeEmpty();
        meters.Sum("orvano.infrastructure.jobs.enqueued").ShouldBe(0);
        (await JobsDriver.RunDueAsync(_host.Services, Token)).ShouldBeEmpty(); // nothing left behind to run
        _probes.Runs.ShouldBeEmpty();
    }

    [Theory]
    [Trait("Spec", "0013:AC-16"), Trait("Spec", "0013:ENQ-1"), Trait("Spec", "0013:JOB-5")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enqueue_racing_an_open_transaction_for_the_same_dedupe_key_waits_and_inserts_only_when_that_one_rolls_back(bool firstCommits)
    {
        var options = new EnqueueOptions { DedupeKey = "probe:race" };
        var inserted = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var db = ProbeContext.On(_host.Services);
        Task first = db.InTransactionAsync(
            async token =>
            {
                inserted.TrySetResult((await Queue.EnqueueAsync(db, new ProbeJob(ProbeMode.Ok), options, token))!.Value);
                await finish.Task.WaitAsync(token);
                if (!firstCommits)
                {
                    throw new InvalidOperationException("the first change fails");
                }
            },
            Token);
        Guid firstId = await inserted.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Task<Guid?> second = Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), options, Token);
        await Eventually.TrueAsync(async () => await WaitingInsertsAsync(Token) == 1, TimeSpan.FromSeconds(5), "the second insert did not wait", Token);
        second.IsCompleted.ShouldBeFalse();

        finish.SetResult();
        if (firstCommits)
        {
            await first;
        }
        else
        {
            await Should.ThrowAsync<InvalidOperationException>(first);
        }

        Guid? secondId = await second.WaitAsync(TimeSpan.FromSeconds(10), Token);
        IReadOnlyList<JobRows.Row> rows = await _rows.WhereAsync("dedupe_key = 'probe:race'", Token);
        if (firstCommits)
        {
            secondId.ShouldBeNull();
            rows.ShouldHaveSingleItem().Id.ShouldBe(firstId);
        }
        else
        {
            secondId.ShouldNotBeNull();
            rows.ShouldHaveSingleItem().Id.ShouldBe(secondId.Value);
        }
    }

    [Fact]
    [Trait("Spec", "0013:AC-16"), Trait("Spec", "0013:ENQ-1"), Trait("Spec", "0013:SND-6")]
    public async Task Enqueue_retried_by_the_execution_strategy_leaves_exactly_one_job_and_one_signal()
    {
        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        int attempts = 0;
        var ids = new List<Guid?>();

        await using (var db = ProbeContext.On(_host.Services))
        {
            await db.InTransactionAsync(
                async token =>
                {
                    ids.Add(await Queue.EnqueueAsync(db, new ProbeJob(ProbeMode.Ok), null, token));
                    if (++attempts == 1)
                    {
                        throw new NpgsqlException("forced transient failure", new TimeoutException());
                    }
                },
                Token);
        }

        attempts.ShouldBe(2);
        JobRows.Row row = (await _rows.WhereAsync("true", Token)).ShouldHaveSingleItem();
        row.Id.ShouldBe(ids[1]!.Value);
        (await tap.TakeAsync(Token)).ShouldBe([JobSignal]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-16"), Trait("Spec", "0013:ENQ-4")]
    public async Task Enqueue_of_a_future_job_writes_it_queued_at_its_time_and_sends_no_signal()
    {
        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        DateTimeOffset later = TimeProvider.System.GetUtcNow().AddHours(1).ToOffset(TimeSpan.FromHours(2));

        Guid? id = await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), new EnqueueOptions { RunAt = later }, Token);

        (await _rows.FindAsync(id!.Value, Token))!.RunAt.ShouldBe(later, TimeSpan.FromMilliseconds(1));
        (await tap.TakeAsync(Token)).ShouldBeEmpty();
    }

    [Theory]
    [Trait("Spec", "0013:AC-16"), Trait("Spec", "0013:ENQ-1"), Trait("Spec", "0013:AC-27")]
    [InlineData("queued")]
    [InlineData("succeeded")]
    [InlineData("dead")]
    public async Task Enqueue_with_a_taken_dedupe_key_returns_null_and_sends_nothing_whatever_the_first_jobs_status(string status)
    {
        // The same kind with no project on both: NULLS NOT DISTINCT makes them collide (JDB-2).
        var options = new EnqueueOptions { DedupeKey = "probe:once" };
        Guid first = (await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), options, Token))!.Value;
        if (status != "queued")
        {
            await _database.ExecuteAsync($"UPDATE jobs.job SET status = '{status}', finished_at = now() WHERE id = $1", Token, first);
        }

        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());

        Guid? second = await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), options, Token);

        second.ShouldBeNull();
        (await _rows.CountAsync("true", Token)).ShouldBe(1);
        (await tap.TakeAsync(Token)).ShouldBeEmpty();
        meters.Sum("orvano.infrastructure.jobs.enqueued").ShouldBe(0);
    }

    [Theory]
    [Trait("Spec", "0013:AC-16"), Trait("Spec", "0013:ENQ-1"), Trait("Spec", "0013:JDB-2")]
    [InlineData("other-kind")]
    [InlineData("other-project")]
    [InlineData("project-versus-none")]
    [InlineData("none-versus-project")]
    public async Task Enqueue_with_a_dedupe_key_taken_under_another_kind_or_project_inserts_when_the_key_is_scoped(string scope)
    {
        var project = Guid.CreateVersion7();
        var first = new EnqueueOptions { DedupeKey = "probe:scoped", ProjectId = scope == "none-versus-project" ? null : project };
        EnqueueOptions second = scope switch
        {
            "other-project" => first with { ProjectId = Guid.CreateVersion7() },
            "project-versus-none" => first with { ProjectId = null },
            "none-versus-project" => first with { ProjectId = project },
            _ => first,
        };
        (await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), first, Token)).ShouldNotBeNull();

        Guid? id = scope == "other-kind"
            ? await Queue.EnqueueAsync(new ShortJob(ProbeMode.Ok), second, Token)
            : await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), second, Token);

        id.ShouldNotBeNull();
        (await _rows.CountAsync("dedupe_key = 'probe:scoped'", Token)).ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "0013:AC-16"), Trait("Spec", "0013:ENQ-1"), Trait("Spec", "0013:JDB-2")]
    public async Task Enqueue_with_a_dedupe_key_taken_by_the_same_kind_and_project_returns_null()
    {
        var options = new EnqueueOptions { DedupeKey = "probe:project", ProjectId = Guid.CreateVersion7() };
        (await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), options, Token)).ShouldNotBeNull();

        (await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), options, Token)).ShouldBeNull();

        (await _rows.CountAsync("dedupe_key = 'probe:project'", Token)).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-16"), Trait("Spec", "0013:ENQ-1"), Trait("Spec", "0013:JDB-2")]
    public async Task Enqueue_without_a_dedupe_key_never_collides_with_or_without_a_project()
    {
        var project = new EnqueueOptions { ProjectId = Guid.CreateVersion7() };

        Guid?[] ids =
        [
            await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), null, Token),
            await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), null, Token),
            await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), project, Token),
            await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), project, Token),
        ];

        ids.ShouldAllBe(id => id != null);
        (await _rows.CountAsync("dedupe_key IS NULL", Token)).ShouldBe(4);
    }

    [Fact]
    [Trait("Spec", "0013:AC-17"), Trait("Spec", "0013:ENQ-2")]
    public async Task Enqueue_on_a_context_with_no_transaction_throws_naming_InTransactionAsync()
    {
        await using var db = ProbeContext.On(_host.Services);

        (await Should.ThrowAsync<InvalidOperationException>(() => Queue.EnqueueAsync(db, new ProbeJob(ProbeMode.Ok), null, Token)))
            .Message.ShouldContain("InTransactionAsync");
        (await _rows.CountAsync("true", Token)).ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-17"), Trait("Spec", "0013:ENQ-2")]
    public async Task Enqueue_on_a_context_without_the_conventions_throws_saying_only_platform_contexts_send_signals()
    {
        await using var db = new PlainContext(new DbContextOptionsBuilder<PlainContext>()
            .UseNpgsql(postgres.ConnectionString(_settings["Database:Name"])).Options);
        await using (await db.Database.BeginTransactionAsync(Token))
        {
            (await Should.ThrowAsync<InvalidOperationException>(() => Queue.EnqueueAsync(db, new ProbeJob(ProbeMode.Ok), null, Token)))
                .Message.ShouldContain("Only platform database contexts send signals");
        }

        (await _rows.CountAsync("true", Token)).ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-17"), Trait("Spec", "0013:ENQ-2"), Trait("Spec", "0013:SND-5")]
    public async Task Enqueue_on_a_context_that_joined_another_contexts_transaction_throws_naming_UseTransaction()
    {
        await using var owner = ProbeContext.On(_host.Services);
        await using var joined = ProbeContext.On(_host.Services);

        await owner.InTransactionAsync(
            async token =>
            {
                joined.Database.SetDbConnection(owner.Database.GetDbConnection());
                await joined.Database.UseTransactionAsync(owner.Database.CurrentTransaction!.GetDbTransaction(), token);
                (await Should.ThrowAsync<InvalidOperationException>(() => Queue.EnqueueAsync(joined, new ProbeJob(ProbeMode.Ok), null, token)))
                    .Message.ShouldContain("UseTransaction");
            },
            Token);

        (await _rows.CountAsync("true", Token)).ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-17"), Trait("Spec", "0013:ENQ-3")]
    public async Task Enqueue_of_a_kind_with_no_handler_throws_naming_the_kind_from_both_overloads()
    {
        await using var db = ProbeContext.On(_host.Services);

        (await Should.ThrowAsync<InvalidOperationException>(() => Queue.EnqueueAsync(new UnregisteredJob(), null, Token)))
            .Message.ShouldBe("No job handler is registered for kind 'test.unregistered' (spec 0013 JQ-5).");
        await db.InTransactionAsync(
            async token => await Should.ThrowAsync<InvalidOperationException>(() => Queue.EnqueueAsync(db, new UnregisteredJob(), null, token)),
            Token);
        (await _rows.CountAsync("true", Token)).ShouldBe(0);
    }

    [Theory]
    [Trait("Spec", "0013:AC-17"), Trait("Spec", "0013:ENQ-3")]
    [InlineData("payload")]
    [InlineData("dedupe-long")]
    [InlineData("dedupe-empty")]
    [InlineData("empty-project")]
    [InlineData("missing-project")]
    public async Task Enqueue_throws_an_ArgumentException_before_any_write_when_an_argument_is_out_of_range(string problem)
    {
        (IProbeJob job, EnqueueOptions? options) = problem switch
        {
            "payload" => ((IProbeJob)new ProbeJob(ProbeMode.Ok, new string('x', 70 * 1024)), (EnqueueOptions?)null),
            "dedupe-long" => (new ProbeJob(ProbeMode.Ok), new EnqueueOptions { DedupeKey = new string('k', 201) }),
            "dedupe-empty" => (new ProbeJob(ProbeMode.Ok), new EnqueueOptions { DedupeKey = "" }),
            "empty-project" => (new ProbeJob(ProbeMode.Ok), new EnqueueOptions { ProjectId = Guid.Empty }),
            _ => (new ProjectJob(ProbeMode.Ok), null),
        };
        await using var db = ProbeContext.On(_host.Services);

        // With a context too: the checks come before the context's, so it needs no transaction to refuse.
        if (job is ProbeJob probe)
        {
            await Should.ThrowAsync<ArgumentException>(() => Queue.EnqueueAsync(probe, options, Token));
            await Should.ThrowAsync<ArgumentException>(() => Queue.EnqueueAsync(db, probe, options, Token));
        }
        else
        {
            await Should.ThrowAsync<ArgumentException>(() => Queue.EnqueueAsync((ProjectJob)job, options, Token));
            await Should.ThrowAsync<ArgumentException>(() => Queue.EnqueueAsync(db, (ProjectJob)job, options, Token));
        }

        (await _rows.CountAsync("true", Token)).ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-18"), Trait("Spec", "0013:ENQ-6"), Trait("Spec", "0013:AC-27")]
    public async Task Enqueue_without_a_context_commits_its_own_transaction_and_sends_the_job_signal()
    {
        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());
        var project = Guid.CreateVersion7();

        Guid? id = await Queue.EnqueueAsync(new ProjectJob(ProbeMode.Ok), new EnqueueOptions { ProjectId = project, DedupeKey = "p:1" }, Token);

        JobRows.Row row = (await _rows.FindAsync(id!.Value, Token)).ShouldNotBeNull();
        row.ProjectId.ShouldBe(project);
        row.DedupeKey.ShouldBe("p:1");
        (await tap.TakeAsync(Token)).ShouldBe([JobSignal]);
        meters.Sum("orvano.infrastructure.jobs.enqueued", "kind", "test.project").ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:AC-18"), Trait("Spec", "0013:ENQ-6")]
    public async Task Enqueue_without_a_context_leaves_the_callers_open_transaction_untouched()
    {
        await using var db = ProbeContext.On(_host.Services);

        await Should.ThrowAsync<InvalidOperationException>(() => db.InTransactionAsync(
            async token =>
            {
                await db.Database.ExecuteSqlAsync($"INSERT INTO probe_note VALUES ({Guid.CreateVersion7()})", token);
                await Queue.EnqueueAsync(new ProbeJob(ProbeMode.Ok), null, token);
                throw new InvalidOperationException("the caller's change fails");
            },
            Token));

        (await NotesAsync(Token)).ShouldBe(0);
        (await _rows.CountAsync("true", Token)).ShouldBe(1); // its own transaction committed regardless (ENQ-6)
    }

    [Fact]
    [Trait("Spec", "0013:AC-15"), Trait("Spec", "0013:JDB-1"), Trait("Spec", "0013:JDB-2"), Trait("Spec", "0013:JDB-3")]
    public async Task Migrate_creates_the_jobs_tables_with_their_indexes_storage_parameters_and_no_pending_model_changes()
    {
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            JobsDbContext jobs = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
            jobs.Database.HasPendingModelChanges().ShouldBeFalse();
            (await jobs.Database.GetPendingMigrationsAsync(Token)).ShouldBeEmpty();
        }

        (await ScalarAsync("SELECT string_agg(indexname, ',' ORDER BY indexname) FROM pg_indexes WHERE schemaname = 'jobs'"))
            .ShouldBe("ix_job_due,ix_job_finished,ix_job_lease,ix_job_project,ix_schedule_due,pk___ef_migrations_history,pk_job,pk_schedule,ux_job_dedupe_key,ux_schedule_name");
        (await ScalarAsync("SELECT array_to_string(reloptions, ',') FROM pg_class WHERE oid = 'jobs.job'::regclass"))
            .ShouldBe("autovacuum_vacuum_scale_factor=0.05,fillfactor=70");
        (await ScalarAsync("SELECT string_agg(conname, ',' ORDER BY conname) FROM pg_constraint WHERE contype = 'c' AND conrelid = 'jobs.job'::regclass"))
            .ShouldBe("ck_job_attempt,ck_job_dedupe_key,ck_job_finished,ck_job_kind,ck_job_last_error,ck_job_lease,ck_job_locked_by,ck_job_max_attempts,ck_job_status,ck_job_trace_parent");
        (await ScalarAsync("SELECT string_agg(conname, ',' ORDER BY conname) FROM pg_constraint WHERE contype = 'c' AND conrelid = 'jobs.schedule'::regclass"))
            .ShouldBe("ck_schedule_cron,ck_schedule_name");
        (await ScalarAsync("SELECT pg_get_indexdef('jobs.ix_job_due'::regclass)")).ShouldEndWith("(run_at, id) WHERE (status = 'queued'::text)");
        (await ScalarAsync("SELECT pg_get_indexdef('jobs.ix_job_lease'::regclass)")).ShouldEndWith("(locked_until, id) WHERE (status = 'running'::text)");
        (await ScalarAsync("SELECT pg_get_indexdef('jobs.ux_job_dedupe_key'::regclass)"))
            .ShouldEndWith("(kind, project_id, dedupe_key) NULLS NOT DISTINCT WHERE (dedupe_key IS NOT NULL)");
        (await ScalarAsync(
            "SELECT string_agg(column_name || ':' || data_type, ',' ORDER BY ordinal_position) FROM information_schema.columns "
            + "WHERE table_schema = 'jobs' AND table_name = 'schedule'"))
            .ShouldBe("id:uuid,name:text,cron:text,enabled:boolean,next_run_at:timestamp with time zone,"
                + "last_fired_at:timestamp with time zone,created_at:timestamp with time zone,updated_at:timestamp with time zone");
        (await ScalarAsync("SELECT data_type FROM information_schema.columns WHERE table_schema = 'jobs' AND table_name = 'job' AND column_name = 'payload'"))
            .ShouldBe("jsonb");
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using NpgsqlCommand command = _database.DataSource.CreateCommand(sql);
        return await command.ExecuteScalarAsync(Token) as string;
    }

    /// <summary>Job inserts now blocked on another transaction's lock.</summary>
    private async Task<long> WaitingInsertsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = _database.DataSource.CreateCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%INSERT INTO jobs.job%'");
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<long> NotesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = _database.DataSource.CreateCommand("SELECT count(*) FROM probe_note");
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed record UnregisteredJob : IJob
    {
        public static string Kind => "test.unregistered";
    }

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options);
}
