-- Auth policies and abuse protection (spec 0014). One row of rules per project (no row means the defaults), three
-- more method switches (anonymous users and required MFA), anonymous users, the new session methods and end reasons,
-- and the purpose of an MFA ticket (a challenge, or an enrollment before the first session). Every existing row
-- reads its old behavior through these defaults (AC-40).
CREATE TABLE orvano.auth_policies (
    project_id                             text        PRIMARY KEY,
    sign_ups_enabled                       boolean     NOT NULL DEFAULT true,
    require_verified_email                 boolean     NOT NULL DEFAULT false,
    block_disposable_emails                boolean     NOT NULL DEFAULT false,
    -- Host names as AC-8 normalizes them (ASCII, lowercase, no trailing dot); the domain checks each entry.
    blocked_email_domains                  text[]      NOT NULL DEFAULT '{}' CHECK (cardinality(blocked_email_domains) <= 500),
    allowed_email_domains                  text[]      NOT NULL DEFAULT '{}' CHECK (cardinality(allowed_email_domains) <= 500),
    password_min_length                    smallint    NOT NULL DEFAULT 8 CHECK (password_min_length BETWEEN 8 AND 64),
    password_common_check                  boolean     NOT NULL DEFAULT true,
    password_breached_check                boolean     NOT NULL DEFAULT false,
    access_token_seconds                   integer     NOT NULL DEFAULT 900 CHECK (access_token_seconds BETWEEN 300 AND 3600),
    session_idle_seconds                   integer     NOT NULL DEFAULT 2592000 CHECK (session_idle_seconds BETWEEN 3600 AND 7776000),
    session_absolute_seconds               integer     NOT NULL DEFAULT 31536000 CHECK (session_absolute_seconds BETWEEN 86400 AND 31536000),
    -- Null means no limit.
    max_sessions_per_user                  integer     CHECK (max_sessions_per_user BETWEEN 1 AND 1000),
    -- The app servers whose X-Orvano-Client-IP is the limit IP (AC-16).
    trusted_server_cidrs                   cidr[]      NOT NULL DEFAULT '{}' CHECK (cardinality(trusted_server_cidrs) <= 20),
    sign_in_failed_email_ip_limit          smallint    NOT NULL DEFAULT 10 CHECK (sign_in_failed_email_ip_limit BETWEEN 3 AND 100),
    sign_in_failed_email_ip_window_minutes smallint    NOT NULL DEFAULT 15 CHECK (sign_in_failed_email_ip_window_minutes BETWEEN 1 AND 1440),
    sign_in_failed_ip_limit                integer     NOT NULL DEFAULT 100 CHECK (sign_in_failed_ip_limit BETWEEN 10 AND 10000),
    sign_up_ip_limit                       integer     NOT NULL DEFAULT 60 CHECK (sign_up_ip_limit BETWEEN 1 AND 10000),
    anonymous_ip_limit                     integer     NOT NULL DEFAULT 30 CHECK (anonymous_ip_limit BETWEEN 1 AND 10000),
    email_send_ip_limit                    integer     NOT NULL DEFAULT 300 CHECK (email_send_ip_limit BETWEEN 10 AND 100000),
    updated_at                             timestamptz NOT NULL DEFAULT now(),
    CHECK (session_idle_seconds <= session_absolute_seconds)
);

-- Anonymous users (AC-28) and required MFA (AC-27), beside the TOTP and passkey switches.
ALTER TABLE orvano.auth_method_settings
    ADD COLUMN anonymous_enabled   boolean  NOT NULL DEFAULT false,
    ADD COLUMN anonymous_idle_days smallint NOT NULL DEFAULT 30 CHECK (anonymous_idle_days BETWEEN 1 AND 365),
    ADD COLUMN mfa_required        boolean  NOT NULL DEFAULT false,
    ADD CONSTRAINT auth_method_settings_mfa_required_check CHECK (NOT mfa_required OR totp_enabled OR passkeys_enabled);

-- A guest has no email while anonymous; a password hash may wait for a pending upgrade (AC-30).
ALTER TABLE orvano.auth_users
    ADD COLUMN is_anonymous boolean NOT NULL DEFAULT false,
    ADD CONSTRAINT auth_users_anonymous_email_check CHECK (NOT is_anonymous OR email IS NULL);
-- The hourly retention finds idle guests per project (AC-31).
CREATE INDEX auth_users_anonymous_idx ON orvano.auth_users (project_id, created_at) WHERE is_anonymous;

ALTER TABLE orvano.auth_sessions DROP CONSTRAINT auth_sessions_method_check;
ALTER TABLE orvano.auth_sessions ADD CONSTRAINT auth_sessions_method_check
    CHECK (method IN ('password', 'sign_up', 'magic_link', 'email_code', 'recovery', 'oauth', 'id_token', 'passkey', 'anonymous'));

ALTER TABLE orvano.auth_sessions DROP CONSTRAINT auth_sessions_end_reason_check;
ALTER TABLE orvano.auth_sessions ADD CONSTRAINT auth_sessions_end_reason_check
    CHECK (end_reason IN ('sign_out', 'revoked', 'password_changed', 'user_blocked', 'reuse_detected', 'password_reset',
                          'account_claimed', 'mfa_enabled', 'mfa_reset', 'session_limit', 'mfa_required'));

-- A challenge (spec 0013) or an enrollment before the first session (AC-27).
ALTER TABLE orvano.auth_mfa_tickets
    ADD COLUMN purpose text NOT NULL DEFAULT 'challenge' CHECK (purpose IN ('challenge', 'enroll'));
