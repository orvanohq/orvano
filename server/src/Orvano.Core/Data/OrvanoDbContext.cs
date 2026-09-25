using Microsoft.EntityFrameworkCore;

namespace Orvano.Core.Data;

/// <summary>
/// EF Core model for fixed platform tables in schema <c>orvano</c>. Tables change only through the SQL
/// files in <c>server/migrations/platform/</c>; the CI drift check keeps this model matching them.
/// </summary>
public sealed class OrvanoDbContext(DbContextOptions<OrvanoDbContext> options) : DbContext(options)
{
    public DbSet<SchemaMigrationRow> SchemaMigrations => Set<SchemaMigrationRow>();
    public DbSet<EventRow> Events => Set<EventRow>();
    public DbSet<JobRow> Jobs => Set<JobRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("orvano");

        model.Entity<SchemaMigrationRow>(e =>
        {
            e.ToTable("schema_migrations");
            e.HasKey(x => x.Version);
            e.Property(x => x.Version).HasColumnName("version").ValueGeneratedNever();
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Sha256).HasColumnName("sha256");
            e.Property(x => x.AppliedAt).HasColumnName("applied_at");
        });

        model.Entity<EventRow>(e =>
        {
            e.ToTable("events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Subject).HasColumnName("subject");
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.DispatchedAt).HasColumnName("dispatched_at");
        });

        model.Entity<JobRow>(e =>
        {
            e.ToTable("jobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Queue).HasColumnName("queue");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.RunAt).HasColumnName("run_at");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Attempts).HasColumnName("attempts");
            e.Property(x => x.MaxAttempts).HasColumnName("max_attempts");
            e.Property(x => x.LeaseUntil).HasColumnName("lease_until");
            e.Property(x => x.LockedBy).HasColumnName("locked_by");
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.FinishedAt).HasColumnName("finished_at");
        });
    }
}

public sealed class SchemaMigrationRow
{
    public int Version { get; set; }
    public required string Name { get; set; }
    public required string Sha256 { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}

public sealed class EventRow
{
    public long Id { get; set; }
    public string? ProjectId { get; set; }
    public required string Type { get; set; }
    public string? Subject { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}

public sealed class JobRow
{
    public long Id { get; set; }
    public required string Queue { get; set; }
    public required string Kind { get; set; }
    public string? ProjectId { get; set; }
    public required string Payload { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public required string Status { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
