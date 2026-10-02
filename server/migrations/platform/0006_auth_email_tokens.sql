-- Email verification, password reset, magic links, email codes, and email change (spec 0010). One table of short
-- lived, single use secrets, each stored only as a hash (links: SHA-256 of the token; codes: an HMAC keyed by the
-- master key, which is not in the database). Redeeming a token deletes its row, so a used token is a missing row.
-- Also records how each session began, and two new reasons a session ends.

CREATE TABLE orvano.auth_email_tokens (
    -- Codes set it in code (Guid.CreateVersion7()), since it is part of the HMAC input.
    id          uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id  text        NOT NULL,
    kind        text        NOT NULL CHECK (kind IN ('verification', 'recovery', 'magic_link', 'email_code', 'email_change')),
    -- Null only for a magic link or code sent to an email that has no user yet.
    user_id     uuid        REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    -- Where the email went: the user's email, the typed email, or the new address of an email change.
    email       text        NOT NULL CHECK (char_length(email) BETWEEN 1 AND 320),
    secret_hash bytea       NOT NULL CHECK (octet_length(secret_hash) = 32),
    -- The ID of the master key the code's HMAC was made with; codes only.
    mac_key_id  text,
    -- Wrong guesses of a code.
    attempts    smallint    NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    expires_at  timestamptz NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    CHECK (user_id IS NOT NULL OR kind IN ('magic_link', 'email_code')),
    CHECK ((kind = 'email_code') = (mac_key_id IS NOT NULL))
);
-- The link lookup.
CREATE UNIQUE INDEX auth_email_tokens_secret_key ON orvano.auth_email_tokens (secret_hash) WHERE kind <> 'email_code';
-- At most one live token per project, kind, and user, or per project, kind, and unknown email (AC-4).
CREATE UNIQUE INDEX auth_email_tokens_user_key ON orvano.auth_email_tokens (project_id, kind, user_id) WHERE user_id IS NOT NULL;
CREATE UNIQUE INDEX auth_email_tokens_email_key ON orvano.auth_email_tokens (project_id, kind, lower(email)) WHERE user_id IS NULL;
-- The code lookup and the "newest replaces older" delete.
CREATE INDEX auth_email_tokens_email_idx ON orvano.auth_email_tokens (project_id, lower(email), kind);
-- The hourly retention sweep.
CREATE INDEX auth_email_tokens_expires_at_idx ON orvano.auth_email_tokens (expires_at);
-- Blocking, deleting, and changing the email of a user delete their tokens.
CREATE INDEX auth_email_tokens_user_id_idx ON orvano.auth_email_tokens (user_id);

ALTER TABLE orvano.auth_sessions
    ADD COLUMN method text NOT NULL DEFAULT 'password'
        CHECK (method IN ('password', 'sign_up', 'magic_link', 'email_code', 'recovery'));

ALTER TABLE orvano.auth_sessions DROP CONSTRAINT auth_sessions_end_reason_check;
ALTER TABLE orvano.auth_sessions ADD CONSTRAINT auth_sessions_end_reason_check
    CHECK (end_reason IN ('sign_out', 'revoked', 'password_changed', 'user_blocked', 'reuse_detected', 'password_reset', 'account_claimed'));
