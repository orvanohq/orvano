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

    public DbSet<OAuthProviderRow> OAuthProviders => Set<OAuthProviderRow>();

    public DbSet<IdentityRow> Identities => Set<IdentityRow>();

    public DbSet<OAuthFlowRow> OAuthFlows => Set<OAuthFlowRow>();

    public DbSet<IdTokenUseRow> IdTokenUses => Set<IdTokenUseRow>();

    public DbSet<MethodSettingsRow> MethodSettings => Set<MethodSettingsRow>();

    public DbSet<TotpFactorRow> TotpFactors => Set<TotpFactorRow>();

    public DbSet<RecoveryCodeRow> RecoveryCodes => Set<RecoveryCodeRow>();

    public DbSet<PasskeyRow> Passkeys => Set<PasskeyRow>();

    public DbSet<MfaTicketRow> MfaTickets => Set<MfaTicketRow>();

    public DbSet<WebAuthnChallengeRow> WebAuthnChallenges => Set<WebAuthnChallengeRow>();

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
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.Aal).HasColumnName("aal").HasDefaultValue((short)1);
            e.Property(x => x.Amr).HasColumnName("amr").HasDefaultValueSql("'{}'");
            e.Property(x => x.StrongAuthAt).HasColumnName("strong_auth_at");
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

        model.Entity<OAuthProviderRow>(e =>
        {
            e.ToTable("auth_oauth_providers");
            e.HasKey(x => new { x.ProjectId, x.Provider });
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.Enabled).HasColumnName("enabled");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ClientSecretCiphertext).HasColumnName("client_secret_ciphertext");
            e.Property(x => x.ClientIdsExtra).HasColumnName("client_ids_extra");
            e.Property(x => x.AppleTeamId).HasColumnName("apple_team_id");
            e.Property(x => x.AppleKeyId).HasColumnName("apple_key_id");
            e.Property(x => x.ApplePrivateKeyCiphertext).HasColumnName("apple_private_key_ciphertext");
            e.Property(x => x.MicrosoftTenant).HasColumnName("microsoft_tenant");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        });

        model.Entity<IdentityRow>(e =>
        {
            e.ToTable("auth_identities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.Subject).HasColumnName("subject");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.EmailVerified).HasColumnName("email_verified");
            e.Property(x => x.ProviderRefreshCiphertext).HasColumnName("provider_refresh_ciphertext");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.LastSignInAt).HasColumnName("last_sign_in_at");
            e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<OAuthFlowRow>(e =>
        {
            e.ToTable("auth_oauth_flows");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.Purpose).HasColumnName("purpose");
            e.Property(x => x.LinkUserId).HasColumnName("link_user_id");
            e.Property(x => x.StateHash).HasColumnName("state_hash");
            e.Property(x => x.RedirectUrl).HasColumnName("redirect_url");
            e.Property(x => x.CodeChallenge).HasColumnName("code_challenge");
            e.Property(x => x.ProviderVerifierCiphertext).HasColumnName("provider_verifier_ciphertext");
            e.Property(x => x.NonceHash).HasColumnName("nonce_hash");
            e.Property(x => x.ResultCiphertext).HasColumnName("result_ciphertext");
            e.Property(x => x.CodeHash).HasColumnName("code_hash");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.LinkUserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<IdTokenUseRow>(e =>
        {
            e.ToTable("auth_id_token_uses");
            e.HasKey(x => x.TokenHash);
            e.Property(x => x.TokenHash).HasColumnName("token_hash");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        });

        model.Entity<MethodSettingsRow>(e =>
        {
            e.ToTable("auth_method_settings");
            e.HasKey(x => x.ProjectId);
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.TotpEnabled).HasColumnName("totp_enabled");
            e.Property(x => x.PasskeysEnabled).HasColumnName("passkeys_enabled");
            e.Property(x => x.RpId).HasColumnName("rp_id");
            e.Property(x => x.RpName).HasColumnName("rp_name");
            e.Property(x => x.AndroidCertFingerprints).HasColumnName("android_cert_fingerprints");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        });

        model.Entity<TotpFactorRow>(e =>
        {
            e.ToTable("auth_totp_factors");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.SecretCiphertext).HasColumnName("secret_ciphertext");
            e.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
            e.Property(x => x.LastUsedStep).HasColumnName("last_used_step");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasOne<UserRow>().WithOne().HasForeignKey<TotpFactorRow>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<RecoveryCodeRow>(e =>
        {
            e.ToTable("auth_recovery_codes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.CodeMac).HasColumnName("code_mac");
            e.Property(x => x.MacKeyId).HasColumnName("mac_key_id");
            e.Property(x => x.UsedAt).HasColumnName("used_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<PasskeyRow>(e =>
        {
            e.ToTable("auth_passkeys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.CredentialId).HasColumnName("credential_id");
            e.Property(x => x.PublicKey).HasColumnName("public_key");
            e.Property(x => x.SignCount).HasColumnName("sign_count");
            e.Property(x => x.Aaguid).HasColumnName("aaguid");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Transports).HasColumnName("transports");
            e.Property(x => x.BackupEligible).HasColumnName("backup_eligible");
            e.Property(x => x.BackedUp).HasColumnName("backed_up");
            e.Property(x => x.RpId).HasColumnName("rp_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<MfaTicketRow>(e =>
        {
            e.ToTable("auth_mfa_tickets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.TicketHash).HasColumnName("ticket_hash");
            e.Property(x => x.Method).HasColumnName("method");
            e.Property(x => x.PendingPasswordHash).HasColumnName("pending_password_hash");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.UserAgent).HasColumnName("user_agent");
            e.Property(x => x.Sdk).HasColumnName("sdk");
            e.Property(x => x.Ip).HasColumnName("ip");
            e.Property(x => x.Attempts).HasColumnName("attempts");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<WebAuthnChallengeRow>(e =>
        {
            e.ToTable("auth_webauthn_challenges");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Purpose).HasColumnName("purpose");
            e.Property(x => x.ChallengeHash).HasColumnName("challenge_hash");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.TicketId).HasColumnName("ticket_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MfaTicketRow>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
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

    /// <summary>The provider of an <c>oauth</c> or <c>id_token</c> session (spec 0012); null for every other method.</summary>
    public string? Provider { get; set; }

    /// <summary>The assurance level, 1 or 2 (spec 0013, AC-25); never goes back to 1.</summary>
    public short Aal { get; set; } = 1;

    /// <summary>The authentication methods, sorted (spec 0013, AC-25).</summary>
    public string[] Amr { get; set; } = [];

    /// <summary>The last MFA or passkey check on this session; only moves forward.</summary>
    public DateTimeOffset? StrongAuthAt { get; set; }
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

/// <summary><c>auth_oauth_providers</c> (spec 0012): one project's settings for one provider, its secrets sealed.</summary>
internal sealed class OAuthProviderRow
{
    public required string ProjectId { get; set; }

    /// <summary>One of <see cref="Domain.OAuthProviders"/>.</summary>
    public required string Provider { get; set; }

    public bool Enabled { get; set; }

    public string? ClientId { get; set; }

    /// <summary>Sealed with associated data <c>auth_oauth_providers:&lt;project&gt;:&lt;provider&gt;:client_secret_ciphertext</c>.</summary>
    public byte[]? ClientSecretCiphertext { get; set; }

    public required string[] ClientIdsExtra { get; set; }

    public string? AppleTeamId { get; set; }

    public string? AppleKeyId { get; set; }

    /// <summary>Sealed PKCS#8 PEM.</summary>
    public byte[]? ApplePrivateKeyCiphertext { get; set; }

    public string? MicrosoftTenant { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>auth_identities</c> (spec 0012): a provider account linked to a user.</summary>
internal sealed class IdentityRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    public Guid UserId { get; set; }

    public required string Provider { get; set; }

    /// <summary>The provider's subject. Personal data: never log it.</summary>
    public required string Subject { get; set; }

    /// <summary>The provider's email. Personal data: never log it.</summary>
    public string? Email { get; set; }

    public bool EmailVerified { get; set; }

    /// <summary>Apple only: the sealed <see cref="Domain.AppleGrant"/>.</summary>
    public byte[]? ProviderRefreshCiphertext { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSignInAt { get; set; }
}

/// <summary><c>auth_oauth_flows</c> (spec 0012): one redirect flow, from start to redemption.</summary>
internal sealed class OAuthFlowRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    public required string Provider { get; set; }

    /// <summary>One of <see cref="Domain.FlowPurposes"/>.</summary>
    public required string Purpose { get; set; }

    public Guid? LinkUserId { get; set; }

    public byte[]? StateHash { get; set; }

    public required string RedirectUrl { get; set; }

    public required string CodeChallenge { get; set; }

    public byte[]? ProviderVerifierCiphertext { get; set; }

    public byte[]? NonceHash { get; set; }

    public byte[]? ResultCiphertext { get; set; }

    public byte[]? CodeHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary><c>auth_id_token_uses</c> (spec 0012, AC-9): an ID token that already signed in, as its SHA-256.</summary>
internal sealed class IdTokenUseRow
{
    public required byte[] TokenHash { get; set; }

    public required string ProjectId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary><c>auth_method_settings</c> (spec 0013, AC-1): a project's TOTP and passkey settings. A missing row reads as the defaults.</summary>
internal sealed class MethodSettingsRow
{
    public required string ProjectId { get; set; }

    public bool TotpEnabled { get; set; } = true;

    public bool PasskeysEnabled { get; set; }

    public string? RpId { get; set; }

    public string? RpName { get; set; }

    public string[] AndroidCertFingerprints { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>auth_totp_factors</c> (spec 0013): a user's authenticator app secret, pending until confirmed.</summary>
internal sealed class TotpFactorRow
{
    public Guid UserId { get; set; }

    public required string ProjectId { get; set; }

    /// <summary>Sealed with associated data <c>auth_totp_factors:&lt;userId&gt;:secret_ciphertext</c>.</summary>
    public required byte[] SecretCiphertext { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public long? LastUsedStep { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>auth_recovery_codes</c> (spec 0013, AC-10): one recovery code, only as a <c>SecretBox.Mac</c> tag.</summary>
internal sealed class RecoveryCodeRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    public Guid UserId { get; set; }

    public required byte[] CodeMac { get; set; }

    public required string MacKeyId { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>auth_passkeys</c> (spec 0013): a WebAuthn credential of a user.</summary>
internal sealed class PasskeyRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    public Guid UserId { get; set; }

    public required byte[] CredentialId { get; set; }

    public required byte[] PublicKey { get; set; }

    public long SignCount { get; set; }

    public Guid? Aaguid { get; set; }

    /// <summary>Personal data: never log it.</summary>
    public required string Name { get; set; }

    public string[] Transports { get; set; } = [];

    public bool BackupEligible { get; set; }

    public bool BackedUp { get; set; }

    /// <summary>The RP ID at registration; the passkey is active only while it equals the project's current one.</summary>
    public required string RpId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary><c>auth_mfa_tickets</c> (spec 0013, AC-7): step one passed, waiting for the second factor.</summary>
internal sealed class MfaTicketRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the ticket.</summary>
    public required byte[] TicketHash { get; set; }

    /// <summary>How step one signed in, one of <see cref="Domain.SessionMethod"/>.</summary>
    public required string Method { get; set; }

    /// <summary>The Argon2id hash of a recovery's new password, applied only at step two.</summary>
    public string? PendingPasswordHash { get; set; }

    public string? Provider { get; set; }

    public string? UserAgent { get; set; }

    public string? Sdk { get; set; }

    public IPAddress? Ip { get; set; }

    public short Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary><c>auth_webauthn_challenges</c> (spec 0013): one WebAuthn ceremony's challenge, only as SHA-256.</summary>
internal sealed class WebAuthnChallengeRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    /// <summary><c>register</c>, <c>sign_in</c>, <c>mfa</c>, or <c>step_up</c>.</summary>
    public required string Purpose { get; set; }

    public required byte[] ChallengeHash { get; set; }

    public Guid? UserId { get; set; }

    public Guid? TicketId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
