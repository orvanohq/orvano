# 0015. Rationale: tables, rows, and the data API

The decision record behind [index.md](index.md). `/develop` builds from the index; this file explains why.

## Context

Scope row 16 opens the Databases epic (v0.4): create a table in the console, then create, read, update, delete, filter, sort, and paginate rows from every SDK through a generated API, with new columns appearing without a restart. It is tagged GA, so it gets a fresh model review and docs in the same change. Row 17 (row level permissions and app teams) ships in the same version but has its own spec, and rows 18, 19, and 22 (SQL editor, migrations and type generation, realtime) build on whatever this row decides.

Much is already fixed. Spec 0002 gives each project one Postgres schema and one `NOLOGIN` role (`p_<id>`), reached only through `ProjectScope`, with raw Npgsql and a small SQL builder for per project tables, no Postgres extensions, and a transactional outbox for events. Spec 0003 gives scoped API keys and a 20 character project ID (the column allows 60). Spec 0001's contract allows only primitive query parameters and no unions, and SdkGen generates five SDK surfaces from it; Spec 0004 gives app users bearer access tokens. The `orvano_app` role has no DDL rights on the platform schema and no `CREATE` on the database; only `migrate` and `worker` hold `orvano_admin`.

The forces: Orvano is "Postgres first", so data must live in real tables that SQL, `pg_dump`, and later tools see as they are. The API must be expressible in the contract so SdkGen can generate it, and the query language must work identically from TS (in browsers and Workers), Dart (including Flutter web), and .NET (including `netstandard2.0`). The server is small (2 CPU, 4 GB), runs one image, and adds no infrastructure. Row 17 has not decided its enforcement model, yet this row must ship no path by which a table is public by accident. And table structure will also change from outside this API (row 18's SQL editor, row 19's migrations, an admin in `psql`), which the API must notice without a restart.

Not deciding means rows 17, 18, 19, and 22 each invent their own idea of what a table, a row, a filter, and a row event are.

## Options considered

### Option 1: REST over real tables, one filter string, handwritten builders (chosen)

Real tables in the project's schemas, structure read live from `pg_catalog`, a resource style REST API (`/v1/databases/{database}/tables/{table}/rows`) whose list takes one `filter` string in a small published grammar plus `sort`, `select`, `cursor`, and `limit`, with a handwritten query builder per SDK language producing that string. All in a new `Orvano.Databases` module in plain C#.

**Pros**:
- Fits the contract as it is: every parameter is a primitive string, so SdkGen generates every operation.
- No new process or extension; the parser and SQL builder are small and fully testable.
- One grammar for the wire, docs, console, and three SDKs, debuggable with `curl`.
- The catalog as the only source of truth means SQL made changes appear with no sync.

**Cons**:
- A grammar and a keyset SQL builder to write and maintain ourselves, including tricky null and mixed direction ordering.
- Three handwritten builders to keep in step (mitigated by shared test vectors).
- Less expressive than PostgREST on day one (no embedded relations, no JSON paths).

### Option 2: Run PostgREST beside the server

Ship PostgREST (a mature Haskell server that turns a Postgres schema into a REST API) as a sidecar, pointed at the project schemas, with Orvano minting the JWTs it expects.

**Pros**:
- Battle tested query language with relations, JSON paths, and RPC.
- Little query code of our own.

**Cons**:
- A new process and language in the image or compose file, against spec 0002's one image, three services shape.
- PostgREST serves one set of schemas from one config and expects Postgres RLS and JWT roles; multi project routing, our scoped API keys, our problem details, and our events would all need a proxy layer in front of it.
- Its dynamic, per column query parameters can't be described in the contract, so SdkGen can't generate the calls.

### Option 3: Documents in a JSON column (Appwrite style)

One shared table per project storing each row as a `jsonb` document with a collection name, validated against an Orvano held schema.

**Pros**:
- No DDL at runtime: adding an attribute is a metadata change.
- No catalog cache or NOTIFY.

**Cons**:
- Not real tables: SQL, indexes, foreign keys, `pg_dump` readability, and type safety all degrade, which is the opposite of "Postgres first" and spec 0002's tenant model.
- Orvano must own a schema copy and its validation forever, and rows 18 and 19 inherit a document store.

### Option 4: GraphQL

A GraphQL endpoint generated from the catalog (with a .NET GraphQL server, since spec 0002 rules out the `pg_graphql` extension).

**Pros**:
- Flexible selection and nested queries in one round trip.

**Cons**:
- A schema that changes at runtime per project means rebuilding GraphQL schemas on every DDL, per instance.
- SdkGen generates REST from OpenAPI; GraphQL needs a second client stack in five SDKs.
- Query cost control (depth, breadth) becomes its own project, for a CRUD shaped need.

## Rationale

The deciding forces are the contract and Postgres first. SdkGen only generates what the contract can say, and the contract says primitive parameters and no unions; a single `filter` string is the only query shape that satisfies that without teaching SdkGen dynamic parameters (Option 2) or a second protocol (Option 4). Real tables (Options 1 and 2) are the product promise of spec 0002, which rules out documents (Option 3). Between Options 1 and 2, PostgREST's maturity does not outweigh running a second server that knows nothing about Orvano's projects, keys, errors, or events; the proxy we would need in front of it is most of Option 1 anyway, without the control.

Reading structure live from `pg_catalog` is what makes "new columns appear without a restart" true for every path that changes structure, including ones Orvano does not run, and it removes the drift class of bug entirely. The cost is a cache that must be invalidated, which NOTIFY (fast path), a time to live (backstop), and a self heal on "column does not exist" (correctness) cover between them.

The access stance is deliberately conservative because the tag is GA and row 17 is undecided: denying every non key caller, before any lookup, means no table is public by accident and no name leaks, while the seam (`IRowAccess`) is narrow enough that row 17 can implement RLS or application rules behind it. The engineer accepted that the client half of the "done when" is proven against `Test` fixtures until row 17 ships.

Named databases were the engineer's choice over the recommended one database per project. Mapping each to its own schema owned by the existing project role keeps spec 0002's isolation exactly as it is, and the 63 byte name limit forced `d_<databaseId>` for the extra schemas. Because the api cannot create schemas, creation is a worker job, mirroring project provisioning, rather than widening `p_<id>`'s privileges.

### Calls made while writing (recommended, not asked)

| Call | Pick | Why | Runner up |
|---|---|---|---|
| How `main` exists | Synthesized, no `db_databases` row | Every existing project gets it with no data migration, and it can never be out of step with `p_<id>` | Insert a row per project from a provisioning consumer |
| Count shape | Separate `rows.count` operation (`GET .../tables/{table}/count`) in place of `count=true` on list | The contract's list convention is exactly `items` plus `nextCursor` (SdkGen's iterator relies on it); a separate call keeps it, and the console's "Count rows" button maps to it directly | A `total` field on the list response, breaking the convention |
| Field errors | Optional `Problem.errors` (`{ field, message }[]`), `detail` still starting with the first field | The engineer asked for every bad field and batch indexes; additive to `Problem`, and existing clients keep reading `detail` | One field per problem in `detail` only, as specs 0009 and 0010 do |
| Who resolves callers | `IAppCallerResolver` in Auth `Contracts/` | Auth owns tokens and already wraps API key checks in `PublicRequests`; exposing one resolver keeps Databases out of Auth's internals and serves storage and functions later | Move `PublicRequests` into Core |
| Writing events as the project role | `ProjectScope` gains `afterAsApp` (`SET LOCAL ROLE NONE`, then platform work) | `p_<id>` has no rights on `orvano.events`; one transaction keeps row and event atomic; the role switch stays inside the one helper spec 0002 allows | A second transaction for the event (loses atomicity) |
| `updated_at` trigger home | One function in a shared `orvano_fn` schema | Your schemas hold only your tables, and one function is upgraded in one place; `USAGE` to `PUBLIC` exposes nothing sensitive | A copy of the function in every schema |
| Purge of extra schemas | `DROP OWNED BY p_<id> CASCADE` in Platform's purge | Platform need not know Databases exists; it drops every schema the role owns | Platform asking Databases for its schema list |
| Tables without the standard key | PK other than `id uuid` → readable only (list, count); no PK → neither | Keyset paging needs a unique tie breaker; addressing rows by arbitrary keys varies the ID type per table | Address rows by any single column PK |
| Tie breaker direction | `id` follows the last sort column's direction | Lets the common single direction case use a plain row comparison | Always ascending `id`, forcing the expanded predicate more often |
| Cursor binding | Hash of `filter` and `sort` in the cursor, not signed | Reuse with another query is caught as `invalid_cursor`; tampering only moves the start point within rows the caller may already read | An HMAC signed cursor (needs a key in every role) |
| Null literal | None; use `isnull` and `notnull` | Removes the ambiguity of the text `null` in text columns | A `null` keyword with a quoted escape for the text |
| `decimal` precision | Unconstrained `numeric` | Exact for any value; precision and scale can come with row 18's type changes | Ask for precision and scale per column |
| Database IDs and limits | 20 character ID like projects, 10 extra per project, 500 tables, 200 columns | Fits the 63 byte name limit, and caps catalog growth on a small server well below Postgres' own limits (1,600 columns) | No caps |
| Timeouts | 5 s public, 15 s console and DDL, 3 s lock wait | Short enough to protect a 2 CPU server, long enough for real queries on indexed tables | One 30 s timeout everywhere |
| Odd column names | Returned in rows but not filterable, sortable, selectable, or writable | The grammar's column token stays simple and unambiguous | Quoted column names in the grammar |
| Logging | Names, counts, durations, codes; never values, filter strings, or bodies | Rows hold personal data and filter strings carry values (emails); AGENTS.md forbids logging payloads | Logging filter shapes with values stripped |
| Rate limit | None new | The engineer's call: the in memory limiter is per instance and would throttle shared IPs, and row 17 plus the self hoster's proxy fit better | 600 requests per minute per IP for clients |

### Cross check (2026-10-10)

An independent read on another model found the design sound and named gaps, all applied with the engineer's approval: unknown names now trigger a cache reload (Postgres never sees them, so the `42703` path alone could not fire); column defaults are spliced as Postgres quoted literals, the one place a value is not a parameter; only reserved keywords (`R`, `T`) are refused; advisory locks make limits and names race free; problem bodies never carry Postgres error text, which can hold values; row calls get a lock timeout so a schema change answers `table_busy`, not `query_timeout`; plus the grammar's parsing rules, the cursor's contents, batch inserts one row at a time, provision and delete row locks, and an api notification hook (the api has no listener today).

### Evidence: what already exists

- `server/src/Orvano.Core/Data/ProjectScope.cs`: runs work as `p_<id>` with `search_path = p_<id>` for the whole transaction, so the outbox can't be written inside it today.
- `server/src/Orvano.Core/Paging/PageCursor.cs`: `(created_at, id)` cursors, `DefaultLimit = 25`, `MaxLimit = 100`; this row reuses the limit rule and adds its own cursor for arbitrary sorts.
- `server/src/Orvano.Auth/Endpoints/PublicRequests.cs`: internal `RequireUser`, `RequireApiKey`, `CurrentUser`, `CurrentKey`; no operation yet accepts both a bearer token and a key.
- `contract/platform/api-keys.tsp`: `ApiKeyScope` holds only `users.read` and `users.write`.
- `contract/errors.tsp`: `Problem` has `type`, `title`, `status`, `detail`, `code`, `requestId`; field errors are carried in `detail` as `field: message`.
- `server/migrations/platform/`: up to `0010_auth_policies.sql`, so this row's migration is `0011`.
