-- Platform schema, migration ledger, event outbox, and job queue (spec 0002).
-- Runs as orvano_admin, which owns database orvano. Roles are created once by the installer.

CREATE SCHEMA orvano;
REVOKE ALL ON SCHEMA orvano FROM PUBLIC;
GRANT USAGE ON SCHEMA orvano TO orvano_app;

-- orvano_app gets DML on every platform table created from here on.
ALTER DEFAULT PRIVILEGES IN SCHEMA orvano GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO orvano_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA orvano GRANT USAGE, SELECT ON SEQUENCES TO orvano_app;

CREATE TABLE orvano.schema_migrations (
    version    integer     PRIMARY KEY,
    name       text        NOT NULL,
    sha256     text        NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT now()
);
-- The app reads the schema version for readiness; only the migrate role writes it.
REVOKE INSERT, UPDATE, DELETE ON orvano.schema_migrations FROM orvano_app;

CREATE TABLE orvano.events (
    id            bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    project_id    text,
    type          text        NOT NULL,
    subject       text,
    payload       jsonb       NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    dispatched_at timestamptz
);
CREATE INDEX events_undispatched_idx ON orvano.events (id) WHERE dispatched_at IS NULL;

CREATE TABLE orvano.jobs (
    id           bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    queue        text        NOT NULL,
    kind         text        NOT NULL,
    project_id   text,
    payload      jsonb       NOT NULL,
    priority     integer     NOT NULL DEFAULT 0,
    run_at       timestamptz NOT NULL DEFAULT now(),
    status       text        NOT NULL DEFAULT 'queued'
                             CHECK (status IN ('queued', 'running', 'succeeded', 'failed', 'dead')),
    attempts     integer     NOT NULL DEFAULT 0,
    max_attempts integer     NOT NULL CHECK (max_attempts > 0),
    lease_until  timestamptz,
    locked_by    text,
    last_error   text,
    created_at   timestamptz NOT NULL DEFAULT now(),
    finished_at  timestamptz
);
CREATE INDEX jobs_queued_idx ON orvano.jobs (queue, priority, run_at) WHERE status = 'queued';
