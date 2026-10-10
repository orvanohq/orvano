using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Orvano.Databases.Data;

/// <summary>A named database of a project (spec 0015, data model): one row per database besides <c>main</c>.</summary>
internal sealed class DatabaseRow
{
    public required string Id { get; init; }

    public required string ProjectId { get; init; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public required string SchemaName { get; init; }

    /// <summary><c>provisioning</c> or <c>active</c>; the API's <c>failed</c> is provisioning plus <see cref="ProvisionFailedAt"/>.</summary>
    public required string Status { get; set; }

    public DateTimeOffset? ProvisionFailedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// EF Core model of the Databases module's table in schema <c>orvano</c> (spec 0015). Internal to the module, so no
/// other module can query it. It changes only through the SQL migrations; the drift check keeps this model matching
/// them. Project tables are never mapped here: they are read live from <c>pg_catalog</c>.
/// </summary>
internal sealed class DatabasesDbContext(DbContextOptions<DatabasesDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseRow> Databases => Set<DatabaseRow>();

    /// <summary>A context on an open connection the caller owns; it never opens or closes it.</summary>
    public static DatabasesDbContext On(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<DatabasesDbContext>().UseNpgsql(connection).Options);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("orvano");

        model.Entity<DatabaseRow>(e =>
        {
            e.ToTable("db_databases");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Slug).HasColumnName("slug");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.SchemaName).HasColumnName("schema_name");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.ProvisionFailedAt).HasColumnName("provision_failed_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.ProjectId, x.Slug }).IsUnique();
            e.HasIndex(x => x.SchemaName).IsUnique();
        });
    }
}
