# 0011. Platform data model: rationale

The decision record behind [index.md](index.md). `/develop` builds from `index.md` and does not need this file.

## Context

Orvano keeps two kinds of data. The first is the data its users' apps store, which lives in one database per project (spec 0006 TEN-1, spec 0007 PG-2). The second is Orvano's own: who may sign in to the console, which organizations and projects exist, the keys that open them, the end users of each app with their sessions and tokens, the secrets Orvano must use again, and the usage, audit, and log history later slices need. This spec is about the second kind, all of which sits in the one platform database.

Several earlier decisions press on it. The server is a modular monolith where each module owns one schema and never touches another's tables (spec 0002 DAT-1, LAY-4), yet a project is something nearly every module hangs data off. Project isolation is automatic through a named query filter (DAT-2a). The data API must stay engine neutral, so a later MongoDB or MySQL project must still have users, sessions, and keys. Sessions and keys have exact storage rules already (TOK, KEY, SCR in spec 0006), and the platform database is PostgreSQL 18 only, reached by EF Core 10 (spec 0007). Specs 0002 and 0006 both left the id format, the table shapes, and the schema split to this feature.

The scope sets the bar: the model must carry every slice (team roles, usage, audit, messaging targets) without a breaking redesign. Backwards compatibility is not required before beta, but a redesign of keys, anchors, or ids after slice 1 would touch every module and every SDK. The build approach is Tracer Bullet, so whatever feature 8 builds must not turn into a horizontal layer of tables that no slice uses yet.

Orvano stores personal data of other people's users and secrets for many projects on one install. What gets deleted, what is kept, and for how long are security questions as much as data questions.

## Options considered

### Option 1: One shared core schema

All anchor and identity tables (organizations, projects, operators, end users, keys, sessions) sit in one `core` schema owned by a single module. Other modules point at them freely, and settings sit in a `jsonb` column on the project.

**Pros**:

- Fewest moving parts. One place to look, and simple joins.
- No cross schema anchor mapping in EF.

**Cons**:

- One module ends up owning auth, console accounts, and settings for everyone, so LAY-4 boundaries erode and that module turns into a god module.
- Untyped settings, written by every module, lose validation and migrations.
- End user sign in and operator sign in rules sit side by side, so one filter mistake mixes them.

### Option 2: Module owned schemas joined only at four anchors (chosen)

Each module owns its schema. Tables may point across schemas only at `platform.organization`, `platform.project`, `platform.operator`, and `auth.user`, with fixed delete rules. History tables keep plain ids. Settings are typed, one table per module. Ids are uuid v7 made in the app. Feature 8 builds only the conventions and the tests.

**Pros**:

- Keeps LAY-4 intact while the database still guarantees that deleting a project or user leaves nothing behind.
- Typed, module owned settings and secrets stay next to the code that uses them.
- The architecture tests turn the rules into build failures.
- Slices stay vertical: each feature creates its own tables when it needs them.

**Cons**:

- Anchor FKs tie migration order across modules and need the key only anchor mapping trick in EF.
- Creating a project needs one transaction across three modules (TXN-1).
- Raw SQL for partitioned tables.

### Option 3: Ids only, no cross schema constraints

Like option 2, but no FK ever crosses a schema. Each module listens for `ProjectDeleted` and `UserDeleted` events and deletes its own rows.

**Pros**:

- The purest module isolation. Any module could move to its own database later.
- No migration order coupling.

**Cons**:

- Correctness rests on every handler running. A failed or forgotten handler leaves orphans, often personal data the operator believes is gone.
- Nothing in the database stops a row pointing at a project that never existed.

### Option 4: End users inside each project database

Like option 2 for console data, but end users, identities, and sessions live in an `auth` schema inside every project database, Supabase style.

**Pros**:

- App SQL can join to users directly, and row permissions can use real FKs.
- A project's whole dataset is one database to back up.

**Cons**:

- Breaks engine neutrality. A non Postgres project would need a second auth store.
- Every project database needs auth migrations, and signing key lookup by `kid` (TOK-3) and the revocation set (TOK-6) would have to fan out across databases.
- Auth tables would sit beside SQL that the SQL editor runs as the project role (TEN-3).

## Rationale

Option 2 is the one that keeps both earlier promises: module boundaries (LAY-4) and a database that cannot keep orphans. Option 3 is cleaner on paper, but it moves the guarantee that "deleting a project removes it" from a constraint into a set of event handlers, and for an install holding other people's personal data that is the wrong place for it. Limiting FKs to four anchors gives most of option 3's isolation: no module depends on another's internal tables, and only on four stable rows whose shape will not change. Option 1 is simpler today, but it gives one module everyone's data and throws away the typed settings that feature 13's CORS cache and feature 14's token lifetimes need.

End users go in the platform database (rejecting option 4) because the scope commits to an engine neutral data API and to more engines later. TOK-3 and TOK-6 also both assume one table to look up keys and revocations in. The cost is that app SQL cannot join to users, which is acceptable since rows store an owner id, and permissions (feature 9) check ids, not joins.

The smaller calls follow the same forces. Ids are made in the app so that SCR-1's authenticated extra data and the outbox payloads know the id before insert. They are plain uuids in public, because a prefix codec in six SDKs buys readability at a real cost. Deletes are hard, with an async teardown, because soft delete would put a filter on every query and a hole in every unique index, and would leave personal data behind. Deleting the project's data key during teardown is a free safety net: any secret a teardown step missed becomes unreadable. Partitions are kept by an in house job rather than `pg_partman`, because spec 0007 keeps PostgreSQL the only dependency and some managed hosts lag on extensions. The audit trigger exists because PG-2a gives migrate and serve one owner role, so grants cannot make the table append only.

The engineer's choices matched the recommendation on every question in the design conversation, so there is no recorded override.

### Changes from the cross check (2026-10-18)

An independent model read the first draft. The engineer accepted every recommended fix. Five of them changed a decision made in the conversation:

- **Project creation (TXN-1)** moved from one transaction shared across three `DbContext`s to a `creating` status with a separate commit per module. Sharing a connection and transaction across contexts fights the retry execution strategy (DAT-8): nested strategies throw, and a retry runs the whole delegate again with fresh random keys. A `creating` project is invisible like a `deleting` one, and teardown already knows how to clean up any partial state.
- **Refresh reuse window**: the server cannot return "the same new pair" when it stores only hashes, so it issues a sibling token instead, and `replaced_by_id` is gone. Used tokens are purged 7 days after use, so a 90 day session does not keep thousands of rows.
- **Signing key `retired` state** dropped: a key leaves the JWKS and is deleted at `retire_at`. Keeping it 30 days more served nothing.
- **Revocation expiry** is a constant 61 minutes (the TOK-2 maximum plus skew), so it works after the project row is gone and survives a change to the project's lifetime.
- **Short codes** use an HMAC under a key derived from the root key: a plain SHA-256 of a 6 digit code is reversed by trying all million codes.

The remaining fixes corrected how the conventions are built on EF Core 10 and Npgsql 10: a convention plugin, named CHECK constraints, xmin, anchor overloads, a pass through `EncryptedValue` with an `ISecretProtector`, index APIs, and the `labels` CHECK. They also covered races (atomic token use, lock order for owners and user deletes), partition safety (a `DEFAULT` partition, the trigger on the parent), and the DAT-2a exceptions.

## AC-10 traceability: scope features to tables

| Feature                                   | Tables it owns or extends                                                                                                                                                                                       |
| ----------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 12 Self host install                      | `platform.instance` (setup state, shared with 13)                                                                                                                                                               |
| 13 Console accounts & projects            | `platform.operator`, `operator_session`, `operator_recovery_code`, `organization`, `membership`, `project`, `project_settings`, `api_key`, `instance`; `crypto.data_key`, `crypto.key_check` if TOTP ships here |
| 14 Email and password auth                | `auth.user`, `identity`, `session`, `refresh_token`, `session_revocation`, `one_time_token` (reset), `signing_key`, `project_settings`; `crypto.*` if not built in 13                                           |
| 15 Project databases                      | `databases.database`                                                                                                                                                                                            |
| 16 Tables and rows API                    | none in the platform database (rows live in project databases; owner ids reference `auth.user.id`)                                                                                                              |
| 17 Email delivery & templates             | `auth.project_settings` (sender), `auth.email_template`                                                                                                                                                         |
| 18 Magic link, email code, verify & reset | `auth.one_time_token` purposes, `auth.identity.verified_at`                                                                                                                                                     |
| 19 Anonymous sessions                     | `auth.identity` kind `anonymous`                                                                                                                                                                                |
| 20 Phone SMS sign in                      | `auth.identity` kind `phone`, `auth.one_time_token` purpose `sms_code`                                                                                                                                          |
| 21 Next.js server sessions                | none (uses `auth.session`)                                                                                                                                                                                      |
| 22 End user management                    | `auth.user.labels`, `auth.user.status`, `auth.session`                                                                                                                                                          |
| 23 Rate limits & abuse guards             | its own choice of store (Follow-up), limits as columns in `<module>.project_settings`                                                                                                                           |
| 24 Schema management                      | `databases.schema_migration`                                                                                                                                                                                    |
| 25 Queries & relations                    | none                                                                                                                                                                                                            |
| 26 Row permissions                        | permission columns per feature 9 (in project databases and `storage.bucket`)                                                                                                                                    |
| 27 SQL editor                             | `databases.saved_query`                                                                                                                                                                                         |
| 55 Row import & export                    | none (large files run as `jobs.job`; files in the file store)                                                                                                                                                   |
| 28 CLI core                               | `platform.api_key` (CI keys); CLI sign in reuses `platform.operator_session` or a token table feature 28 adds in `platform`                                                                                     |
| 56 Type generation                        | none (reads schema from the project database)                                                                                                                                                                   |
| 29 Buckets & files                        | `storage.bucket`, `storage.file`                                                                                                                                                                                |
| 30 Resumable uploads                      | `storage.upload`                                                                                                                                                                                                |
| 31 Image transformations                  | none (cache in the file store)                                                                                                                                                                                  |
| 32 Developer docs site                    | none                                                                                                                                                                                                            |
| 33 Platform events                        | `<module>.outbox` (spec 0007 EVT-1)                                                                                                                                                                             |
| 34 Database change subscriptions          | none in the platform database (feature 34 decides the project database side)                                                                                                                                    |
| 35 Broadcast & presence                   | none (in memory)                                                                                                                                                                                                |
| 36 Webhooks                               | `realtime.webhook`, `realtime.webhook_delivery`                                                                                                                                                                 |
| 37 Function runtimes & deployments        | `functions.function`, `functions.deployment`                                                                                                                                                                    |
| 38 Function triggers                      | `functions.execution`; schedules as columns on `functions.function`                                                                                                                                             |
| 39 Function variables & secrets           | `functions.variable`                                                                                                                                                                                            |
| 40 Static sites                           | `sites.site`, `sites.deployment`                                                                                                                                                                                |
| 41 Server rendered sites                  | `sites.site`, `sites.deployment`                                                                                                                                                                                |
| 42 Custom domains & certificates          | `sites.domain`                                                                                                                                                                                                  |
| 43 Email & SMS messaging                  | `messaging.provider`, `target`, `topic`, `subscription`, `message`, `delivery`                                                                                                                                  |
| 44 Push notifications                     | `messaging.target` kind `push`, `messaging.provider`                                                                                                                                                            |
| 45 Usage metrics & metering               | `operations.usage_hourly`, `operations.usage_daily`                                                                                                                                                             |
| 46 Request & function logs                | `operations.request_log`; function logs via `functions.execution`                                                                                                                                               |
| 47 Platform error monitoring              | none (OpenTelemetry, spec 0002 LOG-4)                                                                                                                                                                           |
| 48 Audit logs                             | `operations.audit_event`                                                                                                                                                                                        |
| 49 End user data export & delete          | `operations.privacy_request`                                                                                                                                                                                    |
| 50 Backups & restore                      | none (a schedule table in `operations` if feature 50 wants one, anchored to project)                                                                                                                            |
| 51 Console team members & roles           | `platform.membership.role` values, `platform.invitation`                                                                                                                                                        |
| 52 SDK publishing & versioning            | none                                                                                                                                                                                                            |
| 53 Instance upgrades                      | none (migrations)                                                                                                                                                                                               |
| 59 Server releases                        | none                                                                                                                                                                                                            |
| 60 Example apps & starters                | none                                                                                                                                                                                                            |
