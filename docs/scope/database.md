# Scope: Database

Typed, Postgres native tables with an automatic REST API, migrations and a table editor. Part of the [Orvano scope](index.md). The permission engine these features run under is feature 29 in [Platform core](platform.md).

## M3 (Phase 1: Foundation)

### 30. Tables & migrations · needs a decision
Tables are real Postgres tables, and every schema change is a migration you can preview before it runs.
**Done when:** a table lives in the project schema with automatic `id`, `created_at` and `updated_at`; the 9 column types (the file type is wired up in M4), single, composite and unique indexes, and one to many relations work; every schema change has a plan (SQL preview, destructive change warning) and an apply; a drop needs explicit confirmation.
- [ ] Design it (spec): `/architect tables & migrations`

### 31. Rows API · needs a decision · Beta
The automatic REST API for each table. It turns caller input into SQL, so it gets the stricter tier.
**Done when:** each table has endpoints for list, get, create, update, delete and bulk insert; filters (`eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `in`, `contains`, `is_null`), sort, `select` with relation expansion one level deep, cursor pagination and an optional total count work; every query is parameterized and runs under RLS; `owner_id` is set on create.
- [ ] Design it (spec): `/architect rows api`

### 32. Server transactions · needs a decision
Several writes that succeed or fail together, for server keys and functions only.
**Done when:** `POST /v1/db/transactions` runs several writes atomically; a failed step rolls back all of them; client tokens are refused.
- [ ] Design it (spec): `/architect server transactions`

### 33. Table editor · needs a decision
The spreadsheet style page for browsing and editing rows, the console's most used screen.
**Done when:** the console shows the table list and a row grid with filter, sort, paging, insert, edit and delete, matching the `TableEditor` mockup; relation and enum cells render properly; empty and error states show.
- [ ] Design it (spec): `/architect table editor`

### 34. Schema, permissions & migrations pages · needs a decision
The console tabs for changing a table's shape and rules, with every change shown as a migration first.
**Done when:** the console can add and change columns, indexes and table rules, each shown as a migration plan before it applies; the migration history lists every migration with its diff; a destructive change asks the person to type the name.
- [ ] Design it (spec): `/architect schema, permissions & migrations pages`

### 35. Read only SQL console · needs a decision
A place to run queries against a project without any risk of changing data.
**Done when:** a member can run `SELECT` statements in a read only transaction with a statement timeout; anything that writes is refused; results show in a grid.
- [ ] Design it (spec): `/architect read only sql console`

### 37. Typed models & SDK queries · needs a decision
Generated row types and the chained query style in the SDKs, so app code is typed end to end.
**Done when:** `orvano types --lang dart|ts` writes row types from the live schema; the TypeScript and Dart SDKs offer the chained query style from the spec (`db.from('posts').select(...).eq(...)`), fully typed; the samples compile in CI.
- [ ] Design it (spec): `/architect typed models & sdk queries`

## Deferred
- **Per row permission lists**: rules stored on each row
- **Aggregations**: counts, sums and groups through the API
- **Many to many helpers**: beyond a plain join table
- **Vector and full text search**: similarity and text queries
- **Client side transactions**: several writes from an app in one go
- **Read replicas**: extra databases for reads
- **Branching**: a copy of a database per branch
