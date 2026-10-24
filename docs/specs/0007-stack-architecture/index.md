# 0007. Stack and architecture: one image on PostgreSQL alone

**Date**: 2026-10-16
**Status**: Accepted
**Updated**: 2026-10-16, from the scaffold: INS-3 and the new INS-5 (root key file permissions on Linux), INS-2 (migrate gets no root key), HSH-2 result recorded.
**Updated**: 2026-10-18, for spec 0012: CCH-2 names end user labels and resource grants as security related entries and says what "permission answers" means; feature 61 builds the signal, cache, job, and outbox runtime.
**Updated**: 2026-10-19, for spec 0013: SIG-1 gives `session` an `["*"]` value, SIG-3 and CCH-2 point to spec 0013 for how a signal joins its transaction and when the writer removes its own entry, REP-2 lists `SignalOptions`, and five `ORVANO_SIGNALS__*` variables are added (the fifth, `HANDLERTIMEOUT`, from the spec 0013 review settlement).
**Updated**: 2026-10-19, for spec 0013 milestone 2 (job queue): JOB-1 adds `trace_parent` and makes the dedupe index a plain unique index, JOB-2 and JOB-4 point to spec 0013 for the claim, fence, and transaction join, JOB-4's default concurrency is also capped at half the platform pool, and JOB-5 schedules are declared in code and synced. Same day, after the milestone 2 cross check: JOB-1's due index splits into a `queued` and a `running` index, and `jobs.schedule` drops `kind` and `payload`. 2026-10-20, spec 0013's milestone 2 review settlement: JOB-1's dedupe key is unique per kind and project, and JOB-4's concurrency limit is half the platform pool less one.
**Updated**: 2026-10-20, for spec 0013 milestone 3 (module outbox): EVT-1 adds `trace_parent`, registers through `AddOutbox` and `HasOutbox()`, and reads through `IOutboxRegistry`; EVT-2 names `IOutboxEvent` and `AddEventHandler`, a subscriber being its own job kind; EVT-3 points to spec 0013 for the dispatcher and measures the 7 days from `occurred_at`; JOB-4's limit becomes half the platform pool less two (the dispatcher's loop connection) with a pool of at least 6.
**Updated**: 2026-10-23, for spec 0014: IMG-3 names the `sha-<commit>` tag, the workflow that publishes it, and the SDK conformance suites that pull it.

## Summary

Orvano ships as one server container image that runs the API, the background jobs, and realtime in one process, and needs nothing but PostgreSQL beside it. Jobs, module events, cache invalidation, and fan out across replicas all use PostgreSQL (a queue table, an outbox table, and `LISTEN/NOTIFY`, its built in message channel). The console is built into the same image and served from the same origin under `/console`, and a self hoster installs with one compose file that adds Caddy for automatic HTTPS. This spec fills every gap that specs 0002, 0003, 0004, 0005, and 0006 left for feature 6, and the scaffold builds from it.

## Decision

**Chosen option**: Option 1: a modular monolith in one image, roles chosen by config, with PostgreSQL as the only required dependency.

Every install runs `ghcr.io/orvanohq/orvano` plus PostgreSQL 18; everything else (Caddy, an S3 compatible store, an OpenTelemetry collector, PgBouncer) is optional and plugs in by config.

**Implementation skills**: `containerize-aspnetcore` (`github/awesome-copilot`, `.claude/skills/containerize-aspnetcore/`) · `docker-compose-patterns` (`docker/skills`, `.claude/skills/docker-compose-patterns/`) · `caching` (`codewithmukesh/dotnet-claude-kit`, `.claude/skills/caching/`) · `opentelemetry` (`codewithmukesh/dotnet-claude-kit`, `.claude/skills/opentelemetry/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `postgresql-table-design` (`wshobson/agents`, `.claude/skills/postgresql-table-design/`) · `testcontainers` (`wshaddix/dotnet-skills`, `.claude/skills/testcontainers/`) · `vite` (`.claude/skills/vite/`) · `pnpm` (`.claude/skills/pnpm/`)

**Where a skill disagrees, this spec wins** (rule GEN-0 in spec 0002). Known conflicts: `containerize-aspnetcore` installs `curl` for health checks and uses full base images (use **IMG-2** and **IMG-4** instead: chiseled image, health check through the server binary); the `caching` and `opentelemetry` skills show Redis backplanes and Aspire ServiceDefaults (use **CCH-1** and **DEV-2** instead).

## Proposed stack

| Layer                 | Choice                                                                                                     | Reason                                                                                                                               |
| --------------------- | ---------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| Architecture          | Modular monolith (spec 0002 LAY-1), one image, roles `api` and `worker` chosen by `ORVANO_ROLES`           | Simplest to self host; splits into separate api and worker containers later without a code change.                                   |
| Language and runtime  | C# 14 on .NET 10, JIT with ReadyToRun                                                                      | Given; ReadyToRun (precompiled code) starts fast while keeping full EF Core and reflection support that Native AOT limits.           |
| Web framework         | ASP.NET Core Minimal APIs on Kestrel                                                                       | Given by spec 0002 API-1.                                                                                                            |
| Platform database     | PostgreSQL 18 only (minimum 18), EF Core 10 with Npgsql 10                                                 | One version to test; 18 brings built in `uuidv7()` for feature 8.                                                                    |
| Project databases     | One database per project (spec 0006 TEN-1), on the platform server by default, a separate server optional  | Smallest install by default, room to split.                                                                                          |
| Connection pooling    | One Npgsql pool per project database, created lazily, small and pruned, with an install wide cap           | No extra service; PgBouncer optional for large installs.                                                                             |
| Background jobs       | In house PostgreSQL queue (`FOR UPDATE SKIP LOCKED`, woken by `NOTIFY`), cron via `Cronos` (MIT)           | No license risk, no mediator (spec 0002 API-7), a few hundred lines Orvano owns.                                                     |
| Module events         | Transactional outbox table per module, delivered by the job runner                                         | Nothing lost on a crash; the same stream realtime and webhooks read later (features 33 to 36).                                       |
| Cache                 | `HybridCache` in memory per replica, tag invalidation broadcast over `NOTIFY`                              | No Redis; replicas stay consistent (spec 0002 PRF-2).                                                                                |
| Cross replica signals | One `LISTEN` connection per process on channel `orvano_signal`                                             | Shared by job wake ups, cache invalidation, and session revocation (spec 0006 TOK-6); a hint backed by polling, never the only path. |
| Password hashing      | Argon2id via `NSec.Cryptography` (MIT, libsodium)                                                          | Maintained, native speed for the 250 ms target, one lane matches spec 0006 PWD-1.                                                    |
| File storage          | `IFileStore` with two providers: local volume (default) and S3 compatible via `AWSSDK.S3` (Apache 2.0)     | Zero setup for small installs, scale out on S3, MinIO, R2, or Garage.                                                                |
| Console               | React and Vite SPA, built into the server image, served same origin at `/console`                          | One container, one origin, so spec 0006 CON-1 and CON-2 cookies and CSRF work without CORS.                                          |
| CORS                  | ASP.NET Core CORS middleware with a per project policy provider, exact origins only                        | Applies spec 0006 NET-4; header lists come from one place so SDK headers always pass.                                                |
| TLS                   | Caddy 2.11 in the reference compose (automatic Let's Encrypt), or Kestrel with operator supplied PEM files | Two supported ways (spec 0006 NET-1), no unmaintained ACME plugin in the server.                                                     |
| Container image       | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, multi arch (amd64, arm64), published to GHCR        | No shell, non root, glibc for libsodium.                                                                                             |
| Install               | Docker or Podman compose: Orvano, PostgreSQL, Caddy, plus a one shot `migrate` step                        | The one supported install form for the first release.                                                                                |
| Local development     | `deploy/compose.dev.yaml` for PostgreSQL (and an optional OpenTelemetry dashboard), apps run directly      | Works with Docker or Podman (spec 0002 TST-5a), no Aspire to learn.                                                                  |
| Operator commands     | Subcommands of the server binary via `System.CommandLine` 2 (MIT)                                          | `orvano serve`, `migrate`, `init`, `healthcheck`, `admin …`; host access only.                                                       |
| Observability         | OpenTelemetry (spec 0002 LOG-4), OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set                            | Already decided; this spec places it in `Orvano.Host`.                                                                               |

## Architecture rules

Each rule has an ID so reviews and later specs can cite it. **MUST** fails the review; **SHOULD** needs a written reason to skip.

### Repos and layout

- **REP-1 (MUST)**: The core monorepo `orvanohq/orvano` has these top level folders:

  ```text
  orvano/
    server/      the .NET solution exactly as spec 0002 LAY-1 (Orvano.slnx, src/, tests/, benchmarks/)
    console/     the React app (spec 0003), its own package.json and pnpm lockfile
    api/         the OpenAPI contract and generator config (feature 10)
    deploy/      Dockerfile, compose.yaml, compose.dev.yaml, Caddyfile, .env.example
    docs/        planning: scope and specs
    .github/     workflows (feature 7) and repo files (feature 58)
    AGENTS.md  CLAUDE.md  README.md  LICENSE NOTICE
  ```

- **REP-2 (MUST)**: The server solution adds one project to LAY-1: `src/Orvano.Infrastructure/`, holding the job runner, the outbox dispatcher, the signal listener, cache invalidation, file store providers, the root key provider (spec 0006 SCR-2), and the project database connection factory. Its only project reference is `Orvano.SharedKernel`; it may use EF Core, Npgsql, `NSec`, `AWSSDK.S3`, and `Cronos` packages. It owns and binds its own options classes (`ProjectDbOptions`, `JobOptions`, `SignalOptions`, `StorageOptions`, `TlsOptions`) per spec 0002 CFG-1; the platform database options stay with `Orvano.Host` (CFG-5), and Infrastructure uses the `NpgsqlDataSource` Host registers. Modules see these through interfaces in `Orvano.SharedKernel` (`IJobQueue`, `IJobHandler<T>`, `IOutbox`, `IOutboxRegistry`, `ISignalBus`, `ICacheInvalidator`, `IFileStore`, `IRootKeyProvider`, `IProjectDbConnectionFactory`), never through `Orvano.Infrastructure` types. `Orvano.Host` registers them. An architecture test enforces that no module references `Orvano.Infrastructure`.
- **REP-3 (MUST)**: SDK repos in the `orvanohq` org, one per package: `sdk-flutter` (`orvano_flutter`), `sdk-dart` (`orvano_server`), `sdk-js` (`@orvano/client`), `sdk-js-server` (`@orvano/server`), `sdk-nextjs` (`@orvano/nextjs`), `sdk-dotnet` (`Orvano.Server`). Package names stay with specs 0004 and 0005 and feature 52.

### Runtime shape

- **RUN-1 (MUST)**: One executable named `orvano` (`AssemblyName`) with subcommands: `serve` (the default when no command is given), `migrate`, `init`, `healthcheck`, and `admin <command>` (spec 0006 CON-6 and SCR-2 commands). Built with `System.CommandLine` 2; `--help` and `--version` work with no configuration. Commands other than `serve` never open an HTTP port. `healthcheck` and `init` run without building the web host or registering modules, so they start fast and need no database or root key.
- **RUN-2 (MUST)**: `ORVANO_ROLES` lists the roles a `serve` process runs: `api` (HTTP, console files, realtime sockets) and `worker` (job runner, outbox dispatcher, schedules). Default `api,worker`. An unknown role or an empty list fails startup (spec 0002 CFG-4). Every role runs the signal listener.
- **RUN-3 (MUST)**: Any number of replicas of any role may run at once. Nothing relies on being the only instance: jobs are claimed with `SKIP LOCKED`, schedules fire through a unique insert, and caches invalidate through signals. Running more than one `api` replica requires the `s3` file store (FIL-2). The reference compose runs one replica.
- **RUN-7 (MUST)**: Upgrades in the first release stop every replica, run `orvano migrate`, then start the new version (backwards compatibility is not required until after beta). Rolling upgrades with expand and contract migrations are a follow up after beta.
- **RUN-4 (MUST)**: The container listens on HTTP port 8080 (`ASPNETCORE_HTTP_PORTS`) and, when TLS files are set (TLS-2), HTTPS on 8443. Routes: `/console` and below serve the console; `/health/live` and `/health/ready` per spec 0002 LOG-6; every other path is the API (prefix and versioning from feature 10). `/` redirects to `/console`.
- **RUN-5 (MUST)**: The server checks `server_version_num >= 180000` on the platform database at startup and on every project database server it connects to, and refuses to start (or to provision) on an older version.
- **RUN-6 (MUST)**: User code (functions, feature 37) and server rendered sites (feature 41) never run inside this process or image. Their sandbox is feature 37's decision.

### PostgreSQL and project databases

- **PG-1 (MUST)**: PostgreSQL 18 is the only supported major. Compose files and Testcontainers (spec 0002 TST-5) pin the same exact `postgres:18.<minor>` tag, chosen at scaffold as the latest 18 minor and bumped by Dependabot.
- **PG-2 (MUST)**: Project databases are created on the server named by `ORVANO_PROJECTDB__HOST` and friends. Every unset `ORVANO_PROJECTDB__*` key falls back to the matching `ORVANO_DATABASE__*` value (spec 0002 CFG-5), so by default they live on the platform server. Provisioning uses a separate admin role (`ORVANO_PROJECTDB__ADMINUSERNAME`, `__ADMINPASSWORD` or `_FILE`; `orvano_provisioner` in the reference compose) that holds `CREATEDB` and `CREATEROLE` only; it is never used for data queries. Each project gets its own login role per spec 0006 TEN-1 and TEN-3; its password is generated and stored under SCR-1. Database and role naming, project schema migrations, idempotent provisioning, and deletion belong to feature 15.
- **PG-2a (MUST)**: The platform database has one owner role (`orvano` in the reference compose), used by both `migrate` and `serve` in the first release. A separate DDL role is a follow up.
- **PG-3 (MUST)**: Project connections come only from `IProjectDbConnectionFactory`. It keeps one `NpgsqlDataSource` per project, created on first use. Each pool: `Maximum Pool Size` `ORVANO_PROJECTDB__POOLSIZE` (default 5), `Minimum Pool Size=0`, `Connection Idle Lifetime=60`, `Keepalive=30`, multiplexing off. The factory counts connections checked out per project; a data source is disposed only when its count is zero and it has been unused for `ORVANO_PROJECTDB__IDLEEVICTION` (default 10 minutes), under the same lock that hands out connections. Connections that must stay open for a session (a `LISTEN` on a project database, the SQL editor's cancel path) never come from these pools; features 27 and 34 open them separately.
- **PG-4 (MUST)**: All caps are per process. A cap, `ORVANO_PROJECTDB__MAXCONNECTIONS` (default 150), bounds open project connections across all pools through one async semaphore, released when a connection is returned. A caller waits at most the command timeout (spec 0002, default 30 s) for a slot, then gets 503 with `Retry-After`. The platform pool is separate (`Maximum Pool Size` 30, `ORVANO_DATABASE__POOLSIZE`). The install guide states the PostgreSQL `max_connections` to set: replicas × (platform pool + project cap + 2, for the signal listener and provisioning) + 10 (migrate, admin sessions, and PostgreSQL's reserved slots). One replica at the defaults needs 192; the reference compose sets 300.
- **PG-5 (SHOULD)**: PgBouncer in transaction mode is a documented option for large installs, for project databases only. The platform database never goes through it, because `LISTEN` needs a session connection.

### Signals (`LISTEN/NOTIFY`)

- **SIG-1 (MUST)**: Each process holds one dedicated session connection to the platform database (outside the platform pool, `Keepalive=30`) that runs `LISTEN orvano_signal`. Payloads are JSON `{ "v": 1, "k": "<kind>", "d": [<values>] }` under 1000 bytes: ids and tags only, never data or secrets. Kinds: `job` (wake the runner, no values), `cache` (tags to invalidate, or `["*"]` to flush everything), `session` (revoked session ids, spec 0006 TOK-6, or `["*"]` to reload the whole revoked set), `hb` (heartbeat). Realtime picks its own data path in feature 33; this channel carries no row data.
- **SIG-2 (MUST)**: A signal is a hint, never the only path. The listener sends itself an `hb` signal every 30 s; no `hb` received within 90 s, or a dropped connection, counts as disconnected. While disconnected the process bypasses its cache (reads go to the database), polls the sessions table for revocations every 5 s, and retries the connection with backoff up to 30 s. On reconnect it clears its whole cache, reloads the revoked session set, and wakes the job runner, because signals sent during the gap are lost. `/health/ready` stays up while disconnected, since this fallback keeps behaviour correct.
- **SIG-3 (MUST)**: Signals are sent with `pg_notify` inside the same transaction as the change they announce, so they fire only on commit. To limit commit contention (each `NOTIFY` takes a database wide lock at commit), a transaction sends at most one signal per kind: values are batched, a `cache` batch over the size limit becomes `["*"]`, and a `job` signal is sent only when a job is due now. Only platform database (control plane) transactions send signals; writes to project databases (the data plane) never do. How a signal joins the transaction (a batch per `DbContext`, emptied at every transaction start and sent by a transaction interceptor just before commit) is spec 0013 SND-1 to SND-8; an oversize `session` batch also becomes `["*"]`.

### Background jobs

- **JOB-1 (MUST)**: Jobs live in the platform database, schema `jobs`, owned by `Orvano.Infrastructure` with its own `DbContext` and migrations history table (spec 0002 DAT-1). Table `jobs.job`: `id uuid` (v7), `kind text`, `payload jsonb`, `project_id uuid null`, `status` (`queued`, `running`, `succeeded`, `dead`), `attempt int`, `max_attempts int`, `run_at timestamptz`, `locked_until timestamptz null`, `locked_by text null`, `last_error text null`, `dedupe_key text null`, `trace_parent text null` (the enqueuing trace, spec 0013 JTL-3), `created_at`, `finished_at`. A unique index on `(kind, project_id, dedupe_key)` (`NULLS NOT DISTINCT`, partial on `dedupe_key IS NOT NULL`, so rows without a key never collide), across every status, so a key stays taken within its kind and project until the row is deleted by JOB-3 cleanup, and no kind or project can suppress another's job; enqueue is `ON CONFLICT (kind, project_id, dedupe_key) WHERE dedupe_key IS NOT NULL DO NOTHING`. Partial indexes on `(run_at, id)` where `status = 'queued'` and on `(locked_until, id)` where `status = 'running'`, so an idle poll stays a short range scan. The table uses `fillfactor` 70 and `autovacuum_vacuum_scale_factor` 0.05, since its rows change often. Every column, CHECK, and index is spec 0013 JDB-1 to JDB-4.
- **JOB-2 (MUST)**: Delivery is at least once. Every `IJobHandler<T>` is idempotent and takes the job's cancellation token. A worker claims up to its free slots with `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED LIMIT n) RETURNING`, in `run_at, id` order, taking jobs that are `queued` with `run_at` due, or `running` with an expired `locked_until` (a crashed worker's job). Each claim adds one to `attempt` and sets the lease (`locked_until`, default 5 minutes; a handler may declare a longer lease for its kind). A handler that runs longer renews its lease every third of the lease length. There is no priority in the first release. The claim statement, the attempt fence on every later write, and renewal are spec 0013 CLM-1 to CLM-8.
- **JOB-3 (MUST)**: A failed attempt goes back to `queued` with `run_at` pushed out by exponential backoff with jitter (base 10 s, cap 1 hour). When `attempt` reaches `max_attempts` (default 10) the job becomes `dead`, including a job that keeps crashing its worker. A built in cleanup schedule deletes `succeeded` jobs after 7 days and `dead` jobs after 30 days. Dead jobs are visible to operators (console screen comes with feature 46).
- **JOB-4 (MUST)**: Enqueue goes through `IJobQueue` inside the caller's transaction when there is one, and sends a `job` signal per SIG-3. The runner also polls every 5 seconds (`ORVANO_JOBS__POLLINTERVAL`) as the fallback. Concurrency per worker process: `ORVANO_JOBS__CONCURRENCY` (default the smallest of processor count × 2, 16, and half the platform pool less two). Startup fails when an explicit value is more than half the platform pool less two (PG-4; the two are the job runner's and the outbox dispatcher's loop connections, spec 0013 JOP-4 and OPL-1), so jobs cannot starve API requests of connections. How the job joins the caller's transaction, and the overload for a caller with no transaction, are spec 0013 ENQ-1 to ENQ-6; options are JOP-1 and JOP-2.
- **JOB-5 (MUST)**: Recurring work lives in `jobs.schedule` (`name` unique, `cron text`, `enabled`, `next_run_at`, `last_fired_at`; each schedule's kind and payload come from its code registration, spec 0013 SCH-2). Cron expressions are parsed by `Cronos` in UTC. A worker fires a schedule by inserting a job with `dedupe_key = '<name>:<fire time>'`, so many workers fire it exactly once (JOB-1). Schedules are declared in code (`AddJobSchedule`) and synced into the table at worker start; missed fires collapse into one (spec 0013 SCH-1 to SCH-7). First schedules: job cleanup, outbox cleanup (EVT-3), local file store temp cleanup (FIL-2), signing key rotation (spec 0006 TOK-4), expired session cleanup.
- **JOB-6 (MUST)**: Work that must outlive a request goes through `IJobQueue` and nothing else (closes spec 0002 ASY-5). Jobs keyed to a project carry `project_id`, and the handler runs inside an `IProjectContext` set to it (spec 0002 API-9).

### Module events (outbox)

- **EVT-1 (MUST)**: A module that announces something adds an `outbox` table to its own schema (`<module>.outbox`: `id uuid` v7, `type text`, `payload jsonb`, `project_id uuid null`, `occurred_at`, `dispatched_at null`, `trace_parent text null`) through one shared EF configuration in `Orvano.SharedKernel` (`modelBuilder.HasOutbox()`, which also adds a partial index on `id` where `dispatched_at IS NULL`), registers its schema and event types with `services.AddOutbox` in `Add<Module>Module` (read back through `IOutboxRegistry`), and writes the event through `IOutbox.Publish` in the same `SaveChanges` and transaction as the change (closes spec 0002 LAY-4). The contracts, the transaction join, and the checks are spec 0013 OBX, OTB, and PUB. Outboxes live in the platform database only; events caused by writes to project databases are feature 34's decision.
- **EVT-2 (MUST)**: Event types are public records in the publishing module's `Contracts/`, versioned by name (`auth.user.created.v1`). Each implements `IOutboxEvent` (static `Type`). Subscribers implement `IEventHandler<TEvent>` in their own module and register in their `Add<Module>Module` with `AddEventHandler<TEvent, THandler>(name)`; the name is the subscriber's delivery job kind (spec 0013 OBX-4, OBX-5, DLV-1).
- **EVT-3 (MUST)**: The outbox dispatcher (worker role, woken by the `job` signal and the JOB-4 poll) loops over the registered outboxes. For each, in one platform database transaction, it claims up to 100 undispatched rows with `FOR UPDATE SKIP LOCKED`, enqueues one job per subscriber (`dedupe_key = '<event id>:<subscriber>'`), and sets `dispatched_at`, so several dispatchers can run and nothing is enqueued without being marked. Delivery to subscribers is at least once and retried by JOB-3; handlers are idempotent. There is no ordering guarantee: handlers use the event's `id` (uuid v7, time ordered) and `occurred_at` when order matters. The outbox cleanup schedule (JOB-5) deletes dispatched rows 7 days after they occurred. The dispatcher's loop, batch, wake, delivery job, and cleanup are spec 0013 DSP, DLV, and OCL.
- **EVT-4 (SHOULD)**: Features 33 to 36 read the same outbox stream for realtime, platform events, and webhooks rather than adding a second event path.

### Cache

- **CCH-1 (MUST)**: `HybridCache` with only its in memory level. No `IDistributedCache` is registered. Keys and tags follow spec 0002 PRF-2.
- **CCH-2 (MUST)**: `RemoveByTagAsync` is wrapped by an `ICacheInvalidator` that sends a `cache` signal and removes locally once the transaction commits (spec 0013 CAC-3); every process applies received tags to its own cache. With SIG-2 in place, the PRF-2 cap rises from 60 seconds to 5 minutes for entries that have a named invalidation path and are not security related. Security related entries (roles, memberships, end user labels, resource grants, project status, API key metadata, signing key lookups) keep the 60 second cap. Permission answers (an allow or deny decision) and API key validity stay uncached (unchanged); the inputs a decision reads, such as a user's labels or a table's grant array, may be cached under the 60 second cap (spec 0012 LBL-2, EVL-5). Every cached entry also carries its project tag `orvano:project:<project id>`, so a project wide change can clear it.

### Password hashing

- **HSH-1 (MUST)**: Argon2id uses `NSec.Cryptography` (`PasswordBasedKeyDerivationAlgorithm.Argon2id`), behind the `IPasswordHasher` interface spec 0006 PWD-6 asks for, in the Auth module. The class writes and parses PHC strings itself (`$argon2id$v=19$m=…,t=…,p=1$<salt>$<hash>`, unpadded base64). Parallelism is always 1. PHC `m` maps to NSec's memory size in KiB and `t` to its number of passes. Concurrency limits follow spec 0006 PWD-4. This closes PWD-6.
- **HSH-2 (MUST)**: The scaffold benchmarks a hash at the PWD-1 default (64 MiB, 3 iterations) on the reference box in `Orvano.Benchmarks`. If it exceeds 250 ms, the default is lowered toward the floor and the result recorded here. **Recorded 2026-10-16**: 91 ms on an Apple M1 Pro (macOS, native) and 195 ms in a linux-arm64 container capped at 4 CPUs and 8 GB on the same machine (the Linux libsodium build is the slower path). Both are under 250 ms, so the PWD-1 default stands. A run on the x86 reference box is still owed (feature 12 confirms that box).
- **HSH-3 (MUST)**: The image must load libsodium on both linux-x64 and linux-arm64. A startup self test hashes and verifies a fixed value and stops the server with a clear message if the native library fails. CI runs that self test (`orvano admin selftest`) inside the built image for both architectures (arm64 under emulation) before publishing. If NSec cannot load on arm64, the fallback is `BouncyCastle.Cryptography` behind the same `IPasswordHasher`, recorded here.

### File storage

- **FIL-1 (MUST)**: `IFileStore` (put stream, get stream with range, delete, exists, list by prefix) with providers `local` and `s3`, selected by `ORVANO_STORAGE__PROVIDER` (default `local`). Object keys are `<project id>/<bucket id>/<file id>` (spec 0006 TEN-4). Feature 29 builds buckets and files on it.
- **FIL-2 (MUST)**: `local` writes under `ORVANO_STORAGE__LOCAL__PATH` (default `/var/lib/orvano/storage`, a compose volume), writing to a temp file under `.tmp/` in the same volume, then renaming. Leftover temp files older than 24 hours are removed by a schedule (JOB-5). `local` is supported for a single replica only; more than one replica needs `s3` (RUN-3). Size limits, multipart, and signed URLs belong to features 29 and 30.
- **FIL-3 (MUST)**: `s3` uses `AWSSDK.S3` with `ORVANO_STORAGE__S3__ENDPOINT`, `__REGION`, `__BUCKET`, `__ACCESSKEY`, `__SECRETKEY` (or `_FILE`), and `__FORCEPATHSTYLE` (default true, for MinIO and Garage). Credentials never reach logs (spec 0006 LOG-1).

### Console serving

- **CSL-1 (MUST)**: The console base path is `/console`, fixed at build time (Vite `base: '/console/'`, spec 0003 WEB-8). The image copies the built files into the server's `wwwroot/console`.
- **CSL-2 (MUST)**: The server serves them with static file middleware and a fallback: any `GET` under `/console` that matches no file returns `index.html` with `Cache-Control: no-cache`; hashed assets get `Cache-Control: public, max-age=31536000, immutable`. Security headers per spec 0006 NET-2 apply to every console response.
- **CSL-3 (MUST)**: In development the Vite dev server (port 5173) proxies every path outside `/console` to the server at `http://localhost:5080`, so the browser sees one origin as in production. The server's development profile listens on 5080.

### TLS and proxy

- **TLS-1 (MUST)**: The reference `deploy/compose.yaml` puts Caddy (`caddy:2.11.<patch>`, pinned) in front, with a `Caddyfile` that reverse proxies `{$ORVANO_DOMAIN}` to `orvano:8080` and gets certificates automatically. Caddy's admin API stays bound inside its container. Compose declares a fixed subnet for its network and a fixed IP for Caddy, and sets `ORVANO_TRUSTED_PROXIES` to that one IP only (spec 0006 NET-1), so no other container can spoof `X-Forwarded-For`. Caddy trusts no forwarded headers from clients, so it sets the client address itself. The server, not Caddy, sends HSTS (NET-1). The Caddyfile sets no body size limit; Kestrel's limits apply (features 29 and 30 set them).
- **TLS-2 (MUST)**: Without a proxy, `ORVANO_TLS__CERTFILE` and `ORVANO_TLS__KEYFILE` (PEM) make Kestrel serve HTTPS on 8443. Setting one without the other fails startup. A changed certificate needs a restart in the first release.

### Cross origin requests (CORS)

CORS (the browser rule that decides which other websites may call the API) follows spec 0006 NET-4. These rules say how the server applies it.

- **COR-1 (MUST)**: CORS runs through ASP.NET Core's CORS middleware with one custom `ICorsPolicyProvider` in `Orvano.Host`. `UseCors` sits after routing and before authentication, so a preflight (the browser's `OPTIONS` check before the real request) is answered without a key or token and never reaches an endpoint (spec 0002 API-4 is unaffected). The provider resolves the project from the route the same way the `IProjectContext` middleware does (spec 0002 API-9; the URL shape is feature 10's), and reads that project's allowed origins from the platform database through `HybridCache`, tagged `orvano:platform:cors:<project id>`. This is a security related entry, so it keeps the 60 second cap and is invalidated by tag when the list changes (CCH-2).
- **COR-2 (MUST)**: A project's allow list holds exact origins only (scheme, host, and port, for example `http://localhost:5173`), empty by default. No wildcards and no pattern matching in the first release. An allowed origin gets `Access-Control-Allow-Origin` set to that exact origin, never `*`. `Access-Control-Allow-Credentials` is never sent (the bearer API does not use cookies, NET-4). Every response on a project route sends `Vary: Origin`. A request from an origin not on the list gets no CORS headers at all, so the browser blocks it.
- **COR-3 (MUST)**: The policy allows methods `GET`, `POST`, `PUT`, `PATCH`, `DELETE`. Allowed request headers are `Authorization`, `Content-Type`, and Orvano's own request headers; exposed response headers are `Retry-After`, the trace header, and Orvano's own response headers. Orvano's header names live once, in an `OrvanoHeaders` class in `Orvano.SharedKernel` that feature 10 fills from the API contract, and the policy reads both lists from it, so a new header is never forgotten in CORS. Preflight answers are cached by the browser for 10 minutes (`Access-Control-Max-Age: 600`).
- **COR-4 (MUST)**: Console routes and instance level routes (`/console`, console management APIs, `/health`) send no CORS headers. The console is same origin (CSL-1), so it never needs them, and no other site can call those routes from a browser.
- **COR-5 (MUST)**: Integration tests cover NET-4 through these rules: an allowed origin gets its exact origin back and no credentials header; an unknown origin gets no CORS headers; a preflight without a key succeeds for an allowed origin; console routes never send `Access-Control-Allow-Origin`; a header added to `OrvanoHeaders` appears in the preflight answer.

### Container image and release

- **IMG-1 (MUST)**: One `deploy/Dockerfile`, multi stage: a Node stage builds the console with pnpm (`--frozen-lockfile`), a .NET SDK stage restores in locked mode (spec 0002 GEN-4) and publishes `Orvano.Host` with ReadyToRun for `linux-x64` and `linux-arm64`, and the final stage is `aspnet:10.0-noble-chiseled`. Build images are pinned by digest.
- **IMG-2 (MUST)**: The final image has no shell and runs as the chiseled default non root user. Writable paths are only the storage volume and `/tmp`. Because there is no shell, the build stage creates `/var/lib/orvano/storage` and the final stage copies it with `--chown=1654:1654` (the chiseled `app` user), so a fresh named volume inherits that owner.
- **IMG-3 (MUST)**: Images go to `ghcr.io/orvanohq/orvano` with tags `<version>`, `<major>.<minor>`, `edge` (main branch), and `sha-<commit>` (every push to main, the full 40 hex commit, never moved), signed and attested per spec 0006 SUP-8. `.github/workflows/image.yml` publishes them, and after the signed `sha-<commit>` image exists it sends the contract dispatch (spec 0014 RGN-5). Every SDK's conformance suite (spec 0014 CNF-6, which pulls `sha-<pinned commit>`, including the Dart suite of spec 0004 DQA-3) and the console end to end tests (spec 0003 QA-5) pull from here.
- **IMG-4 (MUST)**: Container health checks run `orvano healthcheck`, which calls `/health/ready` on the local port and exits 0 or 1. No `curl` in the image.

### Install and migrations

- **INS-1 (MUST)**: `deploy/compose.yaml` defines `postgres`, `migrate` (the Orvano image running `orvano migrate`, restart `no`), `orvano` (depends on `postgres` healthy and `migrate` completed successfully), and `caddy`. Named volumes for PostgreSQL data (mounted at `/var/lib/postgresql`, the PostgreSQL 18 image layout), Orvano storage, and Caddy data. The root key file is mounted as a compose secret and read through `ORVANO_ROOT_KEY_FILE=/run/secrets/orvano_root_key`. A script in `deploy/postgres-init/` runs once on first start of the `postgres` container and creates the `orvano` platform owner role and database and the `orvano_provisioner` role (PG-2, PG-2a), with passwords from `.env`. It runs unchanged on Docker Compose v2 and Podman (`podman compose`).
- **INS-2 (MUST)**: `orvano migrate` applies the `jobs` schema's migrations first, then every module's in the order of spec 0002 DAT-10's `MigrationOrder` list in `Orvano.Host`, holding a PostgreSQL advisory lock on one dedicated connection, then exits. `serve` never migrates; `/health/ready` fails while migrations are pending (spec 0002 LOG-6, DAT-10). `migrate` needs no root key, so the reference compose gives the `migrate` service neither the key secret nor `ORVANO_ROOT_KEY_FILE` (least privilege).
- **INS-3 (MUST)**: `orvano init` is run once, before the first `compose up`, from the image with the install folder bind mounted, **as the operator's own user**, so it can write to a folder the operator owns and the files stay theirs. Docker and rootful Podman: `docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/out:Z" ghcr.io/orvanohq/orvano init --out /out --domain <domain>` (`podman run` takes the same flags). Rootless Podman: `podman run --rm --userns=keep-id -v "$PWD:/out:Z" ghcr.io/orvanohq/orvano init --out /out --domain <domain>` (`keep-id` maps the operator to the same uid inside; it is a rootless only flag). `:Z` relabels the folder for SELinux hosts and is ignored elsewhere. Without `--user`, rootful Docker runs init as uid 1654, which cannot write to the operator's folder, so init fails safely and its message names the `--user` form. It writes `root.key` (spec 0006 SCR-2) and a `.env` with generated database passwords and the operator's `ORVANO_DOMAIN` and `ORVANO_PUBLIC_URL`, following INS-5, and prints the backup warning. `deploy/.env.example` documents every variable with no usable secrets (spec 0006 SCR-5).
- **INS-5 (MUST)**: The install folder is the boundary that protects `root.key`. `root.key` is 0644 and `.env` is 0600, both owned by the operator, inside a folder at 0700. 0644 is what lets the container's non root user (1654, IMG-2) read the mounted secret: compose file secrets are bind mounts that keep host ownership, so under rootful Docker and rootful Podman the file belongs to the operator's uid, and under rootless Podman it appears as root's; either way only the "others" read bit reaches uid 1654. The 0700 folder keeps every other host user out (a host root, and anyone in the `docker` group, can read anything, which spec 0006 already places out of scope). `.env` stays 0600 because only compose, running as the operator, reads it. Compose must therefore run as the same user that ran init (or as root). Compose's long syntax `uid`, `gid`, and `mode` on file secrets is not relied on (Docker Compose ignores it). The server does not check the key file's mode. `init` works in this order, so a refusal changes nothing and a failure leaves nothing behind:
  1. If `root.key` or `.env` already exists in the folder, refuse, changing nothing (not even the folder's mode).
  2. Create the folder if it is missing. Set it to 0700 whether it is new or not, and print one line saying so. Never touch other files in it.
  3. Read the folder's mode back. If it is not 0700 (a folder the operator does not own, a file system without POSIX modes such as FAT or some NFS and CIFS mounts), fail with a message that names the fix: `chown` the folder to yourself, or use a new folder on a local disk.
  4. Create `.env`, then `root.key`, each with create new semantics, then set `root.key` to 0644 explicitly so a strict umask cannot leave it at 0600. If any step fails, delete whatever this run created before exiting with an error.
  5. Print the warning: `root.key` is safe only inside this folder; any copy (a backup, `cp`, `tar`, `docker cp`) must be made owner only (0600) or encrypted, and kept apart from database backups.

  On Windows outside a container, init skips the mode steps and says so; the Docker Desktop VM is the boundary there.

- **INS-4 (SHOULD)**: Running the image alone against an existing PostgreSQL works and is documented as untested in the first release. Kubernetes and a Helm chart wait until after alpha.

### Local development

- **DEV-1 (MUST)**: `deploy/compose.dev.yaml` starts PostgreSQL (the PG-1 tag, port 5432) and, under the compose profile `otel`, the standalone Aspire dashboard as an OTLP receiver and viewer. Contributors run `dotnet run --project server/src/Orvano.Host` and `pnpm --dir console dev`. The README documents Docker first and Podman second (spec 0002 TST-5a).
- **DEV-2 (MUST)**: No Aspire AppHost or ServiceDefaults project. OpenTelemetry, health checks, and resilience setup live in `Orvano.Host` (closes the spec 0002 follow up on Aspire).

## Configuration required

New variables introduced here (all follow spec 0002 CFG rules and accept `_FILE`):

- `ORVANO_ROLES`: roles for `serve` (default `api,worker`)
- `ORVANO_PROJECTDB__HOST`, `__PORT`, `__SSLMODE`, `__ADMINUSERNAME`, `__ADMINPASSWORD`: project database server and provisioning role (fall back to `ORVANO_DATABASE__*`)
- `ORVANO_PROJECTDB__POOLSIZE` (5), `__MAXCONNECTIONS` (150), `__IDLEEVICTION` (10 minutes)
- `ORVANO_DATABASE__POOLSIZE` (30, at least 6)
- `ORVANO_SIGNALS__HEARTBEATINTERVAL` (30 s), `__HEARTBEATTIMEOUT` (90 s), `__RECONNECTBACKOFFCAP` (30 s), `__FALLBACKPOLLINTERVAL` (5 s), `__HANDLERTIMEOUT` (10 s): SIG-2's timings and the per call limit for signal handlers (spec 0013 SOP-1)
- `ORVANO_JOBS__CONCURRENCY` (smallest of processor count × 2, 16, and half of `ORVANO_DATABASE__POOLSIZE` less two), `ORVANO_JOBS__POLLINTERVAL` (5 s)
- `ORVANO_STORAGE__PROVIDER`, `__LOCAL__PATH`, `__S3__ENDPOINT`, `__S3__REGION`, `__S3__BUCKET`, `__S3__ACCESSKEY`, `__S3__SECRETKEY`, `__S3__FORCEPATHSTYLE`
- `ORVANO_TLS__CERTFILE`, `ORVANO_TLS__KEYFILE`
- `ORVANO_DOMAIN`: compose only, read by Caddy

Set by the install from earlier specs: `ORVANO_PUBLIC_URL`, `ORVANO_TRUSTED_PROXIES`, `ORVANO_ROOT_KEY_FILE` (spec 0006), and `ORVANO_DATABASE__*` (spec 0002 CFG-5).

## Consequences

**Positive**:

- A self hoster runs three containers (Orvano, PostgreSQL, Caddy) and learns one database; backups cover jobs, events, and data together.
- Jobs and events commit in the same transaction as the change that caused them, so a crash never loses or invents one.
- Scale out is a config change (more replicas, or split `api` and `worker`), not a rewrite.
- Every open question owed to feature 6 by specs 0002, 0003, 0004, 0005, and 0006 has an answer.

**Negative / tradeoffs**:

- PostgreSQL carries queue, outbox, signal, and data load. Very high job throughput (tens of thousands a second) would need a broker later.
- Orvano owns its job runner and outbox: retries, leases, and dead jobs are code to test and maintain, with no ready made dashboard.
- `NOTIFY` is lossy across reconnects; correctness rests on SIG-2's clear and reload, which must be tested.
- Many projects on one install stress PostgreSQL connections; PG-4's cap turns overload into 503s instead of a crashed database, and big installs need PgBouncer.
- The local file store limits an install to one replica; scaling out means moving files to S3.
- First release upgrades stop the whole install for the length of `migrate`.
- NSec brings a native library (libsodium), so the image is tied to glibc and the supported architectures.
- Kestrel's own TLS needs a restart when the certificate changes.

**Neutral**:

- Spec 0002 gains `Orvano.Infrastructure` in LAY-1 and a looser PRF-2 cap; specs 0003 and 0005 get concrete values for the base path and dev proxy.
- The supported PostgreSQL range may widen later if self hosters ask.

## Follow-up

- [x] Before the scaffold: apply the spec 0002 to 0006 edits below, so the scaffold never builds against a rule this spec changed.
- [x] Scaffold (`/develop stack & architecture`): build the REP-1 layout, RUN-1 commands, INS-1 compose, IMG-1 image, CSL-1 to CSL-3 console serving, and DEV-1, and run the HSH-2 benchmark and HSH-3 self test on amd64 and arm64. Done 2026-10-16; HSH-3 passed on both, HSH-2 recorded above.
- [x] Spec 0002 PRF-1: amended to allow ReadyToRun, matching this spec's stack table and the Dockerfile.
- [x] Apply INS-2, INS-3, and INS-5 to `orvano init`, `README.md`, and `deploy/compose.yaml`. Done 2026-10-16, covered by the init CLI tests and a full install on Docker Desktop.
- [ ] Prove INS-3 and INS-5 on a Linux host (the scaffold was tested only on Docker Desktop for macOS). Expected per engine: rootful Docker with Compose v2 and rootful Podman, the secret mounts as operator owned 0644 and uid 1654 reads it; rootless Podman with `podman compose`, it appears root owned 0644 and uid 1654 reads it; on every engine, another host user cannot read `root.key` and init without `--user` fails safely. If a compose provider copies file secrets with its own mode (some `podman-compose` versions use 0444), record what it does here.
- [ ] Feature 12: run HSH-2 on the x86 reference box and record it here; the install guide covers SELinux hosts (a `:Z` relabel on bind mounts) and both init commands of INS-3.
- [x] Spec 0002: add `Orvano.Infrastructure` to LAY-1 and LAY-3 (REP-2), point ASY-5 and LAY-4 to JOB-6 and EVT-1, raise the PRF-2 cap per CCH-2, set TST-5's PostgreSQL pin per PG-1, close the Aspire follow up (DEV-2).
- [x] Spec 0003: set the base path (`/console`) and dev proxy (CSL-3) in `shared/config.ts`, and close its feature 6 follow up.
- [x] Spec 0004: name `sdk-flutter` and `sdk-dart` and the GHCR image (REP-3, IMG-3) and close its feature 6 follow up.
- [x] Spec 0005: name `sdk-js`, `sdk-js-server`, `sdk-nextjs` (REP-3), recording that one repo per package was kept over one TypeScript repo.
- [x] Spec 0006: PWD-6 is closed by HSH-1, NET-1 by TLS-1 and TLS-2, TEN-3's pooling plan by PG-3 and PG-4.
- [ ] Feature 61 (Background runtime): build REP-2's runtime pieces in `Orvano.Infrastructure`, signals and cache invalidation first (SIG-1 to SIG-3, CCH-1, CCH-2: `ISignalBus`, `ICacheInvalidator`, the listener, heartbeat, and reconnect), then the job runner and schedules (JOB-1 to JOB-6), then the outbox (EVT-1 to EVT-4). The signal and cache contracts are spec [0013](../0013-background-runtime/index.md). Spec 0012's label cache test (AC-10) and features 13 and 14 wait on the first part.
- [ ] Feature 10: the API prefix must stay outside `/console` and `/health` (RUN-4); fill `OrvanoHeaders` with every Orvano request and response header and the trace header name (COR-3).
- [ ] Feature 13: the console's create project flow asks for app origins and validates them as exact origins (COR-2); changing them invalidates the CORS cache tag (COR-1).
- [ ] Later: consider origin patterns (for example any port on `localhost`) if developers ask; not in the first release (COR-2).
- [ ] Feature 12: the self host install guide documents INS-1 to INS-4, PG-4's `max_connections` formula, FIL-2's shared volume note, and both TLS ways.
- [ ] Feature 15: project database and role naming, project schema migrations (trigger, ordering, partial failure), idempotent and resumable provisioning (`CREATE DATABASE` cannot run in a transaction), and deleting a project's database and role (PG-2).
- [ ] Feature 33: choose realtime's low latency data path; SIG-1 carries no row data, and the outbox to job path takes seconds.
- [ ] Feature 34: where events for writes to project databases come from (EVT-1 outboxes cover the platform database only).
- [ ] Features 29 and 30: object size limits, Kestrel body limits, multipart thresholds, and signed download URLs (FIL-2, TLS-1).
- [ ] Feature 23: per project fairness for jobs, so one project cannot fill the queue (JOB-2).
- [ ] Features 10 and 33: the realtime socket path and its timeouts (RUN-4).
- [ ] Feature 37: decide the function sandbox outside this image (RUN-6).
- [ ] Feature 46: an operator view of dead jobs (JOB-3).
- [ ] Feature 53: PostgreSQL major upgrades and rolling upgrades with expand and contract migrations after beta (RUN-7, PG-1); a separate DDL role for `migrate` (PG-2a).
- [ ] `containerize-aspnetcore`, `docker-compose-patterns`, `caching`, and `opentelemetry` were installed for this spec and are not yet listed in `AGENTS.md`; the container and compose skills belong with `deploy/` work, the caching and OpenTelemetry skills with server work (`/sync` or `/audit` places them).
- [ ] MCP servers offered and skipped for now: GitHub MCP (`github/github-mcp-server`) for CI and GHCR, MinIO MCP (`minio/mcp-server-aistor`) for the S3 provider. Connect them in your MCP settings when that work starts.
- [ ] Reconsider Helm and Kubernetes after alpha (INS-4).

## Rationale

Reasoning, options, and references: see [rationale.md](rationale.md).
