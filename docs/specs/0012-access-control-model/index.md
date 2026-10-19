# 0012. Access control model: declarative grants, scoped keys, checks pushed into the query

**Date**: 2026-10-18
**Status**: In Progress
**Updated**: 2026-10-18, to agree with spec 0007: label and resource grant changes travel as `cache` tags (SIG-1, CCH-2), both caches expire after 60 seconds (security related entries), feature 61 builds the signal bus, and specs 0011 and 0006 carry this spec's amendments (build plan step 9).

## Summary

This spec decides who may read or change anything in an Orvano project. Tables, buckets, and functions carry grants (short entries such as `read:users` or `update:user:<id>`), and a table or bucket can switch on row level grants so each row or file carries its own. Nothing is allowed unless a grant allows it. Secret keys and console operators skip grants and are limited by scopes and console permissions instead. Row checks become a filter inside the database query, so any engine can enforce them, and lists, counts, and pages stay correct. Feature 9 builds the shared core now (the grant grammar, the caller, the evaluator, the scope and console permission lists). Features 13, 14, 16, 26, 29, 34, and 38 plug into it.

## Requirements

**User stories**:

- As an app developer, I want to say "anyone can read this table, signed in users can create rows, and each user can change only their own rows" without writing SQL, so that my data is safe on any engine Orvano supports.
- As an app developer, I want a server key that does admin work but only in the areas I choose, so that a leaked key for one job cannot do everything.
- As an end user, I want my private rows and files to be invisible to other users, so that nobody can even learn that they exist.
- As an operator, I want my organization role to decide what I can see and change in the console, so that feature 51 can add narrower roles without rewriting endpoints.
- As the engineer of features 16, 26, 29, 34, and 38, I want one evaluator and one filter helper, so that every module answers "may this caller do this?" the same way.

**Acceptance criteria**:

- **AC-1**: A grant is the text token `<action>:<role>`. Actions are `read`, `create`, `update`, `delete`, `execute`. Roles are `any`, `guests`, `users`, `user:<uuid>` (lowercase, hyphenated uuid), and `label:<name>` (the `auth.user.labels` pattern `[A-Za-z0-9_-]{1,36}`). Input may use `write:<role>`, which expands to `create`, `update`, and `delete` tokens before saving. Any other action or role, including the reserved `team:`, `member:`, and `verified` forms, fails with `validation_failed` naming the token (GRM-1 to GRM-4).
- **AC-2**: The caller's role set is: no session (publishable key only) → `any`, `guests`. End user session, anonymous included → `any`, `users`, `user:<sub>`, and `label:<x>` for each current label. Secret keys, function run keys, and operators bypass grants and are checked against scopes or console permissions instead (CLR-1 to CLR-4).
- **AC-3**: The evaluator allows an action only when a resource grant matches the caller's role set for that action, or the resource has row security on and the row's or file's grants match. With no matching grant it denies. `create` is checked only on the table or bucket; `execute` only on functions; a row or file grant with `create` or `execute` fails with `validation_failed` (EVL-1 to EVL-5).
- **AC-4**: For row and file reads, counts, and bulk updates and deletes, the core returns the token set to match (`<action>:<role>` for each role), and a PostgreSQL test proves that `WHERE <filter> AND _permissions && @tokens`, over a GIN index, returns exactly the allowed rows with correct `limit`, `offset`, and `count`, that a bulk update touches only rows matching its `update` tokens and reports that count, and that a bulk write matching nothing reports 0 (ENF-1 to ENF-4).
- **AC-5**: Denials follow one rule. A resource the caller cannot see (no resource grant of any action for their roles, and row security off) answers `404 not_found` for every action. On a visible resource, a missing action answers `403 forbidden`, except row and file reads, which just leave out what the caller cannot read. A single row or file the caller cannot read answers `404 not_found`; one they can read but not change answers `403 forbidden`. A key missing a scope answers `403 insufficient_scope` (DEN-1 to DEN-4).
- **AC-6**: When an end user or guest sets grants on a row or file, each token they add or remove must name a role in their own role set (so adding `user:<someone else>` or a label they lack is refused with `validation_failed`, and tokens naming other roles survive their update unchanged). On update they may change grants only through the row's or file's own `update` grant; a table level `update` alone makes a `permissions` field `403 forbidden`. Reads return `permissions` only to callers who may change them. Saved grant arrays are deduplicated, sorted, and hold at most 100 tokens. Secret keys and operators may set any valid grant (WRT-1 to WRT-4, WRT-7).
- **AC-7**: A signed in end user who creates a row or file without sending grants gets `read`, `update`, and `delete` for `user:<their id>`. A guest gets none. A secret key or operator gets none (WRT-5).
- **AC-8**: The scope catalog and the ceiling per key type live in code (SCP-1 to SCP-4). Creating a key with a scope outside its type's ceiling fails with `validation_failed`. A stored scope outside the ceiling is refused on use with `403 insufficient_scope`. An endpoint declares its scope, and a call without it answers `403 insufficient_scope`. A publishable key whose client API scope is off answers `403 forbidden` for that whole API.
- **AC-9**: On the data plane the project comes only from the route. The key's project, the token's `aud`, and the project of the token's `kid` must all equal it, or the request answers `401 unauthorized`. A credential that is sent but fails never falls back to guest, and checks run credential, then scope, then visibility (DEN-5). Only the access layer sets `IProjectContext`, and an architecture test fails any other writer (PRJ-1 to PRJ-4).
- **AC-10**: Labels change only through a secret key with `users.write` or an operator with `users.write`. A label change applies on the next request on every server instance: the writer invalidates the user's label tag through `ICacheInvalidator`, which sends spec 0007's `cache` signal (built by feature 61). A lost signal leaves labels stale for at most 60 seconds (LBL-1 to LBL-3).
- **AC-11**: The console permission list is fixed in code (CPL-1). `owner` holds every permission. Each control plane endpoint declares the permission it needs. An operator without membership in the organization, or naming a project outside it, gets `404 not_found`; a member lacking the permission gets `403 forbidden` (CPL-2 to CPL-5).
- **AC-12**: The access core (`Orvano.SharedKernel/Access/`) references no Npgsql or EF Core type, so it stays engine neutral. The only PostgreSQL piece is the `WhereGranted` helper in `Orvano.SharedKernel/Data/`. An architecture test enforces this (ENG-1).

## Decision

**Chosen option**: Option 1, declarative grants on resources with optional row level grants, enforced by pushing a token filter into the query.

Every table, bucket, and function holds a list of grant tokens. Tables and buckets can switch on row security so rows and files hold their own. An action is allowed when either level matches the caller's roles, and denied otherwise. Secret keys and operators bypass grants and are bounded by scopes and console permissions.

**Implementation skills**: `caching` (`codewithmukesh/dotnet-claude-kit`, `.claude/skills/caching/`) for the label and grant caches · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) for `WhereGranted` and the array columns · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) for the endpoint filters · `csharp-xunit` (`github/awesome-copilot`, `.claude/skills/csharp-xunit/`) and `testcontainers` (`wshaddix/dotnet-skills`, `.claude/skills/testcontainers/`) for the tests. Spec 0002 wins where a skill disagrees (GEN-0).

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### Scope and boundaries

- **In**: the grant grammar, caller kinds and role sets, the evaluation rule, the denial rule, who may set grants, creator defaults, the API key scope catalog and ceilings, the console permission catalog and the `owner` mapping, how the data plane resolves the project, label caching, the contracts realtime, functions, and storage follow, which changes are audited, and the columns that hold grants.
- **Out**: the wire format, error body shape, and header names (feature 10); the route that edits grants and the console screens for them (features 16, 24, 26, 29, 37); roles beyond `owner` (feature 51); end user teams (deferred); verified only roles (reserved, see GRM-3); acting as a user from a server key (reserved scope `users.impersonate`, SCP-4); rate limits (feature 23).
- **Already fixed elsewhere, used as is**: key types and storage (spec 0006 KEY-1 to KEY-6), tokens and revocation (TOK-1 to TOK-9), project isolation (TEN-1 to TEN-5, spec 0002 DAT-2a), audit events (LOG-4), the platform catalog and its conventions (spec 0011).

### Rules

Each rule has an ID so reviews and later specs can cite it. **MUST** fails the review; **SHOULD** needs a written reason to skip.

#### GRM: grant grammar

- **GRM-1 (MUST)**: A grant is one text token `<action>:<role>`. Actions: `read`, `create`, `update`, `delete`, `execute`. Roles: `any` (every caller, with or without a session), `guests` (callers with no end user session), `users` (any end user session, anonymous included), `user:<uuid>` (one end user), `label:<name>` (end users carrying that label). The token is the stored form everywhere: platform columns, project rows, SDK payloads.
- **GRM-2 (MUST)**: Parsing is strict and case sensitive. `user:` takes a lowercase hyphenated uuid (36 characters). `label:` takes `[A-Za-z0-9_-]{1,36}`, the same pattern as `auth.user.labels` (spec 0011). Whitespace, empty parts, and unknown actions or roles are `validation_failed` naming the offending token.
- **GRM-3 (MUST)**: Reserved for later and refused today: roles starting `team:` or `member:` (end user teams), and any role ending `:verified` or equal to `users:verified` (verified only variants). Refusing them now means adding them later breaks no stored data.
- **GRM-4 (MUST)**: Input may use `write:<role>`. Normalization expands it to `create:<role>`, `update:<role>`, `delete:<role>` (only the actions valid for the target, EVL-4). `write` is never stored and never returned.

#### CLR: the caller

- **CLR-1 (MUST)**: Every request resolves to exactly one `Caller` of one kind: `guest` (publishable key, no session), `end_user` (publishable key plus a verified access token), `secret_key`, `run_key` (a function run, FNC-2), `operator` (console session), or `system` (jobs and startup, no HTTP).
- **CLR-2 (MUST)**: Role sets: `guest` → `{any, guests}`. `end_user` → `{any, users, user:<sub>}` plus `label:<x>` for each current label (LBL-2). An anonymous session (feature 19) is an `end_user`.
- **CLR-3 (MUST)**: `secret_key`, `run_key`, `operator`, and `system` bypass grants entirely. A `secret_key` and a `run_key` are bounded by scopes (SCP). An `operator` is bounded by console permissions (CPL). `system` is bounded by code review only and is never reachable from HTTP.
- **CLR-4 (MUST)**: A blocked or deleted user is refused through session revocation (TOK-6), not a status lookup on each request. The access core adds no second check.

#### EVL: evaluation

- **EVL-1 (MUST)**: Deny by default. A resource with no grants is reachable only by callers that bypass grants (CLR-3).
- **EVL-2 (MUST)**: For an action `a` on a row or file in a table or bucket `T`: allowed when `T.permissions` contains `a:<r>` for some role `r` in the caller's set, **or** `T.row_security` (or `file_security`) is on and the row's `_permissions` (or the file's `permissions`) contains `a:<r>`. Union, never intersection.
- **EVL-3 (MUST)**: `create` is decided at the table or bucket only. It is not a valid row or file grant.
- **EVL-4 (MUST)**: Valid actions per target: table and bucket `read`, `create`, `update`, `delete`; row and file `read`, `update`, `delete`; function `execute`. Any other action on a target is `validation_failed`.
- **EVL-5 (MUST)**: Resource level grants and the row or file security switch for tables, buckets, and functions are read through HybridCache with a 60 second expiry (a security related entry, spec 0007 CCH-2 and spec 0002 PRF-2). The key and its tag are `orvano:<module>:<entity>:<id>` (for example `orvano:databases:table:<table id>`), plus the project tag `orvano:project:<project id>`. Every change to those columns invalidates the entry's tag through `ICacheInvalidator` inside the same platform database transaction (spec 0007 SIG-3), so a grant change applies on the next request everywhere. Only the grant arrays are cached, never the evaluator's answer, and row and file grants are never cached (they are read in the query, ENF-1).

#### ENF: where the check runs

- **ENF-1 (MUST)**: Row and file checks run inside the query, never by filtering results in memory. The core supplies `Caller.MatchTokens(action)`, the token array to match. The engine adapter turns it into its own filter (PostgreSQL: `_permissions && @tokens`, array overlap).
- **ENF-2 (MUST)**: Per request, the app decides first from the resource grants. If they allow, no row filter is added. If not and row security is on, the row filter is added. If not and row security is off, the request is denied without querying (DEN rules).
- **ENF-3 (MUST)**: Lists, counts, single gets, and bulk updates and deletes by filter all use ENF-2, each with the tokens of its own action (a bulk update matches `update:` tokens). A bulk write changes only rows the caller may change and reports how many it changed. It never fails because hidden rows matched, and a bulk write that matches nothing returns 200 with a count of 0.
- **ENF-4 (MUST)**: Every project table gets the reserved column `_permissions text[] NOT NULL DEFAULT '{}'`, created with the table (features 16, 24). User column names starting with `_` are refused. The GIN index on `_permissions` is built only when row security is switched on (`CREATE INDEX CONCURRENTLY`, as a job) and dropped when it is switched off, so tables without row security pay no index cost. `storage.file.permissions` gets the GIN index `(bucket_id, permissions)` through `btree_gin`, or a plain GIN on `permissions` if that extension is not on the allow list (feature 29 decides by measuring).
- **ENF-5 (MUST)**: Switching row security on applies no grants to existing rows: rows with an empty `_permissions` stay reachable only through table grants and bypass callers. The console warns before the switch and shows how many rows have no grants. A table without the `_permissions` column (made with raw SQL, feature 27) cannot switch row security on until the column is added. Row security reports "on" only after its index is built.

#### DEN: denials

- **DEN-1 (MUST)**: A table, bucket, or function is **visible** to a caller when they hold a resource grant of any action for one of their roles, or (tables and buckets) row security is on. Every action on a resource that is not visible answers `404 not_found`. With row security on, a table's or bucket's name is therefore not secret from callers of the project; only its rows and files are. The full outcome matrix:

  | Resource visible | Action                 | Resource grant matches | Row security | Outcome                                                      |
  | ---------------- | ---------------------- | ---------------------- | ------------ | ------------------------------------------------------------ |
  | no               | any                    | no                     | off          | `404 not_found`                                              |
  | yes              | list, count, get       | yes                    | any          | all rows (no row filter)                                     |
  | yes              | list, count            | no                     | on           | rows whose grants match, possibly none                       |
  | yes              | get one row            | no                     | on           | the row, or `404` if its grants do not match                 |
  | yes              | list, count, get       | no                     | off          | `403 forbidden`                                              |
  | yes              | create                 | no                     | any          | `403 forbidden`                                              |
  | yes              | update, delete one row | no                     | on           | done if row grants match, else `403` if readable, else `404` |
  | yes              | bulk update, delete    | no                     | on           | only matching rows change, count returned                    |
  | yes              | update, delete         | no                     | off          | `403 forbidden`                                              |

- **DEN-2 (MUST)**: On a visible resource, a denied resource level action (`create`, function `execute`, or a table read with row security off) answers `403 forbidden`. Row and file reads never fail for grants: they return only what the caller may read, possibly nothing.
- **DEN-3 (MUST)**: For one row or file by id: not readable → `404 not_found`. Readable but the update or delete is denied → `403 forbidden`. The handler runs the guarded write first, and only when it touches nothing runs one guarded read to tell the two apart. The row can change between the two queries, so a caller may rarely get `403` for a row deleted a moment later; that is accepted (both answers are about a row the caller could see). A caller whose table grant allows the write never takes this path.
- **DEN-4 (MUST)**: A secret key or run key missing the endpoint's scope answers `403 insufficient_scope`. A publishable key whose client API scope is off answers `403 forbidden`. Bad, expired, revoked, or wrong project credentials answer `401 unauthorized` (PRJ-2). Error codes are these names; feature 10 fixes the body shape.
- **DEN-5 (MUST)**: Checks run in this order, and the first failure answers: credential (`401`), then scope or client API (`403`), then resource visibility and grants (`404` or `403`).

#### WRT: who sets grants

- **WRT-1 (MUST)**: Row and file grants are set on create and changed on update, through the same request (a `permissions` field next to the data). On create, a caller allowed to create may send `permissions` with no further check than WRT-2. On update, a `guest` or `end_user` may change `permissions` only when the row's or file's **own** grants give them `update`. A table or bucket level `update` grant lets them change the data, not the grants (a `permissions` field is then `403 forbidden`). This stops anyone with `update:users` on a table from making other people's rows public.
- **WRT-2 (MUST)**: A `guest` or `end_user` may only **add** tokens whose role is in their own role set (labels read through the LBL-2 cache). An end user's set holds `any` and `users`, so `read:any` is allowed and `read:guests` is not. A guest may add `any` and `guests` tokens only. They may **remove** only tokens whose role is in their own set; tokens naming other roles (for example `read:user:<B>` or `update:label:admin` set by a server key) are kept unchanged. A request breaking either rule is `validation_failed` naming the token.
- **WRT-3 (MUST)**: `secret_key` (with the module's write scope) and `operator` (with the matching console permission) may write any valid grant, at every level.
- **WRT-4 (MUST)**: Before saving, every grant array is expanded (GRM-4), deduplicated, and sorted ordinally. After that it holds at most 100 tokens, or the request is `validation_failed`.
- **WRT-5 (MUST)**: When a create sends no `permissions` field: an `end_user` creator gets `read:user:<id>`, `update:user:<id>`, `delete:user:<id>`. A guest, secret key, run key, or operator gets an empty array. Sending `permissions: []` means empty, not default.
- **WRT-6 (MUST)**: Table, bucket, and function grants, the row and file security switches, and a function's `run_scopes` change only through `secret_key` (module write scope) or `operator` (`schema.write`), never from a client.
- **WRT-7 (MUST)**: Reads return a row's or file's `permissions` only to callers who may change them (WRT-1, WRT-3). Every other caller gets the field left out, so a shared row does not reveal other user ids or labels.

#### SCP: API key scopes

- **SCP-1 (MUST)**: Secret key scopes (the secret ceiling): `databases.read`, `databases.write` (rows, tables, their grants), `storage.read`, `storage.write` (files, buckets, their grants), `users.read`, `users.write` (end users, labels, blocking, sessions), `functions.read`, `functions.write` (functions, deployments, grants, variables), `functions.execute`, `messaging.read`, `messaging.write` (topics, targets, sending), `realtime.subscribe`, `sites.read`, `sites.write`.
- **SCP-2 (MUST)**: Publishable key scopes (the publishable ceiling) switch client APIs on or off: `client.auth` (sign in, sessions, refresh, reset), `client.auth.signup`, `client.databases`, `client.storage`, `client.functions`, `client.realtime`. A new publishable key gets all of them. `client.auth.signup` is valid only together with `client.auth`. No publishable scope ever bypasses a grant.
- **SCP-3 (MUST)**: Ceilings live in code (`Scopes.CeilingFor(type)`). Key creation outside the ceiling is `validation_failed`. Each request checks the stored scopes against the ceiling again (spec 0006 KEY-4), and `platform.api_key` gets a CHECK per type (`scopes <@ ARRAY[...]`) as a second guard. Adding a scope is a code change plus a CHECK swap in that feature's migration.
- **SCP-4 (MUST)**: `users.impersonate` is reserved: refused at key creation until a spec defines acting as a user.
- **SCP-5 (MUST)**: Endpoints declare what they need with an endpoint filter: `.RequireScope("databases.read")` on server routes and `.RequireClientApi("client.storage")` on client routes. Only `secret_key` and `run_key` callers pass `RequireScope`. Only `guest` and `end_user` callers pass `RequireClientApi`. An operator reaches data only through control plane routes (CPL).

#### PRJ: project resolution

- **PRJ-1 (MUST)**: On the data plane the project id comes from the route (TEN-2). The access layer resolves the credential, then requires: an API key's `project_id` equals it; a token's `aud` equals it and its `kid` belongs to it (TOK-3); a run key's `aud` equals it.
- **PRJ-2 (MUST)**: Any mismatch answers `401 unauthorized`, the same as a bad credential, so a caller cannot probe which projects exist.
- **PRJ-3 (MUST)**: Only the access layer sets `IProjectContext.Id`, after PRJ-1 passes (data plane) or after the membership check (control plane, CPL-3). An architecture test fails any other type that sets it. Spec 0002 DAT-2a stays as written: the `"project"` query filter reads this context.
- **PRJ-4 (MUST)**: A credential that is sent but fails (bad, expired, revoked, wrong project, a secret key with an `Origin` header per KEY-1) answers `401 unauthorized`. It never falls back to `guest`. A request with no token but a valid publishable key is a `guest`. Resolvers find keys and signing keys through spec 0011 FLT-1's lookups and do not read `IProjectContext` before it is set.

#### CPL: console permissions

- **CPL-1 (MUST)**: The console permission list: `org.read`, `org.write`, `org.delete`, `members.read`, `members.write`, `projects.create`, `project.read`, `project.write`, `project.delete`, `keys.read`, `keys.write`, `users.read`, `users.write`, `data.read`, `data.write`, `schema.write`, `functions.deploy`, `messaging.write`, `sites.write`, `audit.read`, `usage.read`. `data.*` covers both table rows and storage files; `schema.write` covers tables, buckets, functions' grants and `run_scopes`.
- **CPL-2 (MUST)**: `RolePermissions.For(role)` maps a `platform.membership.role` to its permissions in code. `owner` holds every permission. Feature 51 adds roles to this map and to the role CHECK; endpoints do not change.
- **CPL-3 (MUST)**: A control plane endpoint declares `.RequireConsolePermission(ConsolePermission.X)`. The filter loads the operator's membership for the organization in the route (or the project's organization). No membership answers `404 not_found`; membership without the permission answers `403 forbidden`. A project id in the route must belong to that organization, or the answer is `404 not_found`.
- **CPL-5 (MUST)**: `keys.write` lets its holder mint a secret key with any scope inside the ceiling, so it equals full data power over the project. The console says so where roles are assigned (feature 51).
- **CPL-4 (MUST)**: Operators reach project data (rows, files, users) through control plane routes with `data.read`, `data.write`, or `users.write`, and bypass grants there. Every operator data change is audited with the operator as actor (AUD-1).

#### LBL: labels

- **LBL-1 (MUST)**: Labels change only through a `secret_key` with `users.write` or an `operator` with `users.write`. No client route writes labels.
- **LBL-2 (MUST)**: The access layer reads an end user's labels through `ICallerRolesSource`, which the Auth module implements (feature 14) with HybridCache: key and tag `orvano:auth:labels:<user id>`, plus the project tag `orvano:project:<project id>`, 60 second expiry. Labels are roles, so they are a security related entry under spec 0007 CCH-2's 60 second cap.
- **LBL-3 (MUST)**: Every label write calls `ICacheInvalidator` for the tag `orvano:auth:labels:<user id>` inside the write's transaction. That removes the writer's own entry and sends spec 0007's `cache` signal on commit (SIG-1, SIG-3), and every other instance removes the tag. There is no separate label signal kind. Recovery is spec 0007 SIG-2's: while the listener is disconnected each instance bypasses its cache, and on reconnect it clears all of it, so a lost signal leaves labels stale for at most the 60 second expiry.

#### RTM, FNC, STO: contracts for later features

- **RTM-1 (MUST)**: Realtime checks each event before sending it, using the subscriber's current role set and the row's grants at that moment. A delete event goes to subscribers who could read the row just before it was deleted. A grant or label change affects the next event; a subscriber who lost read stops receiving, without reconnecting. Each change event carries the row's grants before and after the change. Gaining read through a grant change sends no event, and losing it sends nothing either.
- **RTM-2 (MUST)**: Subscribing to a table needs it to be visible (DEN-1). Secret keys subscribe with `realtime.subscribe` and get every event.
- **RTM-3 (MUST)**: When a subscriber's session is revoked (TOK-6), the server closes the socket. The label cache bound (LBL-2, at most 60 seconds after a lost signal) applies to open subscriptions too.
- **FNC-1 (MUST)**: A function's `permissions` holds only `execute:<role>` tokens. HTTP calls by a guest or end user need a match. Event and schedule triggers skip the check.
- **FNC-2 (MUST)**: Each run gets a **run key**: a JWT signed with the project signing key (TOK-3), header `typ: orvano-run+jwt` (so it never passes as an access token, TOK-2), claims `iss`, `aud` (project), `fn`, `exe` (execution id), `scp` (space separated, `functions.function.run_scopes`, each inside the secret ceiling), optional `act` (the calling user id, absent for event and schedule triggers), `exp` = the function timeout plus 60 seconds. It is not refreshable. Its caller kind is `run_key`. Its `scp` is checked against the secret ceiling on every use (KEY-4 parity). Feature 38 issues it.
- **FNC-3 (MUST)**: The function receives the calling user's id and labels (or none) as part of its input. That is identity for the code to read; the run key's scopes, not the user's grants, bound what it can do.
- **STO-1 (MUST)**: Buckets behave like tables and files like rows (EVL, ENF, DEN, WRT apply with `file_security` for `row_security`).
- **STO-2 (MUST)**: Anyone who may read a file may mint a signed link for it. The link is the scoped HMAC token of spec 0006 TOK-9, for that one file, read only, expiring in 15 minutes by default and at most 1 hour. It is never accepted as a credential for anything else. It is not rechecked against grants, so it stays valid after a grant change until it expires.

#### AUD: audit

- **AUD-1 (MUST)**: These go to the audit sink as LOG-4 events: table, bucket, and function grant changes; row and file security switches; function `run_scopes` changes; label changes; key create, revoke, and scope changes; membership role changes; every operator data change (CPL-4). Each event records actor, project, action, target, result, and (for grant changes) the before and after arrays. A function run's actor is `actor_kind` `function` with the function id (spec 0011's `operations.audit_event.actor_kind` gains `function`).
- **AUD-2 (MUST)**: Row and file grant changes by clients are not audited one by one. They ride on the data write and its usage metrics.

#### ENG: engine neutrality

- **ENG-1 (MUST)**: `Orvano.SharedKernel/Access/` references no Npgsql, EF Core, or SQL type. Engine adapters own their filter. `WhereGranted` (EF, for platform tables such as `storage.file`) lives in `Orvano.SharedKernel/Data/`. An architecture test enforces both.

### Data model sketch

Grants are typed `text[]` columns on each owning row (spec 0011 COL-4), not a shared table.

| Table                                         | Change                                                                                                                                                                                                                                                | Notes                                                                                                 |
| --------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `databases.table` (**new**, feature 16)       | `id` uuid PK · `project_id` FK project cascade, `IProjectScoped` · `database_id` FK `databases.database` cascade · `name text` (1 to 63, the engine name) · `permissions text[]` default `{}` · `row_security boolean` default false · Std timestamps | Unique `(database_id, name)`. A stable id so grants survive a rename. Feature 24 adds schema columns. |
| project tables (inside each project database) | `_permissions text[] NOT NULL DEFAULT '{}'`; GIN index only while row security is on                                                                                                                                                                  | Read only when `row_security` is on. `_` names reserved (ENF-4, ENF-5).                               |
| `storage.bucket` (29)                         | `permissions text[]` default `{}` · `file_security boolean` default false                                                                                                                                                                             |                                                                                                       |
| `storage.file` (29)                           | `permissions text[]` default `{}` + GIN (ENF-4)                                                                                                                                                                                                       |                                                                                                       |
| `functions.function` (37)                     | `permissions text[]` default `{}` (only `execute:` tokens) · `run_scopes text[]` default `{}`                                                                                                                                                         | CHECK `run_scopes` within the secret ceiling                                                          |
| `platform.api_key` (13)                       | `scopes` values from SCP-1 and SCP-2                                                                                                                                                                                                                  | CHECK per `type` (SCP-3)                                                                              |
| `platform.membership` (13)                    | unchanged, `role` = `owner`                                                                                                                                                                                                                           | permissions mapped in code (CPL-2)                                                                    |
| `auth.user` (14)                              | unchanged, `labels`                                                                                                                                                                                                                                   | written only per LBL-1                                                                                |

Every `permissions` and `_permissions` column has CHECK `cardinality(...) <= 100`. Grant syntax is checked in code (GRM-2), not in a CHECK, as `labels` already is in spec 0011.

### State transitions

None. Grants are plain values; `api_key` keeps its spec 0011 lifecycle.

### API surface

Public routes are owned by the features that use them (16, 24, 26, 29, 37, 38, 51). Feature 10 fixes the wire format. Each resource payload gains a `permissions` string array, and tables and buckets gain `rowSecurity` and `fileSecurity` booleans. The internal contract this feature builds, in `Orvano.SharedKernel/Access/` unless noted:

| Member                                                                                                              | Shape                                                                                                                                    | Used for         | Errors                                      |
| ------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- | ---------------- | ------------------------------------------- |
| `GrantAction`                                                                                                       | enum `Read`, `Create`, `Update`, `Delete`, `Execute`                                                                                     | AC-1             | none                                        |
| `Role`                                                                                                              | readonly record struct; `Any`, `Guests`, `Users`, `User(Guid)`, `Label(string)`; `TryParse`, `ToString`                                  | AC-1             | parse returns false; reserved forms refused |
| `Grant`                                                                                                             | readonly record struct `(GrantAction, Role)`; `TryParse("read:users")`, `ToString()`                                                     | AC-1             | as above                                    |
| `GrantTarget`                                                                                                       | enum `Table`, `Row`, `Bucket`, `File`, `Function`; `AllowedActions`                                                                      | AC-3             | none                                        |
| `GrantSet.Normalize(IEnumerable<string> input, Caller writer, GrantTarget target)`                                  | returns `Result<string[]>`: expands `write`, checks grammar, target actions, writer roles, dedup, sort, limit                            | AC-1, AC-6       | `validation_failed` listing each bad token  |
| `GrantSet.DefaultsFor(Caller creator, GrantTarget target)`                                                          | `string[]` per WRT-5                                                                                                                     | AC-7             | none                                        |
| `CallerKind`                                                                                                        | enum `Guest`, `EndUser`, `SecretKey`, `RunKey`, `Operator`, `System`                                                                     | AC-2             | none                                        |
| `Caller`                                                                                                            | sealed record: `Kind`, `ProjectId?`, `UserId?`, `Labels`, `Scopes`, `OperatorId?`, `BypassesGrants`; `Roles`; `MatchTokens(GrantAction)` | AC-2, AC-4       | none                                        |
| `ICallerContext`                                                                                                    | scoped, `Caller Current`                                                                                                                 | all              | throws if read before the access layer ran  |
| `IProjectContext`                                                                                                   | scoped, `Guid Id`; settable only by the access layer                                                                                     | AC-9             | throws if read before set                   |
| `AccessEvaluator.Decide(Caller, GrantAction, IReadOnlyCollection<string> resourceGrants, bool rowSecurity)`         | returns `AccessPlan`: `Allow`, `FilterRows(string[] tokens)`, `NotFound`, `Forbidden` (ENF-2, DEN-1, DEN-2)                              | AC-3, AC-5       | none                                        |
| `AccessEvaluator.Allows(Caller, GrantAction, resourceGrants, rowSecurity, rowGrants)`                               | `bool`, for in memory checks (realtime per event)                                                                                        | AC-3             | none                                        |
| `Scopes`                                                                                                            | constants for SCP-1, SCP-2; `CeilingFor(KeyType)`; `Validate(KeyType, IEnumerable<string>)`                                              | AC-8             | `validation_failed`                         |
| `.RequireScope(string)`, `.RequireClientApi(string)`                                                                | endpoint filter extensions on `RouteHandlerBuilder`                                                                                      | AC-8             | `403 insufficient_scope`, `403 forbidden`   |
| `ConsolePermission`, `RolePermissions.For(string role)`                                                             | enum per CPL-1; map per CPL-2                                                                                                            | AC-11            | unknown role → empty set                    |
| `.RequireConsolePermission(ConsolePermission)`                                                                      | endpoint filter; uses `IMembershipLookup` (implemented by feature 13)                                                                    | AC-11            | `404 not_found`, `403 forbidden`            |
| `ICallerRolesSource.GetLabelsAsync(Guid projectId, Guid userId, CancellationToken)`                                 | contract; Auth implements with HybridCache (LBL-2)                                                                                       | AC-10            | none                                        |
| `AccessErrors`                                                                                                      | `NotFound`, `Forbidden`, `InsufficientScope`, `Unauthorized` as `Error` values (spec 0002 error style)                                   | AC-5, AC-8, AC-9 | none                                        |
| `WhereGranted<T>(this IQueryable<T>, Expression<Func<T, string[]>> column, string[] tokens)` (`SharedKernel/Data/`) | EF filter translating to `&&`                                                                                                            | AC-4             | none                                        |

### Value sourcing

| Action                | Value                          | Source                                                                            |
| --------------------- | ------------------------------ | --------------------------------------------------------------------------------- |
| Resolve caller        | project id                     | route value only (PRJ-1, TEN-2)                                                   |
| Resolve caller        | key type, scopes, key project  | `platform.api_key` by hash (KEY-2, spec 0011 FLT-1)                               |
| Resolve caller        | user id, session, `aud`, `kid` | verified access token claims (TOK-2, TOK-3)                                       |
| Resolve caller        | labels                         | `auth.user.labels` via `ICallerRolesSource` cache (LBL-2)                         |
| Resolve caller        | run key scopes, acting user    | run key claims `scp`, `act`, issued from `functions.function.run_scopes` (FNC-2)  |
| Resolve caller        | operator permissions           | `platform.membership.role` mapped by `RolePermissions` (CPL-2)                    |
| Decide                | resource grants, row security  | `databases.table`, `storage.bucket`, `functions.function` columns, cached (EVL-5) |
| Decide                | row or file grants             | `_permissions` column, `storage.file.permissions`                                 |
| Create with no grants | default grants                 | creator's `UserId` from the caller (WRT-5)                                        |
| Deny                  | 404 versus 403                 | visibility rule over the resource grants and the caller's roles (DEN-1 to DEN-3)  |
| Audit                 | actor, before and after arrays | the caller, and the column values read before the write (AUD-1)                   |

### Key invariants

- No grant is ever needed for `secret_key`, `run_key`, `operator`, or `system`, and no grant ever lets a `guest` or `end_user` exceed their publishable key's client API scopes.
- A stored grant array is always canonical: expanded, valid, deduplicated, sorted, at most 100.
- A client never stores a token naming a role it does not hold.
- Row reads never leak existence: a filtered row is indistinguishable from a missing one.
- Project resolution never reads a body or query value.

### Security model

This spec is the security model. It satisfies ASVS 5.0 Level 2's access control chapter as spec 0006 requires: deny by default (EVL-1), checks on the server for every request (ENF), least privilege keys (SCP), no existence leaks (DEN), and audited permission changes (AUD-1). Holds other people's personal data, so audit is not optional. The main residual risks are a developer granting `any` too widely and a leaked secret key within its scopes; the console's grant editor (feature 26) should warn on `write:any` and on `update:users` for tables where anonymous sessions are on (anonymous users count as `users`). Known boundary: SQL run as the project's own database role (the SQL editor, feature 27, or the developer's migrations) is the developer acting on their own data and can read or rewrite `_permissions` directly; grants bind API callers, not the project owner's SQL.

### Configuration required

None. No new environment variables. The cache expiry (60 seconds, spec 0007 CCH-2) and the 100 token limit are constants.

### Critical test scenarios

- Happy path: user A creates a row in a table with `create:users` and row security on, gets the default grants, reads it back, and user B's list returns nothing; verifies **AC-3**, **AC-4**, **AC-7**.
- Grammar: `write:users` saves as three tokens; `read:team:x`, `read:User:...`, `delete:users:verified`, and 101 tokens all fail; verifies **AC-1**, **AC-6**.
- Writer rule: user A sending `read:user:<B>` or `update:label:admin` without that label is refused; a secret key sending the same is accepted; verifies **AC-6**.
- Grant takeover: on a table granting `update:users` with row security on, user A updates user B's row data but a `permissions` field answers 403; user A editing their own row keeps a server added `read:user:<B>` token; a reader without update gets no `permissions` field; verifies **AC-6**.
- Fallback: an expired bearer token with a valid publishable key answers 401, not guest results; verifies **AC-9**.
- Denials: a table with no grants for the caller answers 404 on list, get, and create; a readable row with no update grant answers 403; an unreadable one answers 404; verifies **AC-5**.
- Bulk: a filtered delete over 10 matching rows, 4 of them the caller's, deletes 4 and reports 4; verifies **AC-4**.
- Pagination: 250 rows, 100 allowed, pages of 30 return 30, 30, 30, 10 and a count of 100; verifies **AC-4**.
- Scopes: a secret key without `storage.write` gets 403 `insufficient_scope`; creating a publishable key with `databases.write` fails; a tampered stored scope is refused; verifies **AC-8**.
- Project: a token for project A on project B's route answers 401; a test fails a type that sets `IProjectContext` outside the access layer; verifies **AC-9**.
- Labels: removing `label:admin` from a user stops admin only reads on the next request on a second instance, through a `cache` signal for `orvano:auth:labels:<user id>`; verifies **AC-10**.
- Console: an operator with no membership gets 404; `RolePermissions.For("owner")` equals every `ConsolePermission`; verifies **AC-11**.
- Neutrality: the architecture test fails when `Access/` references Npgsql; verifies **AC-12**.

## Build plan

Tracer Bullet: feature 9 builds the shared core as one strand, fully tested, so feature 16 runs end to end on day one. The resolvers that turn a key or token into a `Caller` need the tables from features 13 and 14, so they are built there against the contracts made here.

1. `Access/` grammar: `GrantAction`, `Role`, `Grant`, `GrantTarget`, strict parse and format, reserved forms refused, with unit tests; satisfies **AC-1**.
2. `GrantSet.Normalize` and `GrantSet.DefaultsFor`: `write` expansion, target actions, writer role check, dedup, sort, limit; satisfies **AC-1**, **AC-6**, **AC-7**.
3. `Caller`, `CallerKind`, role sets, `MatchTokens`, `AccessEvaluator.Decide` and `Allows`, `AccessErrors`, with table driven unit tests over every caller kind and target; satisfies **AC-2**, **AC-3**, **AC-5**.
4. `Scopes` catalog, ceilings, `Validate`, and the `.RequireScope` and `.RequireClientApi` endpoint filters, tested on a sample minimal API; satisfies **AC-8**.
5. `ConsolePermission`, `RolePermissions`, `IMembershipLookup`, and `.RequireConsolePermission`, tested with a fake lookup; satisfies **AC-11**.
6. `ICallerContext`, `IProjectContext` (settable only through an internal access layer setter), `ICallerRolesSource`, plus architecture tests for PRJ-3 and ENG-1; satisfies **AC-9**, **AC-12**.
7. `WhereGranted` in `SharedKernel/Data/` and a PostgreSQL 18 test on a sample table with `_permissions text[]` and a GIN index: allowed rows only, correct pages and counts, bulk update touching only allowed rows; satisfies **AC-4**.
8. Label cache contract test, once feature 61 has built `ISignalBus` and `ICacheInvalidator` (spec 0007 SIG-1 to SIG-3, CCH-2): a fake `ICallerRolesSource` caching through HybridCache with the LBL-2 key and tags; invalidating `orvano:auth:labels:<user id>` on one host clears it on a second (two `OrvanoApiFactory` hosts on one database), and an entry expires after 60 seconds; satisfies **AC-10**.
9. Spec amendments (docs only, done 2026-10-18 by `/architect`): spec 0011 catalog gains `databases.table` and the grant columns, `api_key.scopes` CHECK, `function` in `audit_event.actor_kind`, and closes its feature 9 follow up; spec 0006 closes its feature 9 follow up (KEY-4, TEN-2); satisfies **AC-8**, **AC-9**.

## Consequences

**Positive**:

- One model and one evaluator for databases, storage, realtime, and functions, so every module denies the same way and features 16, 26, 29, 34, and 38 stay small.
- Engine neutral: grants are plain text tokens, and each engine only needs "array overlaps array".
- Owner only data works with zero setup (creator defaults), and the console can show grants as a simple table.
- Feature 51 only adds rows to `RolePermissions`; end user teams and verified roles slot into reserved grammar.

**Negative / tradeoffs**:

- No conditions on row values (for example "status equals published"). Developers model those with labels, separate tables, or server code with a secret key. A rule language would be a new feature.
- Every project table carries `_permissions`, even with row security off (a small storage cost). Turning row security on builds an index as a job, so the switch is not instant on large tables.
- Grant editing on rows is narrower than data editing (WRT-1): a table level `update` grant cannot share or unshare rows, so collaborative apps must give collaborators a row level `update` grant.
- Secret keys bypass grants, so a leaked secret key exposes everything in its scopes. Scopes limit the blast radius but do not remove it.
- Label and grant changes rely on the `cache` signal; a missed signal leaves stale labels or resource grants for up to 60 seconds (bounded by the cache expiry and spec 0007 SIG-2's reconnect clear).
- A guarded single write needs a second query on failure to choose 403 or 404 (DEN-3).

**Neutral**:

- Features 13 and 14 build the resolvers (API key, access token, console session) that produce a `Caller`; feature 38 issues run keys.
- `databases.table` becomes a new catalog row owned by the databases module.
- The public error codes `not_found`, `forbidden`, `insufficient_scope`, `unauthorized` are fixed here; feature 10 shapes the body.

## Follow-up

- [ ] Feature 10: carry `permissions`, `rowSecurity`, `fileSecurity` in the contract, and the four error codes (DEN-4).
- [ ] Feature 13: build the API key and console session resolvers, `IMembershipLookup`, and the `api_key.scopes` CHECK (SCP-3, CPL-3).
- [ ] Feature 61: build `ISignalBus` and `ICacheInvalidator` (spec 0007 SIG-1 to SIG-3, CCH-2) first, so build plan step 8 (AC-10) can run.
- [ ] Feature 14: build the access token resolver and `ICallerRolesSource`, invalidating the label tag through `ICacheInvalidator` on every label write (PRJ-1, LBL-2, LBL-3).
- [ ] Features 16, 29, and 37: cache resource grants per EVL-5 and invalidate their tag on every change to `permissions`, `row_security`, `file_security`, or `run_scopes`.
- [ ] Features 16 and 24: create `databases.table`, the `_permissions` column on every table, the index job on the row security switch, and refuse `_` column names (ENF-4, ENF-5).
- [ ] Feature 48: accept `actor_kind` `function` (AUD-1).
- [ ] Feature 26: the console grant editor and SDK helpers (`Permission.read(Role.users())` style), with a warning on `write:any`.
- [ ] Feature 29: decide the `storage.file` index form by measuring (ENF-4).
- [ ] Features 34 and 38: build RTM-1, RTM-2, and FNC-1 to FNC-3.
- [ ] Feature 51: add roles to `RolePermissions` and the membership role CHECK.
- [ ] Deferred end user teams and verified roles: define the reserved `team:`, `member:`, and `:verified` forms (GRM-3).
- [ ] Later: decide `users.impersonate` (SCP-4).
