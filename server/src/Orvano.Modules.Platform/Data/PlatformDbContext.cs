using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary>
/// The <c>platform</c> schema (spec 0011 MOD-1). Public so <c>MigrationOrder</c> can name it; after <c>jobs</c> there
/// (MOD-2). Feature 12 maps <c>platform.instance</c> only (spec 0016).
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    internal DbSet<Instance> Instances => Set<Instance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Spec 0011 CAT-1 and IDS-1: a singleton, keyed 1 and never made by the database.
        modelBuilder.Entity<Instance>(instance =>
        {
            instance.ToTable("instance", table => table.HasCheckConstraint("ck_instance_id", "id = 1"));
            instance.Property(x => x.Id).ValueGeneratedNever();
        });
    }
}

/// <summary>
/// For <c>dotnet ef migrations</c> (spec 0002 DAT-10): the context on a placeholder data source, which the tools never
/// open to build a migration.
/// </summary>
internal sealed class PlatformDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(NpgsqlDataSource.Create("Host=localhost;Database=orvano"))
            .AddSingleton(TimeProvider.System)
            .BuildServiceProvider();
        return new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseOrvanoConventions(PlatformDbContext.Schema, services).Options);
    }
}
