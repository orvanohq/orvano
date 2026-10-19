# 0011. Platform data model: module owned schemas, app made uuid v7 ids, four anchor tables

**Date**: 2026-10-18
**Status**: Accepted
**Updated**: 2026-10-18, for spec 0012 (access control model): CAT-2 gains `databases.table` and the grant columns on buckets, files, and functions; `api_key.scopes` takes spec 0012's scope lists with a CHECK per key type; `audit_event.actor_kind` gains `function`; the feature 9 follow up is closed.

## Summary

This spec maps every record Orvano keeps about itself (operators, organizations, projects, keys, end users, sessions, secrets, usage, audit, and the records later slices add) to the module that owns it, the table it lives in, and how it links to everything else. It sits in the platform database only. The databases your users create stay separate (spec 0007 PG-2). Every id is a uuid v7 (a time ordered unique id) made in the app. Each module keeps its own schema and may point into another module's schema only at four anchor tables: organization, project, operator, and end user. Feature 8 itself builds only the shared conventions and the tests that enforce them. Each later feature creates its own tables from the catalog below, so the slices stay vertical (Tracer Bullet).

## Requirements

**User stories**:

- As the engineer building feature 13 or 14, I want each table I need already designed (columns, keys, delete rules, data class), so that I write the migration and do not redesign it.
- As the engineer of any later slice, I want my tables' owner, anchors, and key fields fixed now, so that usage, audit, messaging, functions, and sites fit without a breaking change.
- As a reviewer, I want naming, id, delete, and data class rules checked by tests, so that a slip fails the build instead of reaching review.
- As an operator, I want deleting a project to remove everything it owns, so that nothing personal lingers except the audit trail, which survives until its retention ends so I can still see what happened.

**Acceptance criteria**:

- **AC-1**: An entity deriving from `Entity` has a uuid v7 `Id` before `SaveChanges` runs (read in the test before any save), and EF never asks the database for it (`ValueGeneratedNever`, set by the convention plugin). A test checks the version nibble is 7, and that ids made with `Guid.CreateVersion7(DateTimeOffset)` at timestamps 1 ms or more apart sort in that order in PostgreSQL. Ids made in the same millisecond are unique but not ordered, and the test does not assume otherwise.
- **AC-2**: For an entity implementing `ITimestamped`, `TimestampInterceptor` sets `CreatedAt` and `UpdatedAt` on insert and only `UpdatedAt` on update, both from the injected `TimeProvider` (UTC). It writes through the change tracker entry, so it works with automatic change detection off, and an owned value that is added, changed, or removed (at any depth, or in an owned collection) stamps `UpdatedAt` on its owner. A test with a fake clock checks the exact values, and that a caller's value for `CreatedAt` is overwritten.
- **AC-3**: `UseOrvanoConventions(schema)` on a module's `DbContextOptionsBuilder` registers an `IConventionSetPlugin` that applies snake case names, the module's default schema, the migrations history table `<schema>.__ef_migrations_history` (spec 0002 DAT-1, DAT-2), `ValueGeneratedNever` on `Entity` keys, the xmin mapping for `IVersioned`, and the `HasTextEnum` CHECK constraints. Inside its `UseNpgsql` call it turns on the default retry (DAT-8) first, then runs the module's `npgsql` callback, then sets the history table last, so the callback can tune the retry but cannot move the history table. It registers `TimestampInterceptor` as a scoped EF service that reads the clock from the context's own options, which makes it run ahead of any interceptor added with `AddInterceptors`, and lets contexts with different clocks share one EF service provider. It replaces EF's `IModelCacheKeyFactory` so the cached model keys on (context type, schema, design time); a module cannot bring its own cache key factory. A test builds a sample context and checks the table, column, index, FK, and CHECK names in its model. `OrvanoConventions.SchemaOf(options)` returns the schema the call set, or null when it was never called. The architecture test `Setup` rule uses it to fail any server context that skips `UseOrvanoConventions`, has a default schema other than that schema, keeps its history table anywhere but `<schema>.__ef_migrations_history`, maps any table outside that schema (anchor stubs excepted, MOD-1, MOD-3), or does not retry on failure, and it fails a leftover `HasTextEnum` mark or an anchor stub not excluded from migrations (signs the plugin did not run).
- **AC-4**: `HasTextEnum<TEnum>()` stores a C# enum as lowercase snake case text: an `_` goes before an upper case letter that follows a lower case letter or digit, then everything is lowercased, so `VerifyOnly` → `verify_only`, `Es256` → `es256`, `Oauth` → `oauth`. The convention plugin adds a CHECK named `ck_<table>_<column>` listing exactly the enum's values, so adding a member is a drop and add of that one named CHECK. It works on `TEnum` and `TEnum?` (a nullable enum gets a nullable column with the same CHECK). The CHECK goes on every table the column lands in: an owned type in its owner's table gets `ck_<owner table>_<prefixed column>`, and a property on a TPC base with no table of its own gets one CHECK per concrete table. The model build throws when two members map to the same stored text (naming both), when the CHECK name is over 63 UTF-8 bytes (PostgreSQL would cut it short), when no table takes the marked column (for example `ToView`), and when the property sits inside a complex type, at any depth (not supported; map it on an entity or owned type). A migration test applies it to PostgreSQL 18, inserts a valid value, and gets a check violation for any other value through raw SQL.
- **AC-5**: `HasAnchor(Anchor.X, fk, onDelete)` links to `platform.organization`, `platform.project`, `platform.operator`, or `auth.user`. It uses one of four key only stub entities, which one helper registers once per context with `ExcludeFromMigrations()` and no project filter. A generated migration for a sample context in schema `sample` creates the FK with the asked delete rule. Neither its `Up()` nor its `Down()` creates or drops `platform.project`. `HasAnchor` throws at model build when `onDelete` breaks REF-3 for that anchor, or is `SetNull` on a non nullable property.
- **AC-6**: An architecture test walks the EF model of every registered `DbContext` (skipping the anchor stubs) and fails when an FK points into another schema at any table other than the four anchors, or uses a delete rule the REF-3 table does not allow for that anchor.
- **AC-7**: An architecture test fails when any property of an entity, an owned type, or a complex type (nested complex types too) of type `string`, `string[]`, `byte[]`, `ReadOnlyMemory<byte>`, `IPAddress`, `IPNetwork`, `NpgsqlInet`, `PhysicalAddress`, `EncryptedValue`, `JsonDocument`, or `JsonElement`, a collection or dictionary of one of those (`List<string>`, `Dictionary<string, string>`), or any non enum type a value converter stores as one of those (`Uri`, a wrapper stored as text) has no `[DataClass]` attribute, or when an `EncryptedValue` property is not marked `DataClass.Secret` with a non empty `Purpose`.
- **AC-8**: An architecture test fails when any key of an `Entity` type or an owned type is store generated, or when two `DbContext` types share a default schema. Anchor stubs and the partitioned entities (VOL-1) are exempt from the single key check. An owned collection (`OwnsMany`) is a table of its own, so EF's default shadow `int` key fails; its item derives from `Entity` and is keyed with `HasKey(x => x.Id)` (IDS-1), and the failure message names that fix. Every rule walks the server contexts the scan finds, and a separate test asserts that set equals `Orvano.Host.Database.MigrationOrder.DbContexts`, so a module context missed by the scan (or missing from `MigrationOrder`) fails the build instead of passing every rule unseen.
- **AC-9**: `EmailAddress.Normalize` trims surrounding white space, applies Unicode NFC, lowercases with the invariant culture, then applies NFC again. It validates nothing and caps no length. Tests cover mixed case, a trailing space, a composed versus decomposed accent, and a plus address (kept as is).
- **AC-10**: Every scope feature from 12 to 60 that stores platform records maps to an owning table in the catalog (CAT-1 and CAT-2) or one fixed by another spec (for example `jobs.job` and `<module>.outbox`, spec 0007). The traceability table in `rationale.md` lists every scope feature in that range against its tables (or "none"), with no feature left unmapped.
- **AC-11**: A sample unique index built with `AreNullsDistinct(false)` rejects a second row that differs only by a null in an indexed column, and a sample partial unique index built with `HasFilter` allows a duplicate outside its filter. Both are checked against PostgreSQL 18.

## Decision

**Chosen option**: Option 2: module owned schemas joined only at four anchors, with the shared conventions built now and each feature building its own tables.

Every platform table belongs to exactly one module's schema, takes an app made uuid v7 key, and points across schemas only at `platform.organization`, `platform.project`, `platform.operator`, or `auth.user`. Feature 8 ships the conventions code and the architecture tests. Features 13 onward create the tables in CAT-1 and CAT-2.

**Implementation skills**: `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `postgresql-table-design` (`wshobson/agents`, `.claude/skills/postgresql-table-design/`) · `testcontainers` (`.claude/skills/testcontainers/`) · `csharp-xunit` (`github/awesome-copilot`, `.claude/skills/csharp-xunit/`)

**Where a skill disagrees, this spec wins** (rule GEN-0 in spec 0002). Known conflicts: `postgresql-table-design` prefers `bigint` identity keys and allows `citext` (use **IDS-1** and **COL-5** instead), and shows `DEFAULT now()` on time columns (use the **COL-1** interceptor, so tests control the clock).

## Feature design

### Scope and boundaries

- **In**: every table in the platform database (spec 0007 PG-2a), its owner module, columns for the tier 1 tables, anchors and must have fields for the tier 2 tables, delete rules, data classes, retention defaults, and the shared EF conventions code.
- **Out**: tables inside project databases (features 15, 16, 24); the scope list and what each scope allows (spec 0012 SCP); route shapes, error format, pagination format (feature 10); the role list beyond `owner` (feature 51); throttle counters (feature 23 picks its store).
- **Already fixed elsewhere, used as is**: `jobs.job` (spec 0007 JOB-1), `<module>.outbox` (EVT-1), the `data_keys` shape (spec 0006 SCR-1), the API key fields (KEY-2), session and token rules (TOK-1 to TOK-10, CON-1 to CON-7).

### Rules

Each rule has an ID so reviews and later specs can cite it. **MUST** fails the review; **SHOULD** needs a written reason to skip.

#### MOD: modules and schemas

- **MOD-1 (MUST)**: One schema per module, named after it (spec 0002 DAT-1): `platform` (new `Orvano.Modules.Platform`: console accounts, organizations, projects, API keys), `auth` (end users), `jobs` and `crypto` (`Orvano.Infrastructure`, each with its own `DbContext`), `databases`, `storage`, `realtime` (webhooks too), `functions`, `sites`, `messaging`, `operations` (audit, usage, request logs, privacy requests).
- **MOD-2 (MUST)**: `MigrationOrder` (DAT-10) is `jobs`, `crypto`, `platform`, `auth`, `databases`, `storage`, `realtime`, `functions`, `sites`, `messaging`, `operations`. A module is added to the list by the feature that creates its first table.
- **MOD-3 (MUST)**: A module reads and writes only its own schema (LAY-4). An FK into another schema (REF) is a constraint, not a read path: code never joins across schemas.

#### IDS: ids

- **IDS-1 (MUST)**: Every entity key is a `uuid` made in the app with `Guid.CreateVersion7()` when the object is built, configured `ValueGeneratedNever`. No table has a database default for its id. Exceptions: singleton tables (`id smallint`, CHECK `id = 1`) and 1:1 tables, whose key is the parent's id. An owned collection (`OwnsMany`) item is no exception: it derives from `Entity` and is keyed with `HasKey(x => x.Id)`, never EF's default shadow `int` key (AC-8).
- **IDS-2 (MUST)**: The public API, the SDKs, the console URLs, and JWT `aud` use the same uuid as text in the standard 36 character form. No prefixes, no second id, no slugs.
- **IDS-3 (MUST)**: Organization and project names are display labels: free text, 1 to 128 characters after trimming, not unique, renamable at any time.
- **IDS-4 (SHOULD)**: Lists page by keyset on `id` (uuid v7 sorts by creation time, to the millisecond; ties still have a stable order on the full uuid), with an index whose leading columns are the list's filter (for example `(project_id, id)`). Partitioned tables page on `(occurred_at, id)` (VOL-1). The page format is feature 10's.

#### COL: columns and types

- **COL-1 (MUST)**: Editable entities implement `ITimestamped` (`created_at`, `updated_at`, both `timestamptz`, set by the interceptor of AC-2) and `IVersioned` (`uint Version`, mapped by the convention plugin to PostgreSQL's `xmin` system column as an `xid` concurrency token, so no real column exists; spec 0002 DAT-9). Append only rows (audit events, usage counters, request logs, refresh tokens, deliveries) carry only their own time column. Who changed a row lives in the audit log, not on the row. `ExecuteUpdate`, `ExecuteDelete`, and raw upserts skip the interceptor, so those statements set `updated_at` themselves (DAT-7).
- **COL-2 (MUST)**: Enum like values are `text` with a CHECK constraint, mapped with `HasTextEnum` (AC-4). Native PostgreSQL enum types are not used. `HasTextEnum` is for a property of an entity or owned type that has a table; the model build throws on an entity with no table and inside a complex type, so a column is never left without its CHECK.
- **COL-3 (MUST)**: Free text columns are `text` with a CHECK on `char_length` (names 1 to 128, user agents up to 512, URLs up to 2048). Hashes are `bytea` with CHECK `octet_length = 32`. Lists of short values are `text[]`. IP addresses are `inet`. Facts that happen once are a nullable `*_at timestamptz`, never a boolean.
- **COL-4 (MUST)**: `jsonb` holds only event and job payloads and audit metadata. Settings, permissions, and anything a query filters on get typed columns.
- **COL-5 (MUST)**: A sign in identifier is stored as typed (for display) beside a `*_normalized` column, and only the normalized column is indexed or unique. Normalizing depends on the kind. **Email** uses `EmailAddress.Normalize` (AC-9); length (254) is checked by the feature's validation. **Phone** is E.164 (the international `+` format) in both columns; feature 20 picks the parser. **OAuth** keeps the provider's subject exactly as given (case sensitive, never lowercased). **Anonymous** uses the user id in lowercase text. No `citext`.
- **COL-6 (MUST)**: Every FK column has an index whose leading column is that FK (EF Core adds it by convention; do not remove it).
- **COL-7 (MUST)**: Index features use the Npgsql 10 API: `HasIndex(...).IsUnique().AreNullsDistinct(false)` for `NULLS NOT DISTINCT` (AC-11), `HasFilter("...")` for partial indexes, `HasMethod("gin")` for arrays, and `ToTable(t => t.HasCheckConstraint("ck_<table>_<rule>", "..."))` for every other CHECK, always with an explicit name.

#### REF: links between tables

- **REF-1 (MUST)**: A table may point at a table in another schema only through the four anchors: `platform.organization`, `platform.project`, `platform.operator`, `auth.user`. Every other link across schemas is a plain `uuid` column with no constraint. Inside one schema, FKs are free.
- **REF-2 (MUST)**: The anchor link is made with `HasAnchor(...)` (AC-5): each `DbContext` maps a key only entity for that anchor table with `ExcludeFromMigrations()`, so EF emits the constraint and never the table. `MigrationOrder` (MOD-2) runs the anchor's schema first.
- **REF-3 (MUST)**: Delete rules allowed per anchor for links from another schema. `HasAnchor` (AC-5) and the AC-6 test enforce this table:

  | Anchor                  | Allowed                | Use                                                                                                                                  |
  | ----------------------- | ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
  | `platform.project`      | `Cascade`              | the row belongs to the project                                                                                                       |
  | `platform.organization` | `Cascade`              | the row belongs to the organization                                                                                                  |
  | `platform.operator`     | `SetNull`              | records who made something (`created_by_operator_id`, `invited_by_operator_id`)                                                      |
  | `auth.user`             | `Cascade` or `SetNull` | `Cascade` when the row belongs to the user (targets); `SetNull` when it only records who made it (`storage.file.created_by_user_id`) |

  Inside the `platform` schema, `platform.project.organization_id` uses `RESTRICT` (LIF-3) and `platform.api_key.created_by_operator_id` uses `SetNull`. AC-6 does not see same schema links, so a model unit test in feature 13 pins these two.

- **REF-4 (MUST)**: History tables keep anchors as plain ids and outlive them until retention removes them: `operations.audit_event`, `operations.usage_hourly`, `operations.usage_daily`, `operations.request_log`, `jobs.job`, every `<module>.outbox`, and `crypto.data_key` (its rows are removed by teardown, LIF-2).

#### LIF: lifecycle and deletion

- **LIF-1 (MUST)**: No soft delete. A row that is deleted is removed, and its cascade follows. What happened is kept in `operations.audit_event` (feature 48).
- **LIF-2 (MUST)**: A project moves `active` → `deleting` (sets `deletion_requested_at`, commits, sends the `cache` signal for that project's tags). A job of kind `project.teardown` (`dedupe_key = 'project.teardown:<id>'`) then calls each module's `TeardownAsync` in reverse `MigrationOrder`. Each step is idempotent, so a retried job resumes. The steps:
  1. Mark the project's queued `jobs.job` rows `dead`, except this one.
  2. Drop project databases and roles (feature 15).
  3. Delete stored files (feature 29), and remove function and site artifacts.
  4. Batch delete (`ExecuteDelete` in chunks of 5000) the high volume owned tables: `auth.refresh_token`, `auth.one_time_token`, `auth.session`, `auth.identity`, `auth.user`, and later features' equivalents.
  5. Run the file step a second time, for blobs that in flight requests wrote after the flip.
  6. Delete the project's `crypto.data_key` rows, which leaves every remaining project secret unreadable.
  7. Delete the `platform.project` row. Its cascade is now small.
- **LIF-3 (MUST)**: An organization can be deleted only when it has no projects, including projects in `creating` or `deleting`. The `RESTRICT` FK enforces it in the database; the API says which projects remain.
- **LIF-4 (MUST)**: While a project is `creating` or `deleting`, the Platform contract `IProjectDirectory` returns no project for it, so keys, tokens, CORS, and every SDK call fail exactly as for an unknown project. Token checks consult `IProjectDirectory` after the `kid` lookup (TOK-3), so a cached `kid` cannot outlive the flip. Only the create flow, the teardown job, and the console's project list (which shows the state) see such a project.
- **LIF-5 (MUST)**: Each organization always has at least one membership with role `owner`. A change that removes or demotes an owner first locks the organization row (`SELECT ... FOR UPDATE`), counts owners, and refuses with `Conflict` if it would leave none. Deleting an operator locks every organization they belong to, in `id` order (so two deletes cannot deadlock), checks each the same way, and refuses if they are the last owner of any. An integration test runs two concurrent demotions of the last two owners and expects exactly one to succeed.
- **LIF-6 (MUST)**: Deleting an end user deletes the `auth.user` row. Its identities, sessions, refresh tokens, one time tokens, and messaging targets cascade. File authorship is set to null. Rows in project databases are feature 49's job. In one transaction, the delete:
  1. locks the user row (`SELECT ... FOR UPDATE`), so a sign in racing with the delete waits or fails;
  2. writes every live session of the user to `auth.session_revocation` (TOK-6);
  3. deletes the user.

#### TXN: creating a project

- **TXN-1 (MUST)**: Project creation is a short series of steps, not one shared transaction:
  1. Platform commits `platform.project` with status `creating` and its `platform.project_settings`.
  2. Each `IProjectLifecycleParticipant.CreateAsync(projectId, ct)` registered in DI commits its own rows in its own `DbContext` and transaction, inside its own execution strategy (DAT-8). Infrastructure writes the project's `crypto.data_key`; Auth writes `auth.project_settings` and the first `auth.signing_key`.
  3. Platform flips the project to `active`.

  Each `CreateAsync` is idempotent: it skips rows that already exist for the project, so a retry never makes a second data key or signing key. If a step fails, the request returns an error and Platform enqueues `project.teardown` for the `creating` project, which the steps of LIF-2 clean up. The project database itself is provisioned afterwards by a job (feature 15).

- **TXN-2 (MUST)**: The same participant interface carries `TeardownAsync(projectId, CancellationToken)` for LIF-2. The interface lives in `Orvano.SharedKernel`; feature 13 adds it with the first caller.

#### PST: settings

- **PST-1 (MUST)**: Per project settings live in a 1:1 table per module, `<module>.project_settings`, keyed by `project_id` (FK to the project, cascade), with typed columns and defaults. A module never writes another module's settings. Install wide settings stay in configuration (spec 0002 CFG), except the setup state in `platform.instance`.

#### ENC: stored secrets

- **ENC-1 (MUST)**: A secret is stored on the row that uses it, as a `bytea` column of type `EncryptedValue` (spec 0006 SCR-1 envelope: format version, data key id, nonce, tag, ciphertext). The EF converter for `EncryptedValue` only passes bytes through (with a value comparer), because a converter cannot see the row; it refuses an empty envelope on save, so a secret left at `default` fails instead of storing an empty `bytea`. `ToString` and JSON both write only the marker `EncryptedValue(<n> bytes)`, never the bytes, and reading one from JSON throws, so an envelope never travels as JSON (spec 0006 LOG-2, LOG-3). Encrypting and decrypting happen in handlers through `ISecretProtector.Protect(plaintext, projectId, rowId, purpose)` and `Unprotect(...)`. The authenticated extra data is the project id (empty for platform scope), the row id, and the purpose, so a value copied to another row or purpose does not decrypt.
- **ENC-2 (MUST)**: The purpose is a fixed string constant set once in code (`[DataClass(DataClass.Secret, Purpose = "auth.signing_key.private")]`), never derived from table or column names, so a rename does not break stored secrets.
- **ENC-3 (MUST)**: `crypto.data_key` and `crypto.key_check` follow SCR-1 and SCR-2 exactly, in schema `crypto` owned by `Orvano.Infrastructure`, which also implements `ISecretProtector` (declared in `Orvano.SharedKernel`). The feature that stores the first secret (feature 13 for operator TOTP, else feature 14 for signing keys) creates them.
- **ENC-4 (MUST)**: Short codes (`email_code`, `sms_code`, operator recovery codes) are too small for a plain hash: a leaked row could be reversed by trying every code. They are stored as HMAC-SHA256 under a key derived from the root key (HKDF, info `orvano.code-hmac.v1`). Link tokens, session tokens, and API keys (32 random bytes) stay plain SHA-256 (TOK-1, KEY-2).

#### CLS: data classes

- **CLS-1 (MUST)**: Every column carrying text, bytes, an address, or JSON is marked with `[DataClass(DataClass.X)]`, where `DataClass` is a plain enum (`Secret`, `Personal`, `Public`, one value each, not flags) per spec 0006 LOG-2 (AC-7). Feature 49 finds a user's personal data through these marks, and the LOG-3 redaction test uses them as a property list. The catalog's **Class** column below is the source.
- **CLS-2 (MUST)**: `jsonb` metadata (audit events, outbox, jobs) holds ids and codes only, never an email, name, or other personal value, so erasing a user never means rewriting history rows.

#### FLT: query filter exceptions

- **FLT-1 (MUST)**: These lookups have no project context yet, or work across projects by design. Each one uses `IgnoreQueryFilters(["project"])` with a `// Deviation DAT-2a` comment and is limited to the exact columns named: API key lookup by `api_key.hash`; signing key lookup by `signing_key.id` (the `kid`); the startup load of `auth.session_revocation`; the control plane reads of an organization's projects (membership checked instead, TEN-2); and the teardown and cleanup jobs. Any other bypass needs a spec change.

#### VOL: high volume tables

- **VOL-1 (MUST)**: `operations.audit_event` is range partitioned by month on `occurred_at`. `operations.request_log` is range partitioned by day on `occurred_at`. Their primary key is `(occurred_at, id)`, so their entities do not derive from `Entity` (exempt in AC-8). The migration replaces EF's `CreateTable` with raw SQL that creates the parent, a `DEFAULT` partition, and the current and next periods; the model snapshot still lists the tables. A scheduled job in the Operations module (Cronos, spec 0007) keeps 3 future periods and drops whole expired periods. If the job stalls, rows land in `DEFAULT` instead of failing the insert, and the job moves them out and logs a Warning. No `pg_partman`.
- **VOL-2 (MUST)**: `operations.audit_event` is append only. No code path updates or deletes a row, and a row trigger created on the parent (PostgreSQL clones it to every partition) raises on `UPDATE` and `DELETE`. Retention drops whole partitions, which does not fire row triggers.
- **VOL-3 (MUST)**: Usage is counted in memory per process and flushed every minute as an upsert into `operations.usage_hourly (project_id, metric, hour)`, adding to `value`. `hour` is a UTC timestamp truncated to the hour. A nightly job writes finished UTC days into `operations.usage_daily` as an overwriting upsert of `SUM(hourly value)`, so a retried job never double counts. Metric names are `<module>.<name>` (for example `databases.rows_read`), defined in code by the owning module.

#### RET: retention defaults

- **RET-1 (MUST)**: Defaults, each settable by the feature that owns the table:
  - ended end user and operator sessions (with their refresh tokens): 7 days after `absolute_expires_at` or `revoked_at`;
  - used refresh tokens of a live session: 7 days after `used_at`;
  - one time tokens: 1 day after expiry or use;
  - `auth.session_revocation` rows: at their `expires_at`;
  - signing keys: deleted at `retire_at`;
  - `operations.audit_event`: 365 days;
  - `operations.request_log`: 7 days;
  - `operations.usage_hourly`: 90 days;
  - `operations.usage_daily`: 730 days;
  - webhook deliveries and function executions: 30 days.

### Data model sketch

**Class**: S secret, P personal, blank public. **Std** means `created_at` and `updated_at` columns plus the `Version` property on `xmin` (no real column, COL-1). All ids are `uuid` unless noted. "FK cascade" across schemas is an anchor link (REF).

#### CAT-1: tier 1 tables (full columns; built by the feature in brackets)

**`platform.instance`** (13): singleton.

| Column               | Type          | Null | Class | Notes                               |
| -------------------- | ------------- | ---- | ----- | ----------------------------------- |
| `id`                 | `smallint`    | no   |       | PK, CHECK `id = 1`                  |
| `setup_token_hash`   | `bytea`       | yes  | S     | CON-6; cleared when setup completes |
| `setup_completed_at` | `timestamptz` | yes  |       |                                     |
| Std                  |               |      |       |                                     |

**`platform.operator`** (13)

| Column                | Type          | Null | Class | Notes                                       |
| --------------------- | ------------- | ---- | ----- | ------------------------------------------- |
| `id`                  | `uuid`        | no   |       | PK                                          |
| `email`               | `text`        | no   | P     | as typed, up to 254                         |
| `email_normalized`    | `text`        | no   | P     | unique                                      |
| `name`                | `text`        | no   | P     | 1 to 128                                    |
| `password_hash`       | `text`        | no   | S     | PHC string (PWD-1)                          |
| `password_changed_at` | `timestamptz` | no   |       |                                             |
| `status`              | `text`        | no   |       | `active`, `disabled`                        |
| `totp_secret`         | `bytea`       | yes  | S     | `EncryptedValue`, platform data key (CON-4) |
| `totp_last_step`      | `bigint`      | yes  |       | replay guard                                |
| `mfa_enabled_at`      | `timestamptz` | yes  |       |                                             |
| `last_sign_in_at`     | `timestamptz` | yes  |       |                                             |
| Std                   |               |      |       |                                             |

**`platform.operator_session`** (13)

| Column           | Type          | Null | Class | Notes                                                                                                           |
| ---------------- | ------------- | ---- | ----- | --------------------------------------------------------------------------------------------------------------- |
| `id`             | `uuid`        | no   |       | PK                                                                                                              |
| `operator_id`    | `uuid`        | no   |       | FK operator, cascade                                                                                            |
| `token_hash`     | `bytea`       | no   | S     | unique (TOK-1)                                                                                                  |
| `csrf_token`     | `bytea`       | no   | S     | 32 bytes, stored plain because CON-2 returns it on every session GET (the one plain secret, see Key invariants) |
| `created_at`     | `timestamptz` | no   |       |                                                                                                                 |
| `last_seen_at`   | `timestamptz` | no   |       | idle timeout from here (CON-3), written at most once a minute                                                   |
| `expires_at`     | `timestamptz` | no   |       | absolute limit                                                                                                  |
| `last_reauth_at` | `timestamptz` | no   |       | CON-7                                                                                                           |
| `revoked_at`     | `timestamptz` | yes  |       |                                                                                                                 |
| `ip`             | `inet`        | yes  | P     |                                                                                                                 |
| `user_agent`     | `text`        | yes  | P     | up to 512                                                                                                       |

**`platform.operator_recovery_code`** (13): `id` PK · `operator_id` FK cascade · `code_hash bytea` S (HMAC, ENC-4) · `used_at` null · `created_at`. Unique `(operator_id, code_hash)`. Regenerating deletes the old set.

**`platform.organization`** (13): `id` PK · `name text` 1 to 128 · Std.

**`platform.membership`** (13)

| Column            | Type   | Null | Class | Notes                                              |
| ----------------- | ------ | ---- | ----- | -------------------------------------------------- |
| `id`              | `uuid` | no   |       | PK                                                 |
| `organization_id` | `uuid` | no   |       | FK organization, cascade                           |
| `operator_id`     | `uuid` | no   |       | FK operator, cascade                               |
| `role`            | `text` | no   |       | `owner` now; feature 51 adds the rest to the CHECK |
| Std               |        |      |       | unique `(organization_id, operator_id)`            |

**`platform.project`** (13): the main anchor.

| Column                  | Type          | Null | Class | Notes                                                               |
| ----------------------- | ------------- | ---- | ----- | ------------------------------------------------------------------- |
| `id`                    | `uuid`        | no   |       | PK                                                                  |
| `organization_id`       | `uuid`        | no   |       | FK organization, **restrict** (LIF-3)                               |
| `name`                  | `text`        | no   |       | 1 to 128                                                            |
| `status`                | `text`        | no   |       | `creating`, `active`, `deleting`                                    |
| `deletion_requested_at` | `timestamptz` | yes  |       | CHECK `(status = 'deleting') = (deletion_requested_at IS NOT NULL)` |
| Std                     |               |      |       | index `(organization_id, id)`                                       |

**`platform.project_settings`** (13): `project_id` PK and FK cascade · `allowed_origins text[]` not null, default empty (exact origins, spec 0007 COR-1) · Std.

**`platform.api_key`** (13)

| Column                   | Type          | Null | Class | Notes                                                                                                                                                               |
| ------------------------ | ------------- | ---- | ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                     | `uuid`        | no   |       | PK                                                                                                                                                                  |
| `project_id`             | `uuid`        | no   |       | FK project, cascade                                                                                                                                                 |
| `type`                   | `text`        | no   |       | `publishable`, `secret` (KEY-1)                                                                                                                                     |
| `name`                   | `text`        | no   |       | 1 to 128                                                                                                                                                            |
| `hash`                   | `bytea`       | no   | S     | SHA-256, unique (KEY-2)                                                                                                                                             |
| `prefix`                 | `text`        | no   |       | 15 characters, CHECK                                                                                                                                                |
| `scopes`                 | `text[]`      | no   |       | spec 0012 SCP-1 (secret) or SCP-2 (publishable); CHECK per `type`, `scopes <@ ARRAY[...]` of that type's ceiling (SCP-3); `users.impersonate` never allowed (SCP-4) |
| `created_by_operator_id` | `uuid`        | yes  |       | FK operator, set null                                                                                                                                               |
| `last_used_at`           | `timestamptz` | yes  |       | written at most once a minute (KEY-3)                                                                                                                               |
| `expires_at`             | `timestamptz` | yes  |       |                                                                                                                                                                     |
| `revoked_at`             | `timestamptz` | yes  |       |                                                                                                                                                                     |
| Std                      |               |      |       | index `(project_id, id)`                                                                                                                                            |

**`auth.user`** (14)

| Column                | Type          | Null | Class | Notes                                                                                                                                                                             |
| --------------------- | ------------- | ---- | ----- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                  | `uuid`        | no   |       | PK; the owner id rows in project databases store                                                                                                                                  |
| `project_id`          | `uuid`        | no   |       | FK project, cascade; `IProjectScoped`                                                                                                                                             |
| `name`                | `text`        | yes  | P     | up to 128                                                                                                                                                                         |
| `status`              | `text`        | no   |       | `active`, `blocked`                                                                                                                                                               |
| `labels`              | `text[]`      | no   |       | default empty; GIN index; CHECK `cardinality(labels) <= 20 AND array_to_string(labels, ',') ~ '^([A-Za-z0-9_-]{1,36}(,[A-Za-z0-9_-]{1,36})*)?$'` (a CHECK cannot hold a subquery) |
| `password_hash`       | `text`        | yes  | S     | PHC string; null for passwordless users                                                                                                                                           |
| `password_changed_at` | `timestamptz` | yes  |       |                                                                                                                                                                                   |
| `last_sign_in_at`     | `timestamptz` | yes  |       |                                                                                                                                                                                   |
| Std                   |               |      |       | index `(project_id, id)`                                                                                                                                                          |

**`auth.identity`** (14; phone in 20, anonymous in 19, OAuth later)

| Column               | Type          | Null | Class | Notes                                                                                                                               |
| -------------------- | ------------- | ---- | ----- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `id`                 | `uuid`        | no   |       | PK                                                                                                                                  |
| `project_id`         | `uuid`        | no   |       | FK project, cascade                                                                                                                 |
| `user_id`            | `uuid`        | no   |       | FK user, cascade                                                                                                                    |
| `kind`               | `text`        | no   |       | `email`, `phone`, `anonymous`, `oauth`                                                                                              |
| `provider`           | `text`        | yes  |       | required when `kind = oauth`, else null (CHECK)                                                                                     |
| `subject`            | `text`        | no   | P     | as typed (email, E.164 phone, provider subject; the user id for anonymous)                                                          |
| `subject_normalized` | `text`        | no   | P     | per kind (COL-5)                                                                                                                    |
| `verified_at`        | `timestamptz` | yes  |       |                                                                                                                                     |
| `last_used_at`       | `timestamptz` | yes  |       |                                                                                                                                     |
| Std                  |               |      |       | unique `(project_id, kind, provider, subject_normalized) NULLS NOT DISTINCT`; unique `(user_id, kind, provider) NULLS NOT DISTINCT` |

**`auth.session`** (14): one refresh token family (TOK-5).

| Column                | Type          | Null | Class | Notes                                                                                                        |
| --------------------- | ------------- | ---- | ----- | ------------------------------------------------------------------------------------------------------------ |
| `id`                  | `uuid`        | no   |       | PK; the `sid` claim                                                                                          |
| `project_id`          | `uuid`        | no   |       | FK project, cascade                                                                                          |
| `user_id`             | `uuid`        | no   |       | FK user, cascade                                                                                             |
| `created_at`          | `timestamptz` | no   |       |                                                                                                              |
| `last_refreshed_at`   | `timestamptz` | no   |       |                                                                                                              |
| `idle_expires_at`     | `timestamptz` | no   |       | 30 day sliding default                                                                                       |
| `absolute_expires_at` | `timestamptz` | no   |       | 90 day cap default                                                                                           |
| `revoked_at`          | `timestamptz` | yes  |       |                                                                                                              |
| `revoke_reason`       | `text`        | yes  |       | `sign_out`, `password_reset`, `password_change`, `user_blocked`, `user_deleted`, `refresh_reuse`, `operator` |
| `ip`                  | `inet`        | yes  | P     |                                                                                                              |
| `user_agent`          | `text`        | yes  | P     | up to 512                                                                                                    |

**`auth.refresh_token`** (14): `id` PK · `project_id` FK cascade · `session_id` FK session, cascade · `token_hash bytea` S, unique · `issued_at` · `used_at` null. A refresh claims its token atomically: `UPDATE ... SET used_at = @now WHERE id = @id AND used_at IS NULL`. If no row changed, the token was already used. Within 10 seconds of that `used_at`, the server issues a fresh sibling refresh token (a new row on the same session) and a new access token. Only hashes are stored, so it cannot return the first pair again; this is the sibling token behaviour TOK-5 describes. After 10 seconds, it revokes the session (`refresh_reuse`).

**`auth.session_revocation`** (14): `session_id uuid` PK (no FK, it outlives the session) · `project_id` (plain id, used to filter) · `expires_at`. Every revocation (sign out, reset, password change, block, delete, reuse, operator action) inserts its row in the same transaction that sets `session.revoked_at`. `expires_at` is a constant `revoked_at + 61 minutes` (the TOK-2 maximum lifetime of 60 minutes plus the 30 second skew, rounded up), so it does not depend on the project row or a later change to its lifetime. TOK-6 loads the unexpired rows at startup and again after every reconnect of the signal listener (NOTIFY can drop messages); new revocations travel as the `session` signal of spec 0007 SIG-1.

**`auth.one_time_token`** (14 for reset; 18, 20)

| Column              | Type          | Null | Class | Notes                                                                                     |
| ------------------- | ------------- | ---- | ----- | ----------------------------------------------------------------------------------------- |
| `id`                | `uuid`        | no   |       | PK                                                                                        |
| `project_id`        | `uuid`        | no   |       | FK project, cascade                                                                       |
| `user_id`           | `uuid`        | yes  |       | FK user, cascade; null when a code is sent to an address with no user yet                 |
| `purpose`           | `text`        | no   |       | `password_reset`, `email_verification`, `magic_link`, `email_code`, `sms_code`            |
| `token_hash`        | `bytea`       | no   | S     | SHA-256 for links, HMAC for codes (ENC-4); partial unique where purpose is a link purpose |
| `target`            | `text`        | no   | P     | address it was sent to                                                                    |
| `target_normalized` | `text`        | no   | P     | codes look up by `(project_id, purpose, target_normalized)`                               |
| `attempts`          | `int`         | no   |       | default 0; codes only                                                                     |
| `expires_at`        | `timestamptz` | no   |       | TOK-9 lifetimes                                                                           |
| `used_at`           | `timestamptz` | yes  |       |                                                                                           |
| `created_at`        | `timestamptz` | no   |       |                                                                                           |

Use is atomic: `UPDATE ... SET used_at = @now WHERE id = @id AND used_at IS NULL AND expires_at > @now`. A wrong code increments `attempts` in one statement. Issuing a new code deletes the unused rows for the same `(project_id, purpose, target_normalized)`. A password change deletes the user's unused `password_reset` rows (TOK-9). Each of these happens in the same transaction as the action.

**`auth.signing_key`** (14)

| Column        | Type          | Null | Class | Notes                                                                                                                  |
| ------------- | ------------- | ---- | ----- | ---------------------------------------------------------------------------------------------------------------------- |
| `id`          | `uuid`        | no   |       | PK; the `kid`, unique install wide (TOK-3)                                                                             |
| `project_id`  | `uuid`        | no   |       | FK project, cascade                                                                                                    |
| `algorithm`   | `text`        | no   |       | `es256`                                                                                                                |
| `public_key`  | `bytea`       | no   |       | SubjectPublicKeyInfo DER                                                                                               |
| `private_key` | `bytea`       | no   | S     | `EncryptedValue`, project data key                                                                                     |
| `status`      | `text`        | no   |       | `pending`, `active`, `verify_only`                                                                                     |
| `activate_at` | `timestamptz` | no   |       | when a `pending` key may start signing                                                                                 |
| `retire_at`   | `timestamptz` | yes  |       | set on demotion: demotion time + project's access token lifetime + 1 hour; the key leaves the JWKS and is deleted then |
| Std           |               |      |       | partial unique `(project_id) WHERE status = 'active'`                                                                  |

Rotation runs as one transaction in two statements, so the non deferrable unique index never sees two active keys. First an `ExecuteUpdate` demotes the old key to `verify_only` and sets `retire_at`; then the new key is promoted to `active`. A scheduled rotation inserts the new key `pending` with `activate_at` at least one JWKS cache lifetime ahead (TOK-4). The rotation job promotes it once `activate_at` has passed.

**`auth.project_settings`** (14): `project_id` PK and FK cascade · `access_token_lifetime` `int` seconds, default 900, CHECK 300 to 3600 (TOK-2) · `refresh_idle_lifetime` `int` seconds, default 30 days · `refresh_absolute_lifetime` `int` seconds, default 90 days · `signing_key_rotation` `int` days, default 180 · `enabled_methods text[]`, default `{email_password}` · Std. Later auth features add columns here.

**`crypto.data_key`** (13 or 14, ENC-2): `id` PK (the key id) · `scope text` (`platform`, `project`) · `project_id` null, plain id (REF-4), CHECK null exactly when scope is `platform` · `version int` · `wrapped_key bytea` S · `root_key_id text` · `created_at` · `retired_at` null. Unique `(scope, project_id, version) NULLS NOT DISTINCT`.

**`crypto.key_check`** (same feature): `id smallint` PK, CHECK `id = 1` · `root_key_id text` · `check_value bytea` S · `created_at`.

**`databases.database`** (15)

| Column           | Type    | Null | Class | Notes                                                                |
| ---------------- | ------- | ---- | ----- | -------------------------------------------------------------------- |
| `id`             | `uuid`  | no   |       | PK                                                                   |
| `project_id`     | `uuid`  | no   |       | FK project, cascade                                                  |
| `engine`         | `text`  | no   |       | `postgres`                                                           |
| `name`           | `text`  | no   |       | display, 1 to 64                                                     |
| `status`         | `text`  | no   |       | `provisioning`, `ready`, `failed`, `deleting`                        |
| `server`         | `text`  | no   |       | name of the configured server, default `default`                     |
| `db_name`        | `text`  | no   |       | unique                                                               |
| `role_name`      | `text`  | no   |       | unique                                                               |
| `role_password`  | `bytea` | no   | S     | `EncryptedValue`                                                     |
| `schema_version` | `int`   | no   |       | default 0                                                            |
| `last_error`     | `text`  | yes  |       | no secrets (LOG-1)                                                   |
| Std              |         |      |       | one database per project in the MVP is a code rule, not a constraint |

#### CAT-2: tier 2 tables (owner, anchors, must have fields; the owning feature adds the rest)

| Table                        | Feature | Anchors (REF-3 rule)                                                              | Must have fields and constraints                                                                                                                                                                                                                                                                                                                                       |
| ---------------------------- | ------- | --------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `platform.invitation`        | 51      | organization cascade; `invited_by_operator_id` set null                           | `email`, `email_normalized` P, `role`, `token_hash` S, `expires_at`, `accepted_at`; partial unique `(organization_id, email_normalized)` where not accepted                                                                                                                                                                                                            |
| `auth.email_template`        | 17      | project cascade                                                                   | `kind` (`verification`, `password_reset`, `magic_link`, `email_code`), `locale`, `subject`, `body`; unique `(project_id, kind, locale)`                                                                                                                                                                                                                                |
| `databases.saved_query`      | 27      | project cascade; `database_id` cascade; `created_by_operator_id` set null         | `name`, `sql text`                                                                                                                                                                                                                                                                                                                                                     |
| `databases.schema_migration` | 24      | `database_id` cascade                                                             | `version`, `checksum` (hex text), `applied_at`; unique `(database_id, version)`                                                                                                                                                                                                                                                                                        |
| `databases.table`            | 16      | project cascade; `database_id` cascade                                            | `name text` (1 to 63, the engine name), `permissions text[]` not null default `{}` CHECK `cardinality(permissions) <= 100`, `row_security boolean` not null default false; unique `(database_id, name)`; a stable id so grants survive a rename; feature 24 adds schema columns (spec 0012 data model)                                                                 |
| `storage.bucket`             | 29      | project cascade                                                                   | `name`, `max_file_size`, `allowed_types text[]`, `permissions text[]` not null default `{}` CHECK `cardinality(permissions) <= 100`, `file_security boolean` not null default false (spec 0012 STO-1)                                                                                                                                                                  |
| `storage.file`               | 29      | project cascade; `bucket_id` cascade; `created_by_user_id` → `auth.user` set null | `path`, `size`, `content_type`, `checksum`, `name` P, `permissions text[]` not null default `{}` CHECK `cardinality(permissions) <= 100` with a GIN index (`(bucket_id, permissions)` through `btree_gin`, or plain GIN on `permissions`; feature 29 measures, spec 0012 ENF-4)                                                                                        |
| `storage.upload`             | 30      | project cascade; `bucket_id` cascade                                              | `offset`, `size`, `expires_at`                                                                                                                                                                                                                                                                                                                                         |
| `realtime.webhook`           | 36      | project cascade                                                                   | `url`, `events text[]`, `signing_secret` S (`EncryptedValue`), `disabled_at`                                                                                                                                                                                                                                                                                           |
| `realtime.webhook_delivery`  | 36      | `webhook_id` cascade                                                              | `event_id` (plain), `status`, `attempt`, `response_status`, `duration_ms`, `created_at`; append only                                                                                                                                                                                                                                                                   |
| `functions.function`         | 37      | project cascade                                                                   | `name`, `runtime`, `active_deployment_id`, `permissions text[]` not null default `{}` CHECK `cardinality(permissions) <= 100` (only `execute:` tokens, spec 0012 FNC-1), `run_scopes text[]` not null default `{}` with a CHECK that it is inside the secret ceiling (spec 0012 FNC-2, SCP-3)                                                                          |
| `functions.deployment`       | 37      | `function_id` cascade                                                             | `status`, `artifact_ref`, `build_log_ref`, `created_at`                                                                                                                                                                                                                                                                                                                |
| `functions.variable`         | 39      | project cascade; `function_id` cascade, null for project wide                     | `key`, `value` S (`EncryptedValue`) or `plain_value` when not secret; unique `(project_id, function_id, key) NULLS NOT DISTINCT`                                                                                                                                                                                                                                       |
| `functions.execution`        | 38      | `function_id` cascade                                                             | `trigger`, `status`, `duration_ms`, `user_id` (plain), `created_at`; append only                                                                                                                                                                                                                                                                                       |
| `sites.site`                 | 40      | project cascade                                                                   | `name`, `framework`, `active_deployment_id`                                                                                                                                                                                                                                                                                                                            |
| `sites.deployment`           | 40      | `site_id` cascade                                                                 | `status`, `artifact_ref`, `preview_host`                                                                                                                                                                                                                                                                                                                               |
| `sites.domain`               | 42      | project cascade                                                                   | `name`, `name_normalized` unique install wide, `target_kind` (`site`, `function`, `api`), `target_id`, `verification_token`, `certificate_status`, certificate private key S (`EncryptedValue`)                                                                                                                                                                        |
| `messaging.provider`         | 43      | project cascade                                                                   | `kind`, `name`, `credentials` S (`EncryptedValue`), `enabled_at`                                                                                                                                                                                                                                                                                                       |
| `messaging.target`           | 43, 44  | project cascade; `user_id` → `auth.user` cascade                                  | `kind` (`email`, `sms`, `push`), `address` P (address or device token), `provider_id`; made from auth identity events, push from devices                                                                                                                                                                                                                               |
| `messaging.topic`            | 43      | project cascade                                                                   | `name`                                                                                                                                                                                                                                                                                                                                                                 |
| `messaging.subscription`     | 43      | `topic_id` cascade; `target_id` cascade                                           | unique `(topic_id, target_id)`                                                                                                                                                                                                                                                                                                                                         |
| `messaging.message`          | 43      | project cascade                                                                   | `kind`, `status`, `scheduled_at`, `content` P                                                                                                                                                                                                                                                                                                                          |
| `messaging.delivery`         | 43      | `message_id` cascade; `target_id` set null                                        | `status`, `provider_message_id`, `error`; append only                                                                                                                                                                                                                                                                                                                  |
| `operations.audit_event`     | 48      | none (REF-4)                                                                      | `id`, `occurred_at`, `organization_id`, `project_id`, `actor_kind` (`operator`, `api_key`, `end_user`, `function`, `system`; `function` is a function run acting through its run key, spec 0012 AUD-1), `actor_id`, `action`, `target_kind`, `target_id`, `result` (`success`, `failure`), `ip inet` P, `user_agent` P, `metadata jsonb` (never secrets); VOL-1, VOL-2 |
| `operations.usage_hourly`    | 45      | none (REF-4)                                                                      | PK `(project_id, metric, hour)`, `value bigint`; VOL-3                                                                                                                                                                                                                                                                                                                 |
| `operations.usage_daily`     | 45      | none (REF-4)                                                                      | PK `(project_id, metric, day date)`, `value bigint`                                                                                                                                                                                                                                                                                                                    |
| `operations.request_log`     | 46      | none (REF-4)                                                                      | `id`, `occurred_at`, `project_id`, `method`, `route` (template), `status`, `duration_ms`, `bytes_in`, `bytes_out`, `actor_kind`, `actor_id`, `ip inet` P, `user_agent` P, `error_code`; VOL-1                                                                                                                                                                          |
| `operations.privacy_request` | 49      | project cascade                                                                   | `user_id` (plain, it outlives the user), `kind` (`export`, `erase`), `status`, `file_ref`, `requested_by_kind`, `requested_by_id`                                                                                                                                                                                                                                      |

### State transitions

- **project**: `creating` → `active` (TXN-1); `creating` → `deleting` when a create step fails; `active` → `deleting` → row removed (LIF-2). No way back from `deleting`.
- **database**: `provisioning` → `ready` or `failed`; `failed` → `provisioning` (retry); `ready` or `failed` → `deleting` → row removed.
- **signing key**: `pending` (in the JWKS, not signing until `activate_at`) → `active` → `verify_only` (old signer, in the JWKS until `retire_at`) → row deleted at `retire_at` (RET-1). A new project's first key starts `active`, since nobody can have cached its JWKS yet.
- **operator**: `active` ↔ `disabled` (disabling revokes every session).
- **end user**: `active` ↔ `blocked` (blocking revokes every session, TOK-6).
- **api key**: active → revoked (`revoked_at` set, final). Expiry is read from `expires_at`, not stored as a state.

### API surface

Feature 8 adds no HTTP endpoints. Its surface is the shared C# API in `Orvano.SharedKernel/Data/`, which every module's `DbContext` uses:

| Member                                                                                                                                                      | Shape                                                                                                                                                                                                                                                                                                                                                                                                                                                             | Used for            | Errors                                                                                                                                                                               |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Entity`                                                                                                                                                    | abstract class, `Guid Id { get; init; } = Guid.CreateVersion7();`                                                                                                                                                                                                                                                                                                                                                                                                 | IDS-1               | none                                                                                                                                                                                 |
| `ITimestamped`                                                                                                                                              | `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`                                                                                                                                                                                                                                                                                                                                                                                                            | COL-1               | none                                                                                                                                                                                 |
| `IVersioned`                                                                                                                                                | `uint Version`, mapped to `xmin` by the plugin                                                                                                                                                                                                                                                                                                                                                                                                                    | DAT-9               | `DbUpdateConcurrencyException` mapped to `Conflict` by the caller                                                                                                                    |
| `TimestampInterceptor`                                                                                                                                      | `SaveChangesInterceptor`, takes `TimeProvider`; registered scoped by `UseOrvanoConventions` with the clock from the context's options, so it runs before any other `SaveChanges` interceptor; stamps the owner of a changed owned value                                                                                                                                                                                                                           | AC-2                | none                                                                                                                                                                                 |
| `UseOrvanoConventions(this DbContextOptionsBuilder, string schema, IServiceProvider services, Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)`        | options extension that calls `UseNpgsql` on the `NpgsqlDataSource` from `services` (retry, then the `npgsql` callback, then the history table), applies snake case, registers `OrvanoConventionSetPlugin` (an `IConventionSetPlugin`, the way `EFCore.NamingConventions` plugs in) and the scoped `TimestampInterceptor` with the `TimeProvider` from `services`, and replaces `IModelCacheKeyFactory` (model keyed on the schema; a module cannot bring its own) | AC-3                | `ArgumentException` on an empty schema; a reused schema is caught by AC-8, not here                                                                                                  |
| `OrvanoConventions.SchemaOf(IDbContextOptions options)`                                                                                                     | static, returns `string?`: the schema `UseOrvanoConventions` set, or null when it was not called                                                                                                                                                                                                                                                                                                                                                                  | AC-3 (`Setup` rule) | `ArgumentNullException` on null                                                                                                                                                      |
| `HasTextEnum<TEnum>(this PropertyBuilder<TEnum>)`                                                                                                           | property extension, also on `PropertyBuilder<TEnum?>`: sets the text converter and an annotation; the plugin turns the annotation into the named CHECK at model finalize                                                                                                                                                                                                                                                                                          | AC-4                | `InvalidOperationException` at model build on two members with the same stored text, a CHECK name over 63 bytes, a marked column no table takes, or a property inside a complex type |
| `HasAnchor<TEntity>(this EntityTypeBuilder<TEntity>, Anchor anchor, Expression<Func<TEntity, Guid>> fk, DeleteBehavior onDelete)` and the same with `Guid?` | entity extension; the stubs are registered once per context by the plugin                                                                                                                                                                                                                                                                                                                                                                                         | AC-5                | `InvalidOperationException` when `onDelete` breaks REF-3, or `SetNull` on the `Guid` overload                                                                                        |
| `Anchor`                                                                                                                                                    | enum: `Organization`, `Project`, `Operator`, `EndUser` (stub tables `platform.organization`, `platform.project`, `platform.operator`, `auth.user`)                                                                                                                                                                                                                                                                                                                | REF-1               | none                                                                                                                                                                                 |
| `DataClass`, `DataClassAttribute`                                                                                                                           | plain enum `Secret`, `Personal`, `Public`; `[DataClass(DataClass.Secret, Purpose = "...")]`                                                                                                                                                                                                                                                                                                                                                                       | CLS-1, ENC-2        | none                                                                                                                                                                                 |
| `EmailAddress.Normalize(string)`                                                                                                                            | static, returns `string`                                                                                                                                                                                                                                                                                                                                                                                                                                          | COL-5, AC-9         | `ArgumentNullException` on null                                                                                                                                                      |
| `EncryptedValue`                                                                                                                                            | `readonly record struct` over `ReadOnlyMemory<byte>`, with a pass through value converter and value comparer the plugin applies; `ToString` and JSON write only `EncryptedValue(<n> bytes)`                                                                                                                                                                                                                                                                       | ENC-1, AC-7         | `JsonException` when read from JSON; `InvalidOperationException` on save when empty                                                                                                  |
| `ISecretProtector`                                                                                                                                          | interface only: `Protect(ReadOnlySpan<byte> plaintext, Guid? projectId, Guid rowId, string purpose)`, `Unprotect(...)`; implemented by Infrastructure with ENC-3                                                                                                                                                                                                                                                                                                  | ENC-1               | none                                                                                                                                                                                 |

### Value sourcing

| Action             | Value produced                        | Source                                                                                                                                                                                                                            |
| ------------------ | ------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Build an entity    | `Id`                                  | `Guid.CreateVersion7()` in `Entity`'s initializer (IDS-1)                                                                                                                                                                         |
| Save an entity     | `created_at`, `updated_at`            | `TimeProvider.GetUtcNow()` in `TimestampInterceptor`, the `TimeProvider` from DI (singleton `TimeProvider.System` in the host, a fake one in tests)                                                                               |
| Save an entity     | `version`                             | PostgreSQL `xmin`, read by EF (DAT-9)                                                                                                                                                                                             |
| Map an enum        | stored text                           | the C# member name by the AC-4 rule (`VerifyOnly` → `verify_only`, `Es256` → `es256`)                                                                                                                                             |
| Map an enum        | CHECK list and name                   | `Enum.GetValues<TEnum>()` at model finalize; name `ck_<table>_<column>`                                                                                                                                                           |
| Map `IVersioned`   | `Version`                             | the `xmin` system column, `xid` type, concurrency token                                                                                                                                                                           |
| Store a secret     | envelope bytes                        | `ISecretProtector.Protect`, with the purpose from the property's `[DataClass]`                                                                                                                                                    |
| Name a constraint  | table, column, FK, index names        | `EFCore.NamingConventions` snake case (DAT-2)                                                                                                                                                                                     |
| Anchor FK          | target schema and table               | the `Anchor` value: `platform.organization`, `platform.project`, `platform.operator`, `auth.user`                                                                                                                                 |
| Anchor FK          | delete rule                           | the `onDelete` argument, checked against REF-3                                                                                                                                                                                    |
| Normalize an email | `*_normalized`                        | `EmailAddress.Normalize(email)` (AC-9)                                                                                                                                                                                            |
| Architecture tests | the set of contexts and their schemas | every type deriving `DbContext` in assemblies named `Orvano.*`, built through its `IDesignTimeDbContextFactory`; the set must equal `MigrationOrder.DbContexts` (AC-8), and each schema is read with `OrvanoConventions.SchemaOf` |
| Architecture tests | each property's class                 | `DataClassAttribute` on the property                                                                                                                                                                                              |

### Key invariants

- Every key is a uuid v7 from the app, or the parent's key for a 1:1 table (IDS-1).
- No FK crosses schemas except to the four anchors, with the REF-3 delete rule (AC-6).
- An organization always has an owner (LIF-5) and cannot be deleted with projects in it (LIF-3).
- A project in `creating` or `deleting` is invisible to the data plane (LIF-4).
- A project's status and `deletion_requested_at` always agree (CHECK).
- At most one `active` signing key per project (partial unique index).
- Identity subjects are unique per project and kind (and provider) on the normalized value.
- Audit events are never updated or deleted row by row (VOL-2).
- No secret is stored in plain form, with one stated exception: the operator session's `csrf_token`, which CON-2 must hand back on every session GET and which is useless without the `HttpOnly` session cookie. Every other secret is an `EncryptedValue` bound to its row and purpose (ENC-1), a SHA-256 hash for 32 byte tokens and keys, or an HMAC for short codes (ENC-4).
- Each refresh token and one time token is consumed once, by an atomic conditional update.

### Security model

- Isolation: every project owned row implements `IProjectScoped` and sits behind the DAT-2a named filter. Tables reached only through a parent (refresh tokens through sessions) still carry `project_id` so the filter and the TEN-5 leak tests apply to them directly.
- Who reads what: only the owning module reads its schema (MOD-3). Cross module needs go through Contracts (`IProjectDirectory`, `IProjectLifecycleParticipant`, and those later features add).
- Secrets: hashes for anything presented back to us (passwords, tokens, keys, codes), `EncryptedValue` for anything we must use again (TOTP seeds, signing keys, database passwords, provider credentials, webhook secrets).
- Personal data: marked P in the catalog and in code (CLS-1), deleted by cascade on user delete (LIF-6), masked in ordinary logs (LOG-2), kept in full only in audit (LOG-4) and only until retention (RET-1).
- Compliance: personal data of end users across many projects (GDPR style erase and export are feature 49). The audit log is required, not optional (feature 48).

### Configuration required

Feature 8 adds none. Retention defaults in RET-1 become settings when their owning feature builds them, following spec 0002 CFG (for example `ORVANO_OPERATIONS__AUDITRETENTIONDAYS`, `ORVANO_OPERATIONS__REQUESTLOGRETENTIONDAYS`).

### Critical test scenarios

- Happy path: a sample `DbContext` in schema `sample` with one entity that is timestamped, versioned, project anchored, has an enum, and has a marked email, migrates on PostgreSQL 18; insert and update set the times from the fake clock, the id is v7, and the names are snake case. Verifies **AC-1** to **AC-5**.
- Failure case: a raw SQL insert of an unknown enum value fails the CHECK; deleting the anchor project cascades the sample row; an FK from the sample context to a non anchor table in another schema fails the architecture test. Verifies **AC-4**, **AC-5**, **AC-6**.
- Guard: an entity with an unmarked `string` property (also inside an owned or nested complex type), an `EncryptedValue` without a purpose, a store generated key, an owned collection on EF's default key, or a second context reusing a schema fails the architecture tests. A context set up with plain `UseNpgsql`, one whose history table was moved after the call, or one with a table outside its schema fails the `Setup` rule. `HasAnchor` with `SetNull` on a `Guid` property throws, and `HasTextEnum` with no table or inside a complex type throws at model build. Verifies **AC-3**, **AC-4**, **AC-5**, **AC-7**, **AC-8**.
- Indexes: a duplicate that differs only by a null is rejected by a `NULLS NOT DISTINCT` index, and a partial unique index ignores rows outside its filter. Verifies **AC-11**.
- Traceability: every scope feature that stores platform records has an owning table. Verifies **AC-10**.

## Build plan

Tracer Bullet: feature 8 is a thin shared strand, and the first real thread through it is feature 13. Nothing here creates a production table.

1. Add `EFCore.NamingConventions` to `Directory.Packages.props` (check its license against spec 0009 DEP-1 and that the version is at least 3 days old, TL-8), and let `Orvano.SharedKernel` reference EF Core relational and Npgsql as LAY-3 allows. Satisfies **AC-3**.
2. Add `Orvano.SharedKernel/Data/`: `Entity`, `ITimestamped`, `IVersioned`, `TimestampInterceptor`, `OrvanoConventionSetPlugin` and `UseOrvanoConventions`, `HasTextEnum`, `Anchor` and `HasAnchor` (both overloads) with the four stubs, `DataClass` and `DataClassAttribute`, `EmailAddress`, the `EncryptedValue` type with its converter and comparer, and the `ISecretProtector` interface. Satisfies **AC-1**, **AC-2**, **AC-3**, **AC-4**, **AC-5**, **AC-9**.
3. In `Orvano.SharedKernel.Tests`, add unit tests for the id, the interceptor (fake `TimeProvider`), the enum naming, and `EmailAddress`, plus PostgreSQL 18 tests (`PostgresFixture`) with a sample context in schema `sample` and a stub `platform.project` table created by the test, covering migration `Up` and `Down`, names, the named CHECK, the cascade, and the two index forms. Satisfies **AC-1** to **AC-5**, **AC-9**, **AC-11**.
4. In `Orvano.ArchitectureTests`, add the model walking tests: the `Setup` rule (AC-3), cross schema FK only to anchors with the REF-3 rules, `[DataClass]` on every listed property type, no store generated keys, one schema per context, and the scanned contexts equal to `MigrationOrder.DbContexts`. They pass with no module contexts yet and are proven by deliberately bad sample contexts kept in the test project. Satisfies **AC-3**, **AC-6**, **AC-7**, **AC-8**.
5. Point `server/AGENTS.md` at this spec in its "Read before you write code" guidance for anyone adding a table (the `/sync` step does the edit), and record the AC-10 traceability table in `rationale.md`. Satisfies **AC-10**.

## Consequences

**Positive**:

- Features 13, 14, and 15 start from complete tables, and every later slice knows its owner and anchors, so the scope's "no breaking redesign" holds.
- Deleting a project is one teardown job plus one row delete; nothing it owned is left behind, and its secrets become unreadable once its data key is gone.
- The rules are tests, not review comments: naming, ids, FKs, and data classes fail the build when broken.
- End users in the platform database keep the data plane engine neutral: a future MongoDB project still has users, sessions, and keys.

**Negative / tradeoffs**:

- Project databases cannot join to users in SQL. App rows hold a user id with no FK, and erasing a user's rows there is feature 49's work.
- Cross module FKs to anchors tie migration order to MOD-2. An anchor table can never be dropped or renamed without touching every module.
- Creating a project spans three modules as separate commits (TXN-1). A failed create leaves a `creating` project for teardown to clean up, and every participant's `CreateAsync` must be idempotent.
- The refresh reuse window issues a sibling token rather than the exact first pair (only hashes are stored). The two clients then hold different but valid tokens in the same session family.
- Partitioned tables need raw SQL in migrations and a maintenance job; EF Core does not model partitions.
- Hard delete means no undo. A deleted project or user is gone, and only the audit log remains.
- The single owner role (PG-2a) means the append only audit guard is a trigger, which the owner role could still disable.

**Neutral**:

- The new Platform module (`Orvano.Modules.Platform`, log block 9000) and the Operations module (8000) are created by features 13 and 45 to 48, not here.
- `crypto` needs its own `DbContext` in Infrastructure beside `jobs`.

## Follow-up

- [x] Feature 9: the scope list for `api_key.scopes`, the permission columns for buckets and tables, and whether `IProjectScoped` filtering is refined (DAT-2a). Closed by spec 0012 (2026-10-18): scopes from SCP-1 and SCP-2 with a CHECK per type (SCP-3), grant columns on `databases.table`, `storage.bucket`, `storage.file`, and `functions.function` in CAT-2, and DAT-2a stays as written (spec 0012 PRJ-3 only fixes who sets `IProjectContext`).
- [ ] Feature 13: create `Orvano.Modules.Platform`, the `IProjectDirectory` and `IProjectLifecycleParticipant` contracts (TXN-1, TXN-2, LIF-4), and the CAT-1 platform tables; create `crypto` (ENC-2) if operator TOTP ships there.
- [ ] Feature 14: the CAT-1 auth tables, the cleanup schedule (RET-1), and `auth.session_revocation` loading for TOK-6.
- [ ] Feature 15: `databases.database`, naming of `db_name` and `role_name`, and the `server` setting's values.
- [ ] Feature 23: where throttle counters live (memory per replica, or a table); not part of this catalog.
- [ ] Feature 28: whether CLI sign in reuses `platform.operator_session` or adds a token table in `platform` (then a plain FK to `platform.operator`, same schema, added to CAT-2).
- [ ] Feature 50: whether backups need a schedule table in `operations` (then anchored to project with `Cascade`, added to CAT-2) or stay as configuration.
- [ ] Feature 48: the audit trigger and partition job (VOL-1, VOL-2); a hash chain stays possible later.
- [ ] Feature 49: erasing a user's audit IP and user agent, and rows in project databases.
- [ ] Feature 53 and spec 0007 PG-2a: a separate DDL role would let grants, not only a trigger, guard the audit table.
- [x] Spec 0006 TOK-5: amend "returns the same new pair" to the sibling token behaviour of `auth.refresh_token`, and TOK-6 "loaded from the sessions table" to `auth.session_revocation`; add ENC-4 (HMAC for short codes) beside TOK-9. A `/sync` or spec update edit. Done 2026-10-18.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
