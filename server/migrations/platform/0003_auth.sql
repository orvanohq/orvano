-- Auth module tables (spec 0003 task 7, spec 0004): app users (console accounts are users of project 'console'),
-- their password hashes, their sessions, and each project's token signing keys. No foreign key reaches another
-- module (spec 0003, AC-17); the project purge job cleans these up by project_id. Runs as orvano_admin; the default
-- privileges from 0001 give orvano_app DML on every table here.

CREATE TABLE orvano.auth_users (
    id                uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id        text        NOT NULL,
    email             text        CHECK (char_length(email) BETWEEN 1 AND 320),
    email_verified_at timestamptz,
    phone             text        CHECK (phone ~ '^\+[1-9][0-9]{1,14}$'),
    phone_verified_at timestamptz,
    name              text        CHECK (char_length(name) <= 256),
    status            text        NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'blocked')),
    metadata          jsonb       NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(metadata) = 'object'),
    last_sign_in_at   timestamptz,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now()
);
-- Cursor lists, newest first.
CREATE INDEX auth_users_project_id_idx ON orvano.auth_users (project_id, created_at, id);
-- One user per email per project, ignoring case (AC-3 maps a violation of this index to 409 user_already_exists).
CREATE UNIQUE INDEX auth_users_email_key ON orvano.auth_users (project_id, lower(email)) WHERE email IS NOT NULL;
CREATE UNIQUE INDEX auth_users_phone_key ON orvano.auth_users (project_id, phone) WHERE phone IS NOT NULL;
-- Prefix search on email (users.list, the console Users page).
CREATE INDEX auth_users_email_prefix_idx ON orvano.auth_users (project_id, lower(email) text_pattern_ops);

CREATE TABLE orvano.auth_passwords (
    user_id    uuid        PRIMARY KEY REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    -- Copied from the user, so the project purge can delete by project.
    project_id text        NOT NULL,
    -- Argon2id in the standard encoded form, which carries its own parameters.
    hash       text        NOT NULL CHECK (hash LIKE '$argon2id$%'),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX auth_passwords_project_id_idx ON orvano.auth_passwords (project_id);

CREATE TABLE orvano.auth_sessions (
    -- Also the access token's sid claim and the first part of the refresh token.
    id                    uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id            text        NOT NULL,
    user_id               uuid        NOT NULL REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    -- SHA-256 of the current refresh token's secret, and the token itself envelope encrypted (spec 0002).
    refresh_hash          bytea       NOT NULL CHECK (octet_length(refresh_hash) = 32),
    refresh_ciphertext    bytea       NOT NULL,
    previous_refresh_hash bytea       CHECK (octet_length(previous_refresh_hash) = 32),
    rotated_at            timestamptz,
    user_agent            text        CHECK (char_length(user_agent) <= 512),
    sdk                   text        CHECK (char_length(sdk) <= 100),
    ip_created            inet,
    ip_last               inet,
    created_at            timestamptz NOT NULL DEFAULT now(),
    last_refreshed_at     timestamptz NOT NULL DEFAULT now(),
    idle_expires_at       timestamptz NOT NULL,
    expires_at            timestamptz NOT NULL,
    ended_at              timestamptz,
    end_reason            text        CHECK (end_reason IN ('sign_out', 'revoked', 'password_changed', 'user_blocked', 'reuse_detected')),
    CHECK ((ended_at IS NULL) = (end_reason IS NULL)),
    CHECK ((previous_refresh_hash IS NULL) = (rotated_at IS NULL)),
    CHECK (idle_expires_at <= expires_at)
);
CREATE INDEX auth_sessions_user_id_idx ON orvano.auth_sessions (user_id, created_at);
CREATE INDEX auth_sessions_project_id_idx ON orvano.auth_sessions (project_id);
-- The hourly retention schedule deletes sessions 30 days after they end or expire (AC-32).
CREATE INDEX auth_sessions_retention_idx ON orvano.auth_sessions ((least(coalesce(ended_at, 'infinity'), idle_expires_at)));

CREATE TABLE orvano.auth_signing_keys (
    -- The kid: 16 random bytes as base64url.
    id                     text        PRIMARY KEY CHECK (id ~ '^[A-Za-z0-9_-]{22}$'),
    project_id             text        NOT NULL,
    alg                    text        NOT NULL CHECK (alg IN ('ES256')),
    public_jwk             jsonb       NOT NULL CHECK (jsonb_typeof(public_jwk) = 'object'),
    -- The PKCS#8 private key, envelope encrypted (spec 0002).
    private_key_ciphertext bytea       NOT NULL,
    status                 text        NOT NULL CHECK (status IN ('active', 'retiring')),
    created_at             timestamptz NOT NULL DEFAULT now(),
    retire_after           timestamptz,
    CHECK ((status = 'retiring') = (retire_after IS NOT NULL))
);
-- At most one active key per project; racing first issues insert with ON CONFLICT DO NOTHING on it (AC-21).
CREATE UNIQUE INDEX auth_signing_keys_active_key ON orvano.auth_signing_keys (project_id) WHERE status = 'active';
CREATE INDEX auth_signing_keys_project_id_idx ON orvano.auth_signing_keys (project_id);
