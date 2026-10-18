using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

using Orvano.SharedKernel.Data;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Data;

// Spec 0011 build plan step 3: the conventions against PostgreSQL 18 (spec 0002 TST-5).
public sealed class PostgresConventionTests(SampleDatabase db) : IClassFixture<SampleDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "0011:AC-1")]
    public async Task Id_is_set_before_save_and_stored_as_is_when_inserted()
    {
        Guid projectId = await db.AddProjectAsync();
        var widget = new Widget { ProjectId = projectId, Email = "a@example.com", EmailNormalized = Unique("a") };
        Guid before = widget.Id;

        await using (SampleContext context = db.CreateContext())
        {
            context.Widgets.Add(widget);
            await context.SaveChangesAsync(Token);
        }

        before.Version.ShouldBe(7);
        await using SampleContext read = db.CreateContext();
        (await read.Widgets.SingleAsync(x => x.Id == before, Token)).Id.ShouldBe(before);
    }

    [Fact]
    [Trait("Spec", "0011:AC-1")]
    public async Task Ids_sort_by_their_timestamp_in_PostgreSQL_when_made_1_ms_or_more_apart()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Guid[] ids = [.. Enumerable.Range(0, 50).Select(i => Guid.CreateVersion7(start.AddMilliseconds(i)))];
        Guid[] shuffled = [.. ids.OrderBy(id => id.GetHashCode())];

        await using NpgsqlCommand command = db.DataSource.CreateCommand("SELECT id FROM unnest($1) AS t(id) ORDER BY id");
        command.Parameters.AddWithValue(shuffled);
        var sorted = new List<Guid>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            sorted.Add(reader.GetGuid(0));
        }

        sorted.ShouldBe(ids);
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Timestamps_come_from_the_clock_and_created_at_is_kept_when_inserted_then_updated()
    {
        Guid projectId = await db.AddProjectAsync();
        DateTimeOffset inserted = db.Clock.Now = new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);
        var widget = new Widget
        {
            ProjectId = projectId,
            Email = "b@example.com",
            EmailNormalized = Unique("b"),
            CreatedAt = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        await using (SampleContext context = db.CreateContext())
        {
            context.Widgets.Add(widget);
            await context.SaveChangesAsync(Token);
            widget.CreatedAt.ShouldBe(inserted);
            widget.UpdatedAt.ShouldBe(inserted);
        }

        DateTimeOffset updated = db.Clock.Now = inserted.AddMinutes(5);
        await using (SampleContext context = db.CreateContext())
        {
            Widget loaded = await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
            loaded.Kind = WidgetKind.VerifyOnly;
            loaded.CreatedAt = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await context.SaveChangesAsync(Token);
            loaded.CreatedAt.ShouldBe(inserted);
        }

        await using SampleContext read = db.CreateContext();
        Widget stored = await read.Widgets.AsNoTracking().SingleAsync(x => x.Id == widget.Id, Token);
        stored.CreatedAt.ShouldBe(inserted);
        stored.UpdatedAt.ShouldBe(updated);
        stored.Kind.ShouldBe(WidgetKind.VerifyOnly);
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Timestamps_come_from_the_clock_when_saved_without_async()
    {
        Guid projectId = await db.AddProjectAsync();
        DateTimeOffset inserted = db.Clock.Now = new DateTimeOffset(2026, 10, 18, 11, 0, 0, TimeSpan.Zero);
        var widget = new Widget { ProjectId = projectId, Email = "s@example.com", EmailNormalized = Unique("s") };
        await using SampleContext context = db.CreateContext();

        context.Widgets.Add(widget);
        context.SaveChanges();
        DateTimeOffset updated = db.Clock.Now = inserted.AddMinutes(1);
        widget.Kind = WidgetKind.Es256;
        context.SaveChanges();

        widget.CreatedAt.ShouldBe(inserted);
        widget.UpdatedAt.ShouldBe(updated);
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public async Task Timestamps_are_set_before_another_interceptor_runs_when_saving()
    {
        Guid projectId = await db.AddProjectAsync();
        var probe = new ProbeInterceptor();
        await using SampleContext context = db.CreateContext(builder => builder.AddInterceptors(probe));

        context.Widgets.Add(new Widget { ProjectId = projectId, Email = "c@example.com", EmailNormalized = Unique("c") });
        await context.SaveChangesAsync(Token);

        probe.SeenCreatedAt.ShouldBe(db.Clock.Now);
    }

    [Fact]
    [Trait("Spec", "0002:DAT-9")]
    public async Task Second_save_throws_a_concurrency_error_when_the_row_changed_since_it_was_read()
    {
        Guid projectId = await db.AddProjectAsync();
        var widget = new Widget { ProjectId = projectId, Email = "d@example.com", EmailNormalized = Unique("d") };
        await using (SampleContext context = db.CreateContext())
        {
            context.Widgets.Add(widget);
            await context.SaveChangesAsync(Token);
            widget.Version.ShouldNotBe(0u);
        }

        await using SampleContext first = db.CreateContext();
        await using SampleContext second = db.CreateContext();
        Widget a = await first.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
        Widget b = await second.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
        a.Kind = WidgetKind.Es256;
        await first.SaveChangesAsync(Token);
        b.Kind = WidgetKind.VerifyOnly;

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
    }

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public async Task Text_enum_check_rejects_an_unknown_value_when_inserted_with_raw_SQL()
    {
        Guid projectId = await db.AddProjectAsync();

        await db.ExecuteAsync(Insert(projectId, "verify_only"));
        PostgresException error = await Should.ThrowAsync<PostgresException>(() => db.ExecuteAsync(Insert(projectId, "VerifyOnly")));

        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.ShouldBe("ck_widget_kind");

        static string Insert(Guid project, string kind) =>
            $"INSERT INTO sample.widget (id, project_id, email, email_normalized, kind, created_at, updated_at) "
            + $"VALUES ('{Guid.CreateVersion7()}', '{project}', 'e', '{Unique("e")}', '{kind}', now(), now())";
    }

    [Fact]
    [Trait("Spec", "0011:AC-5")]
    public async Task Deleting_the_anchor_project_cascades_and_deleting_the_operator_sets_null()
    {
        Guid projectId = await db.AddProjectAsync();
        var operatorId = Guid.CreateVersion7();
        await db.ExecuteAsync($"INSERT INTO platform.operator (id) VALUES ('{operatorId}')");
        var widget = new Widget
        {
            ProjectId = projectId,
            CreatedByOperatorId = operatorId,
            Email = "f@example.com",
            EmailNormalized = Unique("f"),
            Secret = new EncryptedValue(new byte[] { 1, 2, 3 }),
        };
        await using (SampleContext context = db.CreateContext())
        {
            context.Widgets.Add(widget);
            await context.SaveChangesAsync(Token);
        }

        await db.ExecuteAsync($"DELETE FROM platform.operator WHERE id = '{operatorId}'");
        await using (SampleContext context = db.CreateContext())
        {
            Widget stored = await context.Widgets.AsNoTracking().SingleAsync(x => x.Id == widget.Id, Token);
            stored.CreatedByOperatorId.ShouldBeNull();
            stored.Secret.ShouldBe(new EncryptedValue(new byte[] { 1, 2, 3 }));
        }

        await db.ExecuteAsync($"DELETE FROM platform.project WHERE id = '{projectId}'");
        await using SampleContext read = db.CreateContext();
        (await read.Widgets.AnyAsync(x => x.Id == widget.Id, Token)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0011:AC-11")]
    public async Task Nulls_not_distinct_index_rejects_a_duplicate_that_differs_only_by_a_null()
    {
        Guid projectId = await db.AddProjectAsync();
        await using SampleContext context = db.CreateContext();
        context.Gadgets.Add(new Gadget { ProjectId = projectId, Code = null, Name = "one" });
        await context.SaveChangesAsync(Token);

        context.Gadgets.Add(new Gadget { ProjectId = projectId, Code = null, Name = "two" });
        DbUpdateException error = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));

        PostgresException inner = error.InnerException.ShouldBeOfType<PostgresException>();
        inner.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        inner.ConstraintName.ShouldBe("ix_gadget_project_id_code");
    }

    [Fact]
    [Trait("Spec", "0011:AC-11")]
    public async Task Partial_unique_index_allows_a_duplicate_outside_its_filter_and_rejects_one_inside()
    {
        Guid projectId = await db.AddProjectAsync();
        await using SampleContext context = db.CreateContext();
        context.Gadgets.Add(new Gadget { ProjectId = projectId, Code = "a", Name = "key", RevokedAt = db.Clock.Now });
        context.Gadgets.Add(new Gadget { ProjectId = projectId, Code = "b", Name = "key" });
        await context.SaveChangesAsync(Token);

        context.Gadgets.Add(new Gadget { ProjectId = projectId, Code = "c", Name = "key" });
        DbUpdateException error = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));

        error.InnerException.ShouldBeOfType<PostgresException>().ConstraintName.ShouldBe("ix_gadget_project_id_name");
    }

    [Fact]
    [Trait("Spec", "0011:AC-5")]
    public async Task Migration_down_drops_the_module_tables_and_keeps_the_anchor_tables()
    {
        await db.MigrateAsync(up: false);
        try
        {
            await using NpgsqlCommand command = db.DataSource.CreateCommand(
                "SELECT string_agg(table_schema || '.' || table_name, ',' ORDER BY table_schema, table_name) FROM information_schema.tables "
                + "WHERE table_schema IN ('auth', 'platform', 'sample')");
            string? tables = (string?)await command.ExecuteScalarAsync(Token);

            tables.ShouldBe("auth.user,platform.operator,platform.organization,platform.project");
        }
        finally
        {
            await db.MigrateAsync(up: true);
        }
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Updated_at_is_written_when_changes_are_not_detected_automatically()
    {
        Widget widget = await AddWidgetAsync("g");
        DateTimeOffset updated = db.Clock.Now = db.Clock.Now.AddMinutes(2);
        await using (SampleContext context = db.CreateContext())
        {
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            Widget loaded = await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
            context.Entry(loaded).Property(x => x.Kind).CurrentValue = WidgetKind.Es256;
            await context.SaveChangesAsync(Token);
        }

        (await ReadAsync(widget.Id)).UpdatedAt.ShouldBe(updated);
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Updated_at_moves_when_only_an_owned_value_changed()
    {
        Widget widget = await AddWidgetAsync("h", label: "old");
        DateTimeOffset updated = db.Clock.Now = db.Clock.Now.AddMinutes(3);
        await using (SampleContext context = db.CreateContext())
        {
            Widget loaded = await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
            loaded.Label!.Text = "new";
            await context.SaveChangesAsync(Token);
        }

        Widget stored = await ReadAsync(widget.Id);
        stored.Label!.Text.ShouldBe("new");
        stored.UpdatedAt.ShouldBe(updated);
        stored.CreatedAt.ShouldBe(widget.CreatedAt);
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Updated_at_moves_when_an_owned_value_is_added()
    {
        Widget widget = await AddWidgetAsync("h1");
        DateTimeOffset updated = db.Clock.Now = db.Clock.Now.AddMinutes(3);
        await using (SampleContext context = db.CreateContext())
        {
            Widget loaded = await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
            loaded.Label = new WidgetLabel { Text = "added" };
            await context.SaveChangesAsync(Token);
        }

        Widget stored = await ReadAsync(widget.Id);
        stored.Label!.Text.ShouldBe("added");
        stored.UpdatedAt.ShouldBe(updated);
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Updated_at_moves_when_an_owned_value_is_removed()
    {
        Widget widget = await AddWidgetAsync("h2", label: "old");
        DateTimeOffset updated = db.Clock.Now = db.Clock.Now.AddMinutes(3);
        await using (SampleContext context = db.CreateContext())
        {
            Widget loaded = await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
            loaded.Label = null;
            await context.SaveChangesAsync(Token);
        }

        Widget stored = await ReadAsync(widget.Id);
        stored.Label.ShouldBeNull();
        stored.UpdatedAt.ShouldBe(updated);
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Updated_at_moves_when_an_owned_value_two_levels_down_changed()
    {
        Widget widget = await AddWidgetAsync("h3", label: "old", note: "old");
        DateTimeOffset updated = db.Clock.Now = db.Clock.Now.AddMinutes(3);
        await using (SampleContext context = db.CreateContext())
        {
            Widget loaded = await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
            loaded.Label!.Note!.Text = "new";
            await context.SaveChangesAsync(Token);
        }

        Widget stored = await ReadAsync(widget.Id);
        stored.Label!.Note!.Text.ShouldBe("new");
        stored.UpdatedAt.ShouldBe(updated);
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Updated_at_moves_when_an_owned_collection_item_is_added_then_removed()
    {
        Widget widget = await AddWidgetAsync("h4");
        DateTimeOffset added = db.Clock.Now = db.Clock.Now.AddMinutes(3);
        await using (SampleContext context = db.CreateContext())
        {
            Widget loaded = await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
            loaded.Tags.Add(new WidgetTag { Text = "first" });
            await context.SaveChangesAsync(Token);
        }

        Widget afterAdd = await ReadAsync(widget.Id);
        afterAdd.Tags.ShouldHaveSingleItem().Text.ShouldBe("first");
        afterAdd.UpdatedAt.ShouldBe(added);

        DateTimeOffset removed = db.Clock.Now = db.Clock.Now.AddMinutes(3);
        await using (SampleContext context = db.CreateContext())
        {
            Widget loaded = await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
            loaded.Tags.Clear();
            await context.SaveChangesAsync(Token);
        }

        Widget afterRemove = await ReadAsync(widget.Id);
        afterRemove.Tags.ShouldBeEmpty();
        afterRemove.UpdatedAt.ShouldBe(removed);
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Updated_at_stays_when_nothing_changed_and_a_delete_removes_the_row()
    {
        Widget widget = await AddWidgetAsync("i");
        db.Clock.Now = db.Clock.Now.AddMinutes(4);
        await using (SampleContext context = db.CreateContext())
        {
            await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token);
            (await context.SaveChangesAsync(Token)).ShouldBe(0);
        }

        (await ReadAsync(widget.Id)).UpdatedAt.ShouldBe(widget.UpdatedAt);
        await using (SampleContext context = db.CreateContext())
        {
            context.Widgets.Remove(await context.Widgets.SingleAsync(x => x.Id == widget.Id, Token));
            await context.SaveChangesAsync(Token);
        }

        await using SampleContext read = db.CreateContext();
        (await read.Widgets.AnyAsync(x => x.Id == widget.Id, Token)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0011:AC-2")]
    public async Task Timestamps_come_from_each_contexts_own_clock_when_contexts_share_a_database()
    {
        Guid projectId = await db.AddProjectAsync();
        var other = new TestClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var widget = new Widget { ProjectId = projectId, Email = "j@example.com", EmailNormalized = Unique("j") };

        await using var context = SampleContext.Create(db.DataSource, other);
        context.Widgets.Add(widget);
        await context.SaveChangesAsync(Token);

        widget.CreatedAt.ShouldBe(other.Now);
        widget.CreatedAt.ShouldNotBe(db.Clock.Now);
    }

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    [Trait("Spec", "0011:ENC-1")]
    public async Task Null_secret_and_null_text_enum_read_back_as_null_and_a_set_enum_is_stored_as_text()
    {
        Guid projectId = await db.AddProjectAsync();
        Widget widget = await AddWidgetAsync("k");
        var empty = new Gadget { ProjectId = projectId, Code = "empty", Name = "empty" };
        var kinded = new Gadget { ProjectId = projectId, Code = "kinded", Name = "kinded", Kind = WidgetKind.VerifyOnly };
        await using (SampleContext context = db.CreateContext())
        {
            context.Gadgets.AddRange(empty, kinded);
            await context.SaveChangesAsync(Token);
        }

        (await ReadAsync(widget.Id)).Secret.ShouldBeNull();
        await using SampleContext read = db.CreateContext();
        (await read.Gadgets.AsNoTracking().SingleAsync(x => x.Id == empty.Id, Token)).Kind.ShouldBeNull();
        (await read.Gadgets.AsNoTracking().SingleAsync(x => x.Id == kinded.Id, Token)).Kind.ShouldBe(WidgetKind.VerifyOnly);
        await using NpgsqlCommand command = db.DataSource.CreateCommand("SELECT kind FROM sample.gadget WHERE id = $1");
        command.Parameters.AddWithValue(kinded.Id);
        ((string?)await command.ExecuteScalarAsync(Token)).ShouldBe("verify_only");
    }

    [Fact]
    [Trait("Spec", "0011:AC-5")]
    public async Task Deleting_an_end_user_sets_null_or_cascades_and_deleting_the_organization_cascades()
    {
        Guid projectId = await db.AddProjectAsync();
        Guid organizationId = Guid.CreateVersion7(), ownerId = Guid.CreateVersion7(), subjectId = Guid.CreateVersion7();
        await db.ExecuteAsync($"""
            INSERT INTO platform.organization (id) VALUES ('{organizationId}');
            INSERT INTO auth."user" (id) VALUES ('{ownerId}'), ('{subjectId}');
            """);
        var owned = new Gadget { ProjectId = projectId, Code = "owned", Name = "owned", OwnerUserId = ownerId, OrganizationId = organizationId };
        var subject = new Gadget { ProjectId = projectId, Code = "subject", Name = "subject", SubjectUserId = subjectId };
        await using (SampleContext context = db.CreateContext())
        {
            context.Gadgets.AddRange(owned, subject);
            await context.SaveChangesAsync(Token);
        }

        await db.ExecuteAsync($"""
            DELETE FROM auth."user" WHERE id = '{ownerId}';
            DELETE FROM auth."user" WHERE id = '{subjectId}';
            """);
        await using (SampleContext context = db.CreateContext())
        {
            (await context.Gadgets.AsNoTracking().SingleAsync(x => x.Id == owned.Id, Token)).OwnerUserId.ShouldBeNull();
            (await context.Gadgets.AnyAsync(x => x.Id == subject.Id, Token)).ShouldBeFalse();
        }

        await db.ExecuteAsync($"DELETE FROM platform.organization WHERE id = '{organizationId}'");
        await using SampleContext read = db.CreateContext();
        (await read.Gadgets.AnyAsync(x => x.Id == owned.Id, Token)).ShouldBeFalse();
    }

    private async Task<Widget> AddWidgetAsync(string prefix, string? label = null, string? note = null)
    {
        Guid projectId = await db.AddProjectAsync();
        var widget = new Widget
        {
            ProjectId = projectId,
            Email = $"{prefix}@example.com",
            EmailNormalized = Unique(prefix),
            Label = label is null ? null : new WidgetLabel { Text = label, Note = note is null ? null : new LabelNote { Text = note } },
        };
        await using SampleContext context = db.CreateContext();
        context.Widgets.Add(widget);
        await context.SaveChangesAsync(Token);
        return widget;
    }

    private async Task<Widget> ReadAsync(Guid id)
    {
        await using SampleContext read = db.CreateContext();
        return await read.Widgets.AsNoTracking().SingleAsync(x => x.Id == id, Token);
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    private sealed class ProbeInterceptor : SaveChangesInterceptor
    {
        public DateTimeOffset? SeenCreatedAt { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SeenCreatedAt = eventData.Context!.ChangeTracker.Entries<ITimestamped>().Single().Entity.CreatedAt;
            return ValueTask.FromResult(result);
        }
    }
}
