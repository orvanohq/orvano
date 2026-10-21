using System.Diagnostics.Metrics;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.Host.IntegrationTests.Signals;
using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Outbox;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Outbox;

// Spec 0013 build plan step 25: publishing joins the caller's transaction and SaveChanges, refuses before it changes
// anything, and refuses a commit that left an event unsaved. One api host (no dispatcher) on its own database, with the
// sample outbox's tables made from its model; a raw LISTEN connection hears the job signals.
public sealed class OutboxPublishTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string JobSignal = """{"v":1,"k":"job","d":[]}""";

    private readonly ManualClock _clock = new();
    private OrvanoApiFactory _host = null!;
    private TestDatabase _database = null!;
    private OutboxRows _rows = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IOutbox Outbox => _host.Services.GetRequiredService<IOutbox>();

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string> settings = await SignalHosts.SettingsOnOwnDatabaseAsync(postgres, "publish", Token);
        _database = TestDatabase.For(postgres, settings);
        _rows = _database.OutboxRows();
        _host = OutboxHosts.Create(settings, new OutboxProbes(), _clock, roles: "api");
        await SampleOutboxDbContext.CreateTablesAsync(_host.Services, Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    [Trait("Spec", "0013:AC-32"), Trait("Spec", "0013:OTB-1"), Trait("Spec", "0013:OTB-2")]
    public async Task HasOutbox_creates_the_outbox_table_with_its_columns_checks_partial_index_and_storage_parameter()
    {
        await using NpgsqlCommand columns = _database.DataSource.CreateCommand(
            """
            SELECT column_name, data_type, is_nullable FROM information_schema.columns
            WHERE table_schema = 'sample' AND table_name = 'outbox' ORDER BY ordinal_position
            """);
        var found = new List<string>();
        await using (NpgsqlDataReader reader = await columns.ExecuteReaderAsync(Token))
        {
            while (await reader.ReadAsync(Token))
            {
                found.Add($"{reader.GetString(0)} {reader.GetString(1)} {reader.GetString(2)}");
            }
        }

        found.ShouldBe(
            [
                "id uuid NO",
                "dispatched_at timestamp with time zone YES",
                "occurred_at timestamp with time zone NO",
                "payload jsonb NO",
                "project_id uuid YES",
                "trace_parent text YES",
                "type text NO",
            ],
            ignoreOrder: true);
        (await ScalarAsync<string>(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_outbox_type' AND connamespace = 'sample'::regnamespace"))
            .ShouldContain("char_length(type) >= 3");
        (await ScalarAsync<string>(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_outbox_trace_parent' AND connamespace = 'sample'::regnamespace"))
            .ShouldContain("char_length(trace_parent) <= 55");
        (await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE schemaname = 'sample' AND indexname = 'ix_outbox_pending'"))
            .ShouldBe("CREATE INDEX ix_outbox_pending ON sample.outbox USING btree (id) WHERE (dispatched_at IS NULL)");
        (await ScalarAsync<string[]>("SELECT reloptions FROM pg_class WHERE oid = 'sample.outbox'::regclass"))
            .ShouldBe(["autovacuum_vacuum_scale_factor=0.05"]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-33"), Trait("Spec", "0013:PUB-1"), Trait("Spec", "0013:PUB-4"), Trait("Spec", "0013:AC-41")]
    public async Task Publish_and_its_change_are_seen_only_after_the_commit_with_one_job_signal_and_the_clock_time()
    {
        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());
        var note = Guid.CreateVersion7();
        var project = Guid.CreateVersion7();
        Guid first = Guid.Empty;
        long seenBeforeCommit = -1;

        await using (var db = SampleOutboxDbContext.On(_host.Services))
        {
            await db.InTransactionAsync(
                async token =>
                {
                    db.Notes.Add(new SampleNote { Id = note, Text = "hello" });
                    first = Outbox.Publish(db, new SampleNoteAdded(note), project);
                    Outbox.Publish(db, new SampleNoteChained(note), project);
                    await db.SaveChangesAsync(token);
                    seenBeforeCommit = await _rows.CountAsync("true", token) + await NotesAsync(token);
                },
                Token);
        }

        seenBeforeCommit.ShouldBe(0);
        (await NotesAsync(Token)).ShouldBe(1);
        IReadOnlyList<OutboxRows.Row> rows = await _rows.WhereAsync("true", Token);
        rows.Count.ShouldBe(2);
        OutboxRows.Row row = rows.Single(row => row.Id == first);
        row.Type.ShouldBe("sample.note.added.v1");
        JsonDocument.Parse(row.Payload).RootElement.GetProperty("NoteId").GetGuid().ShouldBe(note);
        row.ProjectId.ShouldBe(project);
        row.OccurredAt.ShouldBe(_clock.GetUtcNow());
        row.DispatchedAt.ShouldBeNull();
        IdTime(first).ShouldBe(_clock.GetUtcNow());
        (await tap.TakeAsync(Token)).ShouldBe([JobSignal]); // one per transaction, however many events (SND-4)
        meters.Sum("orvano.infrastructure.outbox.published", "type", "sample.note.added.v1").ShouldBe(1);
        meters.Sum("orvano.infrastructure.outbox.published").ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "0013:AC-33"), Trait("Spec", "0013:PUB-1"), Trait("Spec", "0013:AC-41")]
    public async Task Publish_in_a_rolled_back_transaction_leaves_no_row_no_signal_and_no_count()
    {
        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());

        await using (var db = SampleOutboxDbContext.On(_host.Services))
        {
            await Should.ThrowAsync<InvalidOperationException>(() => db.InTransactionAsync(
                async token =>
                {
                    Outbox.Publish(db, new SampleNoteAdded(Guid.CreateVersion7()), null);
                    await db.SaveChangesAsync(token);
                    throw new InvalidOperationException("the change fails");
                },
                Token));
        }

        (await _rows.CountAsync("true", Token)).ShouldBe(0);
        (await tap.TakeAsync(Token)).ShouldBeEmpty();
        meters.Sum("orvano.infrastructure.outbox.published").ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-33"), Trait("Spec", "0013:PUB-1"), Trait("Spec", "0013:SND-6")]
    public async Task Publish_retried_by_the_execution_strategy_leaves_exactly_one_row_with_the_last_id_and_one_signal()
    {
        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        int attempts = 0;
        var ids = new List<Guid>();

        await using (var db = SampleOutboxDbContext.On(_host.Services))
        {
            await db.InTransactionAsync(
                async token =>
                {
                    ids.Add(Outbox.Publish(db, new SampleNoteAdded(Guid.CreateVersion7()), null));
                    await db.SaveChangesAsync(token);
                    if (++attempts == 1)
                    {
                        throw new NpgsqlException("forced transient failure", new TimeoutException());
                    }
                },
                Token);
        }

        attempts.ShouldBe(2);
        (await _rows.WhereAsync("true", Token)).ShouldHaveSingleItem().Id.ShouldBe(ids[1]);
        (await tap.TakeAsync(Token)).ShouldBe([JobSignal]);
    }

    [Fact]
    [Trait("Spec", "0013:AC-34"), Trait("Spec", "0013:PUB-3")]
    public async Task A_transaction_that_publishes_and_commits_without_saving_fails_before_the_commit_and_sends_nothing()
    {
        await using SignalTap tap = await SignalTap.StartAsync(_database.DataSource, Token);
        using var meters = new MeterRecorder(_host.Services.GetRequiredService<IMeterFactory>());
        var note = Guid.CreateVersion7();

        await using (var db = SampleOutboxDbContext.On(_host.Services))
        {
            InvalidOperationException refused = await Should.ThrowAsync<InvalidOperationException>(() => db.InTransactionAsync(
                async token =>
                {
                    // The change is saved, the event is not.
                    await db.Database.ExecuteSqlAsync($"INSERT INTO sample.note (id, text) VALUES ({note}, 'kept?')", token);
                    Outbox.Publish(db, new SampleNoteAdded(note), null);
                },
                Token));
            refused.Message.ShouldBe(
                "An event published through IOutbox was not saved. Call SaveChangesAsync before the commit (spec 0013 PUB-3).");
        }

        (await NotesAsync(Token)).ShouldBe(0);
        (await _rows.CountAsync("true", Token)).ShouldBe(0);
        (await tap.TakeAsync(Token)).ShouldBeEmpty();
        meters.Sum("orvano.infrastructure.outbox.published").ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-34"), Trait("Spec", "0013:PUB-2"), Trait("Spec", "0013:SND-5")]
    public async Task Publish_refuses_without_a_transaction_on_a_joined_context_and_on_a_context_without_the_conventions()
    {
        await using var db = SampleOutboxDbContext.On(_host.Services);
        Should.Throw<InvalidOperationException>(() => Outbox.Publish(db, new SampleNoteAdded(Guid.CreateVersion7()), null))
            .Message.ShouldContain("InTransactionAsync");

        await using var plain = new SampleOutboxDbContext(new DbContextOptionsBuilder<SampleOutboxDbContext>()
            .UseNpgsql(_host.Services.GetRequiredService<NpgsqlDataSource>()).Options);
        Should.Throw<InvalidOperationException>(() => Outbox.Publish(plain, new SampleNoteAdded(Guid.CreateVersion7()), null))
            .Message.ShouldContain("Only platform database contexts");

        await db.InTransactionAsync(
            async token =>
            {
                await using var joined = SampleOutboxDbContext.On(_host.Services);
                joined.Database.SetDbConnection(db.Database.GetDbConnection());
                await joined.Database.UseTransactionAsync(db.Database.CurrentTransaction!.GetDbTransaction(), token);
                Should.Throw<InvalidOperationException>(() => Outbox.Publish(joined, new SampleNoteAdded(Guid.CreateVersion7()), null))
                    .Message.ShouldContain("UseTransaction");
                joined.ChangeTracker.Entries().ShouldBeEmpty();
            },
            Token);

        db.ChangeTracker.Entries().ShouldBeEmpty();
        (await _rows.CountAsync("true", Token)).ShouldBe(0);
    }

    [Fact]
    [Trait("Spec", "0013:AC-34"), Trait("Spec", "0013:PUB-2")]
    public async Task Publish_refuses_an_unregistered_type_another_modules_type_a_context_without_an_outbox_an_empty_project_and_a_large_payload()
    {
        await using (var db = SampleOutboxDbContext.On(_host.Services))
        {
            await db.InTransactionAsync(
                token =>
                {
                    Should.Throw<InvalidOperationException>(() => Outbox.Publish(db, new UnregisteredSampleEvent(Guid.CreateVersion7()), null))
                        .Message.ShouldBe("No outbox registers the event type 'sample.not.registered.v1'. Add it with services.AddOutbox (spec 0013 OBX-3).");
                    Should.Throw<ArgumentException>(() => Outbox.Publish(db, new SampleNoteAdded(Guid.CreateVersion7()), Guid.Empty))
                        .Message.ShouldContain("Guid.Empty");
                    Should.Throw<ArgumentException>(() => Outbox.Publish(db, new SampleNoteAdded(Guid.CreateVersion7(), new string('x', 70 * 1024)), null))
                        .Message.ShouldContain("64000 bytes");
                    db.ChangeTracker.Entries().ShouldBeEmpty();
                    return Task.CompletedTask;
                },
                Token);
        }

        // The other module's type is registered, but only its own module's context may publish it.
        await using OrvanoApiFactory other = OutboxHosts.Create(
            OrvanoApiFactory.DefaultSettings(postgres), new OutboxProbes(), _clock, roles: "api", services =>
                services.AddOutbox("other", outbox => outbox.Event(SampleOutboxJsonContext.Default.OtherModuleEvent)));
        await using (var db = SampleOutboxDbContext.On(other.Services))
        {
            await db.InTransactionAsync(
                token =>
                {
                    Should.Throw<InvalidOperationException>(() => other.Services.GetRequiredService<IOutbox>().Publish(db, new OtherModuleEvent(Guid.CreateVersion7()), null))
                        .Message.ShouldBe("Publish other.thing.happened.v1 on the other module's own DbContext (spec 0013 PUB-2).");
                    return Task.CompletedTask;
                },
                Token);
        }

        // A context in the right schema whose model maps no outbox.
        await using (var bare = new BareContext(new DbContextOptionsBuilder<BareContext>().UseOrvanoConventions("sample", _host.Services).Options))
        {
            await bare.InTransactionAsync(
                token =>
                {
                    Should.Throw<InvalidOperationException>(() => Outbox.Publish(bare, new SampleNoteAdded(Guid.CreateVersion7()), null))
                        .Message.ShouldBe("The sample context has no outbox table. Call modelBuilder.HasOutbox() in OnModelCreating (spec 0013 OTB-2).");
                    return Task.CompletedTask;
                },
                Token);
        }

        (await _rows.CountAsync("true", Token)).ShouldBe(0);
    }

    private static DateTimeOffset IdTime(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        long milliseconds = 0;
        for (int i = 0; i < 6; i++)
        {
            milliseconds = (milliseconds << 8) | bytes[i];
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    private async Task<long> NotesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = _database.DataSource.CreateCommand("SELECT count(*) FROM sample.note");
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using NpgsqlCommand command = _database.DataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync(Token))!;
    }

    /// <summary>A platform context in the sample schema that never called <c>HasOutbox()</c>.</summary>
    private sealed class BareContext(DbContextOptions<BareContext> options) : DbContext(options);
}
