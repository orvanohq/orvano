-- Databases module (spec 0015, row 16): the named databases of each project, and the shared function schema every
-- project table's trigger uses. Runs as orvano_admin. The tables themselves live in each project's schemas
-- (p_<projectId> for `main`, d_<databaseId> for the rest) and are read live from pg_catalog; nothing about them is
-- copied here. No foreign key reaches another module: the purge job cleans up by project_id.

-- Shared helpers for project tables. Every project role may call them; they run with the caller's rights.
CREATE SCHEMA orvano_fn;
GRANT USAGE ON SCHEMA orvano_fn TO PUBLIC;

-- The orvano_updated_at trigger of every table made through the API: updated_at moves forward on each UPDATE.
CREATE FUNCTION orvano_fn.set_updated_at() RETURNS trigger
    LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog AS
$$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END
$$;

-- The extra databases of a project (`main` has no row: it is the project schema itself). The API's `failed` status is
-- `provisioning` plus provision_failed_at.
CREATE TABLE orvano.db_databases (
    id                  text        PRIMARY KEY CHECK (id ~ '^[a-z0-9]{20}$'),
    project_id          text        NOT NULL,
    slug                text        NOT NULL CHECK (slug ~ '^[a-z][a-z0-9_]{0,39}$' AND slug <> 'main'),
    name                text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 100),
    schema_name         text        NOT NULL UNIQUE CHECK (schema_name = 'd_' || id),
    status              text        NOT NULL CHECK (status IN ('provisioning', 'active')),
    provision_failed_at timestamptz,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    UNIQUE (project_id, slug)
);
