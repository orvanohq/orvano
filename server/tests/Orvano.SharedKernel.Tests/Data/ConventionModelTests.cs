using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

using Npgsql;

using Orvano.SharedKernel.Data;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Data;

// Spec 0011 AC-3, AC-5: the model UseOrvanoConventions builds, read without a database.
public sealed class ConventionModelTests : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create("Host=localhost;Database=unused");
    private readonly SampleContext _context;
    private readonly IModel _model;

    public ConventionModelTests()
    {
        _context = SampleContext.Create(_dataSource, new TestClock(DateTimeOffset.UnixEpoch));
        _model = _context.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public void Tables_and_columns_are_snake_case_in_the_module_schema_when_built()
    {
        IEntityType widget = _model.FindEntityType(typeof(Widget))!;

        _model.GetDefaultSchema().ShouldBe("sample");
        widget.GetSchema().ShouldBe("sample");
        widget.GetTableName().ShouldBe("widget");
        Column(widget, nameof(Widget.EmailNormalized)).ShouldBe("email_normalized");
        Column(widget, nameof(Widget.CreatedByOperatorId)).ShouldBe("created_by_operator_id");
        Column(widget, nameof(Widget.CreatedAt)).ShouldBe("created_at");
        widget.FindPrimaryKey()!.GetName().ShouldBe("pk_widget");
        widget.GetIndexes().Select(index => index.GetDatabaseName())
            .ShouldBe(["ix_widget_created_by_operator_id", "ix_widget_email_normalized", "ix_widget_project_id"], ignoreOrder: true);
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public void History_table_is_in_the_module_schema_when_built()
    {
        var options = RelationalOptionsExtension.Extract(_context.GetService<IDbContextOptions>());

        options.MigrationsHistoryTableName.ShouldBe("__ef_migrations_history");
        options.MigrationsHistoryTableSchema.ShouldBe("sample");
    }

    [Fact]
    [Trait("Spec", "0011:AC-1")]
    [Trait("Spec", "0011:AC-3")]
    public void Entity_keys_are_never_generated_by_the_database_when_built()
    {
        foreach (IEntityType entityType in _model.GetEntityTypes())
        {
            entityType.FindPrimaryKey()!.Properties.ShouldAllBe(property => property.ValueGenerated == ValueGenerated.Never);
        }
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    [Trait("Spec", "0002:DAT-9")]
    public void Version_maps_to_xmin_as_a_concurrency_token_when_the_entity_is_versioned()
    {
        IProperty version = _model.FindEntityType(typeof(Widget))!.FindProperty(nameof(Widget.Version))!;

        Column(version.DeclaringType, nameof(Widget.Version)).ShouldBe("xmin");
        version.GetColumnType().ShouldBe("xid");
        version.IsConcurrencyToken.ShouldBeTrue();
        version.ValueGenerated.ShouldBe(ValueGenerated.OnAddOrUpdate);
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    [Trait("Spec", "0011:AC-4")]
    public void Text_enum_gets_a_named_check_listing_its_values_when_built()
    {
        ICheckConstraint check = _model.FindEntityType(typeof(Widget))!.GetCheckConstraints().ShouldHaveSingleItem();

        check.Name.ShouldBe("ck_widget_kind");
        check.Sql.ShouldBe("\"kind\" IN ('plain', 'verify_only', 'es256')");
        _model.FindEntityType(typeof(Widget))!.FindProperty(nameof(Widget.Kind))!.GetColumnType().ShouldBe("text");
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    [Trait("Spec", "0011:AC-5")]
    public void Anchor_links_are_named_and_use_the_asked_delete_rule_when_built()
    {
        IForeignKey[] links = [.. _model.FindEntityType(typeof(Widget))!.GetForeignKeys()];

        IForeignKey project = links.Single(link => link.PrincipalEntityType.GetTableName() == "project");
        project.PrincipalEntityType.GetSchema().ShouldBe("platform");
        project.GetConstraintName().ShouldBe("fk_widget_project_project_id");
        project.DeleteBehavior.ShouldBe(DeleteBehavior.Cascade);
        project.IsRequired.ShouldBeTrue();

        IForeignKey creator = links.Single(link => link.PrincipalEntityType.GetTableName() == "operator");
        creator.GetConstraintName().ShouldBe("fk_widget_operator_created_by_operator_id");
        creator.DeleteBehavior.ShouldBe(DeleteBehavior.SetNull);
        creator.IsRequired.ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0011:AC-5")]
    public void Anchor_stub_tables_are_excluded_from_migrations_when_built()
    {
        IEntityType[] stubs = [.. _model.GetEntityTypes().Where(entityType => Anchors.IsStub(entityType.ClrType))];

        stubs.Select(stub => $"{stub.GetSchema()}.{stub.GetTableName()}")
            .ShouldBe(["platform.organization", "platform.project", "platform.operator", "auth.user"], ignoreOrder: true);
        stubs.ShouldAllBe(stub => stub.IsTableExcludedFromMigrations());
        stubs.ShouldAllBe(stub => stub.GetDeclaredQueryFilters().Count == 0);
    }

    [Fact]
    [Trait("Spec", "0011:AC-5")]
    public void Generated_migration_creates_and_drops_only_the_module_tables_when_diffed()
    {
        IRelationalModel relational = _model.GetRelationalModel();
        IMigrationsModelDiffer differ = _context.GetService<IMigrationsModelDiffer>();

        IReadOnlyList<MigrationOperation> up = differ.GetDifferences(null, relational);
        IReadOnlyList<MigrationOperation> down = differ.GetDifferences(relational, null);

        var created = up.OfType<CreateTableOperation>().ToList();
        created.Select(table => $"{table.Schema}.{table.Name}").ShouldBe(["sample.gadget", "sample.widget", "sample.widget_tag"], ignoreOrder: true);
        created.Single(table => table.Name == "widget").ForeignKeys
            .Select(fk => $"{fk.Name} -> {fk.PrincipalSchema}.{fk.PrincipalTable} {fk.OnDelete}")
            .ShouldBe(
                ["fk_widget_operator_created_by_operator_id -> platform.operator SetNull", "fk_widget_project_project_id -> platform.project Cascade"],
                ignoreOrder: true);
        string sql = string.Join("\n", _context.GetService<IMigrationsSqlGenerator>().Generate(up, _model).Select(command => command.CommandText));
        sql.ShouldNotContain("xmin");
        sql.ShouldNotContain("CREATE TABLE platform.");
        sql.ShouldNotContain("CREATE TABLE auth.");
        down.OfType<DropTableOperation>()
            .Select(table => $"{table.Schema}.{table.Name}")
            .ShouldBe(["sample.gadget", "sample.widget", "sample.widget_tag"], ignoreOrder: true);
    }

    [Fact]
    [Trait("Spec", "0011:AC-5")]
    public void HasAnchor_throws_when_the_delete_rule_breaks_REF_3()
    {
        var builder = new ModelBuilder();

        Should.Throw<InvalidOperationException>(() =>
            builder.Entity<Widget>().HasAnchor(Anchor.Project, x => x.ProjectId, DeleteBehavior.Restrict));
        Should.Throw<InvalidOperationException>(() =>
            builder.Entity<Widget>().HasAnchor(Anchor.Operator, x => x.CreatedByOperatorId, DeleteBehavior.Cascade));
    }

    [Fact]
    [Trait("Spec", "0011:AC-5")]
    public void HasAnchor_throws_when_SetNull_is_asked_for_a_required_key()
    {
        var builder = new ModelBuilder();

        Should.Throw<InvalidOperationException>(() =>
            builder.Entity<Widget>().HasAnchor(Anchor.EndUser, x => x.ProjectId, DeleteBehavior.SetNull))
            .Message.ShouldContain("nullable");
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public void UseOrvanoConventions_throws_when_the_schema_is_empty() =>
        Should.Throw<ArgumentException>(() => new DbContextOptionsBuilder().UseOrvanoConventions(" ", new EmptyServices()));

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void Nullable_text_enum_gets_a_named_check_and_a_nullable_column_when_built()
    {
        IEntityType gadget = _model.FindEntityType(typeof(Gadget))!;

        ICheckConstraint check = gadget.GetCheckConstraints().ShouldHaveSingleItem();
        check.Name.ShouldBe("ck_gadget_kind");
        check.Sql.ShouldBe("\"kind\" IN ('plain', 'verify_only', 'es256')");
        gadget.FindProperty(nameof(Gadget.Kind))!.IsNullable.ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void Model_build_throws_when_a_CHECK_name_would_pass_63_bytes()
    {
        using var context = new LongTableContext(SampleContext.Options<LongTableContext>(SampleContext.Schema, _dataSource, TimeProvider.System));

        Should.Throw<InvalidOperationException>(() => context.Model).Message.ShouldContain("over 63 bytes");
    }

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void Model_build_throws_when_a_text_enum_has_no_table_for_its_CHECK()
    {
        using var context = new ViewContext(SampleContext.Options<ViewContext>(SampleContext.Schema, _dataSource, TimeProvider.System));

        Should.Throw<InvalidOperationException>(() => context.Model).Message.ShouldContain("not mapped to one");
    }

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void Model_build_throws_when_a_text_enum_is_inside_a_complex_type()
    {
        using var context = new ComplexEnumContext(SampleContext.Options<ComplexEnumContext>(SampleContext.Schema, _dataSource, TimeProvider.System));

        Should.Throw<InvalidOperationException>(() => context.Model).Message
            .ShouldBe("Boxed.Inner.Kind: HasTextEnum is not supported inside a complex type. Map the enum on an entity or owned type instead.");
    }

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void Text_enum_in_an_owned_type_gets_its_check_on_the_owner_table_with_the_prefixed_column_when_built()
    {
        using var context = new OwnedEnumContext(SampleContext.Options<OwnedEnumContext>(SampleContext.Schema, _dataSource, TimeProvider.System));

        IEntityType owned = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Badge))!.FindNavigation(nameof(Badge.Style))!.TargetEntityType;
        owned.GetTableName().ShouldBe("badge");
        ICheckConstraint check = owned.GetCheckConstraints().ShouldHaveSingleItem();
        check.Name.ShouldBe("ck_badge_style_kind");
        check.Sql.ShouldBe("\"style_kind\" IN ('plain', 'verify_only', 'es256')");
    }

    [Fact]
    [Trait("Spec", "0011:AC-4")]
    public void Text_enum_on_a_TPC_base_gets_a_check_on_each_concrete_table_when_built()
    {
        using var context = new TpcEnumContext(SampleContext.Options<TpcEnumContext>(SampleContext.Schema, _dataSource, TimeProvider.System));
        IModel model = context.GetService<IDesignTimeModel>().Model;

        model.FindEntityType(typeof(Shape))!.GetCheckConstraints().ShouldBeEmpty();
        model.FindEntityType(typeof(Circle))!.GetCheckConstraints().ShouldHaveSingleItem().Name.ShouldBe("ck_circle_kind");
        model.FindEntityType(typeof(Square))!.GetCheckConstraints().ShouldHaveSingleItem().Name.ShouldBe("ck_square_kind");
    }

    [Fact]
    [Trait("Spec", "0011:AC-5")]
    public void Organization_and_end_user_links_use_the_asked_delete_rule_when_built()
    {
        IForeignKey[] links = [.. _model.FindEntityType(typeof(Gadget))!.GetForeignKeys()];

        string Rule(string property) =>
            links.Single(link => link.Properties.Single().Name == property) is var link
                ? $"{link.PrincipalEntityType.GetSchema()}.{link.PrincipalEntityType.GetTableName()} {link.DeleteBehavior}"
                : "";

        Rule(nameof(Gadget.OrganizationId)).ShouldBe("platform.organization Cascade");
        Rule(nameof(Gadget.OwnerUserId)).ShouldBe("auth.user SetNull");
        Rule(nameof(Gadget.SubjectUserId)).ShouldBe("auth.user Cascade");
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    [Trait("Spec", "0002:DAT-1")]
    public void History_table_stays_in_the_module_schema_when_the_npgsql_callback_moves_it()
    {
        using var context = new SampleContext(SampleContext.Options<SampleContext>(
            SampleContext.Schema,
            _dataSource,
            TimeProvider.System,
            npgsql: npgsql => npgsql.MigrationsHistoryTable("history", "public")));

        var options = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        (options.MigrationsHistoryTableName, options.MigrationsHistoryTableSchema).ShouldBe(("__ef_migrations_history", "sample"));
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public void Each_schema_gets_its_own_model_when_one_context_class_is_set_up_twice()
    {
        using var other = new SampleContext(SampleContext.Options<SampleContext>("other", _dataSource, TimeProvider.System));

        other.Model.GetDefaultSchema().ShouldBe("other");
        _context.Model.GetDefaultSchema().ShouldBe("sample");
        other.Model.FindEntityType(typeof(Widget))!.GetSchema().ShouldBe("other");
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public void Contexts_share_one_EF_service_provider_when_their_clocks_differ()
    {
        using var other = SampleContext.Create(_dataSource, new TestClock(DateTimeOffset.UnixEpoch));

        other.GetService<IModelSource>().ShouldBeSameAs(_context.GetService<IModelSource>());
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _dataSource.DisposeAsync();
    }

    private static string? Column(ITypeBase entityType, string property) =>
        entityType.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema()));

    private sealed class Flag : Entity
    {
        public WidgetKind Kind { get; set; }
    }

    private sealed class LongTableContext(DbContextOptions<LongTableContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Flag>(flag =>
            {
                flag.ToTable(new string('t', 56));
                flag.Property(x => x.Kind).HasTextEnum();
            });
    }

    private sealed class ViewContext(DbContextOptions<ViewContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Flag>(flag =>
            {
                flag.ToView("flag_view");
                flag.Property(x => x.Kind).HasTextEnum();
            });
    }

    private sealed class BadgeStyle
    {
        public WidgetKind Kind { get; set; }
    }

    private sealed class Badge : Entity
    {
        public BadgeStyle Style { get; set; } = new();
    }

    // Table splitting: the owned type's column lives in the owner's table under the owner's prefix.
    private sealed class OwnedEnumContext(DbContextOptions<OwnedEnumContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Badge>().OwnsOne(x => x.Style, style => style.Property(x => x.Kind).HasTextEnum());
    }

    private abstract class Shape : Entity
    {
        public WidgetKind Kind { get; set; }
    }

    private sealed class Circle : Shape;

    private sealed class Square : Shape;

    // TPC: the abstract base has no table, so the marked column is a column of each concrete table.
    private sealed class TpcEnumContext(DbContextOptions<TpcEnumContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Shape>(shape =>
            {
                shape.UseTpcMappingStrategy();
                shape.Property(x => x.Kind).HasTextEnum();
            });
            modelBuilder.Entity<Circle>();
            modelBuilder.Entity<Square>();
        }
    }

    private sealed class Inner
    {
        public WidgetKind Kind { get; set; }
    }

    private sealed class Boxed : Entity
    {
        public Inner Inner { get; set; } = new();
    }

    // HasTextEnum has no complex type overload, so the mark is set the way a hand written annotation would set it.
    private sealed class ComplexEnumContext(DbContextOptions<ComplexEnumContext> options) : DbContext(options)
    {
        private static readonly string[] Values = ["plain"];

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Boxed>().ComplexProperty(x => x.Inner, inner =>
                inner.Property(x => x.Kind).HasConversion<string>().HasAnnotation("Orvano:TextEnumValues", Values));
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
