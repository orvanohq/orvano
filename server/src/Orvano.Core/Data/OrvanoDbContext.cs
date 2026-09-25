using Microsoft.EntityFrameworkCore;

namespace Orvano.Core.Data;

/// <summary>
/// EF Core model for fixed platform tables in schema <c>orvano</c>. Tables change only through the SQL
/// files in <c>server/migrations/platform/</c>; the CI drift check keeps this model matching them.
/// </summary>
public sealed class OrvanoDbContext(DbContextOptions<OrvanoDbContext> options) : DbContext(options)
{
    /// <summary><c>orvano.schema_migrations</c>: one row per applied platform migration.</summary>
    public DbSet<SchemaMigrationRow> SchemaMigrations => Set<SchemaMigrationRow>();

    /// <summary><c>orvano.events</c>: the transactional outbox.</summary>
    public DbSet<EventRow> Events => Set<EventRow>();

    /// <summary><c>orvano.jobs</c>: the job queue.</summary>
    public DbSet<JobRow> Jobs => Set<JobRow>();

    /// <inheritdoc />
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

/// <summary>A row of <c>orvano.schema_migrations</c>. Only the <c>migrate</c> role writes these.</summary>
public sealed class SchemaMigrationRow
{
    /// <summary>The migration number, the <c>NNNN</c> in <c>NNNN_name.sql</c>.</summary>
    public int Version { get; set; }

    /// <summary>The migration file name without its number or extension.</summary>
    public required string Name { get; set; }

    /// <summary>Hex SHA-256 of the file's LF normalized text, so an edited migration is caught.</summary>
    public required string Sha256 { get; set; }

    /// <summary>When the migration was applied.</summary>
    public DateTimeOffset AppliedAt { get; set; }
}

/// <summary>A row of <c>orvano.events</c>, the outbox. Write through <see cref="Events.Outbox"/>, not EF.</summary>
public sealed class EventRow
{
    /// <summary>Identity column; also the dispatch order.</summary>
    public long Id { get; set; }

    /// <summary>The project the event belongs to, or <see langword="null"/> for a platform event.</summary>
    public string? ProjectId { get; set; }

    /// <summary>The event type name that consumers subscribe to.</summary>
    public required string Type { get; set; }

    /// <summary>The ID of the thing the event is about, if any.</summary>
    public string? Subject { get; set; }

    /// <summary>The event body as <c>jsonb</c>. Never log it.</summary>
    public required string Payload { get; set; }

    /// <summary>When the event was written.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the dispatcher ran its consumers, or <see langword="null"/> while it awaits dispatch.</summary>
    public DateTimeOffset? DispatchedAt { get; set; }
}

/// <summary>A row of <c>orvano.jobs</c>. Enqueue through <see cref="Jobs.JobQueue"/>, not EF.</summary>
public sealed class JobRow
{
    /// <summary>Identity column.</summary>
    public long Id { get; set; }

    /// <summary>The queue a worker claims from (see <see cref="Jobs.JobQueues"/>).</summary>
    public required string Queue { get; set; }

    /// <summary>Selects the handler, for example <c>events.redispatch</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>The project the job runs for, or <see langword="null"/> for platform work.</summary>
    public string? ProjectId { get; set; }

    /// <summary>The handler's input as <c>jsonb</c>. Never log it.</summary>
    public required string Payload { get; set; }

    /// <summary>Lower runs first.</summary>
    public int Priority { get; set; }

    /// <summary>The job is not claimed before this time.</summary>
    public DateTimeOffset RunAt { get; set; }

    /// <summary>One of <c>queued</c>, <c>running</c>, <c>succeeded</c>, <c>failed</c>, or <c>dead</c>.</summary>
    public required string Status { get; set; }

    /// <summary>How many times a worker has claimed the job.</summary>
    public int Attempts { get; set; }

    /// <summary>After this many attempts a failing job is marked <c>dead</c>.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>While <c>running</c>, the lease expiry; the reaper requeues a job whose lease lapsed.</summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    /// <summary>The worker that holds the lease.</summary>
    public string? LockedBy { get; set; }

    /// <summary>The last failure reason. Never includes the payload.</summary>
    public string? LastError { get; set; }

    /// <summary>When the job was enqueued.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the job finished as <c>succeeded</c> or <c>dead</c>.</summary>
    public DateTimeOffset? FinishedAt { get; set; }
}
