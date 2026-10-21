using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Orvano.SharedKernel.Data;

namespace Orvano.SharedKernel.Outbox;

/// <summary>
/// A row of a module's <c>&lt;schema&gt;.outbox</c> (spec 0013 OTB-1). Only <c>IOutbox.Publish</c>'s insert and the
/// dispatcher's and cleanup's SQL change it, so it is neither <see cref="ITimestamped"/> nor <see cref="IVersioned"/>,
/// and its <see cref="ProjectId"/> is a plain id on a history row (spec 0011 REF-4). Modules may not name it
/// (<c>BannedSymbols.Modules.txt</c>, OGR-2): they publish through <see cref="IOutbox"/>.
/// </summary>
public sealed class OutboxEvent : Entity
{
    /// <summary>The event's type (OBX-1).</summary>
    [DataClass(DataClass.Public)]
    public string Type { get; set; } = string.Empty;

    /// <summary>The event record as JSON (ids and codes only, spec 0011 CLS-2), at most 64,000 bytes as published.</summary>
    [DataClass(DataClass.Personal)]
    public string Payload { get; set; } = "{}";

    /// <summary>The project the event is about, a plain id with no foreign key; the delivery jobs' project (DLV-3).</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>The publish time; the id is a uuid v7 of it (PUB-1, OCL-2).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Set when the dispatcher enqueued the deliveries; null while waiting (DSP-3).</summary>
    public DateTimeOffset? DispatchedAt { get; set; }

    /// <summary>The W3C <c>traceparent</c> of the publishing activity, copied into each delivery (OTB-3).</summary>
    [DataClass(DataClass.Public)]
    public string? TraceParent { get; set; }
}

/// <summary>Maps a module's outbox table (spec 0013 OTB-2).</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="OutboxEvent"/> to <c>outbox</c> in the context's default schema, the module schema
    /// <c>UseOrvanoConventions</c> set. Call it in <c>OnModelCreating</c> of a module that announces events, then
    /// generate the module's next migration.
    /// </summary>
    public static ModelBuilder HasOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        return modelBuilder.ApplyConfiguration(new OutboxEventConfiguration());
    }
}

/// <summary>The one shared configuration of every outbox table (OTB-2).</summary>
internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public const string Table = "outbox";

    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        // The type pattern and payload size are checked in code (OBX-1, PUB-2); the table holds only the bounds.
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("ck_outbox_type", "char_length(type) BETWEEN 3 AND 100");
            table.HasCheckConstraint("ck_outbox_trace_parent", "char_length(trace_parent) <= 55");
        });

        // Each row is inserted, updated once, and deleted after 7 days.
        builder.HasStorageParameter("autovacuum_vacuum_scale_factor", 0.05);

        builder.Property(x => x.Type).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();

        // DSP-3's select walks only the waiting rows, in id order.
        builder.HasIndex(x => x.Id).HasDatabaseName("ix_outbox_pending").HasFilter("dispatched_at IS NULL");
    }
}
