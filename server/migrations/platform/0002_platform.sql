-- Platform module tables (spec 0003, row 7 slice): install settings and admins, orgs, memberships,
-- projects, API keys, and platforms. Invitations arrive with row 15. Runs as orvano_admin; the
-- default privileges from 0001 give orvano_app DML on every table here.

CREATE TABLE orvano.platform_install_settings (
    id             smallint    PRIMARY KEY CHECK (id = 1),
    console_signup text        NOT NULL DEFAULT 'invite' CHECK (console_signup IN ('invite', 'open')),
    updated_at     timestamptz NOT NULL DEFAULT now()
);
INSERT INTO orvano.platform_install_settings (id, console_signup) VALUES (1, 'invite');

-- A console user (an auth_users row of project 'console'), by ID only: no foreign key across modules.
CREATE TABLE orvano.platform_install_admins (
    user_id    uuid        PRIMARY KEY,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE orvano.platform_orgs (
    id                 uuid        PRIMARY KEY DEFAULT uuidv7(),
    name               text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 100),
    status             text        NOT NULL CHECK (status IN ('active', 'deleting')),
    deleted_at         timestamptz,
    purge_after        timestamptz,
    created_by_user_id uuid        NOT NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),
    CHECK ((status = 'deleting') = (deleted_at IS NOT NULL AND purge_after IS NOT NULL))
);

CREATE TABLE orvano.platform_memberships (
    id         uuid        PRIMARY KEY DEFAULT uuidv7(),
    org_id     uuid        NOT NULL REFERENCES orvano.platform_orgs (id),
    user_id    uuid        NOT NULL,
    role       text        NOT NULL CHECK (role IN ('owner', 'developer', 'viewer')),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (org_id, user_id)
);
CREATE INDEX platform_memberships_user_id_idx ON orvano.platform_memberships (user_id);

CREATE TABLE orvano.platform_projects (
    id                 text        PRIMARY KEY CHECK (id ~ '^[a-z0-9]{1,60}$'),
    org_id             uuid        REFERENCES orvano.platform_orgs (id),
    kind               text        NOT NULL CHECK (kind IN ('app', 'system')),
    name               text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 100),
    status             text        NOT NULL CHECK (status IN ('provisioning', 'active', 'failed', 'deleting')),
    deleted_at         timestamptz,
    purge_after        timestamptz,
    purge_failed_at    timestamptz,
    created_by_user_id uuid,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),
    CHECK ((kind = 'system') = (org_id IS NULL)),
    CHECK ((status = 'deleting') = (deleted_at IS NOT NULL AND purge_after IS NOT NULL)),
    CHECK (purge_failed_at IS NULL OR status = 'deleting')
);
CREATE INDEX platform_projects_org_id_idx ON orvano.platform_projects (org_id, created_at, id);

-- The system project console accounts belong to (AC-6). It is never provisioned and has no schema.
INSERT INTO orvano.platform_projects (id, org_id, kind, name, status) VALUES ('console', NULL, 'system', 'Console', 'active');

CREATE TABLE orvano.platform_api_keys (
    id                 uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id         text        NOT NULL REFERENCES orvano.platform_projects (id),
    name               text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 100),
    prefix             text        NOT NULL CHECK (char_length(prefix) = 12),
    secret_hash        bytea       NOT NULL UNIQUE CHECK (length(secret_hash) = 32),
    scopes             text[]      NOT NULL CHECK (cardinality(scopes) > 0),
    expires_at         timestamptz,
    last_used_at       timestamptz,
    created_by_user_id uuid        NOT NULL,
    created_at         timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX platform_api_keys_project_id_idx ON orvano.platform_api_keys (project_id, created_at, id);

CREATE TABLE orvano.platform_platforms (
    id         uuid        PRIMARY KEY DEFAULT uuidv7(),
    project_id text        NOT NULL REFERENCES orvano.platform_projects (id),
    type       text        NOT NULL CHECK (type IN ('web', 'android', 'ios', 'macos', 'windows', 'linux')),
    name       text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 100),
    identifier text        NOT NULL CHECK (char_length(identifier) BETWEEN 1 AND 255),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX platform_platforms_identifier_key ON orvano.platform_platforms (project_id, type, lower(identifier));
CREATE INDEX platform_platforms_project_id_idx ON orvano.platform_platforms (project_id, created_at, id);
