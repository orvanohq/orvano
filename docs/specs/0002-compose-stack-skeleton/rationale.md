# 0002. Rationale: Compose stack skeleton

The decision record for [index.md](index.md). A build never needs this file. Read it when you want to know why, or before you change the decision.

## Context

> ⚠️ Premise note: the scope says `docker compose up` on a clean machine. No Orvano image is published until feature 40, so a truly clean machine has nothing to pull. Read literally, the criterion cannot pass in M0. The right framing for now is a fresh clone plus Docker, with the images built from source. The image only path is still written and tested, so it starts working the day feature 40 publishes.

Orvano's promise is that one Compose command on one small server gives a complete backend. Today the repo has a scaffold that builds, but nothing runs as a deployment: no Dockerfile, no Compose file, no health endpoint, no logging setup. Every later feature assumes this ground exists.

The forces that shape it:

- **Tracer bullet.** The project proves the whole pipe first. This feature is that pipe at the deployment end: internet edge, process, database, executor. It must be real, only narrow.
- **One small server.** 2 vCPU and 4 GB. Every extra container costs memory and is one more thing for a self hoster to keep alive.
- **The self hoster never builds.** They get a Compose file and images. What they run should be what gets tested.
- **A public repo from M0.** No secret and no default password can ever be committed.
- **The executor is the riskiest piece.** Later it runs untrusted code and controls Docker. Its reach should be small from the first day, and its real design belongs to feature 54 at the stricter tier.
- **Two supported processor types.** x64 and arm64. A break on one should not wait for a release to be found.
- **Reference rules are the architecture.** The executor may reference `Orvano.Wire` and `Orvano.ServiceDefaults` only, and test projects could not reference an executable. Shared health and logging code, and a real test of `/readyz`, both run into those rules.
- **Spec 0001 left one thing here.** How `orvano` and `orvano-executor` talk was handed to this feature and feature 54.

Left undecided, each of these becomes a guess in the middle of the build: which file a self hoster gets, how HTTPS works with no domain, what a container health check runs in an image with no shell, and whether the first test project breaks an architecture rule.

## Options considered

### Option 1: a narrow but real stack, tested end to end

An image only `compose.yaml` plus a `compose.build.yaml` override. Chiseled images with a built in `healthcheck` argument. Three private networks. One domain setting that defaults to `localhost`. A smoke test that CI runs on every change. The socket proxy, the executor protocol and the console stay with their own features.

**Pros**:
- The self hoster file is tested as it ships.
- Small attack surface from the start: no shell in the images, no Docker socket, the executor cut off from Postgres.
- Each later feature adds to it without reshaping it.

**Cons**:
- Two Compose files and a longer contributor command.
- A little extra code for the probe, and no shell for debugging.
- CI gets slower by a few minutes.

### Option 2: the simplest thing that starts

One `compose.yaml` with `image` and `build` together, the default .NET images with `curl` for health checks, the default single network, a fallback password, and CI that only builds the images.

**Pros**:
- The least to write and the shortest command.
- A shell in every container for debugging.

**Cons**:
- The install script must later ship a second, edited copy of the Compose file, and that copy is not the one CI tested.
- A known fallback password in a public repo can reach a real server by accident.
- One flat network lets Caddy and the executor reach Postgres.
- A broken Caddyfile or Compose file can merge unnoticed.

### Option 3: the full topology now

Option 1, plus `docker-socket-proxy`, a live call from `orvano` to the executor, and the console built into the image.

**Pros**:
- The whole final shape is visible in M0.
- The executor wire is proven early.

**Cons**:
- Mounts the Docker socket months before anything uses it.
- Settles parts of the sandbox boundary outside feature 54, which is the one feature the scope puts at the stricter tier for exactly this.
- Decides how the console is served before feature 5 settles how a static export handles its page addresses.

## Rationale

Option 1 follows from three of the forces. The self hoster never builds, so the file they get must be a file of its own, and the cheapest way to keep it honest is to test it directly with a build override on top. The project is tracer bullet, so the thread has to be proven end to end on every change, which is what the smoke test buys. And the executor is the riskiest piece, so the skeleton gives it the smallest reach it can have (its own network, no socket, no calls) and leaves every real sandbox decision to feature 54.

Option 2 saves an afternoon and costs later: a second Compose file that drifts, a password that should never have existed, and no automatic proof that the stack starts. Option 3 looks like more progress, but it is the kind that has to be redone. The socket proxy permissions, the channel between the two processes and the console routing each have an owner feature that will know more than this one does.

### Calls made in this spec

Your answers from the design conversation are marked **you chose**. The rest are calls made here, each with the runner up. An independent cross check on another model then found four things that would break the build and several unnamed values. You chose to apply its recommended fixes, which are now in `index.md`.

| Topic | Pick | Why | Runner up |
|---|---|---|---|
| Compose files | **You chose** image file plus build override | The shipped file is the tested file | One file with both |
| HTTPS with no domain | **You chose** one `ORVANO_DOMAIN`, default `localhost` | One Caddyfile and one path for local, CI and production | A separate local override |
| Socket proxy | **You chose** to leave it for feature 54 | Nothing risky sits unused | Add it now, fully denied |
| Executor link | **You chose** private HTTP with an address setting, no calls yet | Fixes the shape, leaves the channel to feature 54 | A live probe at startup |
| Base image and probe | **You chose** chiseled images with a built in probe | No shell or package manager to exploit | The default image with `curl` |
| Shared code | **You chose** `Orvano.ServiceDefaults` for logging and `/healthz`, `Orvano.Infrastructure` for readiness | Both processes already reference ServiceDefaults | Repeat it in each `Program.cs` |
| Host tests | **You chose** to let `Orvano.IntegrationTests` reference `Orvano.Host` | Every later endpoint test needs the real app in memory | A tiny test only host |
| Secrets | **You chose** required values and an example file | No default secret anywhere | A development fallback |
| CI images | **You chose** both processor types, cross compiled | arm64 breaks show in the pull request | x64 only |
| CI smoke test | **You chose** yes | Keeps the thread proven | Build only |
| Database setting | **You chose** a .NET connection string now | No URL parsing code before external Postgres exists | `DATABASE_URL` from day one |
| Health body | **You chose** status only | Tells a stranger nothing | A list of checks |
| Probe in place of the skill's sidecar | Built in `healthcheck` argument | The `docker-compose-patterns` skill suggests a `curl` sidecar per distroless service. That is two more containers on a 4 GB server and a third party image to pin and trust | The sidecar |
| Container health means liveness | The probe calls `/healthz`, not `/readyz` | A database outage should not mark the app unhealthy and tempt a platform to restart it. Restarting does not fix Postgres | Probe `/readyz` |
| Where the health check is declared | Dockerfile `HEALTHCHECK` | It travels with the image to Coolify, Dokploy and plain Docker. Compose can still override it | Declare it in `compose.yaml` |
| Publish mode | Framework dependent on the ASP.NET runtime image | Both images share base layers, and patching the runtime is a base image bump | Self contained on the runtime deps image |
| Networks | Three: `edge`, `data`, `executor`, the last two internal | Caddy cannot reach Postgres, and the executor cannot either. The skill says custom networks are for isolation, and this is that case | Two networks, with the executor beside Postgres |
| Who waits for whom | `caddy` waits for `orvano` healthy, `orvano` waits for `postgres` healthy, nobody waits for the executor | When `up` returns, HTTPS works. A broken executor never blocks the API | `caddy` starts as soon as `orvano` starts |
| Readiness probe | `SELECT 1` with a 2 second limit, result reused for 1 second | Bounds the response time when Postgres hangs, and bounds what anonymous traffic can make the database do | No reuse, rely on feature 8 rate limits |
| Readiness logging | On change of state only, with the health check library's own log category set to `Critical` | A probe every few seconds would bury the log during an outage | Log every failed probe |
| Caddy global option | `skip_install_trust` | The container cannot use a trust store, and the attempt prints plain text that breaks the JSON log rule | No global block, accept the noise |
| Test outage method | Pause the Testcontainers Postgres, do not stop it | A restarted container gets a new host port, so the app could never reconnect | Fix the host port, which can clash on a busy machine |
| Health check library | The built in ASP.NET Core health checks with one small check of our own | Ten lines, no dependency | A community Postgres health check package |
| Dev log format | JSON everywhere except `Development` | Contributors read the console. The Aspire dashboard already shows structured logs | JSON everywhere |
| Request logging | Caddy's access log is the request log | One line per request, already JSON, no app code | ASP.NET Core HTTP logging |
| Postgres logs | Left as Postgres text | Postgres writes JSON logs to files only, not to stdout | A log collector, far too much for a skeleton |
| File name | `compose.yaml` | The current standard name, and what the installed skill requires | `docker-compose.yml`, as the design spec words it |
| Compose project name | `name: orvano` in the file | Volume and network names stay the same whatever folder the files sit in | Let the folder name decide |
| Postgres tag | `17` | The project rule: the same tag in Compose, the AppHost and Testcontainers | A pinned minor |
| Postgres login and database | `orvano` and `orvano`, from the image's own settings | Enough for an empty database. Feature 7 designs the real roles | The default `postgres` superuser |
| Version setting | `ORVANO_VERSION` is required, no default | A missing pin is how surprise upgrades happen | Default to `latest` |
| Hardening scope | Full set on the two Orvano images, `no-new-privileges` only on Caddy and Postgres | We control our own images. The upstream images need some privileges at startup, and guessing which is how a stack fails to boot | The full set on all four |
| Forwarded headers | Not in the skeleton | Nothing reads the client address or scheme yet, and which proxy to trust is a security call for feature 8 | Turn them on now |
| Caddy in the AppHost | No | Aspire already gives endpoints, and the Compose smoke test covers the Caddyfile | Run Caddy as an Aspire container |
| AppHost data volume | None yet | Nothing to keep until feature 7 | A persistent volume |
| Smoke test home | `deploy/smoke-test.sh`, called by CI | One script for CI, contributors and `/check verify` | Steps written inline in the workflow |
| Docs | `deploy/README.md` for now | The docs site does not exist until feature 6 | Wait for the docs site |
