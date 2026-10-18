using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

using Orvano.SharedKernel.Data;

namespace Orvano.SharedKernel.Tests.Data;

// The spec 0011 critical test scenarios: a sample module context in schema "sample" with one entity that is
// timestamped, versioned, project anchored, has a text enum, a marked email, a secret, an owned label with an owned note,
// and owned tags; and one that
// links to every other anchor and has a nullable text enum.
internal enum WidgetKind
{
    Plain,
    VerifyOnly,
    Es256,
}

internal sealed class Widget : Entity, ITimestamped, IVersioned
{
    public Guid ProjectId { get; set; }

    public Guid? CreatedByOperatorId { get; set; }

    [DataClass(DataClass.Personal)]
    public string Email { get; set; } = "";

    [DataClass(DataClass.Personal)]
    public string EmailNormalized { get; set; } = "";

    public WidgetKind Kind { get; set; }

    [DataClass(DataClass.Secret, Purpose = "sample.widget.secret")]
    public EncryptedValue? Secret { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }

    public WidgetLabel? Label { get; set; }

    public List<WidgetTag> Tags { get; set; } = [];
}

internal sealed class WidgetLabel
{
    [DataClass(DataClass.Public)]
    public string Text { get; set; } = "";

    public LabelNote? Note { get; set; }
}

// An owned type inside an owned type, so owner stamping walks two ownership levels.
internal sealed class LabelNote
{
    [DataClass(DataClass.Public)]
    public string Text { get; set; } = "";
}

// An owned collection item keyed on its own app made Id (IDS-1), in a table of its own.
internal sealed class WidgetTag : Entity
{
    [DataClass(DataClass.Public)]
    public string Text { get; set; } = "";
}

// AC-11: one NULLS NOT DISTINCT unique index and one partial unique index.
internal sealed class Gadget : Entity
{
    public Guid ProjectId { get; set; }

    [DataClass(DataClass.Public)]
    public string? Code { get; set; }

    [DataClass(DataClass.Public)]
    public string Name { get; set; } = "";

    public DateTimeOffset? RevokedAt { get; set; }

    public WidgetKind? Kind { get; set; }

    public Guid? OrganizationId { get; set; }

    public Guid? OwnerUserId { get; set; }

    public Guid? SubjectUserId { get; set; }
}

internal sealed class SampleContext(DbContextOptions<SampleContext> options) : DbContext(options)
{
    public const string Schema = "sample";

    public DbSet<Widget> Widgets => Set<Widget>();

    public DbSet<Gadget> Gadgets => Set<Gadget>();

    public static SampleContext Create(NpgsqlDataSource dataSource, TimeProvider clock, Action<DbContextOptionsBuilder>? before = null) =>
        new(Options<SampleContext>(Schema, dataSource, clock, before));

    public static DbContextOptions<TContext> Options<TContext>(
        string schema,
        NpgsqlDataSource dataSource,
        TimeProvider clock,
        Action<DbContextOptionsBuilder>? before = null,
        Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
        where TContext : DbContext
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(dataSource)
            .AddSingleton(clock)
            .BuildServiceProvider();
        var builder = new DbContextOptionsBuilder<TContext>();
        before?.Invoke(builder);
        builder.UseOrvanoConventions(schema, services, npgsql);
        return builder.Options;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(widget =>
        {
            widget.HasAnchor(Anchor.Project, x => x.ProjectId, DeleteBehavior.Cascade);
            widget.HasAnchor(Anchor.Operator, x => x.CreatedByOperatorId, DeleteBehavior.SetNull);
            widget.Property(x => x.Kind).HasTextEnum();
            widget.HasIndex(x => x.EmailNormalized).IsUnique();
            widget.OwnsOne(x => x.Label, label => label.OwnsOne(x => x.Note));
            widget.OwnsMany(x => x.Tags, tag => tag.HasKey(x => x.Id));
        });

        modelBuilder.Entity<Gadget>(gadget =>
        {
            gadget.HasAnchor(Anchor.Project, x => x.ProjectId, DeleteBehavior.Cascade);
            gadget.HasAnchor(Anchor.Organization, x => x.OrganizationId, DeleteBehavior.Cascade);
            gadget.HasAnchor(Anchor.EndUser, x => x.OwnerUserId, DeleteBehavior.SetNull);
            gadget.HasAnchor(Anchor.EndUser, x => x.SubjectUserId, DeleteBehavior.Cascade);
            gadget.Property(x => x.Kind).HasTextEnum();
            gadget.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique().AreNullsDistinct(false);
            gadget.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique().HasFilter("revoked_at IS NULL");
        });
    }
}

/// <summary>A clock the test sets (spec 0002 GEN-9).</summary>
public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
