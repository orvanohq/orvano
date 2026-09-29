-- Org invitations (spec 0008, row 15). A row lives until it is accepted, revoked, or replaced (all delete it),
-- until 30 days after it expired (the org's next invitation create deletes it), or until its org is purged.
-- Only the SHA-256 of the token is stored. The inviter is a console user by ID: no foreign key across modules.

CREATE TABLE orvano.platform_invitations (
    id                 uuid        PRIMARY KEY DEFAULT uuidv7(),
    org_id             uuid        NOT NULL REFERENCES orvano.platform_orgs (id),
    email              text        NOT NULL CHECK (char_length(email) BETWEEN 1 AND 320),
    role               text        NOT NULL CHECK (role IN ('owner', 'developer', 'viewer')),
    token_hash         bytea       NOT NULL UNIQUE CHECK (length(token_hash) = 32),
    invited_by_user_id uuid        NOT NULL,
    expires_at         timestamptz NOT NULL,
    created_at         timestamptz NOT NULL DEFAULT now()
);

-- One invitation per email per org, ignoring case. It leads with org_id, so it also serves the foreign key.
CREATE UNIQUE INDEX platform_invitations_org_email_key ON orvano.platform_invitations (org_id, lower(email));

-- The owners' list, keyset paged on (created_at, id) within an org.
CREATE INDEX platform_invitations_org_created_idx ON orvano.platform_invitations (org_id, created_at, id);

-- The members list, keyset paged on the membership's (created_at, id) within an org.
CREATE INDEX platform_memberships_org_created_idx ON orvano.platform_memberships (org_id, created_at, id);
