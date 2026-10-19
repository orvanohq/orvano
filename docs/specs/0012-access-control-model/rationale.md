# 0012. Access control model: rationale

Decision record for [index.md](index.md). `/develop` does not need this file.

## Context

Orvano holds other people's users, data, and files, and later runs their code. Every module that serves project data (databases, storage, realtime, functions) must answer the same question on every request: may this caller do this action on this thing? If each module answers it alone, the answers drift, and one drifted answer is a data leak. Spec 0006 deferred the question here (roles, permission checks, not found versus forbidden), as did spec 0011 (the scope list, the permission columns) and spec 0002 DAT-2a (whether project filtering is refined).

The forces:

- **Engine neutral data API** (root `AGENTS.md`). PostgreSQL is the first engine, not the last. Whatever describes access must be enforceable by an engine that has no row level security of its own.
- **Two kinds of callers.** Client apps run on untrusted devices with a publishable key and an end user session. Server code holds a secret key and does admin work. Both reach the same data.
- **End users and their labels already exist** (spec 0011 `auth.user.labels`), and anonymous sessions arrive in feature 19. Teams are deferred.
- **Developers expect the owner only pattern** ("users see only their own rows") to work with almost no setup, as it does on Appwrite, Supabase, and Firebase.
- **Correct lists.** Pagination, counts, and bulk writes must stay correct under access rules, and must not reveal hidden rows.
- **ASVS 5.0 Level 2** (spec 0006): deny by default, server side checks, least privilege, audited permission changes.
- **Console roles grow later** (feature 51), and must not force a rewrite of every control plane endpoint.

Not deciding now means features 16, 26, 29, 34, and 38 each invent a model.

## Options considered

### Option 1: Declarative grants, optional row level grants, checks pushed into the query (chosen)

Resources carry lists of `<action>:<role>` tokens. Tables and buckets can turn on row level tokens. The server computes the caller's roles and adds an "array overlaps" filter to the query.

**Pros**:

- Engine neutral: any engine with arrays (or a side index) can enforce it.
- Easy to show and edit in the console and SDKs; developers coming from Appwrite know it.
- Lists, counts, and bulk writes stay correct because the filter lives in the query.

**Cons**:

- No conditions on row values; anything beyond role matching needs labels, table design, or server code.
- Every project table carries a grants column and an index.

### Option 2: Rule expressions (Firebase rules or CEL style)

Each resource has an expression such as `auth.uid == row.owner_id || 'admin' in auth.labels`, compiled to each engine's filter.

**Pros**:

- Expresses conditions on row values, the most powerful option.
- No per row grants column; ownership is just a column.

**Cons**:

- Orvano must build and secure a language, a sandbox, and a compiler per engine; a compiler bug is a data leak.
- Hard to show in a console and hard for developers to test.

### Option 3: Native SQL policies (PostgreSQL row level security)

Developers write RLS policies; the server sets the caller's claims per transaction, as Supabase does.

**Pros**:

- The database enforces it, a strong second line even against server bugs.
- Full SQL power.

**Cons**:

- Puts PostgreSQL syntax into the public model, breaking the engine neutral rule.
- Per transaction settings complicate the shared pools of spec 0007 PG-3, and every future engine needs a different path.

### Option 4: Resource level roles only

Grants only on tables and buckets, no per row grants; owner only data needs an owner column plus a built in "owner" rule.

**Pros**:

- Simplest and fastest; no grants column in user tables.

**Cons**:

- Sharing a single row with one other user or a label is impossible.
- The built in owner rule is a special case that becomes a small rule language over time.

## Rationale

Option 1 is the only one that keeps the public model engine neutral while still handling per owner and per row sharing. Option 3 is ruled out by the engine neutral rule and by pooling. Option 2 is the most powerful, but building a safe language and one compiler per engine is a large standing security risk for a small team, and nothing in the scope needs value conditions yet. Option 4 is too weak for the owner only pattern without growing its own special cases. Option 1 is also the most familiar shape for developers moving from Appwrite, a stated peer in the scope.

Pushing the check into the query, not filtering after a read, is what keeps pagination, counts, and bulk writes correct and stops timing leaks. Union semantics (table OR row) make the common setups one step each: public read is a table grant, owner only is a row grant.

The engineer's choices on top (all agreed in the interview): deny by default; `any`, `guests`, `users`, `user:<id>`, `label:<name>` now, with teams and verified roles reserved; CRUD plus a `write` shorthand; anonymous sessions count as users; revocation, not a status lookup, refuses blocked users; secret keys bypass grants within module level read and write scopes; publishable keys required and able to switch client APIs off; impersonation reserved; a client may only grant roles it holds; creators own their rows by default; hide what you cannot read (404) and say forbidden (403) only for what you can see; realtime checks every event; functions get execute grants and a scoped run key; anyone who can read a file can mint a signed link; operators bypass grants under a fixed console permission list; labels set only by server and console, read through a cache cleared by signal; grants stored as typed columns on each owning row; at most 100 per array; resource grant and label changes audited; bulk writes change only allowed rows; stale user grants left in place; and the core built now as feature 9.

### Calls made while writing (recommend items)

- **The scope list (SCP-1, SCP-2)**: module read and write scopes for secret keys plus a few action scopes (`functions.execute`, `realtime.subscribe`), and a separate `client.*` list for publishable keys so the two ceilings cannot overlap. Runner up: one shared list with a type flag, rejected because a shared name invites a publishable key holding a server scope.
- **Run key as a signed JWT (FNC-2)**, not a stored key row: it is short lived and per run, so storing and revoking rows would only add writes. The `typ` header keeps it from passing as an access token, reusing TOK-2's guard. Runner up: a temporary `platform.api_key` row with `expires_at`, rejected for write load and cleanup.
- **Visibility rule (DEN-1)**: a resource is visible when the caller holds any resource grant or row security is on. This gives one clear 404 versus 403 rule. Runner up: always 403 on resources, rejected because it reveals table and bucket names to callers with no access.
- **Project mismatch is 401 (PRJ-2)**, not 403 or 404, so a valid key for project A tells you nothing about project B.
- **Grant syntax checked in code, length in a CHECK**: a CHECK cannot validate each array element without a function, and spec 0011 already does the same for labels.
- **Resource grant cache (EVL-5)**: HybridCache with tag invalidation through `ICacheInvalidator`, the same path as labels; 60 seconds bounds staleness if a signal is lost (see the reconciliation below).
- **`databases.table` registry row**: grants must survive a table rename and must be readable without opening the project database, so they live on a platform row with a stable id. Runner up: a reserved schema inside each project database, rejected because the control plane would need a project connection for every check.
- **Existing `IProjectContext` and DAT-2a**: kept as written; this spec only fixes who may set the context (PRJ-3).

### Changes from the cross check (2026-10-18)

An independent read only review found these; the engineer accepted every recommended fix.

- Table level `update` could rewrite any row's grants and make it public. Now only a row's own `update` grant changes its grants (WRT-1), clients add or remove only their own roles' tokens (WRT-2), and `permissions` is returned only to callers who may change it (WRT-7).
- A failing credential must never fall back to guest, and checks have a fixed order (PRJ-4, DEN-5).
- The denial outcomes are now a full matrix (DEN-1), with the single row race accepted (DEN-3).
- The row GIN index is built only when row security turns on (ENF-4, ENF-5), which removes most of the write cost of the grants column.
- Smaller gaps closed: bulk writes matching nothing (ENF-3), realtime grant changes and socket close (RTM-1, RTM-3), run key `act` and per use scope checks (FNC-2), `run_scopes` ownership and audit (WRT-6, AUD-1), project in organization and `keys.write` power (CPL-3, CPL-5), `client.auth.signup` dependency (SCP-2), label signal payload (LBL-3), signed link expiry (STO-2), the raw SQL boundary (Security model), and the `function` audit actor.

### Reconciled with spec 0007 (2026-10-18)

Building step 8 showed three places where this spec disagreed with spec 0007. Each was settled in favour of the existing platform rule.

- **Signal format**: LBL-3 had its own `labels_changed` signal with a `<projectId>:<userId>` payload, but SIG-1 fixes a JSON payload and a closed list of kinds. Labels and resource grants now ride the existing `cache` kind as tags (`orvano:auth:labels:<user id>`, `orvano:<module>:<entity>:<id>`), so SIG-1 is unchanged and the reconnect rule is SIG-2's, not a second copy. Runner up: a new `labels` kind added to SIG-1, rejected because it is a second invalidation path for what is plainly cache invalidation, with its own handler and recovery to test.
- **Expiry**: LBL-2 and EVL-5 said 5 minutes, but CCH-2 (and spec 0002 PRF-2) cap security related entries such as roles at 60 seconds. Labels are roles and resource grants decide access, so both now expire after 60 seconds, and CCH-2 names them in its list. This is still not caching a permission answer: the evaluator's decision is computed on every request from cached inputs. Runner up: raise CCH-2's cap to 5 minutes for signal backed security entries, rejected because the saving (one indexed read per user or resource per minute per replica) is small and the cost is a five times longer window after a lost signal.
- **Owner of the signal bus**: AC-10 called the signal "existing", but nothing builds spec 0007's `ISignalBus` or `ICacheInvalidator` yet, and no feature owned them. A new foundation feature 61 (Background runtime) now builds spec 0007's signals, cache invalidation, jobs, and outbox, signals first. Runner up: build the bus inside feature 9, rejected because the bus also serves sessions (feature 14), CORS (feature 13), and jobs, and belongs in `Orvano.Infrastructure`, not the access core. The cost is that feature 9 closes only after feature 61's first part lands.

### Access layer entry point and endpoint default deny (2026-10-19)

The review of feature 9 (`docs/reviews/2026-10-18-review-access-control-model.md`, Minors 1 and 6) left three items for a design call. They were settled by `/architect` on recommendation, without an interview; each can be overridden before features 13 and 14 build against it.

- **Resolver entry point (RSV)**: `AccessContext.Establish` is internal and only `*.Tests` see the internals, but the resolvers live in features 13, 14, and 38. Chosen: a public `ICallerResolver` contract that returns a result (`NotPresent`, `Resolved`, `Failed`) and an access layer middleware that runs the resolvers in a fixed order, compares the project, and does the one `Establish`. The setter stays private to the access layer, and the project comparison (PRJ-1, PRJ-2) lives in one place instead of three resolvers. Passing the caller so far lets the token resolver build an `end_user` on the publishable key's `guest` without the two features calling each other. Runner up: a public `ICallerEstablisher` service that resolvers call themselves, rejected because any code holding it could set the project (it is PRJ-3's setter made public under a new name) and each resolver would repeat the project check. Also rejected: one more `InternalsVisibleTo` per module (breaks the architecture test and grows with every resolver), and one resolver per plane owned by the host (puts key and token logic outside their modules).
- **Endpoint default deny (EPT)**: access was opt in per endpoint, so a forgotten requirement meant no check at all. Chosen: every endpoint carries a requirement or an explicit `AllowPublicAccess()`, checked at startup (the host will not start), by an integration test that lists the endpoints (so CI names the offender before a deploy), and at request time (an undeclared endpoint answers `404`). Startup is the real guard because it also covers endpoints mapped only under some roles or settings; the test gives a readable failure in CI; the request time answer covers endpoints added after startup. Runner up: a test only, rejected because a host built in a way the test does not build would ship unchecked. Also rejected: reusing ASP.NET's `AllowAnonymous` as the public marker, because other libraries add it and it means "skip ASP.NET authorization", which Orvano does not use; one marker in Orvano's own vocabulary is unambiguous.
- **Groups**: the extensions become generic over `IEndpointConventionBuilder`, so a module can declare `RequireScope` once on a `MapGroup`. A mix of requirement kinds on one endpoint is refused at startup, since the kinds admit disjoint caller kinds and a mix would deny everyone silently.
- **Public endpoints get no caller (EPT-5)**: resolvers do not run on them, so a stale console cookie or a bad bearer token cannot turn a health probe or the console page into a `401`. Runner up: resolve and ignore failures on public endpoints, rejected because it adds work and a "sometimes there is a caller" state for no current use.
- **Wording**: since PR #33 the requirements are endpoint metadata enforced by the middleware, not endpoint filters, which is what put them before binding (DEN-5). SCP-5, CPL-3, and the API surface now say so. PR #35's `MayChangeItemGrants(caller, rowSecurity, rowGrants)` and `Normalize`'s refusal of client writers on resource targets are now in WRT-1 and WRT-6, and the input caps in GRM-2.
