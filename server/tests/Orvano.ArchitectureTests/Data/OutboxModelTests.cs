using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Orvano.SharedKernel.Outbox;
using Orvano.Testing;

using Shouldly;

using Xunit;

namespace Orvano.ArchitectureTests.Data;

// Spec 0013 OTB-1, OTB-2, OTS-3, AC-32: HasOutbox maps the outbox table as specified, and a context that calls it still
// keeps the spec 0011 rules. The sample context is not in MigrationOrder, so the rules run on it here explicitly.
public sealed class OutboxModelTests
{
    private static readonly IModel Sample = SampleContexts.ModelOf<SampleOutboxDbContext>(SampleOutboxDbContext.Schema);

    [Fact]
    [Trait("Spec", "0013:OTS-3"), Trait("Spec", "0013:AC-32"), Trait("Spec", "0011:AC-6"), Trait("Spec", "0011:AC-7"), Trait("Spec", "0011:AC-8")]
    public void A_context_with_an_outbox_keeps_the_data_model_rules()
    {
        DataModelRules.CrossSchemaLinks(Sample).ShouldBeEmpty();
        DataModelRules.DataClasses(Sample).ShouldBeEmpty();
        DataModelRules.Keys(Sample).ShouldBeEmpty();
        SampleContexts.SetupWithConventions<SampleOutboxDbContext>(SampleOutboxDbContext.Schema).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:OTB-1"), Trait("Spec", "0013:OTB-2"), Trait("Spec", "0013:AC-32")]
    public void HasOutbox_maps_the_outbox_columns_checks_partial_index_and_storage_parameter_in_the_context_schema()
    {
        IEntityType outbox = Sample.FindEntityType(typeof(OutboxEvent)).ShouldNotBeNull();

        outbox.GetTableName().ShouldBe("outbox");
        outbox.GetSchema().ShouldBe("sample");
        outbox.GetProperties().Select(p => $"{p.GetColumnName()} {p.GetColumnType()} {(p.IsNullable ? "null" : "not null")}").ShouldBe(
            [
                "id uuid not null",
                "dispatched_at timestamp with time zone null",
                "occurred_at timestamp with time zone not null",
                "payload jsonb not null",
                "project_id uuid null",
                "trace_parent text null",
                "type text not null",
            ],
            ignoreOrder: true);
        outbox.GetCheckConstraints().Select(check => $"{check.Name}: {check.Sql}").ShouldBe(
            ["ck_outbox_trace_parent: char_length(trace_parent) <= 55", "ck_outbox_type: char_length(type) BETWEEN 3 AND 100"],
            ignoreOrder: true);
        IIndex pending = outbox.GetIndexes().ShouldHaveSingleItem();
        pending.GetDatabaseName().ShouldBe("ix_outbox_pending");
        pending.GetFilter().ShouldBe("dispatched_at IS NULL");
        pending.Properties.Select(property => property.Name).ShouldBe([nameof(OutboxEvent.Id)]);
        outbox.GetStorageParameters().ShouldBe(new Dictionary<string, object?> { ["autovacuum_vacuum_scale_factor"] = 0.05 });
        outbox.GetForeignKeys().ShouldBeEmpty(); // a plain project id (spec 0011 REF-4)
    }
}
