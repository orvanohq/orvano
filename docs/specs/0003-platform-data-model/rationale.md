# 0003. Platform data model: rationale

Decision record for [index.md](index.md). `/develop` builds from `index.md` and can skip this file.

## Context

Orvano is a self hosted backend where one install runs many projects from one console (scope row 3). Every product, from auth in v0.1 to backups in v0.14, stores rows that belong to a project, checks who may act on a project, and authenticates calls with a session or an API key. Whatever shape the project, the org, the member, the key, and the user take now is copied into dozens of later tables, queries, and SDK configs. Getting it wrong means a migration of every product table later, on installs we do not operate.

Spec 0002 already fixed part of the ground. Each project has its own Postgres schema and `NOLOGIN` role (`p_<id>`), reached only through `ProjectScope`, so Postgres itself blocks cross project access to project data. The public API holds no DDL rights, so creating a schema is a worker job, and projects therefore go through a `provisioning` state. Spec 0001 routes every project call with `X-Orvano-Project` and keeps API keys out of client SDKs by construction. Spec 0002 left three questions to this row: the project ID format (limited to `[a-z0-9]`, at most 60 characters), how to model provisioning, and where app users live.

The forces pulling on the model: the scope requires that environments (dev, staging, prod, row 36) arrive later without a breaking migration, and that usage metering and a future managed cloud fit in too. The install runs on a small server exposed to the internet, so the default posture must be safe: an open console sign up page on a fresh server is an invitation to strangers. Self hosters make mistakes with no support team behind them, so a wrong click on "delete project" should be recoverable. Auth is the product's most sensitive surface (rows 8, 10, 12, 13, 14 are all tagged GA), so every duplicated auth code path is a second place to get security wrong. The team is small and the stack is fixed (.NET 10, EF Core for platform tables, raw Npgsql for project schemas), so the model must stay simple to build and operate.

Leaving this undecided blocks v0.1: row 7 (console accounts, orgs, projects) and row 8 (app user sign up) cannot start without it.

## Options considered

### Option 1: Project centric model (chosen)

The project is the only unit of isolation. It owns one Postgres schema and role. Everything the platform knows about a project (its API keys, platforms, app users) is a row in the shared `orvano` schema with a `project_id` column, managed with EF Core by the module that owns it. An environment is a full project linked to a parent project, added later as two nullable columns. Console accounts are app users of a built in system project `console`, so one auth system serves both.

**Pros**:
- Nothing from spec 0002 or spec 0001 changes when environments arrive: `p_<id>`, `ProjectScope`, `X-Orvano-Project`, and SDK config keep their meaning.
- Platform rows use EF Core with fixed table names, which is the stack's convention for platform tables.
- One hardened auth system protects the console too (Appwrite runs its console the same way).
- Listing users across projects, and the console Users page, are simple queries.

**Cons**:
- App users of all projects share one table, so their isolation rests on application code, not on Postgres.
- Backing up one project means its schema plus its rows in shared tables.
- Each environment costs a full schema and role, and counts as a project.
- The console depends on the app user auth module, so an auth bug can reach it.

### Option 2: Environment centric model

An `environments` table sits between the project and everything else. Each project gets a `production` environment on creation. Schemas, roles, keys, platforms, and users are keyed by environment, and the SDKs send an environment alongside the project.

**Pros**:
- Models row 36 literally: environments are inside one project, with one project ID.
- Settings that are truly shared by all environments (name, members) live once, on the project.

**Cons**:
- Every table, index, event, and job gains an environment dimension today, for a feature that ships in v0.14.
- The schema name rule (`p_<projectId>`, already coded in `ProjectScope`) has to change to an environment ID, and the SDK routing header gains a second value.
- Most installs will never create a second environment, yet everyone pays the complexity.

### Option 3: Schema contained model

App user data lives in a second per project schema (`p_<id>_auth`), owned by `orvano_admin`, so Postgres isolates users just like project data. The project role sees users only through a narrow view. Console accounts live in their own `platform_accounts` table with their own auth.

**Pros**:
- Postgres, not application code, keeps one project's users from another's.
- A project's full data is a set of schemas, so per project backup is `pg_dump -n`.
- The console's identity is fully separate from app user auth.

**Cons**:
- Every auth query runs against a dynamic schema, so it cannot use EF Core, and each migration of the auth tables must run once per project.
- The schema count per project doubles, which weighs on the Postgres catalog that spec 0002 already flags as a limit.
- Console auth (passwords, sessions, later MFA and passkeys) is built and secured twice.

## Rationale

Option 1 wins because it satisfies the scope's hardest requirement, environments without a breaking migration, by adding nothing now. Treating each environment as a child project keeps every existing contract intact: the schema rule already coded in `ProjectScope`, the header every SDK sends, and the config a developer pastes into an env file. Option 2 would satisfy it too, but only by making every table and call carry an environment from v0.1, a cost paid by every install for a feature most will not use until v0.14. Supabase branching shows that separate project identities per environment work well for developers: switching environment means switching one ID.

On app users, the forces are isolation versus operability. Option 3's per project auth schema is the stronger isolation story, but it throws away EF Core for the most query heavy module, runs every auth migration once per project, and doubles catalog growth on a small server. Option 1 keeps users in one EF managed table and makes the isolation risk explicit and testable instead: every query goes through a module that takes the project ID as input, and AC-5's tests prove a key or header for another project is refused. Project data proper (the tables developers create) still gets Postgres level isolation from spec 0002, which is where untrusted SQL (the SQL editor, functions) will run.

Making console accounts users of a `console` project follows from auth being the GA surface. Every hardening row (MFA, passkeys, session revocation, abuse limits) then protects the console at no extra cost, and there is one password hashing and session code path to review. The risk this creates, that the public app user API could be used to create console accounts and skip the invite only rule, is closed by refusing the `console` project on every public route (AC-4, AC-6).

The smaller decisions follow the same forces: safe defaults on an internet facing server (invite only sign up, keys shown once and stored as hashes, a grace period before destructive purges), and the smallest model that leaves clean extension points (nullable columns for environments and per project roles, new tables for metering and the cloud).

### Smaller decisions and their runners up

| Decision | Chosen | Why | Runner up |
|---|---|---|---|
| Row IDs | UUIDv7 from Postgres 18 `uuidv7()` | Native, time ordered for compact indexes, a standard type in every SDK language | Stripe style prefixed text IDs, which read better in logs but need custom generation and validation everywhere |
| Project ID | 20 random chars of `[a-z0-9]` | Fits the 60 char limit, about 103 bits of randomness, no names leaked into schema names | A custom readable ID, friendlier but permanent, clash prone, and visible in the catalog |
| Org requirement | Every project in an org; personal org made at sign up | One ownership path for access checks and, later, billing | A project owned by an account or an org, which doubles every check |
| Console sign up | First account is install admin, then invite only | A fresh public server is not open to strangers | Open sign up by default |
| Install setting home | One database row, edited in the console | One source of truth the install admin controls | An env var that overrides the row, handy for automation but two places to look |
| Install admin powers | Settings and accounts, no project access | Keeps org roles meaningful on a shared server | Superuser over every org |
| Member roles | Org level owner, developer, viewer | Exactly row 15; per project grants can be added as a nullable column | Org role plus per project grants now |
| Developer powers | Build, not destroy | Developers ship without being able to delete projects or members | Owners alone hold API keys |
| Deletion | `deleting` with a grace period, then a purge job | Recovers from wrong clicks, then really frees disk and removes personal data | Immediate hard delete by job |
| Grace period | `ORVANO_DELETE_GRACE_DAYS`, default 7, 0 to 90 | Self hosters with little disk or strict retention can shorten it | Fixed at 7 days |
| Org deletion | Blocked while a project is not `deleting` | No mass delete by accident | Cascade into the grace period |
| Last owner | Blocked; a sole member org with no live projects goes with the account | Orgs never orphan, and people can still delete their own account | Promote the longest serving developer |
| Cross module references | IDs only, cleanup through events and jobs | Keeps spec 0002's module rule literal, lets a module move out later | Foreign keys without cascade |
| Key scopes | A scope list from a contract catalog | Row 7 needs scoped keys; one catalog keeps SDKs and server in step | Full access keys, scopes later |
| Key storage | SHA-256 hash and a visible prefix, shown once | A dump never yields a working key; 256 bit secrets need no slow hash | Envelope encrypted and shown again |
| Key expiry | Optional `expires_at`, plus `last_used_at` | Good hygiene without breaking a forgotten server on a fixed date | Required expiry within a year |
| `last_used_at` writes | At most once per 60 seconds per key | Avoids a write on every request | Every request, simpler and write heavy |
| Web origins | Exact host, one level `*.` wildcard, `localhost` any port | Covers preview deploys without letting in the whole web | Exact full origins only |
| Platform types | `web`, `android`, `ios`, `macos`, `windows`, `linux` | Every Flutter target, recorded even where Orvano cannot verify it | Web and mobile only |
| App user row | Identity core now, credentials and sessions in row 8 | Fixes what other modules reference, leaves row 8's GA session decision to its own spec | Location and keys only |
| Project states | `provisioning`, `active`, `failed`, `deleting` | Each has a real caller now; `paused` can be added as one value | Add `paused` now |
| Cloud room | No columns; the org is the billing unit; metering and cloud add tables | Nothing sits unused on self hosted installs | Plan and limits columns on orgs now |
| Enumerations | `text` with a `CHECK` | A new value is one `ALTER TABLE`, no enum type juggling | Postgres enum types |
| EF contexts | One `DbContext` per module | The compiler stops a module from querying another's tables | One shared context with module configurations |
| Restore path | Always back through `provisioning` | One idempotent path whatever the state before delete | Remember the prior state in a column |
| Provision retries | 5 attempts, then `failed` with a Retry action | A broken provision shows in minutes, not hours | The default 10 attempts |
| Project not ready | 409 `project_not_ready`; deleted or unknown is 404 | Callers can tell "wait" from "gone" without revealing deleted projects | 503 with `Retry-After`, which SDKs would retry only on GET |
| Audit trail source | An outbox event for every platform mutation | Row 38 and webhooks get a source from day one, in the same transaction | Leave auditing to row 38 |

### Refinement made while writing

You chose "blocked" for the last owner rule. Every account also gets a personal org, so a strict reading would stop anyone from ever deleting their account until they deleted their personal org first. The spec therefore deletes an org along with the account when that account is its only member and no live project remains, and still blocks deletion for any org with other members or live projects (AC-10).
