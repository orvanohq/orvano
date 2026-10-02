using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Orvano.Auth.Data;

/// <summary>
/// EF Core model of the Auth module's tables in schema <c>orvano</c> (spec 0004). Internal to the module, so no other
/// module can query them. Tables change only through the SQL migrations; the drift check keeps this model matching
/// them. Each unit of work binds a context to its own connection and transaction, so events commit with the change.
/// </summary>
internal sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<UserRow> Users => Set<UserRow>();

    public DbSet<PasswordRow> Passwords => Set<PasswordRow>();

    public DbSet<SessionRow> Sessions => Set<SessionRow>();

    public DbSet<SigningKeyRow> SigningKeys => Set<SigningKeyRow>();

    public DbSet<EmailTokenRow> EmailTokens => Set<EmailTokenRow>();

    /// <summary>A context on an open connection the caller owns; it never opens or closes it.</summary>
    public static AuthDbContext On(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(connection).Options);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("orvano");

        model.Entity<UserRow>(e =>
        {
            e.ToTable("auth_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.EmailVerifiedAt).HasColumnName("email_verified_at");
            e.Property(x => x.Phone).HasColumnName("phone");
            e.Property(x => x.PhoneVerifiedAt).HasColumnName("phone_verified_at");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            e.Property(x => x.LastSignInAt).HasColumnName("last_sign_in_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        });

        model.Entity<PasswordRow>(e =>
        {
            e.ToTable("auth_passwords");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Hash).HasColumnName("hash");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasOne<UserRow>().WithOne().HasForeignKey<PasswordRow>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SessionRow>(e =>
        {
            e.ToTable("auth_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.RefreshHash).HasColumnName("refresh_hash");
            e.Property(x => x.RefreshCiphertext).HasColumnName("refresh_ciphertext");
            e.Property(x => x.PreviousRefreshHash).HasColumnName("previous_refresh_hash");
            e.Property(x => x.RotatedAt).HasColumnName("rotated_at");
            e.Property(x => x.UserAgent).HasColumnName("user_agent");
            e.Property(x => x.Sdk).HasColumnName("sdk");
            e.Property(x => x.IpCreated).HasColumnName("ip_created");
            e.Property(x => x.IpLast).HasColumnName("ip_last");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.LastRefreshedAt).HasColumnName("last_refreshed_at").HasDefaultValueSql("now()");
            e.Property(x => x.IdleExpiresAt).HasColumnName("idle_expires_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.EndedAt).HasColumnName("ended_at");
            e.Property(x => x.EndReason).HasColumnName("end_reason");
            e.Property(x => x.Method).HasColumnName("method").HasDefaultValueSql("'password'");
            e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<EmailTokenRow>(e =>
        {
            e.ToTable("auth_email_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.SecretHash).HasColumnName("secret_hash");
            e.Property(x => x.MacKeyId).HasColumnName("mac_key_id");
            e.Property(x => x.Attempts).HasColumnName("attempts");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SigningKeyRow>(e =>
        {
            e.ToTable("auth_signing_keys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Alg).HasColumnName("alg");
            e.Property(x => x.PublicJwk).HasColumnName("public_jwk").HasColumnType("jsonb");
            e.Property(x => x.PrivateKeyCiphertext).HasColumnName("private_key_ciphertext");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.RetireAfter).HasColumnName("retire_after");
        });
    }
}

/// <summary><c>auth_users</c>: an app user, or a console account in project <c>console</c>.</summary>
internal sealed class UserRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    /// <summary>As typed, trimmed. Personal data: never log it.</summary>
    public string? Email { get; set; }

    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public string? Phone { get; set; }

    public DateTimeOffset? PhoneVerifiedAt { get; set; }

    public string? Name { get; set; }

    /// <summary><c>active</c> or <c>blocked</c>.</summary>
    public required string Status { get; set; }

    /// <summary>A JSON object, as text.</summary>
    public required string Metadata { get; set; }

    public DateTimeOffset? LastSignInAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>auth_passwords</c>: the Argon2id hash of a user's password.</summary>
internal sealed class PasswordRow
{
    public Guid UserId { get; set; }

    public required string ProjectId { get; set; }

    public required string Hash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>auth_sessions</c>: one sign in, and its refresh token as a hash plus an encrypted copy.</summary>
internal sealed class SessionRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    public Guid UserId { get; set; }

    public required byte[] RefreshHash { get; set; }

    public required byte[] RefreshCiphertext { get; set; }

    public byte[]? PreviousRefreshHash { get; set; }

    public DateTimeOffset? RotatedAt { get; set; }

    public string? UserAgent { get; set; }

    public string? Sdk { get; set; }

    public IPAddress? IpCreated { get; set; }

    public IPAddress? IpLast { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastRefreshedAt { get; set; }

    public DateTimeOffset IdleExpiresAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>One of <see cref="Domain.SessionEndReason"/>, set together with <see cref="EndedAt"/>.</summary>
    public string? EndReason { get; set; }

    /// <summary>How the session began, one of <see cref="Domain.SessionMethod"/>.</summary>
    public required string Method { get; set; }
}

/// <summary>
/// <c>auth_email_tokens</c> (spec 0010): one live email link or code. The secret is stored only as a hash, and
/// redeeming deletes the row.
/// </summary>
internal sealed class EmailTokenRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    /// <summary>One of <see cref="Domain.EmailTokenKinds"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>Null only for a magic link or code sent to an email with no user yet.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Where the email went. Personal data: never log it.</summary>
    public required string Email { get; set; }

    /// <summary>Links: SHA-256 of the token; codes: the HMAC.</summary>
    public required byte[] SecretHash { get; set; }

    /// <summary>The master key ID of a code's HMAC; null for links.</summary>
    public string? MacKeyId { get; set; }

    public short Attempts { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>auth_signing_keys</c>: a project's ES256 key pair, the private half envelope encrypted.</summary>
internal sealed class SigningKeyRow
{
    public required string Id { get; set; }

    public required string ProjectId { get; set; }

    public required string Alg { get; set; }

    /// <summary>The public JWK as JSON text.</summary>
    public required string PublicJwk { get; set; }

    public required byte[] PrivateKeyCiphertext { get; set; }

    /// <summary><c>active</c> or <c>retiring</c>.</summary>
    public required string Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RetireAfter { get; set; }
}
