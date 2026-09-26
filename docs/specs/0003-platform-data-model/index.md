# 0003. Platform data model: orgs, projects, keys, platforms, and app users

**Date**: 2026-09-26
**Status**: Proposed

## Summary

This spec fixes the internal tables every Orvano product hangs off: console accounts, organizations, members and roles, projects, API keys, platforms (allowed web hosts and app IDs), and the identity row of a project's app users. The project stays the one unit of isolation: each project gets its own Postgres schema, and everything else is a row in the shared `orvano` schema keyed by the project ID. Later, each environment (dev, staging, prod) becomes a child project, so environments arrive without a breaking migration. Row 3 builds nothing itself: rows 7, 8, and 15 each build their slice of this model, end to end.

## Requirements

**User stories**:
- As the person who installs Orvano, I want the first account to become the install admin and later sign ups to be invite only, so that a server on the public internet is not open to strangers.
- As a developer, I want to create orgs and projects, each project isolated from the others, so that one install can run many apps safely.
- As an org owner, I want members with owner, developer, and viewer roles, so that teammates can build without being able to destroy things.
- As a developer, I want scoped API keys whose secret is shown once, so that a leaked database dump never leaks a working key.
- As a developer, I want to register web hosts and app IDs as platforms, so that only my apps talk to my project from a browser.
- As an org owner, I want a deleted project to be restorable for a grace period, so that a wrong click is not a disaster.
- As the Orvano team, I want environments, usage metering, and a future cloud to fit into this model without a breaking migration.

**Acceptance criteria** (the contract):
- **AC-1**: One install holds many orgs, and each org holds many projects. Every project of kind `app` belongs to exactly one org. The only project without an org is the seeded system project `console`.
- **AC-2**: A new project gets a server generated ID of exactly 20 characters from `[a-z0-9]`, drawn from a cryptographic random source without modulo bias. Its Postgres role and schema are `p_<id>`.
- **AC-3**: Creating a project stores it as `provisioning` and enqueues `platform.project.provision` in the same transaction. The job creates the role, the schema, and the grant, and sets `active`. Running it twice has the same result as running it once. If its last attempt fails, the project becomes `failed`. An owner or developer can retry, which sets `provisioning` again and enqueues a new job.
- **AC-4**: Public API calls to a project that is `provisioning` or `failed` get 409 `project_not_ready`. Calls to a project that is `deleting`, purged, unknown, or `console` get 404 `project_not_found`.
- **AC-5**: Project data stays isolated. Every platform row owned by a project (keys, platforms, app users) carries its `project_id`, and every query filters by it. An API key used with an `X-Orvano-Project` that is not its own project is rejected with 401 `invalid_api_key`.
- **AC-6**: Console accounts are app users of the system project `console`. The public API never serves the `console` project (AC-4), so console accounts can only be created and signed in through `/v1/console/*`, where the install sign up policy applies.
- **AC-7**: The first console account on an install becomes an install admin, even when two sign ups race. After that, a sign up without a valid invitation is refused with 403 `signup_closed`, unless the install admin has set the sign up mode to `open`.
- **AC-8**: Every new console account gets a personal org, named from the account, with that account as `owner`, in the same transaction. Any account can create more orgs.
- **AC-9**: Org members hold exactly one role: `owner`, `developer`, or `viewer`. Each action is allowed or denied exactly as the permission matrix in *Security model* says, and a denied action gets 403 `forbidden`.
- **AC-10**: An org always keeps at least one owner. Removing, demoting, or leaving as the last owner fails with 409 `last_owner`. Deleting a console account fails the same way while it is the last owner of an org that has other members or live projects. A live project is one in `provisioning`, `active`, or `failed`; a `deleting` project does not count. An org where the account is the only member and no live project remains moves to `deleting` along with the account, and its purge waits for its `deleting` projects as usual.
- **AC-11**: An API key's secret is `orv_sk_` plus 43 base64url characters (32 random bytes). It is returned only in the create response. The database stores its SHA-256 hash and a 12 character visible prefix, and nothing else of the secret.
- **AC-12**: A key holds a nonempty set of scopes from the contract's `ApiKeyScope` catalog. Creating a key with an unknown scope fails with 400 `invalid_request`. A call outside the key's scopes fails with 403 `insufficient_scope`. An expired key fails with 401 `invalid_api_key`. `last_used_at` moves forward at most once per 60 seconds per key.
- **AC-13**: A platform is one of `web`, `android`, `ios`, `macos`, `windows`, `linux`, and it is unique per project, type, and identifier (ignoring case). A web platform matches a browser `Origin` by the rules in *Web origin matching*. Android and Apple identifiers must match the patterns in *Platform identifiers*, and Windows and Linux identifiers are free labels.
- **AC-14**: Deleting a project sets `deleting` and stops serving it at once. Within `ORVANO_DELETE_GRACE_DAYS` (default 7) an owner can restore it. After that, a purge job drops its schema and role and removes its keys, platforms, and app users. If the purge job runs out of attempts, the project stays `deleting` with `purge_failed_at` set, and an owner or an install admin can retry the purge.
- **AC-15**: Deleting an org fails with 409 `org_not_empty` while any of its projects is not `deleting`. A deleted org can be restored within the grace period. Its purge runs only after all its projects are purged. While an org is `deleting`, every change to it, its members, its invitations, and its projects, except restoring the org, fails with 409 `org_not_active`, and its pending invitations cannot be accepted.
- **AC-16**: The app user identity row holds `email` (optional, unique per project ignoring case), `phone` (optional, E.164, unique per project), their verification times, `name`, `status` (`active` or `blocked`), and `metadata`. The same email may exist in two projects as two separate users.
- **AC-17**: No table has a foreign key into another module's table. Cross module cleanup runs through outbox events and jobs.
- **AC-18**: Environments can be added later by adding nullable columns to `platform_projects` only. No existing column, key, schema name, or header changes.
- **AC-19**: Every create, update, or delete of an org, membership, project, API key, platform, or install setting writes an outbox event in the same transaction, with IDs, the actor, and changed field names only, never a secret. The actor is `{ "type": "user", "id": <console user ID> }` for console actions and `{ "type": "system", "id": null }` for jobs.

## Decision

**Chosen option**: Option 1: a project centric model. The project is the only unit of isolation, and environments are child projects. Users, keys, and platforms are rows in the shared `orvano` schema keyed by project ID. Console accounts are users of a built in `console` project.

One isolation unit (the project, with its own `p_<id>` schema and role) and one identity system (app users, which also serve the console) carry every product from v0.1 to the cloud.

**Implementation skills**: `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `optimizing-ef-core-queries` (`dotnet/skills`, `.claude/skills/optimizing-ef-core-queries/`) · `supabase-postgres-best-practices` (`supabase/agent-skills`, `.claude/skills/supabase-postgres-best-practices/`) · `testcontainers-integration-tests` (`aaronontheweb/dotnet-skills`, `.claude/skills/testcontainers-integration-tests/`)

## Feature design

### Data model sketch

All tables live in schema `orvano`. Conventions for every table below:

- IDs are `uuid` with default `uuidv7()` (native in Postgres 18), except `platform_projects.id`.
- Timestamps are `timestamptz not null default now()`. `updated_at` is set by the application on every update, with no triggers.
- Enumerations are `text` with a `CHECK (col IN (...))`, not Postgres enum types, so a later migration can add a value with one `ALTER TABLE`.
- Names (`name` columns) are trimmed, 1 to 100 characters, and not unique.
- Foreign keys exist only between tables of the same module (AC-17), and every foreign key column has an index.
- There are no Postgres extensions (spec 0002). Case insensitive uniqueness uses a unique index on `lower(col)`.

**Platform module** (`Orvano.Platform`, table prefix `platform_`):

| Table | Column | Type | Null | Notes |
|---|---|---|---|---|
| `platform_install_settings` | `id` | smallint | no | PK, `CHECK (id = 1)`; the migration seeds the one row |
| | `console_signup` | text | no | `invite` \| `open`, default `invite` |
| | `updated_at` | timestamptz | no | |
| `platform_install_admins` | `user_id` | uuid | no | PK; a console user (an `auth_users.id` in project `console`), no FK |
| | `created_at` | timestamptz | no | |
| `platform_orgs` | `id` | uuid | no | PK |
| | `name` | text | no | |
| | `status` | text | no | `active` \| `deleting` |
| | `deleted_at` | timestamptz | yes | set when `deleting` |
| | `purge_after` | timestamptz | yes | set when `deleting` |
| | `created_by_user_id` | uuid | no | console user, no FK |
| | `created_at`, `updated_at` | timestamptz | no | |
| `platform_memberships` | `id` | uuid | no | PK |
| | `org_id` | uuid | no | FK `platform_orgs` |
| | `user_id` | uuid | no | console user, no FK; index |
| | `role` | text | no | `owner` \| `developer` \| `viewer` |
| | `created_at`, `updated_at` | timestamptz | no | |
| | | | | UNIQUE (`org_id`, `user_id`) |
| `platform_invitations` (row 15) | `id` | uuid | no | PK |
| | `org_id` | uuid | no | FK `platform_orgs` |
| | `email` | text | no | as typed, trimmed |
| | `role` | text | no | `owner` \| `developer` \| `viewer` |
| | `token_hash` | bytea | no | SHA-256 of the invite token; UNIQUE |
| | `invited_by_user_id` | uuid | no | console user, no FK |
| | `expires_at` | timestamptz | no | created + 7 days |
| | `accepted_at`, `revoked_at` | timestamptz | yes | |
| | `created_at` | timestamptz | no | |
| | | | | UNIQUE (`org_id`, `lower(email)`) WHERE `accepted_at IS NULL AND revoked_at IS NULL` |
| `platform_projects` | `id` | text | no | PK, `CHECK (id ~ '^[a-z0-9]{1,60}$')`; generated IDs are 20 chars |
| | `org_id` | uuid | yes | FK `platform_orgs`; index |
| | `kind` | text | no | `app` \| `system`; `CHECK ((kind = 'system') = (org_id IS NULL))` |
| | `name` | text | no | |
| | `status` | text | no | `provisioning` \| `active` \| `failed` \| `deleting` |
| | `deleted_at`, `purge_after` | timestamptz | yes | set when `deleting` |
| | `purge_failed_at` | timestamptz | yes | set when the purge job runs out of attempts; cleared by a purge retry |
| | `created_by_user_id` | uuid | yes | null only for the seeded `console` row |
| | `created_at`, `updated_at` | timestamptz | no | |
| `platform_api_keys` | `id` | uuid | no | PK |
| | `project_id` | text | no | FK `platform_projects`; index |
| | `name` | text | no | |
| | `prefix` | text | no | first 12 chars of the secret (`orv_sk_` plus 5) |
| | `secret_hash` | bytea | no | SHA-256 of the full secret; UNIQUE |
| | `scopes` | text[] | no | `CHECK (cardinality(scopes) > 0)` |
| | `expires_at` | timestamptz | yes | null means never |
| | `last_used_at` | timestamptz | yes | |
| | `created_by_user_id` | uuid | no | console user, no FK |
| | `created_at` | timestamptz | no | |
| `platform_platforms` | `id` | uuid | no | PK |
| | `project_id` | text | no | FK `platform_projects` |
| | `type` | text | no | `web` \| `android` \| `ios` \| `macos` \| `windows` \| `linux` |
| | `name` | text | no | |
| | `identifier` | text | no | web: hostname pattern; others: package or bundle ID; 1 to 255 chars |
| | `created_at`, `updated_at` | timestamptz | no | |
| | | | | UNIQUE (`project_id`, `type`, `lower(identifier)`) |

Seed rows (in the migration that creates the tables): `platform_install_settings (1, 'invite')`, and `platform_projects ('console', NULL, 'system', 'Console', 'active', ...)`. The `console` project is never provisioned and has no schema.

**Auth module** (`Orvano.Auth`, table prefix `auth_`), identity core only; row 8 adds credentials and sessions as their own tables:

| Table | Column | Type | Null | Notes |
|---|---|---|---|---|
| `auth_users` | `id` | uuid | no | PK |
| | `project_id` | text | no | no FK (AC-17); index (`project_id`, `created_at`, `id`) for cursor lists |
| | `email` | text | yes | as typed, trimmed; at most 320 chars |
| | `email_verified_at` | timestamptz | yes | |
| | `phone` | text | yes | E.164, `CHECK (phone ~ '^\+[1-9][0-9]{1,14}$')` |
| | `phone_verified_at` | timestamptz | yes | |
| | `name` | text | yes | at most 256 chars |
| | `status` | text | no | `active` \| `blocked`, default `active` |
| | `metadata` | jsonb | no | default `'{}'`, `CHECK (jsonb_typeof(metadata) = 'object')`; at most 16 KB serialized (checked in code) |
| | `created_at`, `updated_at` | timestamptz | no | |
| | | | | UNIQUE (`project_id`, `lower(email)`) WHERE `email IS NOT NULL` |
| | | | | UNIQUE (`project_id`, `phone`) WHERE `phone IS NOT NULL` |

**Per project** (spec 0002, unchanged): role `p_<projectId>` (`NOLOGIN`) owning schema `p_<projectId>`, granted to `orvano_app` `WITH INHERIT FALSE, SET TRUE`.

**Relationships**:

```
platform_orgs 1 ──< N platform_memberships >── 1 console user (auth_users, project 'console', by ID)
platform_orgs 1 ──< N platform_invitations
platform_orgs 1 ──< N platform_projects (kind 'app')
platform_projects 1 ──< N platform_api_keys
platform_projects 1 ──< N platform_platforms
platform_projects 1 ──< N auth_users (by ID, across modules)
platform_projects 1 ── 1 Postgres schema and role p_<id>
```

**EF Core mapping**: one `DbContext` per module, internal to it (`PlatformDbContext` in `Orvano.Platform`, `AuthDbContext` in `Orvano.Auth`), both on the `orvano_app` data source with default schema `orvano`. A module cannot even query another module's tables. `OrvanoDbContext` in `Orvano.Core` keeps only the kernel tables. The EF drift check (spec 0002) is extended to compare every module context with `information_schema.columns`.

**Room for what comes later** (AC-18):
- Environments (row 36): add `parent_project_id text NULL REFERENCES platform_projects` and `environment text NULL` to `platform_projects`. The parent is the production project; each child is a full project with its own ID, schema, keys, platforms, and users. SDKs switch environments by switching the project ID.
- Usage metering (row 37) and a future cloud (private repo modules): their own tables keyed by `org_id` or `project_id`. The org is the unit a cloud bills. No column is reserved now.
- Per project member roles: a nullable `project_id` on `platform_memberships` later narrows a membership, with no break.
- Pausing a project: one more `status` value.

### State transitions

**Project** (`kind = 'app'`):

```
            create                   provision job succeeds
  (none) ──────────▶ provisioning ─────────────────────────▶ active
                        │   ▲                                   │
      last attempt fails│   │ retry (owner, developer)          │ delete (owner)
                        ▼   │                                   ▼
                       failed ──────── delete (owner) ─────▶ deleting ──── purge job at purge_after ───▶ (row removed)
                                                                │
                                                                │ restore (owner, org active)
                                                                ▼
                                                          provisioning (the job is idempotent, so a
                                                          restored project with a schema turns active at once)
```

- `delete` is allowed from `provisioning`, `active`, and `failed`, and sets `deleted_at = now()` and `purge_after = now() + grace`. It also enqueues `platform.project.purge` with `run_at = purge_after`.
- `restore` clears `deleted_at` and `purge_after`, sets `provisioning`, and enqueues `platform.project.provision`. There is one path back, whatever the state was before the delete.
- The provision job sets `active` only if the status is still `provisioning` when it commits (so a delete during provisioning wins).
- The purge job does its work only if the status is still `deleting` and `purge_after <= now()` (so a restore wins). Otherwise it succeeds as a no op.
- Every console transition (delete, restore, retry provisioning, retry purge) is one conditional `UPDATE ... WHERE id = @id AND status = @expected` that checks the affected row count, never a read followed by a write. Zero rows means the state moved underneath the caller, and the endpoint answers 409 `project_not_ready` or 404 as AC-4 says. Together with the purge job's row lock, a restore racing a purge either wins cleanly or finds the row gone.
- `retry purge` (owner or install admin) is allowed only while `deleting` with `purge_failed_at` set. It clears `purge_failed_at` and enqueues a new `platform.project.purge` with `run_at = now()`.

**Org**: `active ──delete (owner, all projects deleting)──▶ deleting ──purge job──▶ (row removed)`, and `deleting ──restore (owner)──▶ active`. Restoring an org does not restore its projects. Each is restored on its own, and only while the org is `active`.

**API key**: exists until deleted. There is no revoked state, because deleting the row is the revoke. Expiry is checked on use.

**Invitation** (row 15): pending, then accepted, revoked, or expired (by `expires_at`).

### Work items (jobs and events)

| Kind | Queue | Enqueued by | Does | Max attempts |
|---|---|---|---|---|
| `platform.project.provision` | `platform` | create, retry, restore | In one `orvano_admin` transaction: create role `p_<id>` if missing (a `DO` block checking `pg_roles`), `CREATE SCHEMA IF NOT EXISTS p_<id> AUTHORIZATION p_<id>`, `GRANT p_<id> TO orvano_app WITH INHERIT FALSE, SET TRUE`, set `active` where still `provisioning`, write event `platform.project.provisioned`. When `job.Attempts >= job.MaxAttempts` and the work throws, set `failed` in a separate transaction, then rethrow. | 5 |
| `platform.project.purge` | `platform` | delete | In one `orvano_admin` transaction, after locking the project row and checking the purge condition: `DROP SCHEMA IF EXISTS p_<id> CASCADE`, `DROP ROLE IF EXISTS p_<id>`, delete its keys and platforms and the project row, write event `platform.project.purged`. When `job.Attempts >= job.MaxAttempts` and the work throws, set `purge_failed_at = now()` in a separate transaction, write `platform.project.purge_failed`, then rethrow. | 10 (default) |
| `platform.org.purge` | `platform` | org delete | Locks the org row `FOR UPDATE` and checks it is still `deleting` with `purge_after <= now()`, else succeeds as a no op. If project rows of the org still exist, re-enqueue itself at the latest `purge_after` of those projects plus 1 minute. Otherwise delete invitations, memberships, and the org, and write `platform.org.purged`. | 10 |
| `auth.project.purge_users` | `auth` | Auth consumer `auth.purge_users` of `platform.project.purged` | Delete `auth_users` (and row 8's credential and session rows) where `project_id` matches, in batches of 1000 until none are left. | 10 |
| (consumer) `platform.remove_memberships` | | Platform consumer of `auth.user.deleted` where `projectId = 'console'` | Enqueues a job that deletes that user's memberships and install admin row. | |

The `platform` and `auth` queues are registered by their modules (spec 0002's `IWorkRegistry`). Only the worker holds `ORVANO_DB_ADMIN_URL`, so only jobs issue DDL. The API role never does.

**Outbox events written by the Platform module** (AC-19): `platform.org.created|updated|deleting|restored|purged`, `platform.member.added|role_changed|removed`, `platform.project.created|updated|provisioned|failed|deleting|restored|purge_failed|purged`, `platform.key.created|deleted`, `platform.platform.created|updated|deleted`, `platform.install.settings_updated`, `platform.install.admin_added`. Payload: the affected IDs, the `actor` (AC-19: a console user, or `system` for jobs), and for updates the names of changed fields. Never a key secret, hash, or invite token. `project_id` on the event row is set for project scoped events and null for org and install events. Row 38 builds the audit log by consuming these.

### API surface

This spec designs no HTTP endpoint. Rows 7, 8, and 15 add the operations to the contract (spec 0001), and each builds on the model and rules here. What this spec fixes is the surface those rows must honor.

**Module contracts** (public types in each module's `Contracts` namespace, the only way across modules):

| Contract | Module | Method | Returns | Used by |
|---|---|---|---|---|
| `IProjectDirectory` | Platform | `GetServableAsync(string projectId, CancellationToken)` | `ProjectLookup`: `Servable(ProjectInfo)`, `NotReady`, or `NotFound`, applying AC-4 in one place (`Servable` only for `active` projects of kind `app`) | the public request pipeline, the only lookup public routes may use |
| `IProjectDirectory` | Platform | `GetAsync(string projectId, CancellationToken)` | `ProjectInfo?` (`Id`, `OrgId`, `Kind`, `Status`, `PurgeFailedAt`), any state | console routes and jobs only |
| `IApiKeyVerifier` | Platform | `VerifyAsync(string projectId, string secret, CancellationToken)` | `ApiKeyVerification` (`Valid`, `KeyId`, `Scopes`) | request authentication (row 8) |
| `IConsoleAccess` | Platform | `GetOrgRoleAsync(Guid userId, Guid orgId, CancellationToken)` and `GetProjectRoleAsync(Guid userId, string projectId, CancellationToken)` | `OrgRole?` | every console endpoint's permission check |
| `IConsoleAccountGuard` | Platform | `CheckDeleteAsync(Guid userId, CancellationToken)` | `Allowed`, or the orgs that block it (AC-10) | Auth, before deleting a console user |
| `IConsoleSignupPolicy` | Platform | `AdmitAsync(NpgsqlTransaction tx, string email, string? inviteToken, CancellationToken)` | `Admitted` (with `IsFirstAccount`, the invite's org and role) or `Refused` | console sign up (rows 7 and 8) |
| `IConsoleAccountCreated` | Platform | `OnCreatedAsync(NpgsqlTransaction tx, Guid userId, string displayName, SignupAdmission admission, CancellationToken)` | nothing | Auth calls it inside the sign up transaction: it adds the install admin row (first account), the personal org and owner membership, and the invite's membership |
| `IUserDirectory` | Auth | `GetManyAsync(string projectId, IReadOnlyCollection<Guid> ids, CancellationToken)` | `UserSummary` (`Id`, `Email`, `Name`, `Status`) | the console members list |

The console sign up transaction is owned by Auth (it creates the user). It calls `AdmitAsync` first, which locks `platform_install_settings` with `SELECT ... FOR UPDATE` so that exactly one racing sign up sees an empty `platform_install_admins`. Then Auth inserts the user and calls `OnCreatedAsync`, all on the same transaction.

**Error codes** to add to `contract/errors.tsp`, each added by the row that first returns it:

| Code | Status | When | Row |
|---|---|---|---|
| `project_not_found` | 404 | unknown, purged, `deleting`, or `console` project on a public route | 7 |
| `project_not_ready` | 409 | project `provisioning` or `failed` | 7 |
| `forbidden` | 403 | the console role does not allow the action | 7 |
| `last_owner` | 409 | AC-10 | 7 |
| `org_not_empty` | 409 | AC-15 | 7 |
| `org_not_active` | 409 | create or restore a project in a `deleting` org | 7 |
| `signup_closed` | 403 | AC-7 | 7 |
| `invalid_api_key` | 401 | unknown, expired, or wrong project key | 8 |
| `insufficient_scope` | 403 | the key lacks the operation's scope | 8 |
| `origin_not_allowed` | 403 | the browser `Origin` matches no web platform | 8 |

**Scopes**: a TypeSpec `enum ApiKeyScope` in the contract, generated into every SDK and `Orvano.Contract` by SdkGen. Values are `<resource>.<read|write>`. Row 8 adds the first two, `users.read` and `users.write`, and each product row adds its own. Each server audience operation declares its required scope with a new extension `x-orvano-scope` (SdkGen refuses a `server` or `both` operation without one, except `health`). A `write` scope does not imply `read`. Scopes that later leave the catalog are ignored on existing keys.

### Web origin matching

A web platform's `identifier` is stored lowercase and is one of:
- a hostname (`app.example.com`), which matches that exact host on any scheme and port;
- `*.` plus a hostname with at least two labels (`*.example.com`), which matches exactly one more label (`a.example.com`, not `example.com` nor `a.b.example.com`);
- `localhost`, which matches `localhost` on any port;
- an IPv4 literal (`127.0.0.1`), which matches that exact address on any port.

`*` alone, `*.com`, and patterns with a `*` anywhere else are refused at create with 400 `invalid_request`. An `Origin` of `null` never matches. A request with no `Origin` header (servers, Flutter mobile and desktop) is not checked, because spec 0001 relies on no origin check for those. CORS preflight (`OPTIONS`) answers with the request's origin, since a preflight cannot carry the project header value. The real request is then checked, and gets 403 `origin_not_allowed` on no match. Native types (`android`, `ios`, `macos`, `windows`, `linux`) are recorded for row 12's native sign in (which checks the ID token audience against them), and Orvano cannot verify them on plain requests.

### Platform identifiers

Checked at create and update, with 400 `invalid_request` on a mismatch. Stored as typed (uniqueness ignores case).

| Type | Identifier | Rule |
|---|---|---|
| `web` | hostname pattern | *Web origin matching* above |
| `android` | package name | `^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$`, at most 255 chars |
| `ios`, `macos` | bundle ID | `^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$`, at most 255 chars |
| `windows`, `linux` | free label | 1 to 255 chars, no whitespace or control characters |

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Create project | `id` | 20 chars, each drawn from `abcdefghijklmnopqrstuvwxyz0123456789` with `RandomNumberGenerator.GetItems` (unbiased) |
| Create project | `org_id` | request input (row 7), org must be `active` (row locked `FOR SHARE`) |
| Create project | `created_by_user_id` | the console session's user (row 8 session model) |
| Create project | initial `status` | constant `provisioning` |
| Provision and purge | role and schema name | `ProjectScope.RoleName(id)` (spec 0002) |
| Delete project or org | `purge_after` | `now()` plus `ORVANO_DELETE_GRACE_DAYS` days |
| Purge org | when to run | its own `purge_after`, pushed out to the latest project `purge_after` plus 1 minute |
| Any public project call | which project | `X-Orvano-Project` header (spec 0001), resolved by `IProjectDirectory` |
| Any public project call | whether it is served | `platform_projects.status` and `kind` (AC-4) |
| Create API key | secret | `orv_sk_` plus base64url without padding of 32 bytes from `RandomNumberGenerator` |
| Create API key | `prefix` | first 12 chars of the secret |
| Create API key | `secret_hash` | SHA-256 of the full secret's UTF-8 bytes |
| Create API key | `scopes` | request input, each checked against `ApiKeyScope` |
| Create API key | `expires_at` | request input: null, or a time in the future (the console offers 30, 90, 365 days or a date) |
| Verify API key | the key | `X-Orvano-Key` header (spec 0001 temporary name, row 8 may rename), hashed and looked up by `secret_hash` |
| Verify API key | valid or not | row found, `project_id` equals the header project, `expires_at` null or later than now, project `active` |
| Verify API key | `last_used_at` | `now()`, written only when the stored value is null or older than 60 seconds (constant `ApiKeyUsage.Resolution`) |
| Check a browser origin | allowed hosts | `platform_platforms` rows of type `web` for the header project |
| Console permission check | the caller's role | `platform_memberships.role` for (the project's `org_id`, the session user) |
| Console sign up | first account or not | `platform_install_admins` is empty, read under the `platform_install_settings` row lock |
| Console sign up | allowed or not | `platform_install_settings.console_signup`, or a valid invite token (row 15) |
| Personal org | `name` | the account's `name`, else the part of the email before `@`, followed by `'s org`, cut to 100 chars |
| Invitation (row 15) | token | 32 random bytes, base64url; stored as SHA-256 in `token_hash` |
| Invitation (row 15) | `expires_at` | created plus 7 days (constant) |
| App user | uniqueness | `lower(email)` and `phone` within `project_id` |
| Events | payload | affected IDs, changed field names, and `actor`: the console session user for console actions, `{ type: system, id: null }` in jobs (AC-19) |
| Retry purge | allowed or not | `platform_projects.status = 'deleting'` and `purge_failed_at IS NOT NULL` |
| Test fixtures | seeded projects, keys, users | `tests/scenarios/fixtures.yaml` keys `projects`, `apiKeys`, `users` (see Build plan task 6) |

### Key invariants

- A project of kind `app` has exactly one org, and the `system` project has none (DB `CHECK`).
- Project IDs match `^[a-z0-9]{1,60}$` (DB `CHECK` and `ProjectScope`).
- An org with at least one membership always has at least one `owner`. Every membership change locks the org row `FOR UPDATE` first, so two owners cannot demote each other at once.
- Once any console account exists, at least one install admin exists. The last install admin cannot be removed, blocked, or deleted.
- A project is served on public routes only while `active` and `kind = 'app'`, decided only in `IProjectDirectory.GetServableAsync`.
- A `deleting` org gains no project, member, or invitation. Project create and restore take the org row `FOR SHARE` and require `active`, and org delete and org purge take it `FOR UPDATE`, so no project can appear under an org that is being purged.
- Only the worker issues DDL, and only inside the provision and purge jobs.
- A key's secret never touches the database, logs, events, or error messages. Only its hash and prefix are stored.
- `created_at` never changes, and IDs are never reused (the 20 character random space makes collision practically impossible, and a unique violation on insert retries with a new ID).
- No foreign key crosses a module boundary.
- Every mutation listed in AC-19 writes its event in the same transaction.

### Security model

**Console roles** (per org, from `platform_memberships`):

| Action | owner | developer | viewer |
|---|---|---|---|
| See the org, its projects, members, keys (name, prefix, scopes, dates), platforms | yes | yes | yes |
| Create a project, edit a project's name and settings, retry provisioning | yes | yes | no |
| Delete or restore a project, retry a failed purge | yes | no | no |
| Create an API key | yes | yes | no |
| Delete an API key | yes, any | only keys they created | no |
| Add, edit, delete platforms | yes | yes | no |
| Invite, remove members, change roles | yes | no | no |
| Rename, delete, or restore the org | yes | no | no |
| Leave the org | yes (not as last owner) | yes | yes |

**Install admin** (from `platform_install_admins`): retry a failed project purge on any org (a failed purge can hold personal data the owner wants gone), change `console_signup`, list console accounts, block or delete them (subject to AC-10), add another install admin, list every org's name and ID. It grants no project access: an install admin sees project data only through their own memberships.

**Other rules**:
- The public API refuses the `console` project everywhere (AC-4, AC-6). No API key or platform can be created for it.
- API keys are SHA-256 hashed. The secret has 256 bits of randomness, so a slow password hash adds nothing, and a fast hash keeps lookup cheap. Hash lookup uses the unique index, so no key is compared in a loop.
- Every console action is logged at `Information` with the actor ID, org ID, project ID, and action name. It is never logged with a secret, token, or email body.
- API key verification failures are counted by the rate limiter (row 14 sets the policy) and never say which check failed beyond `invalid_api_key`.
- Personal data: `auth_users.email`, `phone`, `name`, and `metadata` are personal data of the developer's end users. Deletion is real: the purge job removes them for good. Row 8 (GA) owns the rest of the auth data handling.

### Configuration required

- `ORVANO_DELETE_GRACE_DAYS`: days between deleting a project or org and its purge. An integer from 0 to 90, default 7. Validated at startup by every role (the API writes `purge_after`, and the worker purges). 0 purges at the next job run.

### Critical test scenarios

- Happy path: on a fresh database, sign up the first console account. It becomes install admin with a personal org. Create a project, and the provision job makes `p_<id>` and sets `active`. Create a key with `users.read` and a web platform, and a call with that key and project passes the key check. Verifies **AC-1**, **AC-2**, **AC-3**, **AC-7**, **AC-8**, **AC-11**.
- Concurrency: two first sign ups at once give exactly one install admin. Two owners demoting each other at once leave one owner. Verifies **AC-7**, **AC-10**.
- Provisioning failure: a provision job forced to fail on every attempt leaves the project `failed`. Retry makes it `active`. Running the job twice after success changes nothing. Verifies **AC-3**.
- Deletion: delete a project, and its calls get 404 at once. Restore it within the grace period, and it is `active` with its data. Delete again with grace 0: the purge drops schema and role, removes keys, platforms, and users, and a restore that races the purge either wins cleanly or finds the row gone. Verifies **AC-4**, **AC-14**.
- Org deletion: deleting an org with an active project gives 409 `org_not_empty`. After its projects are deleted, the org purge waits for them. While the org is `deleting`, inviting, changing a role, and renaming get 409 `org_not_active`. Verifies **AC-15**.
- Purge failure: a purge job forced to fail on every attempt leaves the project `deleting` with `purge_failed_at` set. Retry purge by the install admin completes it. Verifies **AC-14**.
- Isolation: a key for project A used with header project B gets 401. An expired key gets 401. A key without the operation's scope gets 403. `console` as the project header gets 404. As `orvano_app` without `ProjectScope`, reading `p_<id>` tables fails with permission denied. Verifies **AC-4**, **AC-5**, **AC-6**, **AC-12**.
- Auth/permission: a viewer creating a key, and a developer deleting a project or another member's key, each get 403 `forbidden`. A sign up without an invite on an `invite` install gets 403 `signup_closed`. Verifies **AC-7**, **AC-9**.
- Origins: `*.example.com` allows `https://a.example.com` and refuses `https://example.com` and `https://a.b.example.com`. `localhost` allows any port. Creating `*.com` or `*` fails. Verifies **AC-13**.
- App users: the same email in two projects is two users. The same email in different case in one project is refused. Verifies **AC-16**.
- Structure: a schema test finds no foreign key between `platform_` and `auth_` tables, and the drift check passes for both module contexts. Every AC-19 mutation writes one event, and no event payload contains `orv_sk_`. Verifies **AC-17**, **AC-19**.
- Environments ready: a test migration adding `parent_project_id` and `environment` to `platform_projects` applies on a populated database with no other change. Verifies **AC-18**.

## Build plan

Row 3 is design only. The build lands with the rows that first use each part, so every table ships with a feature that exercises it end to end (Tracer Bullet). Rows 7 and 8 together are the v0.1 thread, and row 7's console sign up needs row 8's `auth_users` and sign in. Build task 7 (row 8) before or alongside task 3 (row 7). Migration numbers are the next free ones when each task lands.

**Row 7, Console accounts, orgs & projects** (v0.1):

1. Create project `Orvano.Platform` (added to `OrvanoModules`, `Orvano.slnx`, and the Dockerfile restore step). Migration `NNNN_platform.sql` creates `platform_install_settings`, `platform_install_admins`, `platform_orgs`, `platform_memberships`, `platform_projects`, `platform_api_keys`, `platform_platforms` with the seeds, plus `PlatformDbContext`. Extend the drift check to every module context. Satisfies **AC-1**, **AC-17**, **AC-18**.
2. Domain types with unit tests, free of ASP.NET, EF, and Npgsql: `ProjectIdGenerator`, the project and org state machines, the role permission matrix, the last owner rule, `ApiKeySecret` (generate, prefix, hash), `WebOriginPattern` (validate, match), and `PersonalOrgName`. Satisfies **AC-2**, **AC-9**, **AC-10**, **AC-11**, **AC-13**.
3. Console sign up rules: `IConsoleSignupPolicy` and `IConsoleAccountCreated` (first account becomes install admin, personal org, invite only gate), `IConsoleAccountGuard`, and the Platform consumer of `auth.user.deleted`. Satisfies **AC-6**, **AC-7**, **AC-8**, **AC-10**.
4. Projects: create, retry, delete, restore, retry purge, with the `platform.project.provision` and `platform.project.purge` jobs, `IProjectDirectory` (both lookups), and the AC-4 responses; the `org_not_active` rule; orgs: create, rename, delete, restore, and `platform.org.purge`; `ORVANO_DELETE_GRACE_DAYS` validated at startup. Satisfies **AC-1**, **AC-3**, **AC-4**, **AC-14**, **AC-15**.
5. API keys and platforms: create, list, delete, scope validation against `ApiKeyScope`, `IApiKeyVerifier` with the throttled `last_used_at`, and `IConsoleAccess`. The console operations go in the contract (the `console` audience) with the error codes above. Satisfies **AC-5**, **AC-9**, **AC-11**, **AC-12**, **AC-13**.
6. Outbox events for every AC-19 mutation. Extend `tests/scenarios/fixtures.yaml` with `projects` (`id`, `name`), `apiKeys` (`project`, `secret`, `scopes`), and `users` (`project`, `email`), seeded through the same domain code in `Test` only. Satisfies **AC-19**, **AC-5**.

**Row 8, App user sign up & sign in** (v0.1):

7. Project `Orvano.Auth`. Migration `NNNN_auth_users.sql` creates `auth_users` with its unique and list indexes, plus `AuthDbContext` and `IUserDirectory`. Its credentials and sessions tables come from row 8's own spec. Satisfies **AC-6**, **AC-16**, **AC-17**.
8. Request authentication: resolve `X-Orvano-Project` through `IProjectDirectory.GetServableAsync` (AC-4), verify keys through `IApiKeyVerifier` including scope checks from `x-orvano-scope`, and check browser origins against web platforms. Adds `ApiKeyScope` with `users.read` and `users.write`, `x-orvano-scope` in SdkGen, and the error codes `invalid_api_key`, `insufficient_scope`, `origin_not_allowed`. Satisfies **AC-4**, **AC-5**, **AC-12**, **AC-13**.
9. Console accounts as users of project `console`: the `/v1/console` sign up path calls `AdmitAsync` and `OnCreatedAsync` in one transaction. The Auth consumer `auth.purge_users` and its job. Satisfies **AC-6**, **AC-7**, **AC-8**, **AC-14**.

**Row 15, Console team members & roles** (v0.3):

10. Migration `NNNN_platform_invitations.sql` and the invite flow: create, accept (through `AdmitAsync`), revoke, expire; member role change and removal with the org row lock. Satisfies **AC-7**, **AC-9**, **AC-10**.

**Row 36, Environments & schema promotion** (v0.14): adds the two nullable columns in *Room for what comes later*, and nothing else in this model. Satisfies **AC-18** (verified when row 36 builds).

## Consequences

**Positive**:
- Nothing built on spec 0002 changes: `p_<id>`, `ProjectScope`, `X-Orvano-Project`, and SDK config all keep working when environments arrive.
- One auth system protects both app users and the console, so each later hardening row (MFA, passkeys, rate limits) covers the console with no second build.
- A leaked database dump exposes no working API key or invite token.
- Deletion is forgiving for 7 days, then real: disk is reclaimed and personal data is gone.
- Every platform mutation already writes an event, so row 38's audit log and the webhooks product have a source from day one.

**Negative / tradeoffs**:
- App users of every project share one table, so Postgres itself does not isolate them. Only application code filtering by `project_id` does. A missed filter leaks users across projects, which is why AC-5's tests and the module contracts matter.
- Backing up or restoring one project (row 35) means dumping its schema and copying its platform rows `WHERE project_id = ...`, not only `pg_dump -n`.
- An environment is a full project, so each environment costs its own schema and role, and quotas count it as a project.
- Console accounts depend on the Auth module: row 7 cannot finish its sign up before row 8's user table and sign in exist, and a bug in app user auth can reach the console.
- There are no cross module foreign keys, so a membership can briefly point at a deleted user until the cleanup job runs. Readers must tolerate a missing user.
- The owner rule and first account rule need row locks and careful tests, since the database alone cannot express them.
- A restored project passes through `provisioning` for a moment, even when its schema is intact.

**Neutral**:
- Two new modules (`Orvano.Platform`, `Orvano.Auth`) each with their own `DbContext`, and a drift check over both.
- A new contract extension (`x-orvano-scope`) and enum (`ApiKeyScope`), which SdkGen must learn.
- A new setting, `ORVANO_DELETE_GRACE_DAYS`.

## Follow-up

- [ ] Row 8's spec must decide the console session format and CSRF rule as well as app sessions, since console accounts are app users of `console` (spec 0001 left the console cookie format to rows 7 and 8).
- [ ] Spec 0002: its Follow-up item for row 3 (ID format, `provisioning` state, where app users live) is answered here; mark it done and point to this spec. Also update its Postgres layout table, which says the worker uses `orvano_admin` "for provisioning jobs only": the purge jobs here need it too (`DROP SCHEMA`, `DROP ROLE`).
- [ ] Row 35 (backups): per project backup and restore must include the project's rows in `auth_users`, `platform_api_keys`, and `platform_platforms`, not only its schema.
- [ ] Row 36 (environments): confirm the child project shape; the console shows a project family with an environment switcher.
- [ ] Row 38 (audit log): consume the AC-19 events into a durable audit table before events are pruned (7 days).
- [ ] Row 14 (abuse protection): set rate limits on API key failures and console sign up.
- [ ] Row 12 (native sign in): match the ID token audience against `android`, `ios`, and `macos` platform identifiers.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
