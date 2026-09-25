# Epic: Databases

Postgres first: real tables and SQL, an auto generated data API, and row level permissions tied to Orvano auth. See [index.md](index.md) for the full plan.

### 16. Tables, rows & data API · needs a decision · GA
Create a table in the console, then create, read, update, delete, filter, sort, and paginate rows from every SDK through an auto generated API. Decides the API shape and query language.
**Done when:** a table made in the console is instantly usable from all SDKs with filters and pagination, and new columns appear without a restart.
- [ ] Design it (spec): `/architect tables, rows & data API`

### 17. Row level permissions & app teams · needs a decision · GA
Permission rules per table (and per row) based on the signed in user, their teams, and their roles, enforced on every API call. App teams let users group together, invite members, and share data.
**Done when:** a user reads only rows the rules allow, team members share team rows, and server SDKs with an API key can act beyond the rules on purpose.
- [ ] Design it (spec): `/architect row level permissions & app teams`

### 18. SQL & table editor · needs a decision
A spreadsheet style table editor, relationships, indexes, full text search, and a SQL editor with saved queries in the console.
**Done when:** you can model related tables, add an index, run a SQL query, and edit rows inline, with changes recorded so the CLI can pull them as a migration.
- [ ] Design it (spec): `/architect SQL & table editor`
