using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.SharedKernel.Data;

namespace Orvano.ArchitectureTests.Data;

// Contexts that keep or break the spec 0011 rules on purpose, to prove the AC-6 to AC-8 checks catch each slip.
internal static class SampleContexts
{
    public static readonly ServiceProvider Services = new ServiceCollection()
        .AddSingleton(NpgsqlDataSource.Create("Host=localhost;Database=unused"))
        .AddSingleton(TimeProvider.System)
        .BuildServiceProvider();

    public static IModel ModelOf<TContext>(string schema)
        where TContext : DbContext
    {
        DbContextOptions<TContext> options = new DbContextOptionsBuilder<TContext>().UseOrvanoConventions(schema, Services).Options;
        using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        return context.GetService<IDesignTimeModel>().Model;
    }

    /// <summary>The <see cref="DataModelRules.Setup"/> slips of a context set up by <paramref name="setUp"/>.</summary>
    public static IReadOnlyList<string> SetupOf<TContext>(Action<DbContextOptionsBuilder<TContext>> setUp)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        setUp(builder);
        using var context = (TContext)Activator.CreateInstance(typeof(TContext), builder.Options)!;
        return [.. DataModelRules.Setup(context)];
    }

    public static IReadOnlyList<string> SetupWithConventions<TContext>(string schema)
        where TContext : DbContext => SetupOf<TContext>(builder => builder.UseOrvanoConventions(schema, Services));

    /// <summary>Plain <c>UseNpgsql</c>, as a module that forgot <c>UseOrvanoConventions</c> would write it.</summary>
    public static IReadOnlyList<string> SetupWithoutConventions<TContext>()
        where TContext : DbContext =>
        SetupOf<TContext>(builder => builder.UseNpgsql(Services.GetRequiredService<NpgsqlDataSource>()));
}

internal sealed class Thing : Entity
{
    public Guid ProjectId { get; set; }

    public Guid? OwnerId { get; set; }

    public Guid? OtherId { get; set; }

    [DataClass(DataClass.Public)]
    public string Name { get; set; } = "";

    [DataClass(DataClass.Secret, Purpose = "sample.thing.secret")]
    public EncryptedValue? Secret { get; set; }
}

internal sealed class Other : Entity;

internal sealed class FakeProject
{
    public Guid Id { get; init; }
}

internal enum ThingKind
{
    Plain,
    Fancy,
}

internal sealed class MarkedAddress
{
    [DataClass(DataClass.Personal)]
    public string Street { get; set; } = "";
}

internal sealed class MarkedContact
{
    [DataClass(DataClass.Personal)]
    public string Email { get; set; } = "";
}

internal sealed class Profile : Entity
{
    public MarkedAddress Home { get; set; } = new();

    public MarkedContact Contact { get; set; } = new();

    public ThingKind Kind { get; set; }
}

/// <summary>Keeps every rule: anchor links through HasAnchor, a same schema FK, marked properties, owned and complex.</summary>
internal sealed class GoodContext(DbContextOptions<GoodContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Other>();
        modelBuilder.Entity<Profile>(profile =>
        {
            profile.OwnsOne(x => x.Home);
            profile.ComplexProperty(x => x.Contact);
            profile.Property(x => x.Kind).HasTextEnum();
        });
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasAnchor(Anchor.Project, x => x.ProjectId, DeleteBehavior.Cascade);
            thing.HasAnchor(Anchor.EndUser, x => x.OwnerId, DeleteBehavior.SetNull);
            thing.HasOne<Other>().WithMany().HasForeignKey(x => x.OtherId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

/// <summary>Breaks REF-1: an FK into another schema at a table that is not an anchor.</summary>
internal sealed class NonAnchorLinkContext(DbContextOptions<NonAnchorLinkContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Other>().ToTable("other", "elsewhere", table => table.ExcludeFromMigrations());
        modelBuilder.Entity<Thing>().HasOne<Other>().WithMany().HasForeignKey(x => x.OtherId);
    }
}

/// <summary>Breaks REF-3: the anchor link HasAnchor made, changed afterwards to a delete rule HasAnchor would refuse.</summary>
internal sealed class WrongDeleteRuleContext(DbContextOptions<WrongDeleteRuleContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Thing>(thing =>
        {
            thing.HasAnchor(Anchor.Project, x => x.ProjectId, DeleteBehavior.Cascade);
            thing.Metadata.GetForeignKeys().Single().DeleteBehavior = DeleteBehavior.Restrict;
        });
}

/// <summary>Breaks REF-2: a real entity of the module mapped onto platform.project, so it is not the stub.</summary>
internal sealed class RealAnchorTableContext(DbContextOptions<RealAnchorTableContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FakeProject>().ToTable("project", "platform");
        modelBuilder.Entity<Thing>().HasOne<FakeProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class Tagged : Entity
{
    public Guid ProjectId { get; set; }

    public ThingKind Kind { get; set; }
}

/// <summary>Uses HasAnchor and HasTextEnum, so a setup without UseOrvanoConventions leaves both half done.</summary>
internal sealed class ConventionUserContext(DbContextOptions<ConventionUserContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Tagged>(tagged =>
        {
            tagged.HasAnchor(Anchor.Project, x => x.ProjectId, DeleteBehavior.Cascade);
            tagged.Property(x => x.Kind).HasTextEnum();
        });
}

internal sealed class Unmarked : Entity
{
    public string Note { get; set; } = "";

    [DataClass(DataClass.Secret)]
    public EncryptedValue NoPurpose { get; set; }

    [DataClass(DataClass.Personal, Purpose = "sample.unmarked.wrong_class")]
    public EncryptedValue WrongClass { get; set; }

    public byte[]? Hash { get; set; }

    // Text the plain type list would miss: a primitive collection and a type EF stores as text through a converter.
    public List<string> Emails { get; set; } = [];

    public Uri? Website { get; set; }
}

/// <summary>Breaks CLS-1 and ENC-2.</summary>
internal sealed class UnmarkedContext(DbContextOptions<UnmarkedContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Unmarked>();
}

/// <summary>Breaks MOD-1 and MOD-3: one of its tables is put in public, outside the module schema.</summary>
internal sealed class ForeignTableContext(DbContextOptions<ForeignTableContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Other>();
        modelBuilder.Entity<Composite>().ToTable("composite", "public");
    }
}

internal sealed class Address
{
    public string Street { get; set; } = "";

    [DataClass(DataClass.Personal)]
    public string City { get; set; } = "";
}

internal sealed class Phone
{
    public string Number { get; set; } = "";
}

internal sealed class Contact
{
    public string Email { get; set; } = "";

    public Phone Phone { get; set; } = new();
}

internal sealed class Holder : Entity
{
    public Address Home { get; set; } = new();

    public Contact Contact { get; set; } = new();
}

/// <summary>Breaks CLS-1 inside an owned type and inside a nested complex type.</summary>
internal sealed class UnmarkedValueObjectContext(DbContextOptions<UnmarkedValueObjectContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Holder>(holder =>
        {
            holder.OwnsOne(x => x.Home);
            holder.ComplexProperty(x => x.Contact, contact => contact.ComplexProperty(x => x.Phone));
        });
}

internal sealed class Counter
{
    public int Id { get; set; }
}

internal sealed class Composite : Entity
{
    public Guid Other { get; set; }
}

/// <summary>Breaks IDS-1: a database made key, an Entity key made by the database, and an Entity not keyed on Id.</summary>
internal sealed class StoreGeneratedKeyContext(DbContextOptions<StoreGeneratedKeyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Counter>();
        modelBuilder.Entity<Other>().Property(x => x.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<Composite>().HasKey(x => new { x.Id, x.Other });
    }
}

internal sealed class Line
{
    [DataClass(DataClass.Public)]
    public string Text { get; set; } = "";
}

internal sealed class KeyedLine : Entity
{
    [DataClass(DataClass.Public)]
    public string Text { get; set; } = "";
}

internal sealed class Order : Entity
{
    public List<Line> Lines { get; set; } = [];

    public List<KeyedLine> KeyedLines { get; set; } = [];
}

/// <summary>Two owned collections: one on EF's default database made key (breaks IDS-1), one keyed on an app made Id.</summary>
internal sealed class OwnedCollectionContext(DbContextOptions<OwnedCollectionContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Order>(order =>
        {
            order.OwnsMany(x => x.Lines);
            order.OwnsMany(x => x.KeyedLines, line => line.HasKey(x => x.Id));
        });
}
