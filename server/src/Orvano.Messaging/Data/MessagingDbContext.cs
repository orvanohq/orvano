using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Orvano.Messaging.Data;

/// <summary>
/// EF Core model of the Messaging module's tables in schema <c>orvano</c> (spec 0009). Internal to the module, so no
/// other module can query them. Tables change only through the SQL migrations; the drift check keeps this model
/// matching them.
/// </summary>
internal sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    public DbSet<SmtpSettingsRow> SmtpSettings => Set<SmtpSettingsRow>();

    public DbSet<EmailTemplateRow> EmailTemplates => Set<EmailTemplateRow>();

    public DbSet<EmailRow> Emails => Set<EmailRow>();

    /// <summary>A context on an open connection the caller owns; it never opens or closes it.</summary>
    public static MessagingDbContext On(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<MessagingDbContext>().UseNpgsql(connection).Options);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("orvano");

        model.Entity<SmtpSettingsRow>(e =>
        {
            e.ToTable("messaging_smtp_settings");
            e.HasKey(x => x.ProjectId);
            e.Property(x => x.ProjectId).HasColumnName("project_id").ValueGeneratedNever();
            e.Property(x => x.Host).HasColumnName("host");
            e.Property(x => x.Port).HasColumnName("port");
            e.Property(x => x.Security).HasColumnName("security");
            e.Property(x => x.Username).HasColumnName("username");
            e.Property(x => x.PasswordCiphertext).HasColumnName("password_ciphertext");
            e.Property(x => x.FromEmail).HasColumnName("from_email");
            e.Property(x => x.FromName).HasColumnName("from_name");
            e.Property(x => x.ReplyTo).HasColumnName("reply_to");
            e.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        });

        model.Entity<EmailTemplateRow>(e =>
        {
            e.ToTable("messaging_email_templates");
            e.HasKey(x => new { x.ProjectId, x.Kind, x.Locale });
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.Locale).HasColumnName("locale");
            e.Property(x => x.Subject).HasColumnName("subject");
            e.Property(x => x.Html).HasColumnName("html");
            e.Property(x => x.Text).HasColumnName("text");
            e.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        });

        model.Entity<EmailRow>(e =>
        {
            e.ToTable("messaging_emails");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Template).HasColumnName("template");
            e.Property(x => x.RecipientMasked).HasColumnName("recipient_masked");
            e.Property(x => x.ContentCiphertext).HasColumnName("content_ciphertext");
            e.Property(x => x.Status).HasColumnName("status").HasDefaultValue("queued");
            e.Property(x => x.SmtpSource).HasColumnName("smtp_source");
            e.Property(x => x.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            e.Property(x => x.ErrorCode).HasColumnName("error_code");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
        });
    }
}

/// <summary><c>messaging_smtp_settings</c>: one project's SMTP server. The row of project <c>console</c> is the install's.</summary>
internal sealed class SmtpSettingsRow
{
    public required string ProjectId { get; set; }

    /// <summary>Trimmed and lower cased. Never log it.</summary>
    public required string Host { get; set; }

    public int Port { get; set; }

    /// <summary><c>starttls</c>, <c>tls</c>, or <c>none</c>.</summary>
    public required string Security { get; set; }

    /// <summary>Never log it.</summary>
    public string? Username { get; set; }

    /// <summary>A SecretBox blob bound to this row; never the password.</summary>
    public byte[]? PasswordCiphertext { get; set; }

    public required string FromEmail { get; set; }

    public string? FromName { get; set; }

    public string? ReplyTo { get; set; }

    public Guid UpdatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>messaging_email_templates</c>: a template someone edited. The defaults live in code.</summary>
internal sealed class EmailTemplateRow
{
    public required string ProjectId { get; set; }

    /// <summary><c>verification</c>, <c>recovery</c>, <c>magic_link</c>, or <c>email_code</c>.</summary>
    public required string Kind { get; set; }

    public required string Locale { get; set; }

    public required string Subject { get; set; }

    public required string Html { get; set; }

    /// <summary>Null means the text part is derived from the HTML.</summary>
    public string? Text { get; set; }

    public Guid UpdatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary><c>messaging_emails</c>: the queue and the 30 day log.</summary>
internal sealed class EmailRow
{
    public Guid Id { get; set; }

    public required string ProjectId { get; set; }

    public required string Template { get; set; }

    public required string RecipientMasked { get; set; }

    /// <summary>A SecretBox blob of the recipient and content, set exactly while the row is <c>queued</c>.</summary>
    public byte[]? ContentCiphertext { get; set; }

    /// <summary><c>queued</c>, <c>sent</c>, or <c>failed</c>.</summary>
    public required string Status { get; set; }

    /// <summary><c>project</c> or <c>install</c>, set when sent.</summary>
    public string? SmtpSource { get; set; }

    public int Attempts { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
