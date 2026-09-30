-- Messaging module tables (spec 0009, row 9): SMTP settings, edited email templates, and the email queue
-- and log. Runs as orvano_admin; the default privileges from 0001 give orvano_app DML on every table here.
-- No foreign key reaches another module: the purge job cleans up by project_id.

-- At most one row per project. The row of project 'console' is the install's SMTP, which every project
-- without its own row sends through. The password is a SecretBox blob, never plain text.
CREATE TABLE orvano.messaging_smtp_settings (
    project_id          text        PRIMARY KEY,
    host                text        NOT NULL CHECK (char_length(host) BETWEEN 1 AND 253),
    port                integer     NOT NULL CHECK (port BETWEEN 1 AND 65535),
    security            text        NOT NULL CHECK (security IN ('starttls', 'tls', 'none')),
    username            text        CHECK (char_length(username) BETWEEN 1 AND 256),
    password_ciphertext bytea,
    from_email          text        NOT NULL CHECK (char_length(from_email) BETWEEN 1 AND 320),
    from_name           text        CHECK (char_length(from_name) BETWEEN 1 AND 128),
    reply_to            text        CHECK (char_length(reply_to) BETWEEN 1 AND 320),
    updated_by_user_id  uuid        NOT NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CHECK (password_ciphertext IS NULL OR username IS NOT NULL),
    -- A username never travels over a plain text connection.
    CHECK (security <> 'none' OR username IS NULL)
);

-- A row only for a template someone edited; the defaults live in code. One locale for now.
CREATE TABLE orvano.messaging_email_templates (
    project_id         text        NOT NULL,
    kind               text        NOT NULL CHECK (kind IN ('verification', 'recovery', 'magic_link', 'email_code')),
    locale             text        NOT NULL CHECK (locale = 'en'),
    subject            text        NOT NULL CHECK (char_length(subject) BETWEEN 1 AND 255),
    html               text        NOT NULL CHECK (octet_length(html) <= 102400),
    text               text        CHECK (octet_length(text) <= 102400),
    updated_by_user_id uuid        NOT NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (project_id, kind, locale)
);

-- The queue and the 30 day log. The ID is set in code (the content is sealed to it before the insert) and
-- is the only thing a job payload carries. The content (recipient, subject, bodies) exists only while the
-- row is queued; a final row keeps just the masked recipient.
CREATE TABLE orvano.messaging_emails (
    id                 uuid        PRIMARY KEY,
    project_id         text        NOT NULL,
    template           text        NOT NULL CHECK (template IN ('verification', 'recovery', 'magic_link', 'email_code', 'console_invitation')),
    recipient_masked   text        NOT NULL,
    content_ciphertext bytea,
    status             text        NOT NULL DEFAULT 'queued' CHECK (status IN ('queued', 'sent', 'failed')),
    smtp_source        text        CHECK (smtp_source IN ('project', 'install')),
    attempts           integer     NOT NULL DEFAULT 0,
    error_code         text        CHECK (error_code IN (
        'smtp_unreachable', 'smtp_tls_failed', 'smtp_auth_failed', 'smtp_rejected', 'smtp_timeout',
        'smtp_host_not_allowed', 'email_not_configured', 'project_not_active', 'email_expired', 'email_unreadable')),
    created_at         timestamptz NOT NULL DEFAULT now(),
    completed_at       timestamptz,
    CHECK ((status = 'queued') = (content_ciphertext IS NOT NULL)),
    CHECK ((status = 'queued') = (completed_at IS NULL)),
    CHECK ((status = 'failed') = (error_code IS NOT NULL)),
    CHECK (smtp_source IS NULL OR status = 'sent')
);

-- The log, keyset paged on (created_at, id) within a project, and the hourly count of the install cap.
CREATE INDEX messaging_emails_project_created_idx ON orvano.messaging_emails (project_id, created_at, id);

-- Retention: rows older than 30 days, and queued rows older than 30 minutes.
CREATE INDEX messaging_emails_created_idx ON orvano.messaging_emails (created_at);
