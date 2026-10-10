# Epic: Databases

Postgres first: real tables and SQL, an auto generated data API, and row level permissions tied to Orvano auth. See [index.md](index.md) for the full plan.

### 16. Tables, rows & data API · in-progress · GA
Create a table in the console, then create, read, update, delete, filter, sort, and paginate rows from every SDK through an auto generated API. Decides the API shape and query language.
**Done when:** a table made in the console is instantly usable from all SDKs with filters and pagination, and new columns appear without a restart.
- [x] Design it (spec): `/architect tables, rows & data API`
- [ ] Build it: `/develop tables, rows & data API`
   - [ ] Thin thread, a console table to rows from the .NET SDK: the module, migration `0011`, `ProjectScope` schema and `afterAsApp`, the structure cache, `main`, `tables.create`, `rows.create` and `list`, the contract and SdkGen changes, the first console pages, and a .NET scenario (AC-1, 5, 6, 11, 16, 25, 28)
   - [ ] Rows and queries: the full row API with wire forms, error mapping, timeouts, and row events, then the filter grammar, sort, select, keyset cursors, the pagination property test, and the fuzz test (AC-11 to 20, 25, 26)
   - [ ] Schema changes, named databases, and callers: column and table changes with advisory locks, NOTIFY and self heal, odd tables, the provision job and purge change, `IAppCallerResolver`, `IRowAccess`, the `Test` opener, and the console mirror operations (AC-2 to 4, 7 to 10, 21 to 24, 27)
   - [ ] Every SDK and the full console: query builders in JS, Dart, and .NET with shared test vectors, scenarios on every surface, and the Databases, tables, Rows, and Columns screens with axe and Playwright (AC-29 to 35)
   - [ ] Docs and operations: the three docs pages, the console page, every error fix page, logs and metrics with the no values check (AC-36, 37)
- [ ] Verify it: `/check verify tables, rows & data API`
- [ ] Test it: `/test tables, rows & data API`
- [ ] Review it (fresh model): `/check review tables, rows & data API`
- [ ] Document it: `/document tables, rows & data API`
Spec [0015](../specs/0015-tables-rows-data-api/index.md) · code in `server/src/Orvano.Databases/`, `server/src/Orvano.Core/`, `contract/databases/`, `tools/sdkgen/`, `sdks/`, `console/`, `tests/scenarios/`, `website/`

### 17. Row level permissions & app teams · needs a decision · GA
Permission rules per table (and per row) based on the signed in user, their teams, and their roles, enforced on every API call. App teams let users group together, invite members, and share data.
**Done when:** a user reads only rows the rules allow, team members share team rows, and server SDKs with an API key can act beyond the rules on purpose.
- [ ] Design it (spec): `/architect row level permissions & app teams`

### 18. SQL & table editor · needs a decision
A spreadsheet style table editor, relationships, indexes, full text search, and a SQL editor with saved queries in the console.
**Done when:** you can model related tables, add an index, run a SQL query, and edit rows inline, with changes recorded so the CLI can pull them as a migration.
- [ ] Design it (spec): `/architect SQL & table editor`
