# Verify: Compose stack skeleton · spec 0002 · updated 2026-11-28
_Steps derived from spec 0002 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones._

## UI / manual
- [ ] From the repo root, run `dotnet run --project server/src/Orvano.AppHost`, open the dashboard link it prints → `postgres`, `orvano-db`, `orvano` and `orvano-executor` show Running and Healthy → AC-9
- [x] With the AppHost running, `curl http://localhost:5131/readyz` → `200 {"status":"ok"}` (the connection string came from the AppHost, not from a file) → AC-9
- [x] With the AppHost running, `aspire describe --apphost server/src/Orvano.AppHost/Orvano.AppHost.csproj` → all four resources `Healthy` → AC-9
- [ ] Open `https://localhost/healthz` in a browser after trusting Caddy's root (deploy README) → `{"status":"ok"}` with no certificate warning → AC-2

## Commands
- [x] `deploy/smoke-test.sh` → ends with `Smoke test passed.` (covers AC-1 to AC-4, AC-6 to AC-8, AC-11, AC-12) → AC-10
- [ ] In `deploy/`, `cp .env.example .env && echo "POSTGRES_PASSWORD=$(openssl rand -hex 32)" >> .env`, then `docker compose -f compose.yaml -f compose.build.yaml up --build --wait` → `caddy`, `orvano`, `orvano-executor`, `postgres` all healthy → AC-1
- [x] `curl --cacert caddy-root.crt https://localhost/healthz` and `/readyz` → both `200 {"status":"ok"}`; `curl -I http://localhost/healthz` → `308` to `https://localhost/healthz` → AC-2
- [x] `docker compose stop postgres` → within 3 s `/readyz` is `503 {"status":"unavailable"}`, `/healthz` stays `200`; `docker compose start postgres` → within 5 s of healthy `/readyz` is `200` again, `orvano` RestartCount unchanged → AC-3
- [x] `dotnet test --solution Orvano.slnx` → `ReadinessTests` (pause and unpause Postgres) and `StartupValidationTests` pass → AC-3, AC-8
- [x] `docker compose ps` → `orvano-executor` healthy with no published port; from a throwaway container on the `executor` network, `wget -qO- http://orvano-executor:8080/healthz` → `{"status":"ok"}` → AC-4
- [x] Every health response body has exactly one property, `status` (`HealthEndpointTests`); the orvano log holds an `Error` from `PostgresReadinessCheck` naming the cause after an outage, and no response carries it → AC-5
- [x] `docker compose logs --no-log-prefix orvano orvano-executor caddy | jq -R 'fromjson? // error'` → no error; orvano and executor lines carry `Timestamp` ending in `Z`, `LogLevel`, `Category`, `Message` → AC-6
- [x] `docker compose ps --format json` → only `caddy` has published ports (80/tcp, 443/tcp, 443/udp); `docker compose exec caddy nc -z -w 2 postgres 5432` and `... orvano-executor 8080` fail while `... orvano 8080` succeeds → AC-7
- [x] `docker compose --env-file <file with only ORVANO_VERSION> config --quiet` → fails naming `POSTGRES_PASSWORD`; with only `POSTGRES_PASSWORD` → fails naming `ORVANO_VERSION`; `git grep -n POSTGRES_PASSWORD=` shows no value in any committed file → AC-8
- [x] Push the branch → the CI `images` job builds `orvano` and `orvano-executor` for `linux/amd64` and `linux/arm64` without pushing, and the `smoke` job passes → AC-10
- [x] `docker inspect` both Orvano containers → `Config.User` is `1654`, `HostConfig.ReadonlyRootfs` is `true`, `Config.Healthcheck.Test` ends in `healthcheck`; `docker run --rm --entrypoint /bin/sh <image>` fails (no shell) → AC-11
- [x] Create a table in Postgres, note `openssl x509 -fingerprint` of Caddy's root, run `down` then `up --wait` → table still there, same fingerprint → AC-12
- [x] `dotnet test --solution Orvano.slnx` → `ReferenceRuleCheckerTests.IntegrationTests_referencing_the_host_passes` and `IntegrationTests_referencing_another_executable_is_caught` pass, and the existing `Test_project_referencing_an_executable_is_caught` still passes → AC-13

## Value sourcing
- [x] `/healthz` on both processes → always `ok` while the process answers, even with Postgres stopped → constant
- [x] `/readyz` status and code → flip Postgres off and on; the body follows the readiness check result → readiness check
- [x] Readiness check timing → hammer `/readyz` in a loop while Postgres is healthy; Postgres sees at most one `SELECT 1` a second (cache 1 s, shared probe) → readiness check
- [ ] Database connection → run the host with `ConnectionStrings__orvano` unset, empty, or `not a connection string` → startup fails naming `ConnectionStrings:orvano`; with `GSS Encryption Mode=Prefer` written in it, the value is respected → `ConnectionStrings:orvano`
- [x] Postgres container login and database → `docker compose exec postgres psql -U orvano -d orvano -c 'select 1'` works; changing `POSTGRES_PASSWORD` in `.env` on a fresh volume changes the password → `compose.yaml` and `.env`
- [x] Caddy site domain → set `ORVANO_DOMAIN=orvano.test` (with a hosts entry) → Caddy serves that name; unset or empty → `localhost` → `ORVANO_DOMAIN`
- [x] Caddy certificate → `localhost` gets a cert from Caddy's local authority, stored in `caddy-data` (survives `down`/`up`) → Caddy
- [x] Caddy upstream → stop `orvano` → Caddy answers `502`; start it → `200` → fixed `orvano:8080`
- [x] `healthcheck` probe port → run an Orvano image with `ASPNETCORE_HTTP_PORTS=9090` → it listens on 9090 and the container still turns healthy (probe reads the same variable) → `ASPNETCORE_HTTP_PORTS`
- [x] `ExecutorOptions.BaseUrl` → set `Executor__BaseUrl` to `https://x`, `not a url`, or empty → startup fails naming `Executor:BaseUrl` → `Executor:BaseUrl`
- [x] Image tag → set `ORVANO_VERSION=0.1.0-test` → images named `ghcr.io/orvanohq/<image>:0.1.0-test` → `ORVANO_VERSION`
- [x] Assembly version and label → build with `VERSION=1.2.3` → label `org.opencontainers.image.version=1.2.3`; build without it → label empty and assembly keeps `0.1.0-dev` → `VERSION` build argument
- [x] JSON log fields → one orvano log line has `Timestamp`, `LogLevel`, `Category`, `Message` → built in formatter
- [x] Readiness log cause → after an outage, the `Error` line holds the Npgsql message and no `Password=` → exception message
- [ ] Smoke test root certificate and settings → run `deploy/smoke-test.sh` with `POSTGRES_PASSWORD` exported in your shell and your own stack's volumes present → it ignores the variable, uses project `orvano-smoke`, and leaves your volumes untouched → smoke test

## Acceptance-criteria coverage
- AC-1 smoke test, compose up step · AC-2 HTTPS and redirect steps, browser step · AC-3 outage step, `ReadinessTests`, `StartupValidationTests` · AC-4 executor step · AC-5 single property and log cause steps · AC-6 log step · AC-7 ports and network steps · AC-8 missing settings step, startup validation tests · AC-9 AppHost steps · AC-10 smoke test, CI push step · AC-11 inspect step · AC-12 persistence step · AC-13 reference rule checker tests
