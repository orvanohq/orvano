using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Data;

namespace Orvano.Modules.Platform.Data;

/// <summary>
/// The <c>platform</c> schema (spec 0011 MOD-1). Public so <c>MigrationOrder</c> can name it; after <c>jobs</c> there
/// (MOD-2). Spec 0017's data model adds the CAT-1 tables beside <c>platform.instance</c>. The <c>"project"</c> filter
/// reads <paramref name="project"/>, which is null only where no access layer exists (the migration tools, host commands).
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, IProjectContext? project = null) : DbContext(options)
{
    public const string Schema = "platform";

    private readonly IProjectContext? _project = project;

    internal DbSet<Instance> Instances => Set<Instance>();

    internal DbSet<Operator> Operators => Set<Operator>();

    internal DbSet<OperatorSession> OperatorSessions => Set<OperatorSession>();

    internal DbSet<OperatorResetToken> OperatorResetTokens => Set<OperatorResetToken>();

    internal DbSet<Organization> Organizations => Set<Organization>();

    internal DbSet<Membership> Memberships => Set<Membership>();

    internal DbSet<Project> Projects => Set<Project>();

    internal DbSet<ProjectSettings> ProjectSettings => Set<ProjectSettings>();

    internal DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Spec 0011 CAT-1 and IDS-1: a singleton, keyed 1 and never made by the database.
        modelBuilder.Entity<Instance>(instance =>
        {
            instance.ToTable("instance", table => table.HasCheckConstraint("ck_instance_id", "id = 1"));
            instance.Property(x => x.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Operator>(account =>
        {
            account.ToTable("operator", table =>
            {
                table.HasCheckConstraint("ck_operator_email_length", $"char_length(email) <= {Operator.MaxEmailLength}");
                table.HasCheckConstraint("ck_operator_name_length", $"char_length(name) BETWEEN 1 AND {Operator.MaxNameLength}");
                table.HasCheckConstraint("ck_operator_locale_length", $"char_length(locale) <= {Operator.MaxLocaleLength}");
            });
            account.Property(x => x.Status).HasTextEnum();
            account.HasIndex(x => x.EmailNormalized).IsUnique();
        });

        modelBuilder.Entity<OperatorSession>(session =>
        {
            session.ToTable("operator_session", table =>
            {
                table.HasCheckConstraint("ck_operator_session_token_hash_length", "octet_length(token_hash) = 32");
                table.HasCheckConstraint("ck_operator_session_csrf_token_length", "octet_length(csrf_token) = 32");
                table.HasCheckConstraint("ck_operator_session_user_agent_length", $"char_length(user_agent) <= {OperatorSession.MaxUserAgentLength}");
            });
            session.HasOne<Operator>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Cascade);
            session.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelBuilder.Entity<OperatorResetToken>(token =>
        {
            token.ToTable("operator_reset_token", table =>
                table.HasCheckConstraint("ck_operator_reset_token_token_hash_length", "octet_length(token_hash) = 32"));
            token.HasOne<Operator>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Cascade);
            token.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelBuilder.Entity<Organization>(organization =>
            organization.ToTable("organization", table =>
                table.HasCheckConstraint("ck_organization_name_length", $"char_length(name) BETWEEN 1 AND {Organization.MaxNameLength}")));

        modelBuilder.Entity<Membership>(membership =>
        {
            // Feature 51 adds the other roles to this CHECK (spec 0012 CPL-2).
            membership.ToTable("membership", table => table.HasCheckConstraint("ck_membership_role", $"role IN ('{RolePermissions.Owner}')"));
            membership.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            membership.HasOne<Operator>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Cascade);
            membership.HasIndex(x => new { x.OrganizationId, x.OperatorId }).IsUnique();
        });

        modelBuilder.Entity<Project>(project =>
        {
            project.ToTable("project", table =>
            {
                table.HasCheckConstraint("ck_project_name_length", $"char_length(name) BETWEEN 1 AND {Project.MaxNameLength}");
                table.HasCheckConstraint("ck_project_deletion_requested_at", "(status = 'deleting') = (deletion_requested_at IS NOT NULL)");
            });
            project.Property(x => x.Status).HasTextEnum();

            // LIF-3: an organization with projects can't be removed by accident.
            project.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            project.HasIndex(x => new { x.OrganizationId, x.Id });
        });

        modelBuilder.Entity<ProjectSettings>(settings =>
        {
            settings.ToTable("project_settings", table =>
                table.HasCheckConstraint("ck_project_settings_allowed_origins_count", $"cardinality(allowed_origins) <= {Data.ProjectSettings.MaxAllowedOrigins}"));
            settings.HasKey(x => x.ProjectId);
            settings.Property(x => x.AllowedOrigins).HasDefaultValueSql("'{}'");
            settings.HasOne<Project>().WithOne().HasForeignKey<ProjectSettings>(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            settings.HasQueryFilter(ProjectFilter.Name, x => x.ProjectId == _project!.Id);
        });

        modelBuilder.Entity<ApiKey>(key =>
        {
            key.ToTable("api_key", table =>
            {
                table.HasCheckConstraint("ck_api_key_name_length", $"char_length(name) BETWEEN 1 AND {ApiKey.MaxNameLength}");
                table.HasCheckConstraint("ck_api_key_hash_length", "octet_length(hash) = 32");
                table.HasCheckConstraint("ck_api_key_prefix_length", "char_length(prefix) = 15");
                table.HasCheckConstraint("ck_api_key_last4_length", "char_length(last4) = 4");
                table.HasCheckConstraint("ck_api_key_scopes_present", "cardinality(scopes) >= 1");
                table.HasCheckConstraint("ck_api_key_scopes_publishable", CeilingCheck(KeyType.Publishable));
                table.HasCheckConstraint("ck_api_key_scopes_secret", CeilingCheck(KeyType.Secret));
            });
            key.Property(x => x.Type).HasTextEnum();
            key.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            key.HasOne<Operator>().WithMany().HasForeignKey(x => x.CreatedByOperatorId).OnDelete(DeleteBehavior.SetNull);
            key.HasIndex(x => x.Hash).IsUnique();
            key.HasIndex(x => new { x.ProjectId, x.Id });
            key.HasQueryFilter(ProjectFilter.Name, x => x.ProjectId == _project!.Id);
        });
    }

    // SCP-3: a key's scopes stay inside its type's ceiling. A new scope is a code change plus this CHECK in a migration.
    private static string CeilingCheck(KeyType type)
    {
        string values = string.Join(", ", Scopes.CeilingFor(type).Order(StringComparer.Ordinal).Select(scope => $"'{scope}'"));
        return $"type <> '{TextEnumMapping.ToStored(type)}' OR scopes <@ ARRAY[{values}]::text[]";
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
