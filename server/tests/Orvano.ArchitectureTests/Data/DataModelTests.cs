using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.SharedKernel.Data;

using Shouldly;

using Xunit;

namespace Orvano.ArchitectureTests.Data;

// Spec 0011 AC-6, AC-7, AC-8: every server DbContext keeps the data model rules, and each check catches its slip.
public sealed class DataModelTests
{
    private static readonly IReadOnlyList<ServerContext> Production = DataModelRules.ProductionModels();

    // Every rule below walks Production, so a scan that missed a context would pass them all. MigrationOrder is the
    // list `orvano migrate` applies (server/AGENTS.md: every module DbContext goes there), so the scan must find
    // exactly those.
    [Fact]
    [Trait("Spec", "0011:AC-8")]
    public void Server_contexts_found_by_the_scan_are_exactly_the_ones_migrate_applies()
    {
        Type order = typeof(Program).Assembly.GetType("Orvano.Host.Database.MigrationOrder", throwOnError: true)!;
        var migrated = (IReadOnlyList<Type>)order.GetField("DbContexts")!.GetValue(null)!;

        Production.Select(context => context.Type).ShouldBe(migrated, ignoreOrder: true);
    }

    [Theory]
    [Trait("Spec", "0011:REF-3"), Trait("Spec", "0017:AC-10")]
    [InlineData("Orvano.Modules.Platform.Data.Project", "OrganizationId", DeleteBehavior.Restrict)] // LIF-3
    [InlineData("Orvano.Modules.Platform.Data.ApiKey", "CreatedByOperatorId", DeleteBehavior.SetNull)]
    public void Platform_pins_the_same_schema_delete_rules_of_REF_3(string entity, string property, DeleteBehavior rule)
    {
        IModel model = Production.Single(context => context.Type.Name == "PlatformDbContext").Model;

        model.FindEntityType(entity).ShouldNotBeNull().FindProperty(property).ShouldNotBeNull()
            .GetContainingForeignKeys().ShouldHaveSingleItem().DeleteBehavior.ShouldBe(rule);
    }

    [Fact]
    [Trait("Spec", "0002:DAT-2a"), Trait("Spec", "0017:ACX-5")]
    public void Server_contexts_filter_every_project_scoped_entity_by_project()
    {
        IEnumerable<string> missing =
            from context in Production
            from entity in context.Model.GetEntityTypes()
            where typeof(IProjectScoped).IsAssignableFrom(entity.ClrType)
            where entity.GetDeclaredQueryFilters().All(filter => filter.Key != ProjectFilter.Name)
            select entity.ClrType.Name;

        missing.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0002:DAT-2a")]
    public void Platform_entities_with_a_ProjectId_are_project_scoped()
    {
        IEnumerable<string> missing =
            from entity in Production.Single(context => context.Type.Name == "PlatformDbContext").Model.GetEntityTypes()
            where !Anchors.IsStub(entity.ClrType) && entity.FindProperty(nameof(IProjectScoped.ProjectId)) is not null
            where !typeof(IProjectScoped).IsAssignableFrom(entity.ClrType)
            select entity.ClrType.Name;

        missing.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public void Server_contexts_are_set_up_with_UseOrvanoConventions() =>
        Production.SelectMany(context => context.Setup).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0011:AC-6")]
    public void Server_contexts_link_across_schemas_only_to_anchors_with_REF_3_rules() =>
        Production.SelectMany(context => DataModelRules.CrossSchemaLinks(context.Model)).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0011:AC-7")]
    public void Server_contexts_mark_every_text_bytes_address_and_json_property_with_a_data_class() =>
        Production.SelectMany(context => DataModelRules.DataClasses(context.Model)).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0011:AC-8")]
    public void Server_contexts_have_no_database_generated_keys() =>
        Production.SelectMany(context => DataModelRules.Keys(context.Model)).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0011:AC-8")]
    public void Server_contexts_each_have_their_own_schema() =>
        DataModelRules.SharedSchemas(Production.Select(context => (context.Type, context.Model))).ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0011:AC-6")]
    [Trait("Spec", "0011:AC-7")]
    [Trait("Spec", "0011:AC-8")]
    public void Rules_pass_when_a_context_keeps_them()
    {
        IModel model = SampleContexts.ModelOf<GoodContext>("good");

        DataModelRules.CrossSchemaLinks(model).ShouldBeEmpty();
        DataModelRules.DataClasses(model).ShouldBeEmpty();
        DataModelRules.Keys(model).ShouldBeEmpty();
        SampleContexts.SetupWithConventions<GoodContext>("good").ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public void Scan_finds_the_contexts_of_an_assembly() =>
        DataModelRules.ContextTypes([typeof(GoodContext).Assembly]).ShouldContain(typeof(GoodContext));

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public void Setup_passes_for_a_context_that_uses_HasAnchor_and_HasTextEnum_with_the_conventions() =>
        SampleContexts.SetupWithConventions<ConventionUserContext>("tagged").ShouldBeEmpty();

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    public void Setup_fails_when_a_context_skips_UseOrvanoConventions() =>
        SampleContexts.SetupWithoutConventions<ConventionUserContext>().ShouldBe(
            [
                "ConventionUserContext is not set up with UseOrvanoConventions (AC-3)",
                "ConventionUserContext does not retry on transient failures (DAT-8)",
                "The anchor stub ProjectAnchor maps (none).ProjectAnchor, not an anchor table excluded from migrations (REF-2)",
                "Tagged.Kind uses HasTextEnum but has no CHECK (AC-4)",
            ],
            ignoreOrder: true);

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    [Trait("Spec", "0002:DAT-1")]
    public void Setup_fails_when_a_later_UseNpgsql_moves_the_history_table() =>
        SampleContexts.SetupOf<GoodContext>(builder => builder
                .UseOrvanoConventions("good", SampleContexts.Services)
                .UseNpgsql(SampleContexts.Services.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.MigrationsHistoryTable("history")))
            .ShouldHaveSingleItem()
            .ShouldBe("GoodContext keeps its migrations history in (none).history, not good.__ef_migrations_history (DAT-1)");

    [Fact]
    [Trait("Spec", "0011:AC-3")]
    [Trait("Spec", "0011:MOD-1")]
    public void Setup_fails_when_a_table_is_mapped_outside_the_module_schema() =>
        SampleContexts.SetupWithConventions<ForeignTableContext>("own")
            .ShouldHaveSingleItem()
            .ShouldBe("ForeignTableContext maps Composite to public.composite, outside its schema own (MOD-1, MOD-3)");

    [Fact]
    [Trait("Spec", "0011:AC-6")]
    public void CrossSchemaLinks_fails_when_an_FK_points_at_a_table_in_another_schema_that_is_not_an_anchor() =>
        DataModelRules.CrossSchemaLinks(SampleContexts.ModelOf<NonAnchorLinkContext>("bad_link"))
            .ShouldHaveSingleItem().ShouldBe("bad_link.thing links to elsewhere.other, which is not an anchor (REF-1)");

    [Fact]
    [Trait("Spec", "0011:AC-6")]
    public void CrossSchemaLinks_fails_when_an_anchor_link_uses_a_delete_rule_REF_3_does_not_allow() =>
        DataModelRules.CrossSchemaLinks(SampleContexts.ModelOf<WrongDeleteRuleContext>("bad_rule"))
            .ShouldHaveSingleItem().ShouldBe("bad_rule.thing links to platform.project with Restrict (REF-3 allows Cascade)");

    [Fact]
    [Trait("Spec", "0011:AC-6")]
    public void CrossSchemaLinks_fails_when_an_anchor_table_is_mapped_by_a_real_entity_not_the_stub() =>
        DataModelRules.CrossSchemaLinks(SampleContexts.ModelOf<RealAnchorTableContext>("bad_anchor"))
            .ShouldHaveSingleItem()
            .ShouldBe("bad_anchor.thing links to platform.project through FakeProject, not the anchor stub HasAnchor maps (REF-2)");

    [Fact]
    [Trait("Spec", "0011:AC-7")]
    public void DataClasses_fails_for_an_unmarked_property_and_a_secret_without_a_purpose() =>
        DataModelRules.DataClasses(SampleContexts.ModelOf<UnmarkedContext>("bad_class")).ShouldBe(
            [
                "Unmarked.Hash has no [DataClass] (CLS-1)",
                "Unmarked.NoPurpose is an EncryptedValue not marked DataClass.Secret with a Purpose (ENC-2)",
                "Unmarked.Note has no [DataClass] (CLS-1)",
                "Unmarked.WrongClass is an EncryptedValue not marked DataClass.Secret with a Purpose (ENC-2)",
                "Unmarked.Emails has no [DataClass] (CLS-1)",
                "Unmarked.Website has no [DataClass] (CLS-1)",
            ],
            ignoreOrder: true);

    [Fact]
    [Trait("Spec", "0011:AC-7")]
    public void DataClasses_fails_for_unmarked_text_in_an_owned_type_and_a_nested_complex_type() =>
        DataModelRules.DataClasses(SampleContexts.ModelOf<UnmarkedValueObjectContext>("bad_value")).ShouldBe(
            [
                "Address.Street has no [DataClass] (CLS-1)",
                "Holder.Contact.Email has no [DataClass] (CLS-1)",
                "Holder.Contact.Phone.Number has no [DataClass] (CLS-1)",
            ],
            ignoreOrder: true);

    [Fact]
    [Trait("Spec", "0011:AC-8")]
    public void Keys_fails_for_a_database_generated_key_and_an_entity_not_keyed_on_Id() =>
        DataModelRules.Keys(SampleContexts.ModelOf<StoreGeneratedKeyContext>("bad_key")).ShouldBe(
            [
                "Counter.Id is a key generated by the database (IDS-1)",
                "Other.Id is a key generated by the database (IDS-1)",
                "Composite derives from Entity but is not keyed on Id alone (IDS-1)",
            ],
            ignoreOrder: true);

    [Fact]
    [Trait("Spec", "0011:AC-8")]
    public void Keys_fails_for_an_owned_collection_on_the_default_key_and_passes_one_keyed_on_Id() =>
        DataModelRules.Keys(SampleContexts.ModelOf<OwnedCollectionContext>("owned_rows")).ShouldBe(
            [
                "Line.Id is a key generated by the database (IDS-1); an owned collection item derives from Entity and is "
                    + "keyed with HasKey(x => x.Id)",
            ]);

    [Fact]
    [Trait("Spec", "0011:AC-8")]
    public void SharedSchemas_fails_when_two_contexts_use_the_same_schema() =>
        DataModelRules.SharedSchemas(
            [
                (typeof(GoodContext), SampleContexts.ModelOf<GoodContext>("dup")),
                (typeof(UnmarkedContext), SampleContexts.ModelOf<UnmarkedContext>("dup")),
            ])
            .ShouldHaveSingleItem().ShouldBe("GoodContext and UnmarkedContext share the schema dup (MOD-1)");
}
