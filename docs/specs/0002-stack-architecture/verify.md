# Verify: Stack & architecture · spec 0002 · updated 2026-09-24
_Spec 0002 is a decision spec with no numbered acceptance criteria. These steps come from its "What the scaffold must contain" list (labeled S-1 to S-7 below) and its value sourcing table. `/check verify` runs them; `/test` locks the durable ones._

## Local dev (Aspire)
- [ ] `pnpm install`, then `dotnet run --project dev/Orvano.AppHost` → the dashboard shows `postgres` running, `migrate` finished (exit 0), and `api`, `worker`, `realtime`, `console` running and healthy → S-1
- [ ] Open the `console` URL from the dashboard → the placeholder page reads "API ok, version 0.0.0" → S-1
- [ ] `curl <console-url>/v1/health` → `{"status":"ok","version":"0.0.0"}` answered through the Vite proxy (same origin) → S-1
- [ ] `curl <role-url>/internal/readyz` for api, worker, realtime → `Healthy` 200 on each → S-3
- [ ] Worker logs show "This worker is the scheduler leader" and "Listening on orvano_events, orvano_jobs", with no errors while idle → S-3

## Database
- [ ] In the dev Postgres: schema `orvano` owned by `orvano_admin`, and tables `schema_migrations`, `events`, `jobs` exist → S-2
- [ ] `orvano_admin` is LOGIN and CREATEROLE, not superuser. `orvano_app` is LOGIN, has DML on `events` and `jobs`, and only SELECT on `schema_migrations` (an INSERT as `orvano_app` is denied) → S-2
- [ ] Insert an `orvano.events` row as `orvano_app` → `dispatched_at` is set within about 2 seconds (dispatcher poll fallback) → S-3
- [ ] Insert a `running` job with an expired `lease_until` → within 30 seconds it is `queued` again, or `dead` if `attempts >= max_attempts` (lease reaper) → S-3

## Commands
- [ ] `dotnet build Orvano.slnx` → succeeds with no warnings → S-4
- [ ] `pnpm -r build` → succeeds → S-4
- [ ] `.github/workflows/ci.yml` runs green on GitHub (server, console, compose jobs) → S-4, S-5, S-6
- [ ] Drift check against an empty Postgres 18 bootstrapped by `deploy/postgres/initdb` → "EF model matches the database"; add a stray column to `orvano.jobs` and it fails with exit 1 → S-4
- [ ] Copy `deploy/compose/.env.example` to `.env` and fill in the passwords, then `docker compose -f deploy/compose/docker-compose.yml up -d --build` → `migrate` exits 0 and `api`, `worker`, `realtime` report healthy (via `orvano healthcheck`) → S-5, S-6
- [ ] `curl http://localhost/v1/health` → 200 with the health JSON; `curl http://localhost/internal/readyz` → 404; `curl http://localhost/some/deep/link` → the console `index.html` → S-6
- [ ] `docker compose ... logs` → JSON log lines, no Error or Critical entries → S-6
- [ ] `docker compose ... up -d --force-recreate migrate` → "0 migration(s) applied this run" (safe to run again) → S-6
- [ ] `grep -r "Orvano.Platform" server/` → no matches; `/v1/health` is the only product route → S-7

## Value sourcing
- [ ] Role: run `orvano` with no `ORVANO_ROLE` and no argument → exit 1 with a clear message; `ORVANO_ROLE=api orvano worker` → exit 1 (disagree); `orvano nope` → exit 1 (unknown); `orvano executor` → exit 1 (reserved)
- [ ] Expected schema version: point a role at a database with `schema_migrations` edited to version 0 or 2 → the role refuses to start (exit 1) and says which version it expected
- [ ] Project scope: `ProjectScope.RoleName("Bad-ID")` throws; a 61 character ID throws; `abc123` gives `p_abc123`
- [ ] Master keys: not read yet (no secret encryption code in the scaffold). Check again when the first secret is stored
- [ ] Storage driver: not read yet (from scope row 20). The `orvano-storage` volume mounts, and the directory is writable by UID 1654
- [ ] Public URL: set `ORVANO_PUBLIC_URL=http://127.0.0.1` in `.env` → Caddy serves on that address instead of `localhost`
- [ ] Leader lock: `pg_locks` shows one advisory lock with classid 1330796097 (0x4F525641) and objid 2, held by `orvano-worker`; kill that worker's leader connection (`pg_terminate_backend`) → the worker logs "leader connection lost" and takes the lock back within about 10 seconds
- [ ] Migration lock: two `migrate` runs started together → one waits for the other; only one applies each file
- [ ] Pool sizes: put `Maximum Pool Size=500` in `ORVANO_DB_URL` → the pool is still capped at the budget (api 40); check with a load test or `pg_stat_activity`
- [ ] Schedule timing: the reaper runs every 30 s, pruning runs hourly and on leader start; with `ORVANO_EVENT_RETENTION_DAYS=1`, dispatched events older than a day are deleted
- [ ] Telemetry: every role reports `service.name` as `orvano-api`, `orvano-worker`, and so on in the Aspire dashboard traces
- [ ] Version: change `VERSION` to `0.0.1` and rebuild → `/v1/health` returns `0.0.1`

## Acceptance criteria coverage
- S-1 (one command starts everything, console placeholder, health through the console origin): local dev steps 1 to 3
- S-2 (first migration creates the schema and tables; roles and privileges): database steps 1 and 2
- S-3 (dispatcher, job loop, pruning idle without errors; readyz on every role): local dev steps 4 and 5, database steps 3 and 4
- S-4 (dotnet and pnpm builds, CI, drift check): command steps 1 to 4
- S-5 (chiseled-extra smoke test: Npgsql and time zones): compose job in CI, command step 5 (roles exit on a missing time zone or database)
- S-6 (compose behind Caddy with `orvano healthcheck`): command steps 5 to 8
- S-7 (no product modules besides health): command step 9
