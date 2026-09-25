# 0002. Orvano stack and architecture

**Date**: 2026-09-24
**Updated**: 2026-09-25 (poison events: a failing event consumer no longer stalls the outbox)
**Status**: In Progress

## Summary

Orvano is one .NET 10 program, built as separate modules (auth, databases, storage, and so on), shipped as one container image that starts in different roles: the API, a background worker, a realtime socket server, and a one shot migrator. Everything is stored in PostgreSQL 18, which also carries the internal event stream and the job queue, so a self hoster runs only Postgres, the Orvano image, and Caddy (the web server that handles HTTPS) on a 2 CPU, 4 GB server. The console is a React app served as static files, and each project's data lives in its own Postgres schema (a named folder of tables) guarded by its own Postgres role. For building, it means every later feature plugs into this frame instead of choosing its own.

## Decision

**Chosen option**: Option 1: a .NET 10 modular monolith on PostgreSQL 18 alone, run in roles behind Caddy, with a static React console.

One image, one database, one gateway: Postgres carries data, events, and jobs; Caddy carries TLS and routing; everything else is a module inside the Orvano server.

**Implementation skills**: `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `configuring-opentelemetry-dotnet` (`dotnet/skills`, `.claude/skills/configuring-opentelemetry-dotnet/`) · `optimizing-ef-core-queries` (`dotnet/skills`, `.claude/skills/optimizing-ef-core-queries/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `aspire` (`microsoft/aspire-skills`, `.claude/skills/aspire/`) · `aspire-monitoring` (`microsoft/aspire-skills`, `.claude/skills/aspire-monitoring/`) · `aspire-deployment` (`microsoft/aspire-skills`, `.claude/skills/aspire-deployment/`) · `multi-stage-dockerfile` (`github/awesome-copilot`, `.claude/skills/multi-stage-dockerfile/`) · `tanstack-router` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-router/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`) · `vercel-react-best-practices` (`vercel-labs/agent-skills`, `.claude/skills/vercel-react-best-practices/`) · `pnpm` (`antfu/skills`, `.claude/skills/pnpm/`)

## Proposed stack

| Layer | Choice | Reason |
|---|---|---|
| Architecture pattern | Modular monolith: one codebase, one image, started as `api`, `worker`, `realtime`, `migrate` (and later `executor`) | One builder, one small server; roles can move to other servers later without a rewrite. |
| Server language and runtime | C# 14 on .NET 10 (LTS) | Your choice; spec 0001's generator and server contract types already assume it. |
| API framework | ASP.NET Core 10 Minimal APIs, one `MapGroup` per module under `/v1` | Built in, fast, and maps directly onto spec 0001's generated route constants. |
| Primary database | PostgreSQL 18, official `postgres:18` image, no extensions required | Current stable major; real tables and SQL per project are the product. |
| Tenant isolation | One schema and one `NOLOGIN` role per project (`p_<projectId>`); platform tables in schema `orvano` | Real per project tables, one connection pool, and Postgres itself blocks cross project access. |
| Data access | EF Core 10 (Npgsql provider) for platform tables; raw Npgsql plus a small SQL builder for per project tables | EF for fixed CRUD, SQL where tables are created at runtime. |
| Platform migrations | Versioned plain `.sql` files, embedded in the server, applied by the `migrate` role under an advisory lock | Reviewable SQL, safe repeated runs on many self hosted installs. |
| Event bus | Transactional outbox table plus Postgres `LISTEN/NOTIFY` wake ups | No lost or phantom events, no broker to run. |
| Background jobs | Orvano's own Postgres queue claimed with `FOR UPDATE SKIP LOCKED` | Shapes directly into the Jobs product (row 33); shares the outbox transaction. |
| Cache | `HybridCache` in memory only, no second level store yet | One instance per role on one server; Valkey plugs in as its second level later. |
| Rate limiting | ASP.NET Core rate limiting middleware, in memory | Enough for one API instance; row 14 sets the policies. |
| Realtime transport | Plain WebSocket at `/v1/realtime`, hosted by Kestrel in the `realtime` role, small JSON protocol | Native in browsers, Dart, and .NET; row 22 designs the messages. |
| File storage | `IBlobStore` driver: local volume (default) or any S3 compatible service via `AWSSDK.S3` | No bundled object store; self hosters can point at AWS, R2, B2, or their own. |
| Gateway | Caddy 2, the only public container (ports 80 and 443) | Automatic HTTPS now, on demand TLS for custom domains in row 30. |
| Console | React (current major), Vite, TypeScript, TanStack Router (file routes), TanStack Query | Static SPA, no Node server in production; styling and components come from row 5. |
| Console API client | New `console` audience in the contract, generated into a private `@orvano/console-client` package | Typed, contract checked platform calls that never reach public SDKs. |
| Functions isolation | Reserved `executor` role, the only container ever given the container runtime socket | Keeps root equivalent access away from the API and worker; row 27 picks the sandbox. |
| Secrets at rest | Envelope encryption: AES-256-GCM data keys wrapped by a master key from the environment | A database dump or backup alone never exposes project credentials. |
| Observability | OpenTelemetry (.NET built in), JSON logs to stdout, OTLP export when configured | Instrumented from day one; row 37 picks the log store. |
| Local dev | Aspire 13 AppHost (`dotnet run --project dev/Orvano.AppHost`) | One command starts every role, Postgres, and the console with a trace dashboard. |
| Production packaging | Docker Compose on one Linux host | What self hosters run; row 6 wraps it in the installer. |
| Container images | Chiseled Ubuntu .NET 10 ASP.NET runtime, `chiseled-extra` variant (includes ICU and tzdata), JIT, non root, `linux/amd64` and `linux/arm64`; exact tag pinned in `deploy/server.Dockerfile` | Small and shell free, while keeping time zones and culture data that scheduling and Npgsql need. |
| Image registry | GHCR: `ghcr.io/orvanohq/orvano` (server, all roles) and `ghcr.io/orvanohq/orvano-gateway` (Caddy plus the console build) | Free for public repos, next to the code. |
| JS tooling | pnpm workspaces pinned through Corepack; Node 24 LTS | Strict dependencies catch SDK packaging mistakes. |
| Repo and CI | One monorepo `github.com/orvanohq/orvano`, GitHub Actions | Confirms spec 0001's assumptions. |
| License | Apache 2.0 for the core; cloud only code lives in a separate private repo that plugs in as modules | Easy company adoption; dependencies must be Apache 2.0 compatible (MIT, BSD, Apache). |
| Minimum host | 2 vCPU, 4 GB RAM, Linux amd64 or arm64, Docker Engine with Compose v2 | A common small VPS. Functions (row 27) may raise this floor, since the budget below reserves nothing for the `executor`. |

## Architecture

### Containers on one server

| Container | Image | Role | Exposed | Memory limit | Notes |
|---|---|---|---|---|---|
| `gateway` | `orvano-gateway` | Caddy | 80, 443 (public) | 128 MB | Serves the console build; proxies `/v1`. Volume `orvano-caddy` at `/data` keeps certificates across recreation (avoids Let's Encrypt rate limits). |
| `postgres` | `postgres:18` | database | internal only | 1.25 GB | Data volume `orvano-pg`. |
| `migrate` | `orvano` | `migrate` | none | 256 MB | Runs, applies migrations, exits 0. Others start after it succeeds. |
| `api` | `orvano` | `api` | internal 8080 | 384 MB | Public HTTP API. Mounts volume `orvano-storage` at `/var/lib/orvano/storage`. |
| `worker` | `orvano` | `worker` | internal 8080 (health only) | 384 MB | Event dispatcher, job loop, internal schedules. Mounts `orvano-storage` too. |
| `realtime` | `orvano` | `realtime` | internal 8080 | 256 MB | WebSocket fan out. |
| `executor` | `orvano` | `executor` | internal | reserved | Not started until row 27. Only container that mounts the container runtime socket. |

The limits total about 2.7 GB, which leaves the rest of 4 GB for the OS and page cache. .NET reads the container limit and sizes its heap to it.

**Postgres settings for the 4 GB baseline** (set by the compose file, tunable by the installer): `shared_buffers=512MB`, `effective_cache_size=1536MB`, `work_mem=8MB`, `maintenance_work_mem=128MB`, `max_connections=100`.

**Connection budget** (Npgsql pooling, no PgBouncer):

| Role | Pooled | Dedicated (never returned to a pool) |
|---|---|---|
| `api` | 40 (app) | none |
| `worker` | 15 (app) and 3 (admin), two separate `NpgsqlDataSource` instances | 1 leader lock connection, 1 `LISTEN` connection |
| `realtime` | 5 (app) | 1 `LISTEN` connection |
| `migrate` | 2 (admin) | none |

That is 68 in total, leaving about 30 of 100 for `superuser_reserved_connections`, psql, `pg_dump`, and dev tools. Each role sets `Maximum Pool Size` in code from this table when it builds its data source (not in the connection string, so self hosters cannot drift it by accident).

**Compose start order**: `migrate` depends on `postgres` (`service_healthy`); `api`, `worker`, and `realtime` depend on `migrate` (`service_completed_successfully`) and `postgres` (`service_healthy`); `gateway` depends on `api` and `realtime` (`service_healthy`).

### Roles

| Role | Runs | Started by |
|---|---|---|
| `api` | Minimal API endpoints of every module under `/v1`, and `/internal/*` endpoints on the internal network only | `ORVANO_ROLE=api` or `orvano api` |
| `worker` | Outbox dispatcher, job loop, internal scheduled tasks (with the leader lock), project provisioning | `worker` |
| `realtime` | `GET /v1/realtime` WebSocket, one `LISTEN orvano_events` connection, subscription matching | `realtime` |
| `migrate` | Platform migrations, then exit | `migrate` |
| `executor` | Reserved for functions (row 27) | `executor` |
| (any) | `orvano healthcheck`: calls its own `/internal/readyz` and exits 0 or 1 | compose `healthcheck` (chiseled images have no shell or curl) |

Every role exposes `/internal/healthz` (process alive) and `/internal/readyz` (database reachable and schema version equals the version the binary expects).

If `ORVANO_ROLE` is unset and no role argument is given, or the two disagree, or the value is unknown, the process exits with code 1 and a clear message. There is no default role.

### Request routing (Caddy)

| Path on `ORVANO_PUBLIC_URL` | Goes to |
|---|---|
| `/v1/realtime` | `realtime:8080` (WebSocket upgrade passes through) |
| `/v1/*` | `api:8080` |
| `/internal/*` | answered 404 by Caddy; never proxied |
| everything else | console static files, falling back to `index.html` |

Custom domains (row 30) later use Caddy on demand TLS with `ask http://api:8080/internal/tls/allow`, so a certificate is only issued for a domain Orvano knows. That `ask` call goes from Caddy straight to the API over the internal network; it is not a public route, so the public `/internal/*` 404 rule does not affect it. Sites (row 29) get a separate wildcard base domain. The API trusts `X-Forwarded-*` headers only from the gateway's network.

In local dev there is no Caddy: the Vite dev server proxies `/v1` and `/v1/realtime` (WebSocket) to the API and realtime processes that Aspire starts, so the console is still same origin. The compose file is where Caddy routing gets exercised.

### Postgres layout and isolation

| Postgres role | Login | Used by | Privileges |
|---|---|---|---|
| `postgres` (superuser) | yes | installer only, once, to create the roles and database | everything |
| `orvano_admin` | yes | `migrate` role, and the `worker` for provisioning jobs only | owns database `orvano` and schema `orvano`; `CREATEROLE` |
| `orvano_app` | yes | `api`, `worker`, `realtime` | DML on schema `orvano` only; member of every project role with `INHERIT FALSE, SET TRUE` |
| `p_<projectId>` | no (`NOLOGIN`) | assumed per transaction | owns schema `p_<projectId>`; nothing in `orvano` |

- **Provisioning a project** is a worker job using `orvano_admin`: `CREATE ROLE p_<id> NOLOGIN`, `CREATE SCHEMA p_<id> AUTHORIZATION p_<id>`, `GRANT p_<id> TO orvano_app WITH INHERIT FALSE, SET TRUE`. The public API never holds `CREATEROLE` or DDL rights on the platform schema. A project is therefore usable a moment after it is created; row 3 models that `provisioning` state.
- **Why Postgres 16 or later is a hard requirement**: `GRANT ... WITH INHERIT FALSE, SET TRUE` needs Postgres 16, and `orvano_admin` can grant a project role only because, from Postgres 16, a `CREATEROLE` role automatically gets admin option on the roles it creates. So project roles must always be created by `orvano_admin`, never by the superuser (the installer included).
- **Touching project data**: only through one `Orvano.Core` helper that opens an explicit `NpgsqlTransaction`, runs `SET LOCAL ROLE p_<id>` and `SET LOCAL search_path = p_<id>` inside it, runs the caller's work, and commits. No other code path may issue `SET LOCAL ROLE`, because outside an explicit transaction it reverts before the next statement. `LOCAL` resets at commit, which keeps pooled connections clean. Because `orvano_app` does not inherit project privileges, forgetting the helper fails closed (permission denied) instead of leaking.
- **Naming**: schema and role names are `p_` plus the project ID. Row 3 must choose an ID format limited to `[a-z0-9]` and at most 60 characters (Postgres names cap at 63 bytes).

**Advisory lock keys** (two integer form, constants in `Orvano.Core`): class `0x4F525641` ("ORVA"), object `1` for the migration runner, object `2` for the scheduler leader. New locks take the next object number.

**Platform migrations**: files at `server/migrations/platform/NNNN_<name>.sql`, embedded into the server assembly. The runner takes `pg_advisory_lock(0x4F525641, 1)`, compares each file with `orvano.schema_migrations (version, name, sha256, applied_at)`, applies missing files one transaction each, and fails on a checksum mismatch in an applied file. Migrations only go forward (recovery from a bad upgrade is a restore, row 35 and 38). Every role refuses to start if the database version is lower or higher than the highest embedded file. Per project migrations (row 19) reuse the same runner against a project schema.

**EF model drift check**: because platform tables change through SQL files, not EF migrations, a CI integration test applies every migration to a fresh Postgres 18 and compares each EF entity's table, columns, types, and nullability with `information_schema.columns`. Any mismatch fails the build.

### Events and background work

```
module code ──(one transaction)──▶ data rows + orvano.events row + pg_notify('orvano_events', id)
                                                         │ delivered only after commit
                        ┌────────────────────────────────┴───────────────────────────┐
                        ▼                                                            ▼
      worker: dispatcher claims undispatched events                 realtime: LISTEN, fetch event by id,
      (SKIP LOCKED), enqueues jobs for durable consumers,           push to matching sockets
      marks dispatched, all in one transaction                      (at most once; clients resync)
                        │
                        ▼
      orvano.jobs ──▶ job loop claims (SKIP LOCKED, lease) ──▶ handler ──▶ succeeded | retry | dead
```

- **Outbox `orvano.events`**: `id` (bigint identity), `project_id` (text, null for platform events), `type` (for example `rows.created`), `subject`, `payload` (jsonb), `created_at`, `dispatched_at` (null until dispatched). A partial index on `dispatched_at IS NULL`. The notify payload is only the ID (the 8000 byte limit never matters).
- **Dispatcher** wakes on `NOTIFY` and also polls every 2 seconds, because notifications are not durable across a dropped connection. Durable consumers (webhooks, functions, notifications, added by their rows) register event handlers that only enqueue jobs. Each consumer has a name, and one consumer failing never holds back another consumer or another event (see *Poison events* below).
- **Realtime delivery is at most once** by design; a reconnecting client refetches. Row 22 may add replay from `orvano.events`. If the realtime role's own `LISTEN` connection drops, it reconnects with backoff, issues `LISTEN` again, and treats the gap like a client resync (events in the gap are not replayed).
- **Retention**: dispatched events are deleted after `ORVANO_EVENT_RETENTION_DAYS` (default 7) by an internal scheduled task that runs hourly.
- **Jobs `orvano.jobs`**: `id`, `queue`, `kind`, `project_id` (null for internal work), `payload` (jsonb), `priority`, `run_at`, `status` (`queued`, `running`, `succeeded`, `failed`, `dead`), `attempts`, `max_attempts`, `lease_until`, `locked_by`, `last_error`, `created_at`, `finished_at`.
  - Index: partial index on `(queue, priority, run_at) WHERE status = 'queued'`, created in the first migration.
  - Claim: update the next `queued` rows where `run_at <= now()`, ordered by priority then `run_at`, `FOR UPDATE SKIP LOCKED`, setting `running` and a lease of 60 seconds. Handlers extend the lease every 20 seconds while working.
  - Retries: exponential backoff with jitter; after `max_attempts` the job becomes `dead` (the dead letter state). A reaper runs every 30 seconds and requeues jobs whose lease expired.
  - Delivery is at least once, so every handler must be idempotent.
  - Wake up: `NOTIFY orvano_jobs` on insert, plus a poll fallback.
- **Internal schedules** (event pruning hourly, the lease reaper every 30 seconds) run in the worker instance that holds the leader lock `pg_advisory_lock(0x4F525641, 2)`. That lock is session level, so it is held on the worker's dedicated leader connection, which is never returned to a pool. If that connection drops, the worker stops leader tasks at once, reconnects, and tries to take the lock again every 10 seconds. Key rewrap is not scheduled; it is a job enqueued when a master key is rotated. Product schedules, per project queues, concurrency limits, and the console view belong to rows 33 and 34 and extend these tables.

### Poison events

A poison event is one that makes a consumer fail every time it runs (a bug, or a payload the consumer did not expect). The dispatcher contains the failure to that one consumer on that one event, hands it to the job queue for retries, and keeps going.

**Registration.** A consumer is registered with a name:

```csharp
public interface IWorkRegistry
{
    void OnEvent(string eventType, string consumerName, EventConsumer consumer);
    void HandleJob(string kind, string queue, JobHandler handler);
    void AddInternalSchedule(string name, TimeSpan interval, ScheduledTask task);
}
```

- `consumerName` is `<module>.<purpose>`, lowercase letters, digits, `_` and `.` only, at most 100 characters (for example `webhooks.deliver`, `functions.trigger`). It must be unique for its event type, compared ordinally (case sensitive, like job kinds); a duplicate throws `InvalidOperationException` at startup, the same as a duplicate job kind. A name outside the allowed characters also throws at startup.
- The name is the consumer's stable identity: a redispatch job finds its consumer again by event type plus name. Renaming a consumer is a breaking change for any redispatch job still waiting.
- `WorkRegistry` shape: `public sealed record NamedConsumer(string Name, EventConsumer Consumer);` in `Orvano.Core.Modules`. `ConsumersFor(OutboxEvent e)` returns `IReadOnlyList<NamedConsumer>` in registration order, and `EventConsumer? ConsumerFor(string eventType, string name)` finds one for the redispatch handler. Consumers of an event run in registration order.

**Dispatch pass** (one transaction, as today):

1. Claim up to 100 undispatched events, `ORDER BY id`, `FOR UPDATE SKIP LOCKED` (unchanged).
2. For each event, for each consumer in registration order:
   1. Call the consumer inside its own `try`, and turn the result into a list inside that `try` (a lazy `IEnumerable` would otherwise throw later, outside it).
   2. Validate every `NewJob` it returned, in memory: `Kind` has a registered job handler; `Queue` is one of the registered queues (otherwise no job loop ever claims it); `PayloadJson` parses as JSON; `MaxAttempts` is at least 1. Validation gives clear errors for the common mistakes; it does not try to copy every Postgres rule.
   3. If the list is valid and not empty, enqueue it with `JobQueue.EnqueueManyAsync(tx, jobs, savepoint: "consumer", ct)`. It sends one `NpgsqlBatch`: `SAVEPOINT consumer`, one insert per job (the same insert and `pg_notify` as `EnqueueAsync`), `RELEASE SAVEPOINT consumer`. One round trip per consumer, where today there is one per job.
   4. If Postgres rejects that batch with a deterministic error (a `PostgresException` whose `SqlState` starts with `22`, a data error, or `23`, an integrity error), run `ROLLBACK TO SAVEPOINT consumer`. That undoes only this consumer's inserts and leaves the transaction usable. Example: a payload string containing `\u0000`, which is valid JSON but which `jsonb` rejects with `22P05`.
   5. If step 1 threw, step 2 found an invalid job, or step 4 rolled back, the consumer's output for this event is dropped as a whole (none of its jobs are enqueued, even the valid ones), and one `events.redispatch` job is enqueued for this event and consumer instead (plain `EnqueueAsync`, no savepoint). The event's other consumers are not affected.
3. Mark every claimed event dispatched, commit.

**Transient failures.** Any other database error (a dropped connection, a timeout, `40001`, or a `42` error, which means a broken deployment and not a bad event) ends the pass. The transaction rolls back, nothing is marked, and the same events are claimed again on a later pass, by this worker or another. After a failed pass the dispatcher waits 2 seconds, doubling after each failure in a row up to 30 seconds, instead of the fixed 2 second poll. A `NOTIFY` still wakes it early. The first successful pass resets the delay.

**The `events.redispatch` job**:

| Field | Value |
|---|---|
| `kind` | `events.redispatch` |
| `queue` | `internal` |
| `project_id` | the event's `project_id` |
| `max_attempts` | 25 (about 10 hours of retries with the existing backoff, so a fix deployed the same day heals on its own) |
| `payload` | `{"consumer": "<name>", "event": {"id", "projectId", "type", "subject", "payload", "createdAt"}}`, camelCase, the event's `payload` embedded as JSON (not a string) |

- Code home: a static class `EventRedispatch` in `Orvano.Core.Events` with `const string Kind = "events.redispatch"`, `const int MaxAttempts = 25`, and `NewJob For(OutboxEvent e, string consumer)`, which builds the job above. The dispatcher calls `For`; nothing else builds this job.
- Payload building: `For` builds a `JsonObject`, puts the event's payload in with `JsonNode.Parse(e.Payload)` so it nests as JSON, and serializes with `JsonSerializerDefaults.Web` (camelCase). The handler deserializes the same way into `sealed record EventRedispatchPayload(string Consumer, RedispatchedEvent Event)`, where `RedispatchedEvent` has `long Id, string? ProjectId, string Type, string? Subject, JsonElement Payload, DateTimeOffset CreatedAt`, and rebuilds the `OutboxEvent` with `Payload.GetRawText()`.
- The job carries a full copy of the event, so event pruning never breaks it.
- `CoreWork` registers its handler. The handler rebuilds the `OutboxEvent` from the copy and looks up the consumer by event type and name:
  - Consumer not registered → throw `PermanentJobFailureException` ("Consumer '<name>' for event type '<type>' is no longer registered"). The job goes `dead` at once; no retries.
  - Consumer found → call it, turn the result into a list, validate it with the same rules as the dispatcher, and enqueue its jobs with `EnqueueManyAsync` (no savepoint needed) in one transaction on the app data source. A throw, a validation failure, or a database rejection is an ordinary job failure (backoff, then `dead` after 25 attempts).
- Delivery is at least once. If the worker stops after the enqueue commits but before the job is marked `succeeded`, the consumer's jobs are enqueued twice. That is allowed, because every job handler is already idempotent.
- The event row itself is never touched again. It is dispatched and gets pruned on its normal schedule.

**Permanent job failure** (new, for any handler): `PermanentJobFailureException` in `Orvano.Core.Jobs`. When a handler throws it, the job goes `dead` at once, no matter how many attempts are left. Every other exception keeps the existing retry behavior.

- `JobStore.FailPermanentlyAsync(ClaimedJob job, Exception error, CancellationToken ct)` sets `status = 'dead'`, `finished_at = now()`, `lease_until = NULL`, `locked_by = NULL`, and `last_error` in the same format as `FailAsync` (exception type and message, cut to 2000 characters), guarded by `locked_by = @worker AND status = 'running'` like the others. `attempts` stays as it is (it already counts this attempt).
- `JobLoop.RunAsync` adds `catch (PermanentJobFailureException ex)` before the general `catch (Exception ex)`, logs at `Error`, and calls `FailPermanentlyAsync`.

**Replay until the jobs console exists** (rows 33 and 34): requeue a dead redispatch job by hand with `UPDATE orvano.jobs SET status = 'queued', attempts = 0, run_at = now(), finished_at = NULL WHERE id = <id>`.

**Observability**:

- Every consumer failure logs at `Error` with the event ID, event type, consumer name, and the exception. It never logs the event payload (it can hold user data). `last_error` on the redispatch job keeps the existing format (exception type and message, cut to 2000 characters).
- A failed pass logs at `Error` with the failure count so far and the next delay.
- New meter `Orvano.Events` (registered in `Telemetry.cs` with `AddMeter`): counter `orvano.events.consumer_failures` tagged `event.type`, `consumer`, and `reason` (`threw`, `invalid_job`, or `rejected_by_database`). Row 37 decides the alerts on it.

### Module structure

- One csproj per module: `Orvano.Platform` (orgs, projects, keys; rows 3 and 7), then `Orvano.Auth`, `Orvano.Databases`, `Orvano.Storage`, and so on as their rows are built. Public types live in the module's `Contracts` namespace; everything else is `internal`.
- `Orvano.Core` is the shared kernel: database connections and the `SET LOCAL ROLE` helper, outbox writer, job queue, scheduler, `IBlobStore`, secret encryption, the module interface, problem details.
- `Orvano.Server` is the host: it lists modules explicitly (no assembly scanning) and runs the hooks the current role needs.

```csharp
public interface IOrvanoModule
{
    string Name { get; }
    void ConfigureServices(IServiceCollection services, IConfiguration config); // every role
    void MapApi(RouteGroupBuilder v1);                                          // api role
    void RegisterWork(IWorkRegistry work);                                      // worker role: event handlers, job kinds, schedules
    void RegisterRealtime(IRealtimeRegistry realtime);                          // realtime role
}
```

- A module never reads or writes another module's tables; it calls the other module's public contract. Platform tables carry the module name as a prefix (`orvano.platform_projects`, `orvano.auth_users`) so ownership is visible in SQL.

### Repository layout

Extends the layout in spec 0001:

```
Orvano.slnx                      one .NET solution: server, tools/sdkgen, sdks/dotnet, dev/AppHost
global.json                      pins the .NET 10 SDK
package.json, pnpm-workspace.yaml  pnpm workspaces: contract, sdks/js, sdks/nextjs, sdks/console-client, console
.tool-versions                   Node 24 LTS, Dart SDK (per spec 0001)
LICENSE                          Apache 2.0
server/src/Orvano.Server/        host, role selection, healthcheck command
server/src/Orvano.Core/          shared kernel
server/src/Orvano.Contract/      generated (spec 0001)
server/src/Orvano.<Module>/      one per module, added by its scope row
server/migrations/platform/      NNNN_<name>.sql
server/tests/                    test projects (framework chosen in row 2)
sdks/console-client/             @orvano/console-client, private, generated from `console` audience operations
console/                         React + Vite app (TanStack Router file routes in console/src/routes)
dev/Orvano.AppHost/              Aspire AppHost
deploy/server.Dockerfile         multi stage, chiseled runtime
deploy/gateway/                  Dockerfile (Caddy + console build) and Caddyfile
deploy/compose/                  docker-compose.yml and .env.example (the production shape; row 6 builds the installer around it)
.github/workflows/               CI
```

### Secrets at rest

- `ORVANO_MASTER_KEYS` holds one or more 32 byte keys as `id:base64` pairs separated by commas; the first is active.
- To encrypt a secret: generate a random 32 byte data key, encrypt the value with AES-256-GCM using associated data `<table>:<rowId>:<column>` (so a ciphertext cannot be moved to another row), then encrypt the data key with the active master key (associated data: the key ID). Store one versioned `bytea` blob: format version, key ID, both nonces, the wrapped data key, and the ciphertext with tags. Use .NET `AesGcm`. Data keys and both 12 byte nonces always come from `RandomNumberGenerator` (a cryptographic random source), never `System.Random`, since reusing a GCM nonce breaks the encryption.
- Rotation: put a new key first, run the internal `secrets.rewrap` job (rewraps data keys only), then remove the old key.
- The master key never enters the database, logs, traces, or error messages. Losing it makes stored secrets unrecoverable, so the installer (row 6) and backups (row 35) must treat it as a backup item.

### Value sourcing

| Action | Value | Source |
|---|---|---|
| Process start | which role to run | `ORVANO_ROLE`, or the first CLI argument |
| Process start | expected schema version | highest embedded file in `server/migrations/platform/` |
| Project scoped request | which project | `X-Orvano-Project` header (spec 0001) |
| Project scoped query | schema and role name | `p_` plus the project ID (format from row 3) |
| Encrypt a secret | active key and its ID | first entry of `ORVANO_MASTER_KEYS` |
| Decrypt a secret | which master key | key ID stored in the blob |
| Store or read a file | where bytes live | `ORVANO_STORAGE_DRIVER` plus its settings |
| Public links, Caddy site | base URL | `ORVANO_PUBLIC_URL` |
| Internal leader tasks | which worker runs them | holder of `pg_advisory_lock(0x4F525641, 2)` on the dedicated leader connection |
| Migration run | mutual exclusion | `pg_advisory_lock(0x4F525641, 1)` |
| Pool sizes | `Maximum Pool Size` per role | the connection budget table, set in code |
| Schedule timing | pruning, reaper, lease, heartbeat | hourly, 30 s, 60 s, 20 s (constants in `Orvano.Core`) |
| Event dispatch | which consumers run for an event | `WorkRegistry`, by event type; each consumer's name from `OnEvent` |
| Event dispatch | whether a job from a consumer is valid | the registered job kinds and queues in `WorkRegistry`, plus a JSON parse and `MaxAttempts >= 1` |
| Event dispatch | whether a database error is the event's fault | `PostgresException.SqlState` class `22` or `23` (anything else is transient) |
| Event dispatch | delay after a failed pass | 2 s doubling to 30 s, constants in `Timings` |
| Redispatch job | the event to replay | the copy in the job's `payload.event` (not `orvano.events`, which pruning may empty) |
| Redispatch job | which consumer to rerun | `payload.consumer`, looked up with `payload.event.type` in `WorkRegistry` |
| Redispatch job | retry limit | `EventRedispatch.MaxAttempts` (25) in `Orvano.Core.Events` |
| Telemetry | service name | `OTEL_SERVICE_NAME`, set per role (`orvano-api`, `orvano-worker`, ...) |
| Image tag | version | `VERSION` file (spec 0001), tags `X.Y.Z` and `X.Y` |

### Key invariants

- Only the gateway publishes ports. Postgres and every role stay on the internal network.
- One image for every role; the role changes behavior, never the build.
- Every event is written in the same transaction as the change it describes.
- Every job handler is idempotent.
- One consumer failing on one event never delays another consumer, or any other event.
- Every claimed event is marked dispatched in the pass that claims it, unless the whole pass hits a transient database error.
- Consumers and job handlers never rely on event order. A consumer that failed delivers its jobs later than jobs from newer events.
- Logs never contain event payloads.
- Project data is only touched after `SET LOCAL ROLE p_<id>`; `orvano_app` never inherits project privileges.
- The public API role never holds DDL or role creation rights.
- No module touches another module's tables.
- Only the `executor` container may ever mount the container runtime socket.
- No dependency with a license incompatible with Apache 2.0 enters the core.
- Stored secrets are always envelope encrypted; the master key only lives in the environment.

### Security model

- **Network**: public traffic reaches only Caddy; `/internal/*` is unreachable from outside; forwarded headers are trusted only from the gateway network.
- **Database**: least privilege roles as above; cross project access is blocked by Postgres itself, not only by application code.
- **Processes**: non root, shell free images; the container runtime socket is confined to the future `executor`.
- **Secrets**: envelope encryption at rest; the master key only in the environment file the installer writes with owner only permissions.
- **Console**: same origin as the API, so no CORS for the console; session and CSRF rules come with rows 7 and 8.
- **Supply chain**: pinned SDK, Node, pnpm, and image versions; image signing is left to row 38.
- Compliance: none triggered by this decision itself. Auth and personal data rules arrive with rows 8 and later (tagged GA).

### Configuration required

- `ORVANO_ROLE`: `api` | `worker` | `realtime` | `migrate` | `executor`.
- `ORVANO_PUBLIC_URL`: public base URL, for example `https://orvano.example.com`.
- `ORVANO_DB_URL`: connection string for `orvano_app`.
- `ORVANO_DB_ADMIN_URL`: connection string for `orvano_admin`; given only to `migrate` and `worker`.
- `ORVANO_MASTER_KEYS`: master keys for secret encryption.
- `ORVANO_STORAGE_DRIVER` (`local` | `s3`), `ORVANO_STORAGE_PATH` (default `/var/lib/orvano/storage`), and for S3: `ORVANO_S3_ENDPOINT`, `ORVANO_S3_REGION`, `ORVANO_S3_BUCKET`, `ORVANO_S3_ACCESS_KEY`, `ORVANO_S3_SECRET_KEY`, `ORVANO_S3_FORCE_PATH_STYLE`. Read from row 20 on.
- `ORVANO_EVENT_RETENTION_DAYS`: default 7.
- `ASPNETCORE_ENVIRONMENT`: `Development` | `Test` | `Production` (`Test` enables spec 0001's fixtures and response validation).
- `OTEL_EXPORTER_OTLP_ENDPOINT` (optional) and `OTEL_SERVICE_NAME`.
- `POSTGRES_PASSWORD`, plus the passwords for `orvano_admin` and `orvano_app`: generated by the installer (row 6).

### What the scaffold must contain (row 1's done condition)

`/develop` derives the steps; this is the target it builds to and `/check verify` checks.

- `dotnet run --project dev/Orvano.AppHost` starts Postgres 18, runs `migrate` to completion, then starts `api`, `worker`, `realtime`, and the console dev server. The console shows a placeholder page, and `GET /v1/health` answers through the console's origin.
- The first platform migration creates schema `orvano`, `orvano.schema_migrations`, `orvano.events`, and `orvano.jobs`; the `orvano_admin` and `orvano_app` roles exist with the privileges above.
- The worker runs the dispatcher, the job loop, and the event pruning schedule, all idle without errors; `/internal/readyz` passes on every role.
- `dotnet build Orvano.slnx` and `pnpm -r build` pass locally and in a GitHub Actions workflow, and the EF model drift check runs in CI.
- The pinned `chiseled-extra` image is smoke tested early: the `api` role connects to Postgres over Npgsql and resolves a named time zone inside the container.
- `docker compose -f deploy/compose/docker-compose.yml up` with locally built images boots the same system behind Caddy on `http://localhost`, with compose health checks using `orvano healthcheck`.
- No product modules yet besides the health endpoint; `Orvano.Platform` arrives with rows 3 and 7.
- Poison events are contained as *Poison events* describes: named consumer registration, isolation per consumer with validation and a savepoint per consumer, the growing delay after failed passes, the `events.redispatch` job and its handler, `PermanentJobFailureException`, and the `Orvano.Events` meter. Tests with a real Postgres cover a throwing consumer, an invalid `NewJob`, a `jsonb` rejection (`\u0000`), a missing consumer on redispatch, and a successful redispatch after the consumer is fixed.

## Consequences

**Positive**:
- A self hoster runs three kinds of container (Postgres, Orvano, Caddy) and backs up one database plus one volume.
- Events and jobs share the data's transactions, so there are no lost or phantom events and no dual write bugs.
- Postgres enforces project isolation, which protects the later SQL editor, data API, and functions.
- The same image and roles scale out later: run more `api` or `worker` containers and add Valkey as the cache's second level.
- The Jobs, Webhooks, and Notifications differentiators all build on one queue you own.
- A bad event or a buggy consumer costs only that consumer's work on that event. Everything else keeps flowing, and the failure heals on its own if a fix ships within about 10 hours.
- Poison events reuse the job queue's backoff, dead letter state, and (from row 33) its console view and replay, so there is no second failure store to build or learn.

**Negative / tradeoffs**:
- Postgres is a single point of failure and carries load that a broker or cache would otherwise take. High availability is deferred (cluster install is after 1.0).
- The outbox, job queue, scheduler, migration runner, and secret encryption are code you write and must test well; libraries would have given you some of it.
- One schema and role per project grows the Postgres catalog; thousands of projects on one server would slow `pg_dump` and schema listing. Fine for self hosting, revisit for a managed cloud.
- In memory rate limits, caches, and presence are per instance; running two `api` or `realtime` containers needs Valkey first.
- Project creation becomes asynchronous (a provisioning job), which rows 3 and 7 must show in the console.
- Dev routing (Vite proxy) differs from production routing (Caddy); routing bugs show only in the compose check.
- A redispatched consumer delivers late (up to about 10 hours) and out of order. Consumers whose work loses value with time (for example a notification) must check the event's `createdAt` and decide whether it is still worth sending.
- A consumer that hangs (an endless loop, not a throw) still stalls the dispatcher, because synchronous code cannot be cancelled. The only guard is that consumers stay small and do no IO.
- The `IWorkRegistry.OnEvent` signature changes. Nothing calls it yet, so the change is free now and never again.
- A failed event is stored twice for a while (the event row and the redispatch job's copy). The jobs table has no retention until row 33 adds it.

**Neutral**:
- Spec 0001 gains a fourth audience, `console`, and a private generated package.
- Caddyfile, Aspire AppHost, and SQL migration files are new patterns to learn.
- Row 6 (installer) builds on the compose file defined here; row 5 (design system) fills the console shell.

## Follow-up

- [ ] Update spec 0001 to add the `console` audience (excluded from all public SDKs, generated into `sdks/console-client/`) and to note that this spec confirms .NET 10 and GitHub Actions.
- [ ] Row 3 (platform data model): pick a project ID format limited to `[a-z0-9]`, at most 60 characters; model the project `provisioning` state; decide where app users live (platform schema or project schema).
- [ ] Create the `orvanohq` GitHub org (the plain `orvano` name is unavailable on GitHub) and the `orvanohq/orvano` repo; GHCR images follow the org name. If the org ends up with another name, update the image and repo paths in this spec and in spec 0001.
- [ ] Row 6 (installer): generate the database passwords and `ORVANO_MASTER_KEYS`, and warn that the master key must be backed up. Decide whether to keep a hand written compose file or generate it with Aspire's Docker Compose publisher.
- [ ] Row 22: design the realtime message protocol and decide whether to replay from `orvano.events` on reconnect.
- [ ] Row 27: choose the sandbox behind the `executor` (gVisor, Firecracker, and Kata are all maintained today).
- [ ] Row 37: choose storage for logs and metrics behind the OpenTelemetry export, and alert on `orvano.events.consumer_failures` and on dead `events.redispatch` jobs.
- [ ] Rows 33 and 34: show dead `events.redispatch` jobs in the jobs console with the consumer name and event type, offer a one click replay (replacing the manual SQL in *Poison events*), and add retention for finished jobs.
- [ ] Row 38: add image signing and an SBOM (software bill of materials) to the release pipeline.
- [ ] Before the first build, confirm the current React and Vite majors and the Aspire 13.x release to pin; these were not checked in the landscape pass.
- [ ] The 12 Agent Skills installed for this stack are in `.claude/skills/` but not yet in an `AGENTS.md`. `/audit` (row 2) should list the project wide ones (`dotnet-webapi`, `ef-core`, `optimizing-ef-core-queries`, `configuring-opentelemetry-dotnet`, `aspire`, `aspire-monitoring`, `aspire-deployment`, `multi-stage-dockerfile`, `pnpm`) in root `AGENTS.md` and the console ones (`tanstack-router`, `tanstack-query`, `vercel-react-best-practices`) in `console/AGENTS.md`.
- [ ] MCP servers you chose to connect yourself: Aspire MCP server, GitHub MCP server, Postgres MCP Pro (dev database only). `/audit` should record them on the `MCP servers:` line once connected.
- [ ] No Agent Skill was found for TypeSpec, Caddy, or Npgsql; check again later.

## Rationale

Reasoning, options, and references: see [rationale.md](rationale.md).
