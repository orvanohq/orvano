-- OAuth and ID token sign in with Google, Apple, GitHub, and Microsoft (spec 0012). Four tables, all the Auth
-- module's: each project's provider settings (secrets sealed), the identities that tie a provider account to a user,
-- the short lived redirect flows (state and handoff code only as SHA-256, everything else sealed), and the ID tokens
-- already used for native sign in (only as SHA-256). Sessions also record which provider signed them in.

CREATE TABLE orvano.auth_oauth_providers (
    project_id                   text        NOT NULL,
    provider                     text        NOT NULL CHECK (provider IN ('google', 'apple', 'github', 'microsoft')),
    enabled                      boolean     NOT NULL DEFAULT false,
    -- Google's web client ID, Apple's Services ID, GitHub's client ID, or Microsoft's application ID.
    client_id                    text        CHECK (char_length(client_id) BETWEEN 1 AND 255),
    -- Sealed with SecretBox; Apple has none, since Orvano makes it from the private key.
    client_secret_ciphertext     bytea,
    -- Google's and Apple's native audiences (Android and iOS client IDs, bundle IDs).
    client_ids_extra             text[]      NOT NULL DEFAULT '{}',
    apple_team_id                text        CHECK (apple_team_id ~ '^[A-Z0-9]{10}$'),
    apple_key_id                 text        CHECK (apple_key_id ~ '^[A-Z0-9]{10}$'),
    -- Sealed PKCS#8 PEM of the Sign in with Apple key.
    apple_private_key_ciphertext bytea,
    -- Null means common.
    microsoft_tenant             text,
    created_at                   timestamptz NOT NULL DEFAULT now(),
    updated_at                   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (project_id, provider),
    CHECK (provider <> 'apple' OR client_secret_ciphertext IS NULL),
    CHECK (provider IN ('google', 'apple') OR cardinality(client_ids_extra) = 0),
    CHECK (cardinality(client_ids_extra) <= 10),
    CHECK (provider = 'apple' OR (apple_team_id IS NULL AND apple_key_id IS NULL AND apple_private_key_ciphertext IS NULL)),
    CHECK (provider = 'microsoft' OR microsoft_tenant IS NULL)
);

CREATE TABLE orvano.auth_identities (
    id                          uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id                  text        NOT NULL,
    user_id                     uuid        NOT NULL REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    provider                    text        NOT NULL CHECK (provider IN ('google', 'apple', 'github', 'microsoft')),
    -- Google and Apple sub, GitHub's numeric id as text, Microsoft <tid>:<oid>.
    subject                     text        NOT NULL CHECK (char_length(subject) BETWEEN 1 AND 255),
    -- The provider's email, verified or not. Personal data.
    email                       text        CHECK (char_length(email) BETWEEN 1 AND 320),
    email_verified              boolean     NOT NULL DEFAULT false,
    -- Apple only: sealed JSON { clientId, refreshToken }, revoked when the row goes.
    provider_refresh_ciphertext bytea,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    last_sign_in_at             timestamptz,
    CHECK (provider = 'apple' OR provider_refresh_ciphertext IS NULL)
);
CREATE UNIQUE INDEX auth_identities_subject_key ON orvano.auth_identities (project_id, provider, subject);
CREATE UNIQUE INDEX auth_identities_user_provider_key ON orvano.auth_identities (user_id, provider);

CREATE TABLE orvano.auth_oauth_flows (
    -- Set in code (Guid.CreateVersion7()), since the sealed columns are bound to it.
    id                           uuid        PRIMARY KEY,
    project_id                   text        NOT NULL,
    provider                     text        NOT NULL CHECK (provider IN ('google', 'apple', 'github', 'microsoft')),
    purpose                      text        NOT NULL CHECK (purpose IN ('sign_in', 'link')),
    link_user_id                 uuid        REFERENCES orvano.auth_users (id) ON DELETE CASCADE,
    -- SHA-256 of state; null once the callback claims the flow.
    state_hash                   bytea       CHECK (octet_length(state_hash) = 32),
    redirect_url                 text        NOT NULL CHECK (char_length(redirect_url) BETWEEN 1 AND 2048),
    -- The SDK's S256 challenge, proved by its verifier at redemption.
    code_challenge               text        NOT NULL CHECK (char_length(code_challenge) = 43),
    -- Sealed PKCE verifier Orvano sends to the provider.
    provider_verifier_ciphertext bytea,
    -- SHA-256 of the nonce; null for GitHub.
    nonce_hash                   bytea       CHECK (octet_length(nonce_hash) = 32),
    -- Sealed provider result, set by the callback together with code_hash.
    result_ciphertext            bytea,
    -- SHA-256 of the handoff code.
    code_hash                    bytea       CHECK (octet_length(code_hash) = 32),
    created_at                   timestamptz NOT NULL DEFAULT now(),
    -- Start plus 10 minutes, then callback plus 2 minutes.
    expires_at                   timestamptz NOT NULL,
    CHECK ((purpose = 'link') = (link_user_id IS NOT NULL)),
    CHECK ((code_hash IS NULL) = (result_ciphertext IS NULL))
);
CREATE UNIQUE INDEX auth_oauth_flows_state_key ON orvano.auth_oauth_flows (state_hash) WHERE state_hash IS NOT NULL;
CREATE UNIQUE INDEX auth_oauth_flows_code_key ON orvano.auth_oauth_flows (code_hash) WHERE code_hash IS NOT NULL;
CREATE INDEX auth_oauth_flows_expires_at_idx ON orvano.auth_oauth_flows (expires_at);
CREATE INDEX auth_oauth_flows_link_user_id_idx ON orvano.auth_oauth_flows (link_user_id);
CREATE INDEX auth_oauth_flows_project_id_idx ON orvano.auth_oauth_flows (project_id);

CREATE TABLE orvano.auth_id_token_uses (
    -- SHA-256 of the whole ID token: a replay hits this key.
    token_hash bytea       PRIMARY KEY CHECK (octet_length(token_hash) = 32),
    project_id text        NOT NULL,
    -- The token's exp plus the 30 second clock leeway.
    expires_at timestamptz NOT NULL
);
CREATE INDEX auth_id_token_uses_expires_at_idx ON orvano.auth_id_token_uses (expires_at);
CREATE INDEX auth_id_token_uses_project_id_idx ON orvano.auth_id_token_uses (project_id);

ALTER TABLE orvano.auth_sessions DROP CONSTRAINT auth_sessions_method_check;
ALTER TABLE orvano.auth_sessions ADD CONSTRAINT auth_sessions_method_check
    CHECK (method IN ('password', 'sign_up', 'magic_link', 'email_code', 'recovery', 'oauth', 'id_token'));
ALTER TABLE orvano.auth_sessions
    ADD COLUMN provider text CHECK (provider IN ('google', 'apple', 'github', 'microsoft'));
ALTER TABLE orvano.auth_sessions ADD CONSTRAINT auth_sessions_provider_check_method
    CHECK ((method IN ('oauth', 'id_token')) = (provider IS NOT NULL));
