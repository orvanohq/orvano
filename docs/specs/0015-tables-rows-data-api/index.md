# 0015. Tables, rows, and the data API

**Date**: 2026-10-10
**Updated**: 2026-10-10 (cross check: self heal on unknown names, quoted defaults, reserved words, advisory locks, value free problems, lock timeouts on row calls, grammar and cursor details)
**Status**: In Progress

## Summary

Each project gets real Postgres tables that you create in the console or from a server SDK, grouped into named databases (`main` plus up to 10 more, each its own Postgres schema). Every table is instantly reachable through one generated REST API (create, read, update, delete, filter, sort, paginate) and a handwritten query builder in the JS, Dart, and .NET SDKs. Table structure is read live from Postgres itself, so a new column works on the very next call with no restart. Until row 17 adds permission rules, only server keys and the console can touch rows, and client calls are refused, so nothing insecure ships in the meantime.

## Requirements

**User stories**:
- As a developer, I want to create a table with typed columns in the console, so that I can model my app's data without writing SQL.
- As a developer, I want to create, read, update, delete, filter, sort, and page through rows from every SDK with the same query language, so that my frontend and backend talk to the data the same way.
- As a developer, I want a new column to work on the very next call, so that I can change my schema while my app runs.
- As a developer, I want to group tables into named databases (`main`, `analytics`), so that a growing project stays organized.
- As a backend developer, I want to manage tables from server code with a scoped API key, so that setup scripts and CI can create what my app needs.
- As a project owner, I want client apps locked out of tables until I write permission rules, so that no table is ever public by accident.
- As a viewer in the org, I want to browse tables and rows read only, so that I can help debug without the power to change data.

**Acceptance criteria**:

*Databases*

- **AC-1**: Every `active` project has the database `main`, backed by the existing schema `p_<projectId>`, with no change to project provisioning. `main` has no `db_databases` row: it is synthesized, listed first by `databases.list`, and answered by `databases.get` with `id: "main"`, `slug: "main"`, `name: "Main"`, `status: "active"`, `main: true`, `createdAt: null`. Renaming or deleting it answers 409 `database_protected`. Every SDK call that takes a database defaults it to `main`.
- **AC-2**: `databases.create` takes `slug` (`^[a-z][a-z0-9_]{0,39}$`, not `main`) and `name` (1 to 100 characters, trimmed). After `pg_advisory_xact_lock(hashtext('db:' || projectId))` it counts and checks the slug, then inserts a `db_databases` row with a new 20 character `[a-z0-9]` ID, `schema_name = 'd_' || id`, and `status = 'provisioning'`, enqueues the job `databases.database.provision`, writes the event `databases.database.created`, and answers 201 with the database. The job (worker, `orvano_admin`), in one transaction, reads its row `FOR UPDATE` (a row already gone makes it a no op), runs `CREATE SCHEMA IF NOT EXISTS d_<id> AUTHORIZATION p_<projectId>`, sets `status = 'active'`, writes `databases.database.provisioned`, and sends the schema NOTIFY (AC-10); it is idempotent, and when role `p_<projectId>` no longer exists (the project was purged) it stops without retrying. When the job runs out of attempts it sets `provision_failed_at`, writes `databases.database.failed`, and the API shows `status: "failed"`; `consoleDatabases.retry` (owner or developer) clears `provision_failed_at` and enqueues the job again (400 `invalid_request` on a database that is not `failed`). A slug already used in the project answers 409 `name_taken`; an 11th extra database answers 409 `schema_limit_reached`. Any table or row call on a database that is not `active` answers 409 `database_not_ready`.
- **AC-3**: `databases.update` changes `slug` and/or `name` (same rules as AC-2) and writes `databases.database.updated` with the changed field names. The old slug answers 404 `database_not_found` on the very next request, on every api instance (AC-10).
- **AC-4**: `databases.delete` runs, in one transaction: it reads the row `FOR UPDATE` (so it waits for a running provision job), runs `DROP SCHEMA IF EXISTS d_<id> CASCADE` as the project role (with AC-9's lock timeout), deletes the `db_databases` row, writes `databases.database.deleted`, and sends the schema NOTIFY. It answers 204. Unknown slugs answer 404 `database_not_found`. A database that is still `provisioning` (or `failed`) can be deleted; the job then finds no row and stops.

*Tables and columns*

- **AC-5**: `tables.create` takes `name` and 0 to 200 `columns`. Table and column names match `^[a-z][a-z0-9_]{0,62}$`, are not a reserved Postgres keyword (`pg_get_keywords()` with `catcode` `R` or `T`; unreserved words such as `name`, `type`, and `status` are allowed, since every identifier is quoted), do not start with `pg_` or `orvano`, and a column is not named `id`, `created_at`, or `updated_at`; a bad name answers 400 `invalid_request` with `detail` starting with the input path (`columns[2].name: ...`). The table is created with `id uuid PRIMARY KEY DEFAULT uuidv7()`, `created_at timestamptz NOT NULL DEFAULT now()`, `updated_at timestamptz NOT NULL DEFAULT now()`, then the columns in order, plus the trigger `orvano_updated_at BEFORE UPDATE ... EXECUTE FUNCTION orvano_fn.set_updated_at()`. Each column has a `type` from: `text`, `integer` (int4), `bigint` (int8), `float` (float8), `decimal` (unconstrained numeric), `boolean`, `timestamp` (timestamptz), `date`, `uuid`, `json` (jsonb), `text[]`, `integer[]`, `uuid[]`; plus `required` (NOT NULL, default false), `unique` (a single column UNIQUE constraint, default false), and an optional `default`: `{ kind: "value", value }` (the literal as text: checked with `SELECT $1::<pgtype>`, a failure answering 400 `invalid_request`, then quoted by Postgres with `SELECT quote_literal($1)` and written as `DEFAULT <quoted>::<pgtype>`; array defaults use Postgres array syntax such as `{a,b}`, and `json` defaults are JSON text), or kind `now` (timestamp and date only), `uuidv7` or `random_uuid` (uuid only). Kind `expression` is never accepted as input. Every schema change first takes `pg_advisory_xact_lock(hashtext(<schema>))`, so limits and names are checked without races. A name already used by any table in that schema answers 409 `name_taken`; a 501st table in one database answers 409 `schema_limit_reached`. It answers 201 with the table (AC-6).
- **AC-6**: `databases.list` orders `main` first, then by slug (the cursor is the last slug). `tables.list` (keyset paged by name, 25 by default) and `tables.get` read the structure live from `pg_catalog`, including tables, columns, defaults, and constraints made outside Orvano, never from a copy. A `Table` has `name`, `database` (the slug), `columns`, `readable`, `writable`, and `estimatedRows` (`pg_class.reltuples`, null when never analyzed). A `Column` has `name`, `type` (the list in AC-5, or `other` for any other Postgres type), `pgType` (the Postgres type name, for example `int4` or `_text`), `required`, `unique` (true when a single column UNIQUE constraint or unique index covers exactly that column, read from `pg_constraint.conkey` and `pg_index`, never from names), `default` (null; `now()`, `uuidv7()`, `gen_random_uuid()` as their kinds; a quoted literal with a cast (`'...'::type`, with `''` unescaped), a bare number, or `true`/`false` as `value`; else `{ kind: "expression", value: <the SQL text> }`), `system` (true for `id`, `created_at`, `updated_at` on a standard table), and `writable` (false for system columns, `other` types, generated columns, and names outside `^[A-Za-z_][A-Za-z0-9_]*$`). Only ordinary and partitioned tables are listed (no views).
- **AC-7**: `tables.update` renames a table (AC-5's name rules, 409 `name_taken`) and writes `databases.table.renamed`. `tables.delete` runs `DROP TABLE` (no `CASCADE`; a table that other objects depend on, such as a foreign key or view made in SQL, answers 409 `table_in_use` with `detail` naming the dependency) and writes `databases.table.deleted`. Both send the schema NOTIFY. Unknown tables answer 404 `table_not_found`.
- **AC-8**: `columns.create` adds one column with AC-5's rules; a `required` column needs a `default` unless the table has no rows, else 409 `column_needs_default`. `columns.update` changes any of `name`, `required`, `unique`, `default` (an explicit `default: null` drops it): setting `required` when nulls exist answers 409 `column_has_nulls` with the count in `detail`; setting `unique` when duplicates exist answers 409 `column_has_duplicates` with the count of duplicated values. A Postgres `23502` or `23505` raised by the `ALTER` itself (a row written between the check and the change) answers the same 409s. Type changes are not offered. `columns.delete` drops the column (409 `table_in_use` when an object made in SQL depends on it). System columns, and columns that are not `writable`, can't be changed or dropped (400 `invalid_request`, `column: system columns can't change`). Each writes `databases.column.added`, `.updated` (with changed field names), or `.dropped`, and sends the schema NOTIFY. Unknown columns answer 404 `column_not_found`; a 201st user column answers 409 `schema_limit_reached`.
- **AC-9**: Every schema change runs inside one transaction holding the schema's advisory lock (AC-5), with `SET LOCAL lock_timeout = '3s'` and `SET LOCAL statement_timeout = '15s'`, and that transaction also holds its event and NOTIFY, so a failed change leaves no event and no stale cache. A lock wait past 3 seconds answers 409 `table_busy` with `Retry-After: 1`.
- **AC-10**: A column added, renamed, or dropped through Orvano is usable (or gone) on the next request to every api instance, with no restart: each api instance listens on `orvano_schema` and drops its cached structure for the project named in the payload. A change made outside Orvano (straight `psql`) is seen within 60 seconds (the cache's time to live), and at once when a request names a column or table the cache lacks: a syntactically valid but unknown name makes the api reload that schema's structure (at most once per schema per second) and validate again before answering 404 or 400; and when Postgres raises `42703` or `42P01` (a column or table dropped or renamed behind the cache) it reloads once and runs the request again. The api instance that made a change also drops its own entry right after the commit, without waiting for the NOTIFY.

*Rows*

- **AC-11**: `rows.create` takes a JSON object of column values and answers 201 with the whole row as stored (`RETURNING *`). `id` may be supplied (a uuid), else `uuidv7()` fills it; `created_at` and `updated_at` are never accepted. A missing `required` column without a default, a `null` for a required column, an unknown column, a read only column, or a value that does not fit its type (AC-16) answers 400 `invalid_row`, with `detail` starting with the first bad field (`title: required`) and `errors[]` listing every bad field (`{ field, message }`).
- **AC-12**: `rows.createMany` takes `{ rows: [...] }` with 1 to 100 rows and inserts them in one transaction, one prepared single row `INSERT` per row (so rows may send different columns), all or nothing, answering 201 `{ items }` in input order. Validation errors name the index (`errors[].field` like `rows[3].title`), and a database error names the index of the row whose `INSERT` raised it.
- **AC-13**: `rows.get` returns the row with that `id`, or 404 `row_not_found` (also for a `rowId` that is not a uuid). It takes `select` like `rows.list` (AC-18).
- **AC-14**: `rows.update` (PATCH) sets only the columns it is sent and answers the whole row after the change; `updated_at` moves forward through the trigger. It refuses `id`, `created_at`, `updated_at`, and AC-11's bad fields with 400 `invalid_row`. An empty object answers 200 with the row unchanged, with no UPDATE and no event (a stale `ifUpdatedAt` still answers 409); a body with values equal to the stored ones still runs the UPDATE, moves `updated_at`, and writes an event. With `ifUpdatedAt` (query, the exact `updated_at` string the caller read), it updates only when the stored `updated_at` equals it, else 409 `row_changed`. A missing row answers 404 `row_not_found`.
- **AC-15**: `rows.delete` answers 204, or 404 `row_not_found`, and takes the same optional `ifUpdatedAt` guard (409 `row_changed`).
- **AC-16**: Values travel in JSON as: `text`, `uuid` → string (uuid lowercase); `integer`, `float` → number (`integer` input must be an integer token in range, so `1.0` and `1e3` are refused); `bigint`, `decimal` → string on output, a string or a number on input, read from the raw JSON token and never through a double; `boolean` → boolean; `timestamp` → ISO 8601 in UTC with microseconds (`2026-10-10T08:30:00.123456Z`) on output, any ISO 8601 with an offset on input (none → `invalid_row`); `date` → `YYYY-MM-DD`; `json` → any JSON value (a JSON `null` is stored as SQL NULL); arrays → JSON arrays of the element's form, `null` elements allowed. `NaN` and infinities are refused on input, and stored ones come back as the strings `"NaN"`, `"Infinity"`, `"-Infinity"`. A `null` is `null` for every type. `other` columns come back as their Postgres text form (selected with `::text`) and are never writable. Known types are read with typed Npgsql readers and formatted by Orvano, so timestamps always carry six fraction digits.
- **AC-17**: `rows.list` and `rows.count` take `filter`, a string in the grammar under *Query language* below. Operators: `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `in`, `nin`, `isnull`, `notnull` on every filterable type, and `contains`, `startswith`, `endswith` (case insensitive, with `%`, `_`, and `\` matched literally) on `text` only. `json` and array columns take only `isnull` and `notnull`. Conditions at the top level are ANDed; `and(...)` and `or(...)` groups nest up to 3 levels. A filter longer than 4 KB, with more than 50 conditions, deeper than 3 levels, an `in` list over 100 values, an unknown or unfilterable column, a wrong operator for the type, or a value that does not parse as the column's type answers 400 `invalid_query` with `detail` starting with `filter:` and the character offset.
- **AC-18**: `rows.list` takes `sort` (up to 3 columns, comma separated, `-` prefix for descending, any filterable scalar column), `select` (comma separated column names, default all; unknown names answer 400 `invalid_query`), `limit` (1 to 100, default 25), and `cursor`. With no `sort` it orders by `created_at, id` ascending; `id` is always added as the last tie breaker in the direction of the last sort column (on a table without the standard `id`, its primary key columns are). Nulls sort last ascending and first descending, as in Postgres. The tie breaker is left out when the sort already ends in `id` (or all primary key columns). Duplicate sort columns, and `json`, array, and `other` columns, answer 400 `invalid_query` in `sort`. The cursor is base64url JSON holding each key value read as `::text` (null kept as null) and a SHA-256 of the database ID, table, `filter`, and `sort` (not `select` or `limit`); values are bound back as `$n::<pgtype>` from the cache. Reusing it with a different database, table, filter, or sort, or a cursor the server did not issue, answers 400 `invalid_cursor`. Walking every page of an unchanged table returns each row exactly once, in order, for any sort; rows inserted during the walk never cause a row to repeat.
- **AC-19**: `rows.count` answers `{ count }` for the rows matching `filter`, exactly (`count(*)`).
- **AC-20**: Data calls run with `SET LOCAL lock_timeout = '3s'` and `SET LOCAL statement_timeout = '5s'` (public operations) or `'15s'` (console operations); hitting it answers 422 `query_timeout`, which SDKs never retry. A unique violation (including a duplicate `id`) answers 409 `row_conflict` with `detail` naming the column, found from the constraint name through the cache; a foreign key violation (tables made in SQL) answers 409 `row_conflict` naming the constraint; `detail` is built from names only, and no Postgres `Detail` or `MessageText` (which can hold values) ever reaches a response; a not null, check, or data range error from Postgres answers 400 `invalid_row`; a lock or deadlock error (`55P03`, `40P01`) answers 409 `table_busy` with `Retry-After: 1`; `42501` (a table the project role does not own, made by a superuser) answers 409 `table_not_writable`. A request body over 1 MB answers 413 `request_too_large`.
- **AC-21**: A table with a primary key that is not the standard `id uuid` is `readable: true, writable: false`: `rows.list` and `rows.count` work (its primary key is the tie breaker), and `rows.get`, `create`, `createMany`, `update`, and `delete` answer 409 `table_not_writable`. A table with no primary key is `readable: false, writable: false`, and every row call answers 409 `table_not_writable` ("needs a primary key").

*Callers and access*

- **AC-22**: Row operations accept three callers: no credential, an app user's access token (`Authorization: Bearer`, spec 0004), or an API key (`X-Orvano-Key`, spec 0003). A bad or expired token or key answers 401 exactly as those specs say; sending both `Authorization` and `X-Orvano-Key` answers 400 `invalid_request`. An API key needs `rows.read` for `get`, `list`, `count` and `rows.write` for `create`, `createMany`, `update`, `delete`, else 403 `insufficient_scope`; `databases.*` and `tables.*` reads need `tables.read`, and every database, table, and column change needs `tables.write`. Callers without a key go through `IRowAccess`, which in this row denies every table: 403 `table_access_denied`. For those callers the access check runs before the database and table are looked up, so a denied caller never learns whether a database or table exists. The checks run in this order: project (spec 0003 AC-4), credential (401), scope (403), access for non key callers (403), database (404, 409), table (404, 409), query and body (400), execution.
- **AC-23**: In the `Test` environment only, `tests/scenarios/fixtures.yaml` gains `tables` (project, database, name, columns, `openToUsers`), seeded at startup, after the project fixtures are provisioned, through the same domain code as `tables.create` (a table that already exists is skipped). A `Test` only `IRowAccess` lets any signed in app user of that project (not an anonymous caller) run every row operation on tables marked `openToUsers: true`, and denies the rest. Outside `Test`, the server refuses to start if that implementation is registered.
- **AC-24**: Console operations (`consoleDatabases.*`, `consoleTables.*`, `consoleColumns.*`, `consoleRows.*`) need the console session and pass the CSRF check (spec 0004). Owners and developers may do everything; viewers may list and get databases, tables, and rows and count rows, and get 403 `forbidden` on every change, checked before the target is looked up. Console calls bypass `IRowAccess`.
- **AC-25**: Every row and table call runs in one `ProjectScope` transaction as role `p_<projectId>` with `search_path` set to that database's schema, so a project can never reach another project's data, and a slug from another project is just 404 `database_not_found`. Identifiers in SQL come only from the cached structure and are always double quoted; every value is a parameter. A fuzz test that sends random filter, sort, select, and row input finds no response other than the documented codes and no SQL error code outside AC-20's mapping.

*Events*

- **AC-26**: `rows.create`, `createMany`, `update`, and `delete` write `databases.row.created`, `.updated`, or `.deleted` in the same transaction as the change, one per row: `{ database: <database id, or "main">, table, rowId, columns: [changed column names], actor }` (create: every non null column in the returned row; update: the keys sent; delete: empty), where `actor` is `{ type: "api_key" | "app_user" | "user", id }`. No event carries a value. An empty update writes no event (AC-14). Writes made in SQL (row 18) write no event, and the docs say so.
- **AC-27**: Every database, table, and column change writes its event (AC-2 to AC-8) with the actor and changed names only, plus `pg_notify('orvano_schema', <projectId>)` in the same transaction.

*SDKs*

- **AC-28**: The contract defines every operation in the *API surface* below. `Row` is an open map (`Record<unknown>` marked `x-orvano-dynamic`) that SdkGen maps to TS `Record<string, unknown>`, Dart `Map<String, Object?>`, and .NET `JsonObject`. Row operations are audience `both` with `@useAuth(NoAuth | BearerAuth | ApiKeyAuth)` and an `x-orvano-scope`, which applies to key callers only; SdkGen accepts that combination. Database, table, and column operations are audience `server`. Console operations go only to `console-client`. `Problem` gains an optional `errors` (`{ field, message }[]`). `rowId` and `ifUpdatedAt` are plain `string` parameters (no uuid or date time format), so SDKs pass them through untouched and a non uuid reaches the server for 404 `row_not_found`. Generated docs snippets exist for every operation.
- **AC-29**: `@orvano/js` (root and `./server`), `orvano_core` (and so `orvano_flutter` and `orvano_dart`), and `Orvano` (.NET) ship a handwritten query builder over the generated calls: `orvano.db(name = "main").from<T>(table)` with `where`, `orderBy`, `select`, `limit`, `after(cursor)`, then `list`, `listAll` (an async iterator over every page), `count`, `get`, `create`, `createMany`, `update` (with `ifUpdatedAt`), and `delete`; filter helpers `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `isIn`, `notIn`, `isNull`, `notNull`, `contains`, `startsWith`, `endsWith`, `and`, `or`. The builder always quotes string values, writes dates as ISO 8601 and big numbers as strings, and refuses (before any request) an empty `isIn`, a depth over 3, or more than 50 conditions. A shared file of test vectors (builder input → exact filter and sort strings) passes in all three languages. Typed rows: TS casts to `T`, Dart takes an optional `fromJson`, .NET takes an optional `JsonTypeInfo<T>`; without them rows stay open maps.
- **AC-30**: Shared scenarios run on every SDK surface (spec 0001): server SDKs (`@orvano/js/server`, `orvano_dart`, .NET) create a database and a table, add a column and use it in the very next call, create, get, update with and without a stale `ifUpdatedAt`, delete, filter with an `or` group, sort on two columns, walk pages with `listAll`, and count. Client SDKs (`@orvano/js` in every runtime, Next.js, Flutter) sign in and run the row operations on a fixture table with `openToUsers: true`, and get 403 `table_access_denied` on a closed table and as an anonymous caller.

*Console*

- **AC-31**: The project nav gains "Databases" (Lucide `Database` icon) after "Users", opening `/projects/$projectId/databases`: a table of databases (`main` first) with slug, name, and status, a "New database" dialog (slug, name), and per database rename and delete. A `provisioning` database shows a spinner badge and refreshes until active; a `failed` one shows "Retry". Delete opens a destructive dialog with Cancel focused that warns the data is gone for good, and Delete stays off until you type the slug. `main` shows no rename or delete.
- **AC-32**: `/projects/$projectId/databases/$database` lists the tables (name, estimated rows, a "Read only" badge when not writable) with "Load more", and a "New table" dialog: the name, the three system columns shown fixed, and a column builder (name, type select, Required, Unique, Default by kind) with field errors under each field. On 201 it opens the new table.
- **AC-33**: `/projects/$projectId/databases/$database/tables/$table` has a Rows tab and a Columns tab (search param `tab`). Rows: a grid of 25 rows with "Load more"; a "Count rows" button (calls `count`, shows the total and when it was counted); sort by up to 3 columns from the column menus; a filter panel where you add conditions (column, operator offered for its type, value input for its type) in AND or OR groups, with "Copy as code" showing the filter string (built by `@orvano/js`'s own serializer, not a copy of it) and the JS, Dart, and .NET builder lines; "New row" and a click on a row open a side sheet with an input per writable column type (text, number, switch, date time, date, uuid, a JSON editor that validates, arrays as a list of inputs), whose Save sends `ifUpdatedAt` and on `row_changed` says "This row changed since you opened it" with a Reload button; row delete with a destructive confirm. A table that is not writable shows a note saying why and hides the write actions. A banner says apps can't read or write rows yet: client access comes with permission rules (row 17), and server keys and the console work now.
- **AC-34**: The Columns tab lists every column with type, `pgType` for `other`, Required, Unique, Default, and a "System" badge; "Add column", an edit dialog (rename, Required, Unique, Default), and drop with the typed name confirm. AC-8's 409s show in the dialog's alert with the server's message. Table rename and delete (typed name confirm) sit in the page header menu.
- **AC-35**: Viewers see every page with write actions disabled and "Developers and owners only". Pages set document titles (`Databases · <project> · Orvano`, `<database> · <project> · Orvano`, `<table> · <database> · Orvano`) and use `PageHeading`. Everything follows spec 0005: tokens only, keyboard reachable (the grid, the filter builder, the sheet), focus trapped in dialogs and returned, field errors under fields, server errors in the form alert, no overflow at 375 px, and axe finds zero violations in both themes and densities. The dialogs, the filter builder, and the row sheet have catalog examples in `src/dev/examples.tsx`.

*Docs and operations*

- **AC-36**: The docs site gains *Databases and tables* (databases, tables, column types, the JSON forms of AC-16, the `main` default, events and their SQL caveat), *Query rows* (the grammar, every operator, that text comparisons and ordering follow the database collation, `and` and `or`, sort, select, pagination, count, with a builder snippet per SDK), and *Manage tables from code* (server SDKs and scopes), plus a fix page for every new error code and a console page for the Databases screens.
- **AC-37**: Each row and table call logs, at Information, the project ID, database ID, table name, operation, row count, duration, and outcome code, and never a value, a filter string, or a row body. OpenTelemetry adds a counter `orvano.databases.requests` (tags: operation, caller type, code, which is `ok` on success) and a histogram `orvano.databases.duration`.

## Decision

**Chosen option**: Option 1: real Postgres tables per database schema, read live from the catalog, served by a REST resource API with one `filter` string in a small grammar, and wrapped by a handwritten query builder in each SDK.

A new module, `Orvano.Databases`, owns databases (the `db_databases` table plus each schema), the structure cache, the filter parser and SQL builder, and every operation in this spec; it runs project data work only through `ProjectScope`, and leaves row level rules to row 17 behind `IRowAccess`.

**Implementation skills**: `supabase-postgres-best-practices` (`supabase/agent-skills`, `.claude/skills/supabase-postgres-best-practices/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `security-and-hardening` (`addyosmani/agent-skills`, `.claude/skills/security-and-hardening/`) · `testcontainers-integration-tests` (`aaronontheweb/dotnet-skills`, `.claude/skills/testcontainers-integration-tests/`) · `configuring-opentelemetry-dotnet` (`dotnet/skills`, `.claude/skills/configuring-opentelemetry-dotnet/`) · `workers-best-practices` (`cloudflare/skills`, `.agents/skills/workers-best-practices/`) · `dart-add-unit-test` (`dart-lang/skills`, `.agents/skills/dart-add-unit-test/`) · `dart-write-documentation` (`flutter/agent-plugins`, `.agents/skills/dart-write-documentation/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `tanstack-router` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-router/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`) · `tanstack-table` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-table/`) · `tanstack-form` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-form/`) · `zod` (`pproenca/dot-skills`, `.claude/skills/zod/`) · `shadcn` (`shadcn-ui/ui`, `.claude/skills/shadcn/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`)

## Rationale

Reasoning, the options weighed, and the smaller calls made while writing: see [rationale.md](rationale.md).

## Feature design

### Module seams

`Orvano.Databases` references `Orvano.Core`, `Orvano.Contract`, `Orvano.Platform` (`Contracts/` only: `IProjectDirectory.GetServableAsync`, `IConsoleAccess`), and `Orvano.Auth` (`Contracts/` only). Nothing references it.

| Contract | Owned by | Implemented by | Members | Used for |
|---|---|---|---|---|
| `IAppCallerResolver` | Auth (`Contracts/`) | Auth | `ResolveAsync(HttpContext http, string keyScope, CancellationToken ct)` → `AppCallerResult`: `Anonymous`, `AppUser(Guid UserId, Guid SessionId)`, `ApiKey(Guid KeyId)`, or `Rejected(IResult problem)` (the 401 or 403 that spec 0003 and 0004 already define, built from the same code as `PublicRequests.RequireUser` and `RequireApiKey`) | AC-22: one endpoint accepting three callers |
| `IRowAccess` | Databases (`Contracts/`) | Databases (`DenyClientRowAccess`, the default), `Test` only `TestRowAccess` (AC-23), row 17 later | `DecideAsync(AppCaller caller, string projectId, string databaseSlug, string table, RowOperation op, CancellationToken ct)` → `RowAccessDecision` (`Allow` · `Deny`) | AC-22, AC-23; row 17 grows the decision (for example a row predicate) |
| `ProjectScope.RunAsync` (change) | Core | Core | gains `string schema` (validated `^(p\|d)_[a-z0-9]{1,60}\z`, defaulting to `p_<projectId>`), and an optional `afterAsApp` step that runs `SET LOCAL ROLE NONE` and then the caller's platform side work (outbox, NOTIFY) in the same transaction | AC-4, AC-9, AC-25, AC-26: `p_<id>` has no rights on `orvano.events` |
| Platform purge (change) | Platform | Platform | while acting as `p_<id>`, runs `DROP OWNED BY p_<id> CASCADE` (drops `p_<id>` and every `d_<id>` it owns) in place of `DROP SCHEMA p_<id> CASCADE`, then drops the role | AC-2: schemas the role owns |

**Notifications (change)**: `PgNotificationListener` handlers are built only in the worker and realtime roles today. `IOrvanoModule` gains `RegisterNotifications(INotificationRegistry registry)` (default empty body), and the api role starts one listener when any module registers a channel; Databases registers `orvano_schema` there.

**Structure cache loader**: runs as `orvano_app` on a plain pooled connection (outside `ProjectScope`; `pg_catalog` is readable by every role), filtered by `nspname` and `relkind IN ('r', 'p')`.

Databases registers its consumer of `platform.project.purged`, which enqueues `databases.project.purge` to delete that project's `db_databases` rows (idempotent). The provision job resolves the admin data source from `job.Services`, as `ProjectJobs` does.

### Data model sketch

**Platform side** (schema `orvano`, module `Orvano.Databases`, EF `DatabasesDbContext`, added to the drift check):

| Table | Column | Type | Null | Notes |
|---|---|---|---|---|
| `db_databases` | `id` | text | no | PK, `^[a-z0-9]{20}$` |
| | `project_id` | text | no | index; no FK (spec 0003: no foreign keys across modules) |
| | `slug` | text | no | `CHECK (slug ~ '^[a-z][a-z0-9_]{0,39}$' AND slug <> 'main')`; `UNIQUE (project_id, slug)` |
| | `name` | text | no | 1 to 100 characters |
| | `schema_name` | text | no | `UNIQUE`; always `'d_' \|\| id` (`CHECK`) |
| | `status` | text | no | `CHECK (status IN ('provisioning', 'active'))`; the API's `failed` is derived from `provisioning` plus `provision_failed_at` |
| | `provision_failed_at` | timestamptz | yes | set when the job runs out of attempts |
| | `created_at`, `updated_at` | timestamptz | no | |

**Shared function schema** (created by migration `0011_databases.sql` as `orvano_admin`): schema `orvano_fn` with `GRANT USAGE ON SCHEMA orvano_fn TO PUBLIC`, holding `orvano_fn.set_updated_at()` (`SECURITY INVOKER`, `NEW.updated_at := now(); RETURN NEW`). Nothing else lives there yet; row 17 may add helpers.

**Project side** (each schema `p_<projectId>` for `main`, `d_<databaseId>` for the rest, owned by role `p_<projectId>`): your tables, each with `id uuid PRIMARY KEY DEFAULT uuidv7()`, `created_at timestamptz NOT NULL DEFAULT now()`, `updated_at timestamptz NOT NULL DEFAULT now()`, your columns, single column UNIQUE constraints, and the `orvano_updated_at` trigger. Nothing about tables, columns, or rows is copied into `orvano`.

**Relationships**: project 1:N databases (1 synthesized `main` plus 0 to 10 rows); database 1:1 schema; schema 1:N tables (at most 500 through the API); table 1:N columns (at most 200 user columns through the API); table 1:N rows.

**Limits** (constants in `Domain/`, no settings): 10 extra databases, 500 tables per database, 200 user columns per table, 1 MB request body, 100 rows per batch, filter 4 KB, 50 conditions, depth 3, 100 `in` values, 3 sort columns, page 1 to 100.

### State transitions

Database: `provisioning` → `active` (job succeeds) · `provisioning` → `failed` (job out of attempts; stored as `provisioning` plus `provision_failed_at`) → `provisioning` (retry) · any state → gone (delete). `main` is always `active` while its project is.

### Query language

```
filter     := condition-list
condition-list := expr ("," expr)*                     top level and group items are ANDed
expr       := group | condition
group      := ("and" | "or") "(" condition-list ")"   at most 3 levels deep
condition  := column "." op [ "." value ]
column     := [A-Za-z_][A-Za-z0-9_]*
op         := eq | ne | gt | gte | lt | lte | in | nin | isnull | notnull | contains | startswith | endswith
value      := literal | "(" literal ("," literal)* ")"    the list form for in and nin only
literal    := quoted | bare
quoted     := '"' ( [^"\\] | '\\"' | '\\\\' )* '"'
bare       := [^,()"]+                                  everything up to the next , or ) (dots allowed)
sort       := ["-"] column ("," ["-"] column){0,2}
select     := column ("," column)*
```

Parsing rules: no whitespace is skipped anywhere (a space is part of a bare value); the server parses the URL decoded string and error offsets count in it, and builders percent encode the whole value. `in` and `nin` require the list form, and `()` is an error; a missing value (`title.eq.`) is an error, and empty text is `""`. Inside quotes only `\"` and `\\` are escapes (a backslash before anything else is an error); in a bare value a backslash is literal. `isnull` and `notnull` take no value (a trailing `.value` is an error). Literals per type: boolean exactly `true` or `false`; `integer` and `bigint` an integer token in range; `float` and `decimal` a decimal or exponent token, never `NaN`; timestamp and date as AC-16's input; uuid in any case. Column names match the cache case sensitively; a column named `and` or `or` is told apart from a group by what follows it (`.` versus `(`). `other` columns are not filterable or sortable. There is no null literal: `eq.null` compares with the text `null`. A literal is parsed by the column's type (`true`/`false` for boolean, ISO 8601 for timestamp and date, a uuid, a number, else text). Example: `filter=status.eq.open,or(owner.eq.6f1c...,shared.eq.true),title.contains."a, b"&sort=-created_at,title&select=id,title`. The parser is plain C# in `Domain/` (no dependency), produces a typed tree, and the SQL builder turns the tree into a parameterized `WHERE`, `ORDER BY`, and keyset predicate. Mixed sort directions use the expanded form (`a > $1 OR (a = $1 AND b < $2) ...`), with explicit `IS NULL` branches for nullable sort columns.

### API surface

Project from `X-Orvano-Project` as everywhere. Base: `/v1/databases`. `{database}` is a slug, `{table}` and `{column}` are names, `{rowId}` is a uuid.

| Operation | Method and path | Key inputs | Key outputs | Auth (scope) | Key errors |
|---|---|---|---|---|---|
| `databases.list` | GET `/v1/databases` | cursor, limit | `{ items: Database[], nextCursor }` | key (`tables.read`) | 403 |
| `databases.get` | GET `/v1/databases/{database}` | | `Database` | key (`tables.read`) | 404 `database_not_found` |
| `databases.create` | POST `/v1/databases` | slug, name | 201 `Database` (`provisioning`) | key (`tables.write`) | 400, 409 `name_taken`, `schema_limit_reached` |
| `databases.update` | PATCH `/v1/databases/{database}` | slug?, name? | `Database` | key (`tables.write`) | 404, 409 `name_taken`, `database_protected` |
| `databases.delete` | DELETE `/v1/databases/{database}` | | 204 | key (`tables.write`) | 404, 409 `database_protected`, `table_busy` |
| `tables.list` | GET `.../{database}/tables` | cursor, limit | `{ items: Table[], nextCursor }` | key (`tables.read`) | 404, 409 `database_not_ready` |
| `tables.get` | GET `.../tables/{table}` | | `Table` | key (`tables.read`) | 404 `table_not_found` |
| `tables.create` | POST `.../{database}/tables` | name, columns: ColumnInput[] | 201 `Table` | key (`tables.write`) | 400, 409 `name_taken`, `schema_limit_reached`, `table_busy` |
| `tables.update` | PATCH `.../tables/{table}` | name | `Table` | key (`tables.write`) | 404, 409 `name_taken`, `table_busy` |
| `tables.delete` | DELETE `.../tables/{table}` | | 204 | key (`tables.write`) | 404, 409 `table_in_use`, `table_busy` |
| `columns.create` | POST `.../tables/{table}/columns` | ColumnInput | 201 `Table` | key (`tables.write`) | 400, 404, 409 `name_taken`, `column_needs_default`, `schema_limit_reached` |
| `columns.update` | PATCH `.../columns/{column}` | name?, required?, unique?, default? | `Table` | key (`tables.write`) | 400, 404 `column_not_found`, 409 `column_has_nulls`, `column_has_duplicates` |
| `columns.delete` | DELETE `.../columns/{column}` | | 204 | key (`tables.write`) | 400, 404, 409 `table_in_use`, `table_busy` |
| `rows.list` | GET `.../tables/{table}/rows` | filter?, sort?, select?, cursor?, limit? | `{ items: Row[], nextCursor }` | none, bearer, key (`rows.read`) | 400 `invalid_query`, `invalid_cursor`, 403 `table_access_denied`, 409 `table_not_writable` (no PK), 422 `query_timeout` |
| `rows.count` | GET `.../tables/{table}/count` | filter? | `{ count: int64 }` | none, bearer, key (`rows.read`) | 400, 403, 422 |
| `rows.get` | GET `.../rows/{rowId}` | select? | `Row` | none, bearer, key (`rows.read`) | 403, 404 `row_not_found`, 409 `table_not_writable` |
| `rows.create` | POST `.../tables/{table}/rows` | Row | 201 `Row` | none, bearer, key (`rows.write`) | 400 `invalid_row`, 403, 409 `row_conflict`, 413 |
| `rows.createMany` | POST `.../tables/{table}/rows/batch` | `{ rows: Row[] }` | 201 `{ items: Row[] }` | none, bearer, key (`rows.write`) | 400, 403, 409, 413 |
| `rows.update` | PATCH `.../rows/{rowId}` | Row, ifUpdatedAt? (query) | `Row` | none, bearer, key (`rows.write`); `x-orvano-idempotent` | 400, 403, 404, 409 `row_changed`, `row_conflict` |
| `rows.delete` | DELETE `.../rows/{rowId}` | ifUpdatedAt? (query) | 204 | none, bearer, key (`rows.write`) | 403, 404, 409 `row_changed` |

Console mirrors live under `/v1/console/project/databases/...` with the same shapes and errors, need the console session, and add `consoleDatabases.retry` (POST `.../{database}/retry`, 202). Their names: `consoleDatabases.{list,get,create,update,delete,retry}`, `consoleTables.{list,get,create,update,delete}`, `consoleColumns.{create,update,delete}`, `consoleRows.{list,get,count,create,update,delete}`.

**Models**: `Database { id, slug, name, status: DatabaseStatus (provisioning | active | failed), main: boolean, createdAt: utcDateTime | null }` · `Table { name, database, columns: Column[], readable, writable, estimatedRows: int64 | null }` · `Column { name, type: ColumnType, pgType, required, unique, default: ColumnDefault | null, system, writable }` · `ColumnInput { name, type, required?, unique?, default? }` · `ColumnDefault { kind: DefaultKind (value | now | uuidv7 | random_uuid | expression), value: string | null }` · `ColumnType` values `text`, `integer`, `bigint`, `float`, `decimal`, `boolean`, `timestamp`, `date`, `uuid`, `json`, `text[]`, `integer[]`, `uuid[]`, `other` · `Row` (open map) · `RowBatch { rows: Row[] }` · `RowList { items: Row[] }` · `RowCount { count: int64 }` · `ProblemField { field, message }`.

**New `ApiKeyScope` values**: `rows.read`, `rows.write`, `tables.read`, `tables.write`.

**New error codes**: `database_not_found` (404), `table_not_found` (404), `column_not_found` (404), `row_not_found` (404), `database_not_ready` (409), `database_protected` (409), `name_taken` (409), `schema_limit_reached` (409), `table_busy` (409), `table_in_use` (409), `table_not_writable` (409), `column_needs_default` (409), `column_has_nulls` (409), `column_has_duplicates` (409), `row_conflict` (409), `row_changed` (409), `invalid_query` (400), `invalid_row` (400), `table_access_denied` (403), `query_timeout` (422), `request_too_large` (413). Each gets a fix page.

**Events**: `databases.database.{created,updated,provisioned,failed,deleted}`, `databases.table.{created,renamed,deleted}`, `databases.column.{added,updated,dropped}`, `databases.row.{created,updated,deleted}`.

### Screens

| Route | Shows | Actions |
|---|---|---|
| `/projects/$projectId/databases` | Databases table: slug, name, status badge | New database, rename, delete (typed slug), retry (failed) |
| `/projects/$projectId/databases/$database` | Tables: name, estimated rows, Read only badge; Load more | New table (column builder) |
| `.../databases/$database/tables/$table?tab=rows` | Row grid, filter panel, sort, Load more, Count rows, client access banner | New row, edit row (sheet), delete row, copy as code |
| `.../databases/$database/tables/$table?tab=columns` | Columns with type, rules, default, System badge | Add, edit, drop (typed name); header menu: rename table, delete table (typed name) |

Console code sits beside these routes (`-databases/` folders), built from spec 0005's `DataTable`, dialogs, and sheet, with TanStack Table for the grid.

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Every call | the project | `X-Orvano-Project` through `IProjectDirectory.GetServableAsync` (spec 0003) |
| Every row call | the caller (anonymous, app user, key) | `IAppCallerResolver` from `Authorization` or `X-Orvano-Key` |
| Every row call | allowed or not | key callers: the key's scopes; others: `IRowAccess.DecideAsync` |
| Every call with `{database}` | the schema name | `main` → `ProjectScope.RoleName(projectId)`; else `db_databases.schema_name` by `(project_id, slug)`, cached with the structure |
| Every table and row call | columns, types, PK, unique constraints, defaults | structure cache, loaded from `pg_catalog` (`pg_class`, `pg_attribute`, `pg_constraint`, `pg_attrdef` with `pg_get_expr`), for one schema |
| Structure cache | when to reload | `LISTEN orvano_schema` payload (project ID), the local drop after this instance's own change, 60 second time to live, a valid but unknown name (at most once per schema per second), or Postgres `42703`/`42P01` |
| `Table.estimatedRows` | the estimate | `pg_class.reltuples` (negative → null) |
| `Table.readable`, `writable` | the flags | derived from the cached PK: standard `id uuid` PK → both true; other PK → readable only; none → neither |
| `Column.type` | the API type | `pg_attribute.atttypid` mapped by the table in AC-5 (`int4` → `integer`, `_text` → `text[]`, and so on), else `other` |
| `Column.default` | kind and value | `pg_get_expr(adbin)`: `now()` → `now`, `uuidv7()` → `uuidv7`, `gen_random_uuid()` → `random_uuid`, a cast literal → `value` (the literal text), else `expression` |
| `Column.system` | true or false | name is `id`, `created_at`, or `updated_at` on a writable table |
| `rows.create` | `id`, `created_at`, `updated_at` | input `id` or `uuidv7()`; `now()` defaults |
| `rows.update` | new `updated_at` | the `orvano_updated_at` trigger (`now()`, the transaction start) |
| `ifUpdatedAt` guard | the expected value | query param, compared with `updated_at = $n::timestamptz`; SDKs pass the string from the row unchanged (never through a JS `Date`, which drops microseconds) |
| `rows.list` | the next cursor | the last returned row's sort values and tie breaker (fetched even when `select` omits them, then dropped), plus SHA-256 of `filter` and `sort`, base64url JSON |
| `rows.count` | the count | `SELECT count(*)` with the same `WHERE` |
| `databases.list` | order and cursor | `main` first, then `db_databases.slug` ascending; cursor = the last slug |
| `Column.unique` | true or false | `pg_constraint` (`contype = 'u'`, one entry in `conkey`) or a unique `pg_index` on exactly that column |
| Value default in DDL | the SQL literal | `SELECT quote_literal($1)` after `SELECT $1::<pgtype>` succeeds |
| Row and table events | `actor` | `AppCaller` (`app_user` with the user ID, `api_key` with the key ID) or the console user (`user`) |
| Row events | `database` | `db_databases.id`, or `"main"` |
| Row events | `columns` | create: every non null column in the returned row; update: the keys sent; delete: empty |
| `databases.create` | `id`, `schema_name` | 20 random `[a-z0-9]` characters (the project ID generator); `'d_' \|\| id` |
| Provision job | the owning role | `p_` + `db_databases.project_id` |
| `column_has_nulls` detail | the count | `SELECT count(*) WHERE col IS NULL` before the `ALTER` |
| `column_has_duplicates` detail | the count | `SELECT count(*) FROM (SELECT col FROM t WHERE col IS NOT NULL GROUP BY col HAVING count(*) > 1)` |
| `column_needs_default` | whether rows exist | `SELECT EXISTS (SELECT 1 FROM t)` in the same transaction |
| `row_conflict` detail | the column | the violated constraint name → its columns, from the cache |
| `query_timeout`, `table_busy`, `table_in_use` | which error | Postgres `57014`; `55P03` or `40P01`; `2BP01` |
| Console banner and "Read only" note | the reason | `Table.writable` and `readable`; the banner is shown on every table in this row |
| Copy as code | the snippet | the builder lines generated in the console from the current filter and sort, matching AC-29's test vectors |

### Key invariants

- Project data is touched only inside `ProjectScope` as `p_<projectId>`; the `orvano` side (events, NOTIFY, `db_databases`) is written after `SET LOCAL ROLE NONE` in the same transaction, never by the project role.
- No SQL identifier comes from input text: every table and column name in SQL comes from the structure cache and is double quoted; every value is a parameter, except a column default in DDL, which is a literal Postgres itself quoted (`quote_literal`) after a cast check.
- Every platform statement in `afterAsApp` is schema qualified (`orvano.events`), because `search_path` still points at the project schema.
- No Postgres error text (`MessageText`, `Detail`) reaches a response; problem bodies name fields, columns, and constraints only.
- Structure has one source of truth, `pg_catalog`; no table or column metadata is stored in `orvano`.
- A schema change, its event, and its NOTIFY commit together or not at all.
- A row change and its event commit together or not at all; no event carries a value.
- `main` always maps to `p_<projectId>` and is never renamed or deleted; every other schema is `d_<databaseId>` and owned by `p_<projectId>`.
- A non key caller is denied before any database or table lookup, so denial reveals nothing.
- `api` never runs `CREATE SCHEMA`; only the worker's provision job (as `orvano_admin`) does.

### Security model

- **Isolation**: Postgres enforces it (spec 0002): `orvano_app` reaches a project's schemas only through `SET LOCAL ROLE p_<id>`, which owns only that project's schemas.
- **Server**: API keys with `rows.read`, `rows.write`, `tables.read`, `tables.write`. One scope set covers every database in the project.
- **Clients and anonymous callers**: denied on every table in this row (`DenyClientRowAccess`), with no hint of what exists. Row 17 replaces this with rules.
- **Console**: owners and developers manage everything; viewers read structure and rows; install admins get no extra project access (spec 0003).
- **Injection**: the grammar has no SQL in it; identifiers are checked against the cache and quoted; values are parameters; `contains`, `startswith`, `endswith` escape `%`, `_`, and `\` and use `ILIKE ... ESCAPE '\'`. Fuzz tested (AC-25).
- **Resource use**: statement and lock timeouts, body, filter, batch, and page caps. No new rate limit in this row (API key failures stay limited by spec 0014).
- **Personal data**: rows may hold personal data. It never reaches logs, events, metrics, or problem details (a `detail` names fields and columns, never values).

### Configuration required

No new `ORVANO_*` setting. The api role gains one dedicated `LISTEN orvano_schema` connection (outside the pool, reconnecting with backoff from 1 to 30 seconds and clearing the whole cache after a reconnect), so spec 0002's connection budget grows by one per api instance.

### Critical test scenarios

- Happy path: create a table in the console, add a column from the .NET SDK, then create, filter, sort, page, update, and delete rows from every server SDK, with the new column usable in the very next call, verifies **AC-5**, **AC-8**, **AC-10**, **AC-11** to **AC-19**, **AC-30**.
- Pagination: for each sort shape (one column, mixed directions, a nullable column), walking all pages of a seeded table equals one unpaged ordered query, with inserts during the walk causing no repeats, verifies **AC-18**.
- Freshness: two api instances; add a column through one and use it through the other at once; add one with `psql` and use it (self heal), verifies **AC-10**.
- Concurrency: an update with a stale `ifUpdatedAt` gets 409 `row_changed`; a column change during a long row transaction gets 409 `table_busy` within about 3 seconds, verifies **AC-9**, **AC-14**.
- Failure: a heavy filter hits 422 `query_timeout` and is not retried by the SDK; a unique violation gets 409 `row_conflict` naming the column; a batch with one bad row inserts nothing, verifies **AC-12**, **AC-20**.
- Existing data: making a column required with nulls, unique with duplicates, or adding a required column with no default to a non empty table each get their 409 and change nothing, verifies **AC-8**.
- Auth: an anonymous caller and a signed in app user get 403 `table_access_denied` for an existing and a missing table alike; a key without `rows.write` gets 403 `insufficient_scope`; a viewer gets 403 `forbidden` on every console change; a key for project A with header B gets 401, verifies **AC-22**, **AC-24**, **AC-25**.
- Injection: the fuzz run and hand written cases (quotes, `)` and `,` in values, unicode, a column named like a keyword) produce only documented answers, verifies **AC-17**, **AC-25**.
- Databases: create, wait for `active`, use, rename (old slug 404), delete (schema gone); a forced job failure shows `failed` and Retry works; purging the project drops every `d_` schema, verifies **AC-2** to **AC-4**.
- Events: each row and schema change writes exactly its events with no value inside; a rolled back change writes none, verifies **AC-26**, **AC-27**.

## Build plan

Tracer Bullet: the first slice pushes one table made in the console through a row created and listed from the .NET server SDK, end to end through the contract, server, console, and a scenario; later slices thicken the query language, schema changes, named databases, callers, every SDK, and the console.

1. **Thin thread: a console table, rows from the .NET SDK.** `Orvano.Databases` csproj (and its csproj copy in `deploy/server.Dockerfile`), `DatabasesModule` in `OrvanoModules.Product`; migration `0011_databases.sql` (`orvano_fn`, `set_updated_at()`, `db_databases`) and `DatabasesDbContext` in the drift check; `ProjectScope` gains `schema` and `afterAsApp` with unit and integration tests; the structure cache with its catalog loader and type mapping (no NOTIFY yet, time to live only); `main` synthesized; `Domain/` name rules (with a test that the reserved word list matches `pg_get_keywords()` on Postgres 18); `tables.create`, `tables.get` (all column types and defaults), `rows.create`, `rows.list` (default sort, `limit`, keyset cursor on `created_at, id`); key callers with the four new scopes; the contract (`contract/databases/`), `x-orvano-dynamic` and multi scheme auth in SdkGen, regenerate; console Databases nav, `main` tables list, New table dialog, Rows grid (read only, Load more); a .NET scenario. Satisfies **AC-1**, **AC-5**, **AC-6**, **AC-11**, **AC-16**, **AC-25**, **AC-28**.
2. **The full row API.** `rows.get`, `update` (with `ifUpdatedAt`), `delete`, `createMany`, `count`; every wire form and `invalid_row` with `Problem.errors`; Postgres error mapping, statement timeouts, the 1 MB body limit; row events through `afterAsApp`. Satisfies **AC-11** to **AC-16**, **AC-19**, **AC-20**, **AC-26**.
3. **Query language.** The grammar parser and typed tree in `Domain/` with unit tests (every operator, quoting, depth and size limits, error offsets); the SQL builder (`WHERE`, `ILIKE` escaping, `ORDER BY`, expanded keyset predicates with null branches); `sort`, `select`, cursor binding to filter and sort; the pagination property test against unpaged queries; the fuzz test. Satisfies **AC-17**, **AC-18**, **AC-25**.
4. **Schema changes and freshness.** `tables.update` and `delete`, `columns.create`, `update`, `delete` with AC-8's checks; lock and statement timeouts; DDL events and `pg_notify('orvano_schema')` in the same transaction; the `RegisterNotifications` module hook and the api's listener, invalidation, and the local drop after commit; schema advisory locks; the reload on unknown names; self heal on `42703`/`42P01`; odd tables (`readable`, `writable`, PK tie breaker). Satisfies **AC-7** to **AC-10**, **AC-21**, **AC-27**.
5. **Named databases.** `databases.*` operations; the `databases.database.provision` job, failure and `consoleDatabases.retry`; delete with `DROP SCHEMA ... CASCADE`; slug resolution in the cache; the Platform purge change to `DROP OWNED BY`, and the `databases.project.purge` consumer and job. Satisfies **AC-2**, **AC-3**, **AC-4**.
6. **Callers and access.** `IAppCallerResolver` in Auth `Contracts/`; `IRowAccess` with `DenyClientRowAccess`; the check order of AC-22 with the access check before lookups; `TestRowAccess`, the `tables` fixtures, and the startup refusal outside `Test`; console mirror operations with `IConsoleAccess` and the viewer rules. Satisfies **AC-22**, **AC-23**, **AC-24**.
7. **Every SDK.** The query builder and filter helpers in `@orvano/js`, `orvano_core`, and .NET, with the shared test vectors file and typed rows; shared scenarios for every server and client surface (Node, Bun, Deno, Chromium, workerd, Next.js, Dart, Flutter, .NET). Satisfies **AC-29**, **AC-30**.
8. **Console, complete.** Databases page (create, rename, delete, retry, provisioning refresh), tables list, the table page's Rows tab (filter builder, copy as code, sort, count, row sheet with the conflict flow, delete, banners) and Columns tab (add, edit, drop, rename and delete table, typed confirms); viewer states; catalog examples; browser tests with axe; Playwright flows in the `console` profile. Satisfies **AC-31** to **AC-35**.
9. **Docs and operations.** The three docs pages, the console page, every error fix page; logs and OpenTelemetry metrics with a test that no value or filter string reaches a log line or event. Satisfies **AC-36**, **AC-37**, and re checks **AC-1** to **AC-37** end to end.

## Consequences

**Positive**:
- Real tables in real schemas: everything Postgres offers (SQL, indexes, `pg_dump`) works on your data, and row 18's SQL editor and row 19's migrations build on the same catalog with no sync step.
- One query language across the API, three SDK languages, and the console, with the console teaching it through "Copy as code".
- No new process, extension, or dependency: the parser and SQL builder are a small amount of plain C#.
- Nothing can be public by accident: clients are refused until row 17 exists, and the denial leaks no names.
- Events for every API write, ready for realtime, webhooks, and functions to consume.

**Negative / tradeoffs**:
- Until row 17 ships, client SDKs can't use the data API outside `Test`; the client half of the "done when" is proven only against fixture tables, and the row is not truly usable by browser and mobile apps until row 17 lands in the same version.
- Writes made in SQL emit no events, so realtime and webhooks (rows 22, 31) won't see them unless row 22 adds trigger or logical decoding capture.
- `ALTER TABLE ... ADD CONSTRAINT UNIQUE` blocks writes while it builds its index; on a big table, marking a column unique can hold writes for a while (only the wait to start is capped at 3 seconds). Row 18 can add a concurrent index path.
- A rename breaks every client that uses the old table or column name; there are no aliases.
- The keyset predicate for mixed directions and nullable sort columns is intricate SQL; it is the most likely place for an ordering bug, so it carries a property test.
- `created_at` is the transaction start time, so a long transaction can insert a row that sorts before rows already read in a walk (it is then missed by that walk, never repeated).
- Every api instance holds one more Postgres connection for `LISTEN`.
- `DROP OWNED BY ... CASCADE` in the purge also drops objects other roles made that depend on the project's objects (a view an admin built in `public`), and revokes the role's grants. Acceptable for a purge, and documented.
- The NOTIFY path is strictly needed only for AC-3 (an old slug answering 404 at once) and for listings and flags staying fresh; the self heal on unknown names covers new columns on its own.
- Exact counts can be slow on large tables and may hit the timeout; there is no estimate mode yet beyond `Table.estimatedRows`.
- 21 new error codes, each needing a fix page.
- `x-orvano-dynamic`, multi scheme auth, and `Problem.errors` are three SdkGen and contract changes that every SDK must follow.

**Neutral**:
- A new module (`Orvano.Databases`), a new contract folder (`contract/databases/`), a new platform migration, and a shared function schema `orvano_fn`.
- `ProjectScope` gains a schema and an after step, and the Platform purge switches to `DROP OWNED BY`: small changes to specs 0002 and 0003.
- Auth exposes `IAppCallerResolver`, so later modules (storage, functions) can accept the same three callers.

## Follow-up

- [ ] Row 17 (row level permissions and app teams): implement `IRowAccess` (likely growing it to return a row predicate or to set request level settings for Postgres RLS), decide the anonymous caller's rules, replace this row's client banner, and lift AC-22's blanket denial. The client half of row 16's "done when" closes there.
- [ ] Row 18 (SQL and table editor): type changes, relationships, indexes (including a concurrent unique path), enums, full text search, inline grid editing; the SQL editor's DDL must send `pg_notify('orvano_schema', <projectId>)`.
- [ ] Row 19 (CLI, migrations, type generation): per project migrations must send the same NOTIFY; generated row types plug into the builders' `T`, `fromJson`, and `JsonTypeInfo<T>`.
- [ ] Row 22 (realtime): decide whether to capture SQL writes (triggers or logical decoding) and how to fetch rows per subscriber under row 17's rules; row events here carry no values on purpose.
- [ ] Spec 0002: amend the connection budget (one `LISTEN orvano_schema` connection per api) and the `ProjectScope` paragraph (schema parameter, `afterAsApp`), and note `orvano_fn`, when slice 1 and 4 ship.
- [ ] Spec 0003: amend the purge job (`DROP OWNED BY`) and the `ApiKeyScope` catalog, when slice 5 ships.
- [ ] Spec 0001 and `contract/AGENTS.md`: record `x-orvano-dynamic`, multi scheme `@useAuth` with `x-orvano-scope`, and `Problem.errors`.
- [ ] Quickstarts (`examples/`): add a data step once row 17 lets client apps read a table.
- [ ] `/sync`: add `server/src/Orvano.Databases/AGENTS.md` and a line for it in `server/AGENTS.md` and root `AGENTS.md`.
