# 0002. Compose stack skeleton: Caddy, orvano, Postgres and an executor placeholder

**Date**: 2026-11-27
**Status**: In Progress

## Summary

This spec designs the first real thread through an Orvano deployment. Caddy answers HTTPS in front, `orvano` serves a liveness check (`/healthz`, "is the process up") and a readiness check (`/readyz`, "can it reach Postgres"), Postgres 17 sits behind it, and an executor placeholder answers its own health check beside it. The self hoster file names images only, a second file adds the build steps, and CI starts the whole stack on every change. For building, this means two Dockerfiles, two Compose files, one Caddyfile, a small amount of shared health and logging code, and the first integration test project.

## Requirements

**User stories**:
- As a self hoster, I want one Compose command to bring up a working, HTTPS served Orvano with health checks, so that I can tell at a glance whether my server is alive and ready.
- As a contributor, I want the AppHost to start the same pieces with one command, so that I can work without Compose.
- As a maintainer, I want CI to build every image and prove the stack starts, so that a broken Dockerfile, Compose file or Caddyfile never merges.

**Acceptance criteria** (the contract, each one checkable on its own):
- **AC-1**: On a machine with only Docker Engine and Compose v2, from `deploy/` in a fresh clone, creating `.env` from `.env.example` with a password and running `docker compose -f compose.yaml -f compose.build.yaml up --build --wait` ends with four services running and healthy: `caddy`, `orvano`, `orvano-executor`, `postgres`.
- **AC-2**: `GET https://<ORVANO_DOMAIN>/healthz` and `GET https://<ORVANO_DOMAIN>/readyz` through Caddy return `200` with the body `{"status":"ok"}`. With `ORVANO_DOMAIN` unset the domain is `localhost`, the certificate comes from Caddy's own local certificate authority and validates against Caddy's exported root, and plain HTTP on port 80 redirects to HTTPS.
- **AC-3**: While Postgres is unreachable, `/readyz` returns `503` with `{"status":"unavailable"}` within 3 seconds, `/healthz` still returns `200`, and the `orvano` process stays up. Once Postgres is back, `/readyz` returns `200` again within 5 seconds with no restart.
- **AC-4**: `orvano-executor` answers `GET /healthz` with `200` and `{"status":"ok"}` on its own port. It is not routed by Caddy and publishes no port.
- **AC-5**: The health bodies carry the `status` field only. No check name, version or error text reaches the caller. The cause of a readiness change is written to the log.
- **AC-6**: In the Compose stack, every stdout line from `orvano` and `orvano-executor` is one JSON object with `Timestamp` (UTC), `LogLevel`, `Category` and `Message`. Caddy's access and runtime logs are JSON too. Postgres keeps its own text format.
- **AC-7**: Only `caddy` publishes ports (80 and 443 on TCP, 443 on UDP). Caddy cannot reach Postgres or the executor, and the executor cannot reach Postgres.
- **AC-8**: With `POSTGRES_PASSWORD` or `ORVANO_VERSION` missing, Compose refuses to start and names the missing setting. No secret value exists in any committed file.
- **AC-9**: `dotnet run --project server/src/Orvano.AppHost` starts Postgres 17, `orvano` and `orvano-executor`. `orvano` gets its database connection from the AppHost, its `/readyz` returns `200`, and the dashboard shows both processes healthy.
- **AC-10**: On every push and pull request, CI builds both images for `linux/amd64` and `linux/arm64` without pushing them, and a smoke job runs `deploy/smoke-test.sh`, which starts the Compose stack and checks AC-1 to AC-4, AC-6 to AC-8, AC-11 and AC-12.
- **AC-11**: The `orvano` and `orvano-executor` containers run as a non root user on a chiseled base image (no shell, no package manager) with a read only root filesystem. Their container health comes from the binary's own `healthcheck` argument.
- **AC-12**: Postgres data and Caddy's certificates survive `docker compose down` followed by `up`.
- **AC-13**: The architecture tests let `Orvano.IntegrationTests` reference `Orvano.Host`, and still fail any other test project that references an executable project.

## Decision

**Chosen option**: Option 1: a narrow but real stack, tested end to end

Ship one image only Compose file for self hosters plus a build override, chiseled images with a built in health probe, three private networks, and a CI smoke test that starts the whole stack through Caddy over HTTPS.

**Implementation skills**: `docker-compose-patterns` (`docker/skills`, `.claude/skills/docker-compose-patterns/`) · `aspire` (`microsoft/aspire-skills`, `.claude/skills/aspire/`) · `testcontainers-dotnet` (`testcontainers/claude-skills`, `.claude/skills/testcontainers-dotnet/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`)

Where this spec and the `docker-compose-patterns` skill differ (built in probe in place of a health check sidecar, required password in place of a fallback, the `17` Postgres tag), this spec wins. The reasons are in the rationale.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### Files this feature creates or changes

| Path | What it is |
|---|---|
| `deploy/compose.yaml` | What a self hoster runs. Images only, no `build`. Top level `name: orvano` |
| `deploy/compose.build.yaml` | Override that adds `build` to `orvano` and `orvano-executor`. Used by contributors and CI |
| `deploy/Caddyfile` | One global option and one site block, mounted read only into `caddy` |
| `deploy/orvano.Dockerfile`, `deploy/orvano-executor.Dockerfile` | One Dockerfile per image, built with the repo root as context |
| `deploy/.env.example` | Every setting, with no secret value |
| `deploy/smoke-test.sh` | The end to end check that CI and `/check verify` both run. Needs bash, curl, jq and openssl beside Docker |
| `deploy/README.md` | Rewritten: how to run it, the settings, trusting the local certificate |
| `.dockerignore` (repo root) | Keeps `.git`, `docs`, `**/bin/`, `**/obj/`, `**/node_modules/`, `**/out/` and `**/.next/` out of the build context. The `**/` prefix matters: a bare `obj` matches only at the root, and a host `obj/project.assets.json` (built for your machine, not for Linux) would break `publish --no-restore` |
| `server/src/Orvano.ServiceDefaults/` | Gains JSON logging, the `/healthz` mapping, the health response writer and the `healthcheck` probe |
| `server/src/Orvano.Infrastructure/` | Gains the Postgres data source registration and the readiness check |
| `server/src/Orvano.Host/` | Maps `/healthz` and `/readyz`, binds `ExecutorOptions`. `appsettings.json` sets the `Microsoft.Extensions.Diagnostics.HealthChecks` log category to `Critical` |
| `server/src/Orvano.Executor/` | Maps `/healthz` |
| `server/src/Orvano.AppHost/AppHost.cs` | Database reference, wait, health checks, executor address |
| `server/tests/Orvano.IntegrationTests/` | New. Starts `Orvano.Host` in memory against Testcontainers Postgres |
| `server/tests/Orvano.ArchitectureTests/ReferenceRules.cs` | The one exception from spec 0001's amended test rule |
| `.github/workflows/ci.yml` | Two new jobs: `images` and `smoke` |

### Data model sketch

None. This feature creates no table and no migration. The Postgres image creates one empty database named `orvano`, owned by the login `orvano`. Feature 7 creates the first schema and decides the real database roles.

The only stored state is three named volumes: `postgres-data` (mounted at `/var/lib/postgresql/data`), `caddy-data` (`/data`, certificates) and `caddy-config` (`/config`).

### Services

| Service | Image | Networks | Publishes | Depends on | Container health |
|---|---|---|---|---|---|
| `caddy` | `caddy`, pinned to the current stable 2.x minor. `environment: ORVANO_DOMAIN: ${ORVANO_DOMAIN:-localhost}` | `edge` | `80:80`, `443:443`, `443:443/udp` | `orvano` healthy | `wget -qO /dev/null http://127.0.0.1:2019/config/` (Caddy's admin port, local to the container) |
| `orvano` | `ghcr.io/orvanohq/orvano:${ORVANO_VERSION:?ORVANO_VERSION is required}` | `edge`, `data`, `executor` | nothing | `postgres` healthy | Dockerfile `HEALTHCHECK CMD ["dotnet", "Orvano.Host.dll", "healthcheck"]` |
| `orvano-executor` | `ghcr.io/orvanohq/orvano-executor:${ORVANO_VERSION:?ORVANO_VERSION is required}` | `executor` | nothing | nothing | Dockerfile `HEALTHCHECK CMD ["dotnet", "Orvano.Executor.dll", "healthcheck"]` |
| `postgres` | `postgres:17` | `data` | nothing | nothing | `pg_isready -h 127.0.0.1 -U orvano -d orvano`. The `-h` matters: without it the check passes against the temporary socket only server the image runs on first start, which then restarts |

- `data` and `executor` are marked `internal: true` (no route to the outside). `orvano` is the only service on more than one network.
- `orvano` does not depend on the executor. A broken executor must not stop the API.
- Both Orvano containers listen on port 8080, the .NET container default (`ASPNETCORE_HTTP_PORTS`).
- Every service: `restart: unless-stopped`, `security_opt: no-new-privileges:true`, and the `json-file` log driver with `max-size: 10m` and `max-file: "5"`, set once through a shared YAML anchor.
- `orvano` and `orvano-executor` also get `read_only: true`, a `tmpfs` at `/tmp`, `cap_drop: [ALL]` and `stop_grace_period: 30s`.
- Health check timing for all four: `interval: 10s`, `timeout: 3s`, `retries: 5`, `start_period: 20s`. For the two Orvano images this lives in the Dockerfile `HEALTHCHECK`, so it travels with the image. Compose does not repeat it.
- The two Orvano `HEALTHCHECK` lines use exec form (a JSON array), because the chiseled image has no shell.
- Required settings use the Compose form `${NAME:?NAME is required}`, so Compose stops and names the setting. That applies to `ORVANO_VERSION` in both image names and to `POSTGRES_PASSWORD` in both places it appears (the `postgres` service and the connection string).
- No Docker socket is mounted anywhere. `docker-socket-proxy` arrives with feature 54.

### Caddyfile

One global option and one site block, nothing else:

- Global block `{ skip_install_trust }`. Without it, a `localhost` site makes Caddy try to install its root certificate in the container's trust store, which prints plain text warnings and breaks AC-6. This is the one deliberate global setting.
- Site address `{$ORVANO_DOMAIN:localhost}`. Caddy reads the variable from its own process environment, which the `caddy` service sets from `.env` (see Services). `.env` on its own only fills in the Compose file. Caddy picks the certificate source itself: its local certificate authority for `localhost`, Let's Encrypt for a real domain.
- `reverse_proxy orvano:8080` for every path.
- `encode zstd gzip`.
- `log` with `output stdout` and `format json` (the access log).

No DNS plugin, no on demand TLS, no ACME email setting. The stock image is enough until feature 83.

### Images

Both Dockerfiles follow the same shape:

1. **Build stage** on the .NET SDK image pinned to a `10.0.4xx` tag at or above the `global.json` version (`10.0.404`), so restore never meets an older feature band. Declared with `--platform=$BUILDPLATFORM` so it always runs on the runner's own processor. It copies the root build files (`global.json`, `Directory.Build.props`, `Directory.Packages.props`, `NuGet.config`) and the needed `.csproj` files first, restores with `-a $TARGETARCH`, then copies `server/src` and runs `dotnet publish -c Release -a $TARGETARCH --no-restore -o /app -p:UseAppHost=false`. An optional `VERSION` build argument is passed as `${VERSION:+-p:Version=$VERSION}`, so an empty value leaves `<Version>` from `Directory.Build.props` in force (a bare `-p:Version=$VERSION` would set it to nothing).
2. **Final stage** on the .NET 10 ASP.NET runtime image, Ubuntu chiseled, `extra` variant (it includes culture and time zone data). It copies `/app`, sets `USER $APP_UID`, `ENTRYPOINT ["dotnet", "<assembly>.dll"]`, the `HEALTHCHECK`, and the labels `org.opencontainers.image.source` (`https://github.com/orvanohq/orvano`), `org.opencontainers.image.licenses` (`Apache-2.0`) and `org.opencontainers.image.version` (from `ARG VERSION`, declared again in the final stage).

The apps are published framework dependent (they use the runtime in the base image), so both images share the same base layers.

`compose.build.yaml` sets, per Orvano service, `build.context: ..`, `build.dockerfile: deploy/<name>.Dockerfile` and `build.args.VERSION: ${ORVANO_VERSION}`. The CI `images` job passes `VERSION=0.1.0-dev` the same way. The built image keeps the `image` name from `compose.yaml`, so the local tag is `ghcr.io/orvanohq/orvano:<ORVANO_VERSION>`.

### API surface

| Endpoint | Method | Key inputs | Key outputs | Auth | Key errors |
|---|---|---|---|---|---|
| `orvano` `/healthz` | GET | none | `200`, `{"status":"ok"}` | public | none. If the process can answer, it answers `200` |
| `orvano` `/readyz` | GET | none | `200`, `{"status":"ok"}` | public | `503`, `{"status":"unavailable"}` when Postgres does not answer |
| `orvano-executor` `/healthz` | GET | none | `200`, `{"status":"ok"}` | private network only | none |

- Content type `application/json`. Responses are never cached (the ASP.NET Core health check default headers stay on).
- These are operational endpoints. They stay out of `contract/openapi.json`, and a failing readiness check answers with the status body above, not problem+json. Probes read the status code.
- Mapped with the built in ASP.NET Core health checks (`MapHealthChecks`). `/healthz` runs no checks. `/readyz` runs the checks tagged `ready`. Healthy and degraded map to `200` and `ok`, unhealthy maps to `503` and `unavailable`.

**The `healthcheck` argument.** When the first command line argument is `healthcheck`, the process does not start the web host. It sends `GET http://127.0.0.1:<port>/healthz` with a 2 second timeout and exits `0` on `200`, else `1`, printing nothing. In each `Program.cs` this check comes first and returns the exit code before `WebApplication.CreateBuilder` runs. The container health check is liveness only on purpose: a Postgres outage must not mark `orvano` unhealthy and tempt a platform into restarting it.

### Where the code lives

| Piece | Project | Notes |
|---|---|---|
| JSON console logging | `Orvano.ServiceDefaults`, inside `AddServiceDefaults` | Built in `AddJsonConsole` with `UseUtcTimestamp`, `IncludeScopes`, a `TimestampFormat` in ISO 8601 form ending in `Z` (the formatter writes no `Timestamp` field without one), one object per line. Applied in every environment except `Development`, where the readable console stays. The OpenTelemetry log provider is unchanged |
| Health check service | `Orvano.ServiceDefaults`, inside `AddServiceDefaults` | Calls `builder.Services.AddHealthChecks()`, which `MapHealthChecks` needs. It comes with the `Microsoft.AspNetCore.App` framework, so ServiceDefaults gets no new package |
| `/healthz` mapping and the response writer | `Orvano.ServiceDefaults` | One public method that maps `/healthz`, one public response writer that `/readyz` reuses |
| The `healthcheck` probe | `Orvano.ServiceDefaults` | Called as the first lines of both `Program.cs` files |
| Postgres data source | `Orvano.Infrastructure` | One public method, `AddOrvanoDatabase(this IServiceCollection, IConfiguration)`. Registers a singleton `NpgsqlDataSource` through `Npgsql.DependencyInjection`, from the connection string named `orvano`. Reads the connection string when the data source is first resolved (or through a validated options object), never eagerly in `Program.cs`, so tests can override it. Startup validation still fails fast when it is missing. Opens no connection at startup |
| Postgres readiness check | `Orvano.Infrastructure` | An `IHealthCheck` tagged `ready`, registered as a DI singleton so its cache, its shared probe and its last state live as long as the app (`AddCheck<T>` alone makes a new instance per run). Runs `SELECT 1` with a 2 second limit |
| `/readyz` mapping | `Orvano.Host` | The executor has no readiness endpoint |
| `ExecutorOptions` | `Orvano.Host` | `BaseUrl`, an absolute `http` address. Bound from the `Executor` section, validated with `ValidateOnStart`. Nothing calls it yet. Feature 54 moves it to wherever the executor client lives |

New packages, each with its version in `Directory.Packages.props` (latest stable at build time, `10.0.x` for the Microsoft ones):

| Project | Packages |
|---|---|
| `Orvano.Infrastructure` | `Npgsql.DependencyInjection`, `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` |
| `Orvano.IntegrationTests` | `xunit.v3`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql` |
| `Orvano.ServiceDefaults` | none (spec 0001 limits it to Aspire and OpenTelemetry packages) |

No third party health check package and no Aspire client package in the host.

`Orvano.IntegrationTests.csproj` copies the shape of `Orvano.ArchitectureTests.csproj` (`OutputType` `Exe`, `IsTestProject` `true`, `IsPackable` `false`). Without `IsTestProject`, the architecture tests treat it as an unknown project and fail it.

### Contributor AppHost

- `AddPostgres("postgres")` with image tag `17`, then `AddDatabase("orvano-db", databaseName: "orvano")`. The resource is not named `orvano`, because Aspire resource names must be unique and the project already uses it. No data volume yet, since there is no data worth keeping until feature 7.
- `orvano`: `WithReference(db, connectionName: "orvano")` (which keeps the `ConnectionStrings__orvano` key) and `WaitFor(db)`, `WithHttpHealthCheck("/readyz")`, and `Executor__BaseUrl` set from the executor's `http` endpoint.
- `orvano-executor`: `WithHttpHealthCheck("/healthz")`.
- No Caddy. Aspire gives contributors endpoints and a dashboard. The Caddyfile is exercised by the Compose smoke test.

### Value sourcing

| Action | Value produced / displayed | Source |
|---|---|---|
| `/healthz` (both processes) | `status` = `ok` | Constant. Sent whenever the process answers |
| `/readyz` | `status` and the status code | The Postgres readiness check result |
| Postgres readiness check | Healthy or unhealthy | `SELECT 1` on the registered `NpgsqlDataSource`, cancelled after 2 seconds. The last result is reused for 1 second, counted from when that probe finished, and callers that arrive together share one probe. Worst case a change shows after 3 seconds (1 stale plus 2 probe), which is the AC-3 limit |
| Database connection | Connection string | Configuration key `ConnectionStrings:orvano`. Compose sets `ConnectionStrings__orvano` to `Host=postgres;Port=5432;Database=orvano;Username=orvano;Password=${POSTGRES_PASSWORD}`. The AppHost injects the same key through `WithReference(db, connectionName: "orvano")` |
| Postgres container | Login, database, password | `POSTGRES_USER=orvano` and `POSTGRES_DB=orvano` fixed in `compose.yaml`. `POSTGRES_PASSWORD` from `.env` |
| Caddy site | Domain | `ORVANO_DOMAIN` in the `caddy` container environment, set by `compose.yaml` from `.env` with `localhost` when unset or empty |
| Caddy site | Certificate | Chosen by Caddy from the domain. Stored in the `caddy-data` volume |
| Caddy `reverse_proxy` | Upstream address | Fixed `orvano:8080` (the Compose service name and the .NET container default port) |
| `healthcheck` probe | Port to call | The first port in `ASPNETCORE_HTTP_PORTS`, else `8080` |
| `ExecutorOptions.BaseUrl` | Executor address | Configuration key `Executor:BaseUrl`. Compose sets `Executor__BaseUrl=http://orvano-executor:8080`. The AppHost sets it from the executor's `http` endpoint |
| Compose services | Image tag | `ORVANO_VERSION` from `.env` |
| Image build | Assembly version and the version label | The `VERSION` build argument (`ORVANO_VERSION` through `compose.build.yaml`, `0.1.0-dev` in the CI `images` job). Without it the assembly keeps `<Version>` from `Directory.Build.props` and the label is empty |
| JSON log line | `Timestamp`, `LogLevel`, `Category`, `Message` | Field names of the built in .NET JSON console formatter |
| Readiness log entry | Cause of a change | The exception message from the failed probe. Never the connection string |
| Smoke test | Trusted root certificate | Copied from the `caddy` container at `/data/caddy/pki/authorities/local/root.crt` |
| Smoke test | Settings and project | Its own temporary env file (a generated password, `ORVANO_VERSION=0.1.0-dev`) and its own project name `orvano-smoke`, so it never touches a stack you already run |

### Key invariants

- `compose.yaml` never contains `build`. `compose.build.yaml` contains nothing but `build`.
- The final stage of each Dockerfile has no `RUN` step. That is what lets the arm64 image build without emulation.
- No committed file holds a secret, and no setting has a default password.
- `orvano` starts without Postgres. It fails at startup only when the `orvano` connection string or `Executor:BaseUrl` is missing or malformed, and the error names the setting.
- Liveness never checks a dependency. Readiness checks Postgres only. The executor is never part of `orvano` readiness.
- The readiness check logs on a change of state only (ready to not ready as an error, back to ready as information), not on every probe. The first failed probe after startup counts as a change. The health check library's own error per failed run is silenced by the `Critical` log level for its category.
- `POSTGRES_PASSWORD` uses letters and digits only, because it is placed inside a connection string. The README command generates it that way.
- The Postgres image tag is `17`, the same as the AppHost and Testcontainers.
- The executor references `Orvano.Wire` and `Orvano.ServiceDefaults` only, as before.

### Security model

- `/healthz` and `/readyz` on `orvano` are public and reveal one word. Nothing else is exposed, and no sign in exists yet.
- The cached readiness result means anonymous traffic can cause at most one Postgres probe a second. Real rate limiting arrives with feature 8.
- Network split: Caddy, the only internet facing container, can reach `orvano` only. The executor, the riskiest container later on, has no route to Postgres from day one.
- The two Orvano containers run as a non root user with a read only root filesystem, no Linux capabilities and no way to gain new privileges.
- The only secret is `POSTGRES_PASSWORD`. It lives in `deploy/.env`, which `.gitignore` already excludes.
- No compliance scope applies. The skeleton stores no user data.

### Configuration required

Settings a person sets, in `deploy/.env`:

- `POSTGRES_PASSWORD`: required. The password of the bundled Postgres. Letters and digits only.
- `ORVANO_VERSION`: required. The image tag to run. `.env.example` carries `0.1.0-dev`.
- `ORVANO_DOMAIN`: optional, default `localhost`. The domain Caddy serves.

Settings `compose.yaml` and the AppHost pass to `orvano` (not for people to edit):

- `ConnectionStrings__orvano`: the Postgres connection string.
- `Executor__BaseUrl`: the private address of the executor.

`.env.example` leaves `POSTGRES_PASSWORD` commented out and sets `ORVANO_VERSION=0.1.0-dev`. The README gives one line, run from `deploy/`, that creates a working `.env`:

```bash
cp .env.example .env && echo "POSTGRES_PASSWORD=$(openssl rand -hex 32)" >> .env
```

### Critical test scenarios

- Smoke test setup: the script runs under `env -u POSTGRES_PASSWORD -u ORVANO_VERSION -u ORVANO_DOMAIN` (so runner variables cannot hide anything), writes its own temporary env file, uses `-p orvano-smoke --env-file <tmp>` on every Compose call, and runs `down -v` for that project only in an exit trap. Waits are poll loops with a deadline, never fixed sleeps
- Happy path: `deploy/smoke-test.sh` starts the stack with the build override and `--wait`, copies Caddy's root certificate, retries the first HTTPS call for up to 15 seconds (a healthy Caddy may still be issuing the certificate), and gets `200` and `{"status":"ok"}` from both endpoints over validated HTTPS, verifies **AC-1**, **AC-2**
- Failure case: an integration test pauses the Testcontainers Postgres (`PauseAsync`, not stop, because a restarted container gets a new host port), sees `/readyz` turn `503` within 3 seconds while `/healthz` stays `200`, unpauses it and sees `200` within 5 seconds. The smoke test does the same with `docker compose stop postgres` (the 3 second clock starts when the command returns) and `docker compose start postgres` (the 5 second clock starts when it reports healthy), verifies **AC-3**
- Failure case: an integration test starts the host with no `orvano` connection string, and one with a malformed `Executor:BaseUrl`, and each fails at startup naming the setting, verifies **AC-3**, **AC-8**
- Exposure: an integration test asserts the health bodies have exactly one property. The smoke test asserts that only `caddy` has published ports, that `docker compose ps` shows `orvano-executor` healthy, that a connection to `postgres:5432` and to `orvano-executor:8080` from inside the `caddy` container fails, and that a throwaway container on the `orvano-smoke_executor` network (the `caddy` image named by `docker compose config --images`, running `nc`) cannot reach `postgres:5432`, verifies **AC-4**, **AC-5**, **AC-7**
- Logs: the smoke test pipes `docker compose logs --no-log-prefix` for `orvano`, `orvano-executor` and `caddy` through `jq` and fails on any line that is not JSON, verifies **AC-6**
- Missing settings: Compose reports only the first missing variable, so the smoke test checks each setting on its own. It runs `config --quiet` with an env file that holds only `ORVANO_VERSION` and expects a failure naming `POSTGRES_PASSWORD`, then with one that holds only `POSTGRES_PASSWORD` and expects a failure naming `ORVANO_VERSION`, verifies **AC-8**
- Persistence: the smoke test creates a marker table through `psql` in the `postgres` container, runs `down` (without `-v`) and `up`, then checks the table is still there and the Caddy root certificate has the same fingerprint, verifies **AC-12**
- Rule check: a checker test proves `Orvano.IntegrationTests` may reference `Orvano.Host`, and that any other test project referencing an executable still fails, verifies **AC-13**
- Contributor run: start the AppHost, see `postgres`, `orvano` and `orvano-executor` healthy in the dashboard, and get `200` from `orvano`'s `/readyz`. A manual step for `/check verify`, verifies **AC-9**
- Images: the CI `images` job builds both Dockerfiles for both processor types, and `docker inspect` in the smoke test shows a non root user and a read only root filesystem on both Orvano containers, verifies **AC-10**, **AC-11**

## Build plan

Ordered as a tracer bullet: step 1 is the thinnest thread from the internet edge to the process, and each later step thickens one strand.

1. The first thread. Add the `/healthz` mapping, the response writer and the `healthcheck` probe to `Orvano.ServiceDefaults`, and call them from `Orvano.Host`. Write the root `.dockerignore`, `deploy/orvano.Dockerfile`, `deploy/Caddyfile`, and `compose.yaml` plus `compose.build.yaml` with `caddy` and `orvano` only. `https://localhost/healthz` answers through Caddy, satisfies **AC-1**, **AC-2**, **AC-11**
2. The Postgres strand. Add the data source registration and the readiness check to `Orvano.Infrastructure`, map `/readyz` in the host, add the `postgres` service, the `data` network, the volumes, `deploy/.env.example` and the required settings, satisfies **AC-2**, **AC-3**, **AC-5**, **AC-8**, **AC-12**
3. The host tests. Amend `ReferenceRules.cs` with the one exception (and its checker tests), create `Orvano.IntegrationTests` (the ArchitectureTests csproj shape) with Testcontainers Postgres, add it to `Orvano.slnx`, and write the readiness, exposure and startup failure tests, satisfies **AC-3**, **AC-5**, **AC-8**, **AC-13**
4. The executor strand. Map `/healthz` and the probe in `Orvano.Executor`, write `deploy/orvano-executor.Dockerfile`, add the service and the `executor` network, bind `ExecutorOptions` in the host, satisfies **AC-4**, **AC-7**
5. Logs. Add JSON console logging to `AddServiceDefaults`, the `Critical` level for the health check category, `skip_install_trust` and the access log in the Caddyfile, and the shared log rotation anchor, satisfies **AC-6**
6. Hardening. Read only root filesystem, `tmpfs`, dropped capabilities, `no-new-privileges`, restart policy and the `edge` network split, satisfies **AC-7**, **AC-11**
7. The AppHost. Database reference, wait, health checks and the executor address, satisfies **AC-9**
8. The smoke test. Write `deploy/smoke-test.sh` covering every scenario above that names it, satisfies **AC-1**, **AC-2**, **AC-3**, **AC-4**, **AC-6**, **AC-7**, **AC-8**, **AC-11**, **AC-12**
9. CI. Add the `images` job (`docker/setup-buildx-action`, because the default driver cannot build several platforms; no QEMU, since the final stage has no `RUN`; both Dockerfiles, `linux/amd64` and `linux/arm64`, `VERSION=0.1.0-dev`, no push, `type=gha` cache) and the `smoke` job (runs `deploy/smoke-test.sh`, prints all container logs on failure), with every action pinned to a full commit SHA, satisfies **AC-10**
10. The README. Rewrite `deploy/README.md`: the commands, the three settings, how to trust or skip the local certificate, and the note that `compose.yaml` alone cannot pull images until feature 40, satisfies **AC-1**

## Consequences

**Positive**:
- The file a self hoster will run is the file CI tests on every change.
- A broken Dockerfile, Compose file or Caddyfile fails a pull request, on both processor types.
- The executor is cut off from Postgres before it runs any code.
- The images hold no shell and no package manager, and the stack mounts no Docker socket.
- Later features plug in without reshaping: feature 5 adds a console stage to one Dockerfile, feature 54 adds the socket proxy and the executor calls, feature 93 writes the same two files to `/opt/orvano`.

**Negative / tradeoffs**:
- Until feature 40 publishes images, `compose.yaml` alone cannot start. You need the clone and the build override.
- Two Compose files and a longer command for contributors.
- No shell in the Orvano images, so `docker exec` debugging is not possible. You debug from logs, or with a temporary debug container.
- On `localhost` the certificate is from Caddy's own authority. Browsers and tools warn until you trust the root.
- The probe starts a .NET process every 10 seconds per container. It is small, but not free on a 2 vCPU server.
- `/readyz` can be up to 2 seconds stale.
- CI grows by two jobs and a few minutes.
- `ExecutorOptions` is validated but unused until feature 54.
- A crash before logging starts still prints a plain text stack trace, so not every byte on stdout is JSON in that one case.

**Neutral**:
- Spec 0001 is amended in two places: `Orvano.ServiceDefaults` now maps `/healthz`, and `Orvano.IntegrationTests` (later also `Orvano.ContractTests`) may reference `Orvano.Host`.
- The Compose file is named `compose.yaml`, the current standard name. The design spec says `docker-compose.yml`.
- `orvano` reads a .NET connection string. `DATABASE_URL` for an external Postgres comes with feature 93.
- Postgres logs stay in Postgres' own text format.

## Follow-up

- [ ] At build time, confirm and pin the exact tags and versions: the .NET 10 SDK image (a `10.0.4xx` tag), the .NET 10 chiseled `extra` ASP.NET runtime image, the current stable Caddy 2.x minor, the new NuGet packages, and the commit SHAs of the Docker build actions. No web check ran for this spec.
- [ ] Feature 5 (console shell): add the Node stage that builds the console into `deploy/orvano.Dockerfile`, and serve it at `/console`.
- [ ] Feature 7 (platform data model): separate database logins for schema changes and runtime, migrations at startup, and whether the AppHost Postgres gets a data volume.
- [ ] Feature 8 (API conventions): forwarded headers and which proxy is trusted (nothing reads the client address or scheme yet), and rate limits on the public health endpoints.
- [ ] Feature 40 (release pipeline): publish both images, set `VERSION` from the git tag, and keep `ORVANO_VERSION` in `.env.example` in step.
- [ ] Feature 54 (function sandbox): `docker-socket-proxy`, how the `orvano` to executor channel is authenticated, the real protocol, and the executor's outbound network access (the `executor` network is closed to the outside today).
- [ ] Feature 83 (custom domains and HTTPS): a custom Caddy build with a DNS plugin for the wildcard certificate, on demand TLS and the `ask` endpoint.
- [ ] Feature 93 (install script): `DATABASE_URL` for an external Postgres, an ACME contact email, and writing these files to `/opt/orvano`.
- [ ] Update `docs/design/spec.md` where it says `docker-compose.yml`, so the documents agree.
- [ ] A self hosting page on the docs site, once feature 6 creates the site. Until then `deploy/README.md` is the documentation.
