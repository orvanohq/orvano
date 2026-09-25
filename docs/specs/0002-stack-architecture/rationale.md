# 0002. Rationale: Orvano stack and architecture

## Context

Orvano is a self hosted backend platform (auth, a Postgres first database, storage, functions, sites, webhooks, realtime, messaging, jobs, backups) that one developer builds, and that self hosters install on a single small server. Every later scope row plugs into whatever foundation this spec sets, so a wrong call here is paid for in every version from v0.1 to v1.0 (basis: `docs/scope/index.md`, versions and the "end to end" definition).

The forces:
- **Operating cost for self hosters.** The target is a 2 vCPU, 4 GB server. Comparable platforms run many containers (Appwrite 2.0 lists a 16 container topology), and self hosted Supabase runs one project per stack. Orvano's pitch is many projects per install plus free backups and environments, so every extra always on process eats into the headroom those features need.
- **Builder capacity.** One developer, five SDK surfaces (spec 0001), and four differentiators (Notification Center, Webhooks, Jobs, Backups and environments). Anything that needs its own operations knowledge (a broker, a cache cluster, an object store) competes with product work.
- **Real per project databases.** Rows 16 to 19 and 36 promise real tables, SQL, per project migrations, and schema promotion. Rows 18 and 27 run user supplied SQL and untrusted code, so isolation between projects has to hold even when application code has a bug.
- **Durable fan out.** Realtime, webhooks, functions, notifications, and jobs all react to the same changes. Losing an event, or sending one for a change that rolled back, is a correctness bug users would see in their webhooks and inboxes.
- **Prior commitments.** Spec 0001 already builds the SDK generator and server contract types in C# on .NET 10 and assumes GitHub Actions; this spec was asked to confirm or overturn both.
- **Open core.** Self hosted first with a managed cloud possible later, so the license and module boundaries must allow closed cloud code without forking.

Scope of this decision: the runtime, process and container layout, database setup and isolation, event and job model, gateway, console framework, dev loop, packaging, and repo tooling. Left to their own rows: the platform data model (3), design system (5), installer (6), session model (8), realtime protocol (22), sandbox technology (27), and log storage (37).

## Options considered

### Option 1: .NET 10 modular monolith on Postgres alone (chosen)

One .NET codebase split into modules, one image started in roles, Postgres for data, events (outbox) and jobs, Caddy at the edge, a static React console.

**Pros**:
- Fewest moving parts: three kinds of container, one thing to back up.
- Events and jobs commit atomically with data.
- Postgres roles enforce project isolation.
- Matches spec 0001 and scales out by running more role containers.

**Cons**:
- You write and own the queue, outbox, scheduler, and migration runner.
- Postgres takes queue and pub/sub load, and is a single point of failure.

### Option 2: .NET services per product with a broker and a cache

Appwrite shaped: separate services (auth, databases, storage, functions, realtime, workers), Valkey for cache and pub/sub, a broker such as NATS JetStream or RabbitMQ for events, MinIO or SeaweedFS for files.

**Pros**:
- Each service scales and fails on its own.
- Mature brokers give fan out, replay, and back pressure without custom code.

**Cons**:
- Ten or more containers on a 4 GB box, with more to secure, upgrade, and back up.
- Dual writes between the database and the broker need an outbox anyway.
- Distributed debugging and contract versioning between services for one builder (basis: premature microservices failure pattern).

### Option 3: Compose existing open source parts around Postgres

Supabase shaped: PostgREST for the data API, an existing auth server, an Elixir realtime server reading logical replication, a gateway such as Kong or Envoy, with Orvano code gluing it together.

**Pros**:
- Large pieces (data API, realtime from WAL) already exist and are proven.
- Fast route to feature parity.

**Cons**:
- Many languages and runtimes (Haskell, Go, Elixir, Node) to understand and patch.
- Each part assumes one project per stack, which is exactly the limit Orvano wants to remove.
- Spec 0001's generated server contract types and response validation would only cover the glue.

### Option 4: TypeScript monolith (Node or Bun) with a Next.js console

One TypeScript codebase for the API, workers, and a Next.js console, on Postgres.

**Pros**:
- One language across console, JS SDK, and server.
- Dogfoods `@orvano/nextjs` in the console.

**Cons**:
- Spec 0001's C# generator and server contract types would need redoing.
- Weaker at CPU heavy work (image transforms, crypto, sandbox orchestration).
- A Next.js console needs an always on Node server on a small box.

## Rationale

The deciding forces are the 4 GB self hosting target and a single builder. Option 2 fails both: its container count alone consumes the headroom, and it forces an outbox anyway to avoid dual writes, so it adds a broker without removing the hard part. Option 3 is the fastest to parity but inherits the one project per stack assumption Orvano exists to beat, and spreads maintenance across four runtimes. Option 4 would restart spec 0001 and weakens the CPU heavy products for a modest gain in language unity.

Option 1 puts every durable concern in the one system self hosters already must run and back up (basis: database backed queue first, add a broker only when throughput demands it). The transactional outbox (basis: transactional outbox pattern) gives exactly once recording with at least once delivery, which is what webhooks and notifications need. Schema per project is normally more overhead than it is worth for SaaS tenancy (basis: org isolation failure pattern), but here the tenant's schema is the product: real tables, real migrations, promotion between environments. Pairing it with a `NOLOGIN` role per project and `INHERIT FALSE` membership makes Postgres, not application code, the wall between projects, which matters once rows 18 and 27 run user code (basis: least privilege; Postgres 16 and later role membership options). A modular monolith keeps the option to split later (basis: monolith first) while role images give most of the operational benefit of services today.

Caddy wins the gateway because on demand TLS is the exact mechanism rows 29 and 30 need, with an `ask` hook so certificates are issued only for known domains. MinIO is not bundled because its community edition shows no release since October 2025, and a storage driver interface with local disk and any S3 service keeps the base install lean. Valkey is deferred, not rejected: `HybridCache` takes it as a second level cache with no code changes in modules.

**Decisions made while writing the spec** (not asked in the interview):
- **Vite dev proxy instead of Caddy in the Aspire dev loop.** Wiring a Caddy container to host processes in dev is fragile; Vite's proxy keeps the console same origin, and the compose check exercises the real Caddy routing. This differs from the Aspire option description in the interview, which listed Caddy among the dev resources. Runner up: a Caddy container in the AppHost.
- **Gateway image bundles the console build.** One image fewer, and the console and gateway version together. Runner up: the API serves the console's static files, which puts static traffic on the .NET process.
- **Separate `orvano_admin` and `orvano_app` login roles, with provisioning as a worker job.** The internet facing API never holds DDL or `CREATEROLE`. Cost: project creation is asynchronous. Runner up: give `orvano_app` `CREATEROLE`, which is simpler but lets an API compromise alter the platform schema.
- **Forward only migrations with checksums and a version gate on every role.** Down migrations are rarely tested and dangerous on user data; restores cover rollback. Runner up: paired up and down scripts.
- **`orvano healthcheck` subcommand.** Chiseled images have no shell or curl for compose health checks. Runner up: a curl sidecar or a non chiseled image.
- **Realtime delivery at most once, from `NOTIFY` plus a fetch by ID.** Keeps realtime cheap; durable consumers go through jobs. Runner up: realtime reads the outbox by cursor, which breaks when transactions commit out of ID order.
- **`HybridCache` for caching and the built in rate limiter.** Both are in .NET and accept a distributed backend later. Runner up: `IMemoryCache` plus custom code.
- **`AWSSDK.S3` for the S3 driver** with path style addressing as an option, which covers AWS, R2, B2, and self hosted S3 servers. Runner up: the MinIO .NET client.
- **Memory limits and Postgres settings sized for 4 GB** (table in index.md), and a connection budget of 68 under `max_connections=100`, so no PgBouncer. The cross check raised the limit from 80 to leave room for admin tools. Runner up: PgBouncer in transaction mode, which conflicts with session level advisory locks and `LISTEN`.
- **Module tables prefixed by module name inside schema `orvano`.** Ownership stays visible without one schema per module. Runner up: a schema per module, which complicates EF Core mappings and grants.
- **Node 24 LTS** for tooling and the console build. Runner up: Node 22, which is closer to end of life.

## Poison events (added 2026-09-25)

### Context

The fresh model review of the scaffold (`docs/reviews/2026-09-25-scaffold-stack-architecture.md`) found that `EventDispatcher` runs every consumer for up to 100 events inside one transaction. If any consumer throws, the transaction rolls back, and the next poll claims the same events in the same order. So one bad event stalls every event behind it, for every consumer, forever, until someone edits the database by hand. The job loop already contains failures to one job; the dispatcher did not.

The forces: webhooks, functions, and notifications (rows sold as differentiators) all hang off this dispatcher, so its blast radius is the whole durable event path. Consumers are synchronous, take only the event, and do no IO, so almost every failure happens again on every retry: a bug, or a payload shape the consumer did not expect. Self hosters rarely watch a dead letter view, so healing without a human matters. Nothing registers a consumer yet, so the registration API can still change for free.

### Options considered

**Isolation unit.**
- *One consumer on one event (chosen).* Other consumers of the same event still get their jobs. Con: consumers need stable names, and the registration API changes.
- *The whole event.* Simpler bookkeeping. Con: a bug in one module delays every other module's delivery for that event.
- *Keep the batch, skip the head event after N failures.* The smallest change. Con: the stall still lasts N polls and still hits unrelated events.

**Where a failure goes.**
- *An `events.redispatch` job carrying a copy of the event (chosen).* Reuses the job loop's backoff, dead state, and the future jobs console and replay. Pruning cannot lose it. Con: the event is stored twice while the job lives, and the jobs table has no retention yet.
- *A new `orvano.event_consumer_failures` table.* An explicit failure record. Con: a second dead letter store, its own replay path, and pruning must skip events with open failures.
- *Attempts and last error on `orvano.events`, retried by the dispatcher.* Con: only fits isolation per event, and it copies the job loop's retry logic.

**Guard against failures in the database itself.**
- *Validate in memory, plus a savepoint per consumer sent in one `NpgsqlBatch` with its inserts (chosen).* One code path. A database rejection undoes only that consumer's inserts. It costs one round trip per consumer, where today it is one per job. Con: a failed consumer costs one extra round trip (`ROLLBACK TO SAVEPOINT`), and batching inserts needs a new `JobQueue.EnqueueManyAsync`.
- *Validate in memory, then fall back to one event per transaction with savepoints.* Chosen first in the interview. The normal pass stays savepoint free. Con: two code paths, plus a state machine (how many events to run in single mode, when to return, claiming again fresh for each event so other workers stay safe). The cross check showed the savepoint cost it avoids is smaller than assumed, so it was dropped.
- *Validate in memory only.* Cheapest. Con: any rule the validator misses (for example `\u0000` in `jsonb`) stalls the outbox again.

### Rationale

Containing a failure to one consumer on one event is the only unit that keeps every module independent, which matters because the three differentiators share this dispatcher. Sending the failure into the job queue makes poison handling almost free: backoff, the dead state, leases, and later the console and replay all exist already or are already planned for row 33. The in memory validation catches the common mistakes with clear errors. The savepoint per consumer catches the database rejections validation cannot predict, in the same pass, with no second mode. Sending the savepoint and the inserts as one batch means the guard costs no more round trips than the dispatcher pays today.

Retrying for about 10 hours (25 attempts) turns a same day hotfix into automatic recovery. For code that fails the same way every time, a short retry window only adds noise; a long one covers the one case where retrying helps, a deploy.

**Decisions made while writing the update** (not asked in the interview):
- **Drop a failing consumer's whole output, not just its invalid jobs.** Partial output from a buggy consumer is not trustworthy, and a redispatch reruns the consumer from scratch anyway. Runner up: enqueue the valid jobs and redispatch only the invalid ones, which duplicates the valid ones on redispatch.
- **Deterministic database errors are `SqlState` classes `22` and `23` only.** Those are data and integrity errors caused by the row. A `42` error (undefined table, permission denied) means a broken deployment, which should stall loudly, not quietly turn every event into a redispatch. Runner up: treat every `PostgresException` as deterministic.
- **A growing delay after failed passes, 2 s up to 30 s.** Stops a database outage from filling the logs every 2 seconds, and still recovers quickly. Runner up: keep the fixed 2 second poll.
- **`PermanentJobFailureException` as a general job loop feature.** The missing consumer case needs it, and later handlers (a webhook to a deleted endpoint, for example) will too. Runner up: a special case for `events.redispatch` only inside `JobLoop`.
- **A copy of the event in the job payload.** Keeps redispatch independent of event retention. Runner up: reference the event ID and keep pruning away from events with live redispatch jobs, which couples two tables' lifecycles.
- **Redispatch is at least once.** Enqueuing and completing the job in one transaction would make it exactly once, but it needs a new `JobStore` path. Every handler is idempotent already, so duplicates are within the contract. Runner up: complete in the same transaction.
- **A meter named `Orvano.Events` with one counter tagged by reason.** Enough for row 37 to alert on. Runner up: logs only.

**Changed after the cross check** (an independent read of the spec on another model): the database guard moved from a single mode fallback to a savepoint per consumer, confirmed by the engineer. The spec also now pins down the `NamedConsumer` registry shape, `JobStore.FailPermanentlyAsync` and the `JobLoop` catch order, how the redispatch payload is built and read, and the `EventRedispatch` constants.

## References

**Project sources**:
- `docs/scope/index.md`: versions, differentiators, the five SDK surfaces, Tracer Bullet approach
- `docs/scope/foundations.md`: row 1 done condition; rows 3, 5, 6
- `docs/scope/databases.md`, `functions.md`, `sites.md`, `realtime.md`, `jobs.md`, `operations.md`: the isolation, TLS, fan out, and backup needs this frame must carry
- `docs/specs/0001-api-contract-sdk-pipeline/`: .NET 10, C# generator, audiences, `Test` environment, GitHub Actions assumption
- Installed skills: `aspire-deployment` (`.claude/skills/aspire-deployment/`) for Aspire 13.x and its Docker Compose publishing
- Engineer's picks in the design interview, 2026-09-24

**Practices & standards**:
- Monolith first; premature microservices failure pattern
- Transactional outbox pattern
- Database backed queue first (`FOR UPDATE SKIP LOCKED`)
- Least privilege database roles; defense in depth for tenant isolation
- Envelope encryption with AES-256-GCM and associated data
- Forward only, checksummed schema migrations
- OpenTelemetry for logs, traces, and metrics

**Links** (web verified during the landscape check, 2026-09-24):
- PostgreSQL (18 is current stable): https://www.postgresql.org
- Caddy: https://caddyserver.com
- Traefik: https://traefik.io
- Valkey: https://valkey.io
- Redis: https://redis.io
- Microsoft Garnet: https://github.com/microsoft/garnet
- MinIO: https://min.io
- SeaweedFS: https://github.com/seaweedfs/seaweedfs
- Wolverine: https://github.com/JasperFx/wolverine
- Hangfire: https://www.hangfire.io
- pgmq: https://github.com/tembo-io/pgmq
- Next.js: https://nextjs.org
- gVisor: https://gvisor.dev
- Firecracker: https://firecracker-microvm.github.io
- Kata Containers: https://katacontainers.io
- Aspire MCP server: https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/mcp-server
- GitHub MCP server: https://github.com/github/github-mcp-server
- Postgres MCP Pro: https://github.com/crystaldba/postgres-mcp
