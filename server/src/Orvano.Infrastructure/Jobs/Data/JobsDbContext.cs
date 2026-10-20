using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.SharedKernel.Data;

namespace Orvano.Infrastructure.Jobs.Data;

/// <summary>
/// The <c>jobs</c> schema: the queue and the schedules' state (spec 0013 JDB-1). Public so <c>MigrationOrder</c> can
/// name it; first in that order (spec 0011 MOD-2). Rows change only through the queue's and runner's SQL.
/// </summary>
public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    public const string Schema = "jobs";

    internal DbSet<Job> Jobs => Set<Job>();

    internal DbSet<JobSchedule> Schedules => Set<JobSchedule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // JDB-2: the queue row. Claims and outcomes change it often, so leave room for updates in each page (JOB-1).
        modelBuilder.Entity<Job>(job =>
        {
            job.ToTable("job", table =>
            {
                table.HasCheckConstraint("ck_job_kind", "char_length(kind) BETWEEN 3 AND 100");
                table.HasCheckConstraint("ck_job_attempt", "attempt >= 0 AND attempt <= max_attempts");
                table.HasCheckConstraint("ck_job_max_attempts", "max_attempts BETWEEN 1 AND 100");
                table.HasCheckConstraint("ck_job_dedupe_key", "char_length(dedupe_key) BETWEEN 1 AND 200");
                table.HasCheckConstraint("ck_job_locked_by", "char_length(locked_by) <= 100");
                table.HasCheckConstraint("ck_job_last_error", "char_length(last_error) <= 2000");
                table.HasCheckConstraint("ck_job_trace_parent", "char_length(trace_parent) <= 55");
                table.HasCheckConstraint("ck_job_lease", "(status = 'running') = (locked_until IS NOT NULL AND locked_by IS NOT NULL)");
                table.HasCheckConstraint("ck_job_finished", "(status IN ('succeeded', 'dead')) = (finished_at IS NOT NULL)");
            });
            job.HasStorageParameter("fillfactor", 70);
            job.HasStorageParameter("autovacuum_vacuum_scale_factor", 0.05);

            job.Property(x => x.Kind).IsRequired();
            job.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            job.Property(x => x.Status).HasTextEnum();

            // A plain unique index: nulls are distinct, so rows without a key never collide, and ON CONFLICT
            // (dedupe_key) needs no WHERE.
            job.HasIndex(x => x.DedupeKey).IsUnique().HasDatabaseName("ux_job_dedupe_key");
            // Due rows and leased rows each get their own index, so an idle poll is a short range scan however many
            // future jobs wait (CLM-1).
            job.HasIndex(x => new { x.RunAt, x.Id }).HasDatabaseName("ix_job_due").HasFilter("status = 'queued'");
            job.HasIndex(x => new { x.LockedUntil, x.Id }).HasDatabaseName("ix_job_lease").HasFilter("status = 'running'");
            job.HasIndex(x => x.FinishedAt).HasDatabaseName("ix_job_finished").HasFilter("status IN ('succeeded', 'dead')");
            job.HasIndex(x => x.ProjectId).HasDatabaseName("ix_job_project")
                .HasFilter("project_id IS NOT NULL AND status IN ('queued', 'running')");
        });

        // JDB-3: only what code cannot hold, the schedule's state; its kind and payload come from its registration.
        modelBuilder.Entity<JobSchedule>(schedule =>
        {
            schedule.ToTable("schedule", table =>
            {
                table.HasCheckConstraint("ck_schedule_name", "char_length(name) BETWEEN 3 AND 100");
                table.HasCheckConstraint("ck_schedule_cron", "char_length(cron) BETWEEN 9 AND 100");
            });
            schedule.Property(x => x.Name).IsRequired();
            schedule.Property(x => x.Cron).IsRequired();
            schedule.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_schedule_name");
            schedule.HasIndex(x => x.NextRunAt).HasDatabaseName("ix_schedule_due").HasFilter("enabled");
        });
    }
}

/// <summary>
/// For <c>dotnet ef migrations</c> (spec 0002 DAT-10): the context on a placeholder data source, which the tools never
/// open to build a migration.
/// </summary>
internal sealed class JobsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<JobsDbContext>
{
    public JobsDbContext CreateDbContext(string[] args)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(NpgsqlDataSource.Create("Host=localhost;Database=orvano"))
            .AddSingleton(TimeProvider.System)
            .BuildServiceProvider();
        return new JobsDbContext(new DbContextOptionsBuilder<JobsDbContext>().UseOrvanoConventions(JobsDbContext.Schema, services).Options);
    }
}
