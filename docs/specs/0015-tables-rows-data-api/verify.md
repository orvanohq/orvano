# Verify: Tables, rows & data API · spec 0015 · updated 2026-10-10
_Steps derived from spec 0015 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones._

_Milestone 1 (thin thread) only. Later milestones append their steps here._

## UI / manual
- [ ] Open a project as an owner → the sidebar shows "Databases" after Users and its auth entries → open it → `main` is listed first with an Active badge, and the title reads `Databases · <project> · Orvano` → AC-1, AC-31
- [ ] Open `main` → click "New table" → the dialog shows `id`, `created_at`, `updated_at` as System columns → name it `tasks`, add a column `title` (Text, Required) and a column `views` (Big integer, default value `0`) → Create table → the console opens `/projects/<id>/databases/main/tables/tasks` → AC-5, AC-32
- [ ] In New table, type `Bad Name` and a column named `id` → Create table → each error shows under its own field, nothing is sent → AC-5, AC-32
- [ ] In New table, name a table `select` → the server's "reserved Postgres word" problem shows under Name; name it `tasks` again → "already exists" shows under Name → AC-5, AC-32
- [ ] Sign in as a viewer → "New table" is off with "Developers and owners only"; the tables list and rows grid still show → AC-24, AC-35
- [ ] Open the `tasks` table page → the banner says apps can't read or write rows yet; after a server creates rows, the grid shows them in created order with `NULL` for empty values and Load more after 25 → AC-33
- [ ] Make a table in `psql` with no primary key → open it in the console → it shows "Read only" in the list and the "no primary key" note on its page → AC-6, AC-21

## Commands
- [ ] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-namespace "Orvano.Server.Tests.Databases" --filter-class "*ProjectScopeTests"` → all pass → AC-1, AC-5, AC-6, AC-11, AC-16, AC-18 (default order), AC-22 (key callers, denial), AC-25
- [ ] With a server key that has `tables.write`, `POST /v1/databases/main/tables` with every column type (text, integer, bigint, float, decimal, boolean, timestamp, date, uuid, json, text[], integer[], uuid[]) → 201; `GET /v1/databases/main/tables/<name>` answers the same columns with `pgType` (`int4`, `_text`, ...) and defaults by kind → AC-5, AC-6
- [ ] As the project role in `psql`: `SELECT tgname FROM pg_trigger WHERE tgrelid = 'p_<id>.<table>'::regclass` → `orvano_updated_at`; the table's owner is `p_<id>` → AC-5, AC-25
- [ ] `POST .../rows` with `{"views": 9223372036854775807, "price": "123456789012345678901234567890.000000000000000001", "due_at": "2026-10-10T10:30:00.123456+02:00"}` → the row answers `views` and `price` as exact strings and `due_at` as `2026-10-10T08:30:00.123456Z` → AC-16
- [ ] `POST .../rows` with `{"rank": 1.0, "created_at": "...", "nope": 1}` and a required column missing → 400 `invalid_row`, `detail` starts with the first bad field, `errors[]` lists every field, nothing is written and no event exists → AC-11
- [ ] Create two rows with the same unique value, then one reusing an `id` → 409 `row_conflict`, `detail` names `rank` / `id`, never the value → AC-20 (early part)
- [ ] `GET .../rows?limit=3` and follow `nextCursor` to the end → every row exactly once in created order; reuse that cursor on another table → 400 `invalid_cursor` → AC-18
- [ ] Call `GET .../rows` with no credential, with an app user's token, and for a table that doesn't exist → 403 `table_access_denied` with the same detail every time; both `Authorization` and `X-Orvano-Key` → 400; a key with only `users.read` → 403 `insufficient_scope` → AC-22
- [ ] Run the .NET scenario runner (`dotnet run --project tests/scenarios/runners/dotnet -f net10.0`, and `-f net8.0`) against `tests/scenarios/compose.yml` → "a server creates a table in main, then creates and pages through its rows" passes → AC-28, AC-30 (server part)
- [ ] `docker run ... postgres:18.6` with `deploy/compose/initdb`, then the EF drift check → "EF model matches the database" with `db_databases` → data model
- [ ] `SELECT payload FROM orvano.events WHERE type IN ('databases.table.created', 'databases.row.created')` → names and the actor only, no value → AC-26, AC-27 (early part)

## Value sourcing
- [ ] The project: send `X-Orvano-Project` of a provisioning project → 409 `project_not_ready`; of another org's project in the console → 404 `project_not_found` → value: project
- [ ] The caller: an expired app token → 401 `token_expired` exactly as on `account.*` → value: caller
- [ ] The schema: `main` maps to `p_<id>`; a slug from another project → 404 `database_not_found` → value: schema name
- [ ] Structure is live: add a column in `psql`, wait one second, `tables.get` shows it; drop a column in `psql` and create a row naming it → it heals (the cache reloads) rather than 500 → value: structure cache
- [ ] `Table.estimatedRows`: a never analyzed table answers `null`; after `ANALYZE` it answers a number → value: estimate
- [ ] `Table.readable` / `writable`: a `bigint` primary key table is readable only; a table with no primary key is neither → value: flags
- [ ] `Column.default`: a default made in SQL as `'it''s'::text` reads back as value `it's`; `nextval(...)` reads as `expression` → value: column default
- [ ] `Column.unique`: a unique index made in SQL on exactly one column marks it unique; a two column unique doesn't → value: unique
- [ ] `rows.create` id: send an uppercase uuid as `id` → stored and answered lowercase; leave it out → a version 7 uuid → value: id
- [ ] Row event `database`: an event for `main` says `"main"` → value: event database
- [ ] Value default in DDL: `{"kind": "value", "value": "1.5"}` on an integer column → 400 naming `columns[0].default.value`; a json default with a quote round trips exactly → value: quoted default
- [ ] The cursor: decode one (base64url JSON) and change the hash → 400 `invalid_cursor` → value: next cursor

## Acceptance-criteria coverage
- AC-1 … UI step 1, Commands 1 · AC-5 … UI 2 to 4, Commands 2 and 3, value default · AC-6 … Commands 2, value structure, estimate, flags, default, unique · AC-11 … Commands 5 · AC-16 … Commands 4 · AC-18 … Commands 7, value cursor · AC-21 … UI 7, value flags · AC-22 … Commands 8, value caller · AC-24 … UI 5 (viewer) · AC-25 … Commands 1 and 3, value schema · AC-26, AC-27 … Commands 11 (early) · AC-28 … Commands 9 · AC-31, AC-32, AC-33, AC-35 … UI 1 to 6 (first pages)
- Later milestones: AC-2 to AC-4, AC-7 to AC-10 (beyond the reload), AC-12 to AC-15, AC-17, AC-19, AC-20 (rest), AC-23, AC-24 (rest), AC-29, AC-30 (client part), AC-34, AC-36, AC-37
