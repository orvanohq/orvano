-- MFA with authenticator apps and recovery codes, passkeys, and session strength (spec 0013). Six tables, all the
-- Auth module's: each project's TOTP and passkey settings, a user's TOTP factor (secret sealed), recovery codes (only
-- as SecretBox MACs), passkeys, the short lived MFA tickets between the two sign in steps, and WebAuthn challenges
-- (tickets and challenges only as SHA-256). Sessions also record how strongly they signed in.

CREATE TABLE orvano.auth_method_settings (
    project_id                text        PRIMARY KEY,
    totp_enabled              boolean     NOT NULL DEFAULT true,
    passkeys_enabled          boolean     NOT NULL DEFAULT false,
    -- The passkey domain (AC-1's rule is checked in the domain).
    rp_id                     text        CHECK (char_length(rp_id) BETWEEN 1 AND 253),
    -- Null means the project name.
    rp_name                   text        CHECK (char_length(rp_name) BETWEEN 1 AND 64),
    -- Uppercase colon hex SHA-256 fingerprints of the Android signing certificates.
    android_cert_fingerprints text[]      NOT NULL DEFAULT '{}' CHECK (cardinality(android_cert_fingerprints) <= 10),
    updated_at                timestamptz NOT NULL DEFAULT now(),
    CHECK (NOT passkeys_enabled OR rp_id IS NOT NULL)
);

CREATE TABLE orvano.auth_totp_factors (
    user_id           uuid        PRIMARY KEY REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    project_id        text        NOT NULL,
    -- The 20 byte secret, sealed with SecretBox (associated data auth_totp_factors:<userId>:secret_ciphertext).
    secret_ciphertext bytea       NOT NULL,
    -- Null while pending; a confirmed row means MFA is on (with the project's totp_enabled).
    confirmed_at      timestamptz,
    -- The last accepted 30 second step, so each code works once.
    last_used_step    bigint,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX auth_totp_factors_project_id_idx ON orvano.auth_totp_factors (project_id);
-- The hourly sweep deletes pending rows older than 15 minutes.
CREATE INDEX auth_totp_factors_pending_idx ON orvano.auth_totp_factors (created_at) WHERE confirmed_at IS NULL;

CREATE TABLE orvano.auth_recovery_codes (
    id         uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id text        NOT NULL,
    user_id    uuid        NOT NULL REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    -- SecretBox.Mac tag over the normalized code, with the purpose auth_recovery_codes:<userId>.
    code_mac   bytea       NOT NULL,
    -- The master key ID the tag was made with, so a key rotation never breaks saved codes.
    mac_key_id text        NOT NULL,
    used_at    timestamptz,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX auth_recovery_codes_user_id_idx ON orvano.auth_recovery_codes (user_id);
CREATE INDEX auth_recovery_codes_project_id_idx ON orvano.auth_recovery_codes (project_id);

CREATE TABLE orvano.auth_passkeys (
    id               uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id       text        NOT NULL,
    user_id          uuid        NOT NULL REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    credential_id    bytea       NOT NULL CHECK (octet_length(credential_id) BETWEEN 1 AND 1023),
    -- The COSE public key; public, so stored plain.
    public_key       bytea       NOT NULL,
    sign_count       bigint      NOT NULL CHECK (sign_count >= 0),
    aaguid           uuid,
    name             text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 64),
    transports       text[]      NOT NULL DEFAULT '{}',
    backup_eligible  boolean     NOT NULL,
    backed_up        boolean     NOT NULL,
    -- The RP ID at registration; the passkey is active only while it equals the project's current one.
    rp_id            text        NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    last_used_at     timestamptz
);
CREATE UNIQUE INDEX auth_passkeys_credential_key ON orvano.auth_passkeys (project_id, credential_id);
CREATE INDEX auth_passkeys_user_id_idx ON orvano.auth_passkeys (user_id, created_at);

CREATE TABLE orvano.auth_mfa_tickets (
    id                    uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id            text        NOT NULL,
    user_id               uuid        NOT NULL REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    -- SHA-256 of the ticket; looked up only together with the request's project.
    ticket_hash           bytea       NOT NULL CHECK (octet_length(ticket_hash) = 32),
    -- How step one signed in; the session created at step two keeps it.
    method                text        NOT NULL
        CHECK (method IN ('password', 'sign_up', 'magic_link', 'email_code', 'recovery', 'oauth', 'id_token')),
    -- The Argon2id hash of a recovery's new password, applied only at step two.
    pending_password_hash text,
    provider              text        CHECK (provider IN ('google', 'apple', 'github', 'microsoft')),
    user_agent            text        CHECK (char_length(user_agent) <= 512),
    sdk                   text        CHECK (char_length(sdk) <= 100),
    ip                    inet,
    attempts              smallint    NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 5),
    created_at            timestamptz NOT NULL DEFAULT now(),
    expires_at            timestamptz NOT NULL,
    CHECK (pending_password_hash IS NULL OR method = 'recovery'),
    CHECK ((method IN ('oauth', 'id_token')) = (provider IS NOT NULL))
);
CREATE UNIQUE INDEX auth_mfa_tickets_ticket_key ON orvano.auth_mfa_tickets (project_id, ticket_hash);
CREATE INDEX auth_mfa_tickets_user_id_idx ON orvano.auth_mfa_tickets (user_id, created_at);
CREATE INDEX auth_mfa_tickets_expires_at_idx ON orvano.auth_mfa_tickets (expires_at);

CREATE TABLE orvano.auth_webauthn_challenges (
    -- The challengeId.
    id             uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id     text        NOT NULL,
    purpose        text        NOT NULL CHECK (purpose IN ('register', 'sign_in', 'mfa', 'step_up')),
    -- SHA-256 of the 32 byte challenge.
    challenge_hash bytea       NOT NULL CHECK (octet_length(challenge_hash) = 32),
    user_id        uuid        REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    ticket_id      uuid        REFERENCES orvano.auth_mfa_tickets (id) ON DELETE CASCADE,
    created_at     timestamptz NOT NULL DEFAULT now(),
    expires_at     timestamptz NOT NULL,
    CHECK ((purpose = 'sign_in') = (user_id IS NULL)),
    CHECK ((purpose = 'mfa') = (ticket_id IS NOT NULL))
);
CREATE UNIQUE INDEX auth_webauthn_challenges_challenge_key ON orvano.auth_webauthn_challenges (challenge_hash);
CREATE INDEX auth_webauthn_challenges_project_id_idx ON orvano.auth_webauthn_challenges (project_id);
CREATE INDEX auth_webauthn_challenges_user_id_idx ON orvano.auth_webauthn_challenges (user_id);
CREATE INDEX auth_webauthn_challenges_ticket_id_idx ON orvano.auth_webauthn_challenges (ticket_id);
CREATE INDEX auth_webauthn_challenges_expires_at_idx ON orvano.auth_webauthn_challenges (expires_at);

-- Session strength (AC-25): the assurance level, the sorted authentication methods, and the last strong check.
ALTER TABLE orvano.auth_sessions
    ADD COLUMN aal            smallint    NOT NULL DEFAULT 1 CHECK (aal IN (1, 2)),
    ADD COLUMN amr            text[]      NOT NULL DEFAULT '{}',
    ADD COLUMN strong_auth_at timestamptz;

UPDATE orvano.auth_sessions SET amr = CASE method
    WHEN 'password' THEN ARRAY['pwd']
    WHEN 'sign_up' THEN ARRAY['pwd']
    WHEN 'recovery' THEN ARRAY['pwd']
    WHEN 'magic_link' THEN ARRAY['email']
    WHEN 'email_code' THEN ARRAY['email']
    WHEN 'oauth' THEN ARRAY['fed']
    WHEN 'id_token' THEN ARRAY['fed']
END;

ALTER TABLE orvano.auth_sessions DROP CONSTRAINT auth_sessions_method_check;
ALTER TABLE orvano.auth_sessions ADD CONSTRAINT auth_sessions_method_check
    CHECK (method IN ('password', 'sign_up', 'magic_link', 'email_code', 'recovery', 'oauth', 'id_token', 'passkey'));

ALTER TABLE orvano.auth_sessions DROP CONSTRAINT auth_sessions_end_reason_check;
ALTER TABLE orvano.auth_sessions ADD CONSTRAINT auth_sessions_end_reason_check
    CHECK (end_reason IN ('sign_out', 'revoked', 'password_changed', 'user_blocked', 'reuse_detected', 'password_reset',
                          'account_claimed', 'mfa_enabled', 'mfa_reset'));
