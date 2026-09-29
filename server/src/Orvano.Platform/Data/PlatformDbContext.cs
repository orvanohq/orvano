using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Orvano.Platform.Data;

/// <summary>
/// EF Core model of the Platform module's tables in schema <c>orvano</c> (spec 0003). Internal to the module, so no
/// other module can query them. Tables change only through the SQL migrations; the drift check keeps this model
/// matching them. Each unit of work binds a context to its own Npgsql connection and transaction, so outbox events
/// and jobs commit together with the change.
/// </summary>
internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<InstallSettingsRow> InstallSettings => Set<InstallSettingsRow>();

    public DbSet<InstallAdminRow> InstallAdmins => Set<InstallAdminRow>();

    public DbSet<OrgRow> Orgs => Set<OrgRow>();

    public DbSet<MembershipRow> Memberships => Set<MembershipRow>();

    public DbSet<ProjectRow> Projects => Set<ProjectRow>();

    public DbSet<ApiKeyRow> ApiKeys => Set<ApiKeyRow>();

    public DbSet<PlatformRow> Platforms => Set<PlatformRow>();

    public DbSet<InvitationRow> Invitations => Set<InvitationRow>();

    /// <summary>A context on an open connection the caller owns; it never opens or closes it.</summary>
    public static PlatformDbContext On(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection).Options);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("orvano");

        model.Entity<InstallSettingsRow>(e =>
        {
            e.ToTable("platform_install_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.ConsoleSignup).HasColumnName("console_signup");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        model.Entity<InstallAdminRow>(e =>
        {
            e.ToTable("platform_install_admins");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        model.Entity<OrgRow>(e =>
        {
            e.ToTable("platform_orgs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            e.Property(x => x.PurgeAfter).HasColumnName("purge_after");
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        });

        model.Entity<MembershipRow>(e =>
        {
            e.ToTable("platform_memberships");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.OrgId).HasColumnName("org_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasOne<OrgRow>().WithMany().HasForeignKey(x => x.OrgId);
        });

        model.Entity<ProjectRow>(e =>
        {
            e.ToTable("platform_projects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.OrgId).HasColumnName("org_id");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            e.Property(x => x.PurgeAfter).HasColumnName("purge_after");
            e.Property(x => x.PurgeFailedAt).HasColumnName("purge_failed_at");
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasOne<OrgRow>().WithMany().HasForeignKey(x => x.OrgId);
        });

        model.Entity<ApiKeyRow>(e =>
        {
            e.ToTable("platform_api_keys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Prefix).HasColumnName("prefix");
            e.Property(x => x.SecretHash).HasColumnName("secret_hash");
            e.Property(x => x.Scopes).HasColumnName("scopes");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne<ProjectRow>().WithMany().HasForeignKey(x => x.ProjectId);
        });

        model.Entity<PlatformRow>(e =>
        {
            e.ToTable("platform_platforms");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Identifier).HasColumnName("identifier");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasOne<ProjectRow>().WithMany().HasForeignKey(x => x.ProjectId);
        });

        model.Entity<InvitationRow>(e =>
        {
            e.ToTable("platform_invitations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.OrgId).HasColumnName("org_id");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.TokenHash).HasColumnName("token_hash");
            e.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne<OrgRow>().WithMany().HasForeignKey(x => x.OrgId);
        });
    }
}

/// <summary><c>platform_install_settings</c>: exactly one row, <c>id = 1</c>.</summary>
internal sealed class InstallSettingsRow
{
    public short Id { get; set; }

    /// <summary><c>invite</c> or <c>open</c>.</summary>
    public required string ConsoleSignup { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>platform_install_admins</c>: a console user by ID, no foreign key across modules.</summary>
internal sealed class InstallAdminRow
{
    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>platform_orgs</c>.</summary>
internal sealed class OrgRow
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary><c>active</c> or <c>deleting</c>.</summary>
    public required string Status { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset? PurgeAfter { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>platform_memberships</c>: one role per console user per org.</summary>
internal sealed class MembershipRow
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }

    public Guid UserId { get; set; }

    /// <summary><c>owner</c>, <c>developer</c>, or <c>viewer</c>.</summary>
    public required string Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>platform_projects</c>.</summary>
internal sealed class ProjectRow
{
    public required string Id { get; set; }

    public Guid? OrgId { get; set; }

    /// <summary><c>app</c> or <c>system</c>.</summary>
    public required string Kind { get; set; }

    public required string Name { get; set; }

    /// <summary><c>provisioning</c>, <c>active</c>, <c>failed</c>, or <c>deleting</c>.</summary>
    public required string Status { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset? PurgeAfter { get; set; }

    public DateTimeOffset? PurgeFailedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>platform_api_keys</c>: the hash and prefix of a secret, never the secret.</summary>
internal sealed class ApiKeyRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    public required string Name { get; set; }

    public required string Prefix { get; set; }

    public required byte[] SecretHash { get; set; }

    public required string[] Scopes { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>platform_platforms</c>.</summary>
internal sealed class PlatformRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    /// <summary><c>web</c>, <c>android</c>, <c>ios</c>, <c>macos</c>, <c>windows</c>, or <c>linux</c>.</summary>
    public required string Type { get; set; }

    public required string Name { get; set; }

    public required string Identifier { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>platform_invitations</c> (spec 0008): the hash of an invite token, never the token.</summary>
internal sealed class InvitationRow
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }

    /// <summary>Trimmed, as typed. Personal data: never log it.</summary>
    public required string Email { get; set; }

    /// <summary><c>owner</c>, <c>developer</c>, or <c>viewer</c>.</summary>
    public required string Role { get; set; }

    public required byte[] TokenHash { get; set; }

    public Guid InvitedByUserId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
