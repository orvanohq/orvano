# 0016. Self host install

**Date**: 2026-10-29
**Status**: In Progress

## Summary

This spec decides how someone installs Orvano on a server or a laptop with one command. A short `install.sh` checks for Docker or Podman, runs `orvano init` (which now writes the whole install folder from inside the image, so the files always match the image), starts the reference compose, and prints a one time link to the console's first run setup page. A `--local` flag gives a laptop install on `http://localhost` with no certificate warnings. Feature 12 builds the setup gate (the instance record, the one time setup token, and the token step in the console); feature 13 builds the owner account form that comes after it.

## Requirements

Builds on spec [0007](../0007-stack-architecture/index.md) (the image, the `orvano` commands, the reference compose: IMG, RUN, INS, TLS, PG), spec [0006](../0006-security-baseline/index.md) (CON-1 cookies, CON-6 the setup token, SCR-2 and SCR-5 secrets, NET-1 and NET-2), spec [0011](../0011-platform-data-model/index.md) (`platform.instance`), spec [0012](../0012-access-control-model/index.md) (EPT-3 public endpoints), spec [0014](../0014-api-contract-sdk-generation/index.md) (CPX-3 console boot calls, PRB errors, HDR headers), spec [0003](../0003-react-console-practices/index.md) (RTE-10 boot, URL-5 tokens in links, I18N), spec [0015](../0015-design-system-console-shell/index.md) (`AuthLayout`, AC-21), spec [0008](../0008-coding-standards-tooling/index.md) (CI-2, the `ci-gate`) and spec [0009](../0009-open-source-license/index.md) (no new dependency is expected; any one follows DEP-1). It does not restate their rules; it cites them, and where they disagree with this spec, the change is recorded here and in that spec's `Updated` line.

**User stories**:

- As someone trying Orvano on my laptop, I want one command that brings the whole stack up on `http://localhost`, so that I can open the console without learning compose first.
- As an operator installing on a server, I want one command that gets a real HTTPS certificate for my domain and prints a link to finish setup, so that nobody else can claim my instance before I do.
- As an operator, I want my data, files, and keys to survive a restart or a reboot, and a second run of the installer to change nothing it already made.
- As the engineer of feature 13, I want a clear setup gate (an instance record, a checked token, a console route) to hang the owner account form on.
- As a maintainer, I want CI to prove the install on a clean Linux machine on both CPU types and on rootless Podman, so that a broken install never reaches main.

**Acceptance criteria**:

- **AC-1**: On a clean Linux machine (amd64 or arm64) with Docker Engine and Compose v2, `curl -fsSL https://raw.githubusercontent.com/orvanohq/orvano/main/deploy/install.sh | sh -s -- --local` (or `sh deploy/install.sh --local` from a checkout) creates `./orvano`, writes the install bundle, starts PostgreSQL, `migrate`, Orvano, and Caddy, waits until Orvano is healthy, and exits 0 after printing the console setup link `http://localhost/console/setup#token=<token>` (ISH-1 to ISH-6). Only one install per host is supported (ISH-8).
- **AC-2**: The same with `--domain <domain>` (no `--local`) writes a server install whose Caddy site is that domain on ports 80 and 443 and whose printed link is `https://<domain>/console/setup#token=<token>`. CI checks the files and the compose config for this mode; a real certificate is a verify step (LCL-1, verify.md).
- **AC-3**: `orvano init` writes, in INS-5's order and with its refusal rule widened to every bundle file, `compose.yaml`, `Caddyfile`, `postgres-init/10-orvano-roles.sh`, `.env`, and `root.key`. The three copied files are byte for byte the ones in the image's `/app/install/` folder, which the `Dockerfile` copies from `deploy/`. If any bundle file already exists, init refuses and changes nothing (BND-1 to BND-4).
- **AC-4**: `.env` pins `ORVANO_VERSION` to the image's own version (`sha-<commit>` for a main build, the release version once feature 59 ships), so a restart or `compose pull` never changes the version (PIN-1, PIN-2).
- **AC-5**: With a fresh install, opening `/console/` (or `/`) in a browser lands on `/console/setup`. `GET /v1/console/config` answers `200` with `setupRequired: true`. After feature 13 completes setup, it answers `false` and `/console/setup` redirects to `/console/` (FRS-1 to FRS-3).
- **AC-6**: While setup is not complete and no valid token exists, a start of a process running the `api` role issues a setup token, stores only its SHA-256 hash and an expiry 24 hours later in `platform.instance`, and writes one banner with the setup link to standard output, never through `ILogger`; while a valid token exists, a start leaves it alone and its banner says to run `orvano admin setup-token` for a new link. `orvano admin setup-token` always issues a new token and prints the link, which makes the previous one invalid. Once setup is complete, neither issues a token; the command says so and exits 3 (STP-1 to STP-6).
- **AC-7**: `POST /v1/console/setup/token` with the current, unexpired token answers `204` and does not use the token up. A wrong, expired, or replaced token answers `401 setup_token_invalid`, the same for each. After setup is complete it answers `409 setup_complete`. A request whose `Origin` is not the console origin answers `403` (CON-2 as amended: no CSRF header before a session exists) (STP-7, STP-8).
- **AC-8**: The setup page, inside `AuthLayout`, reads `#token=` from the URL once, removes it with `history.replaceState`, fills the token field, and checks it. A typed or pasted token works the same. A valid token shows the "token accepted" state and keeps the token in memory only (never in storage or the URL). A rejected token shows the error inline and keeps the field; a `403` (wrong origin) says to open the console at `ORVANO_PUBLIC_URL`. Every string is in the message catalogue, and the page passes axe in both themes (FRS-4 to FRS-7).
- **AC-9**: `docker compose down` then `install.sh` again (or `docker compose up -d`) keeps the database, the storage volume, and Caddy's certificates: `platform.instance.created_at` and the SHA-256 of `root.key` and `.env` are unchanged, and the console still lands on setup; a token issued before the restart still works until `install.sh` (ISH-6) replaces it (ISH-7, SMK-3).
- **AC-10**: Running `install.sh` again on a folder that already has `.env` and `root.key` skips init, runs `compose up --wait`, and prints the setup state again (a new link while setup is open, "setup complete, sign in at <url>" after), changing no file (ISH-7).
- **AC-11**: `install.sh` stops before changing anything, with a message that names the fix and a link for the OS, when no supported engine is found, the engine or Compose is older than the minimum, the folder is not writable, the chosen ports are known to be taken, another install already holds the compose project `orvano` on this host, or (on a rerun) the flags conflict with the folder's `.env`. It never installs packages and never runs `sudo` (ISH-2, ISH-3).
- **AC-12**: When both Docker and Podman are present, Docker is used unless `--engine podman` is given. Podman is accepted only with the Docker Compose provider. Rootless Podman runs init with `--userns=keep-id`, Docker (rootful or rootless) and rootful Podman with `--user "$(id -u):$(id -g)"` (INS-3) (ISH-4).
- **AC-13**: In `--local` mode Caddy listens only on `127.0.0.1` (not on the network), serves plain HTTP, and `.env` sets `ORVANO_INSECURE_COOKIES=true` and `ORVANO_PUBLIC_URL=http://localhost[:<port>]`. `--http-port` changes the published port and the link carries it; server mode refuses ports other than 80 and 443 (LCL-1 to LCL-4, PRT-1, BND-4).
- **AC-14**: Orvano itself sends nothing off the host: no update check, no usage ping, no telemetry (image pulls and Caddy's certificate requests are the only outbound traffic, and they are the engine's and Caddy's). OpenTelemetry export stays off unless `OTEL_EXPORTER_OTLP_ENDPOINT` is set (TEL-1).
- **AC-15**: A CI job `install` in `_deploy.yml` runs AC-1, AC-5, AC-6, AC-7, AC-9, and AC-10 on `ubuntu-24.04` with Docker, `ubuntu-24.04-arm` with Docker, and `ubuntu-24.04` with rootless Podman, against the image built from the pull request, and on each leg proves INS-5 (another host user cannot read `root.key`; init without `--user` fails safely on rootful Docker). It joins `ci-gate` (SMK-1 to SMK-5).
- **AC-16**: `docs/self-hosting.md` documents the one command for both modes, the manual path (download, read, run; and `init` plus `compose up` by hand), every `.env` variable, both TLS ways (TLS-1, TLS-2), PG-4's `max_connections` formula, FIL-2's shared volume note, SELinux (`:Z`), both init commands of INS-3, rootless Podman, Windows through WSL2, backups of `root.key` apart from the database, and that running the image alone against an existing PostgreSQL is untested (INS-4). The README's Self hosting section shrinks to the one command and a link (GDE-1, GDE-2).

## Decision

**Chosen option**: Option 1, an install script around an `init` that writes the whole bundle from the image (see [rationale.md](rationale.md)).

`deploy/install.sh` is the one command. It checks the host, runs `orvano init` from the pinned image (which now writes `compose.yaml`, `Caddyfile`, and `postgres-init/` next to `.env` and `root.key`), runs `compose up --wait`, and prints the setup link from `orvano admin setup-token`. Feature 12 owns the setup gate; feature 13 owns the owner account form behind it.

**Implementation skills**: `docker-compose-patterns` (`docker/skills`, `.claude/skills/docker-compose-patterns/`) · `containerize-aspnetcore` (`github/awesome-copilot`, `.claude/skills/containerize-aspnetcore/`, spec 0007 IMG-2 and IMG-4 win) · `authoring-github-workflows` (`dotnet/skills`, `.claude/skills/authoring-github-workflows/`) · `github-actions-hardening` (`github/awesome-copilot`, `.claude/skills/github-actions-hardening/`) · `aspnet-minimal-api-openapi` (`github/awesome-copilot`, `.claude/skills/aspnet-minimal-api-openapi/`, spec 0014 wins)

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### Scope and boundaries

| In feature 12                                                                                                                 | Elsewhere                                                                                                                                 |
| ----------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `deploy/install.sh`, the init bundle, `--local` mode, port settings, the version pin                                          | Server releases, version tags, release notes (feature 59)                                                                                 |
| `Orvano.Modules.Platform` (the project, its `DbContext`, its first migration) with `platform.instance` only                   | `platform.operator`, sessions, organizations, projects, API keys, and the rest of CAT-1 (feature 13)                                      |
| The setup token: issue at start, `orvano admin setup-token`, the check endpoint, the hash and expiry                          | Using the token up: the owner account form, `setup_completed_at`, signing in, `orvano admin reset-operator` (feature 13, CON-6)           |
| `GET /v1/console/config` with `setupRequired`; the console boot's config load; the `/console/setup` route with its token step | `GET /v1/console/session`, the rest of the config payload (instance name, feature switches), the `_shell` and `_auth` routes (feature 13) |
| CI `install` job on Linux (amd64, arm64, rootless Podman)                                                                     | macOS and WSL2 runs (verify.md, by a person); rootful Podman on Linux (verify.md)                                                         |
| `docs/self-hosting.md` and the README pointer                                                                                 | The docs site (feature 32); upgrades between versions (feature 53); backups and restore (feature 50); a Helm chart (after alpha, INS-4)   |

### Rules

#### Install script (ISH)

- **ISH-1 (MUST)**: `deploy/install.sh` is POSIX `sh` (no bash only syntax), checked by `shellcheck` in CI, with `set -eu`. The whole body is functions, and the last line is `main "$@"`, so a download cut short runs nothing. Every engine call that does not need input gets `</dev/null`, and `compose exec` gets `-T`, so a piped `curl | sh` never feeds the rest of the script to a container. Flags: `--local` or `--domain <domain>` (exactly one is required), `--dir <path>` (default `./orvano`), `--version <tag>` (the tag to pull and run init from: default `edge` until feature 59 publishes releases, then the latest release; init then pins `.env` to that image's own version, PIN-2), `--engine docker|podman`, `--http-port <n>` (default 80, the only port flag; `--local` only, BND-4), `--public-url <url>` (`--domain` only, passed to init), `--image <repository>` (default `ghcr.io/orvanohq/orvano`, for CI and mirrors; passed to init, which writes it to `.env`), and `--help`. Unknown flags fail with the usage text.
- **ISH-2 (MUST)**: Before it changes anything, the script checks, in order: the OS (Linux or macOS; anything else stops and names WSL2 for Windows); the engine (ISH-4); the minimum versions (Docker Engine 24 and Compose 2.24, or Podman 4.9 whose `podman compose` provider is Docker Compose 2.24 or later; step 1 may raise these, never lower them); that `--dir` is writable or creatable; on a first run, that no volume labelled `com.docker.compose.project=orvano` exists (ISH-8); and, on a first run, the ports: with `nc -z 127.0.0.1 <port>` when `nc` exists (80 and 443 for `--domain`, the HTTP port for `--local`), else skipped, in which case a bind error from `compose up` is turned into the same message. A rerun checks that `--local` or `--domain` and `--image`, when given, match the folder's `.env` (read with `sed`, never sourced) and stops on a conflict. Any failure prints one line saying what is wrong, one line with the fix, and a docs link, then exits 1. Step 1 result (2026-10-29): `up -d --wait` counted `migrate`'s exit 0 as success, and the `local` and `server` Caddy services shared `172.30.80.10` under separate profiles, on Docker Compose 2.24.2 (the `docker:25.0.0-cli` image against Docker Desktop's engine) and on Docker Desktop's Compose 2.35.1 with Engine 28.1.1; the minimums stay as written. The CI Docker leg proves the runner's own Compose on every run; Podman as the provider is still owed by step 16.
- **ISH-3 (MUST)**: The script never installs packages, never runs `sudo`, never edits files outside `--dir`, and never writes a secret to the terminal other than the setup link. It needs only POSIX `sh` tools and the engine; it downloads nothing itself (`curl` is needed only for the one line form of fetching the script). It does not verify the image signature; the guide shows an optional `cosign verify` (IMG-3) before you run it.
- **ISH-4 (MUST)**: Engine choice: `--engine` if given; else Docker when `docker compose version` works and `docker version` does not report Podman (a `docker` alias to Podman counts as Podman); else Podman when `podman compose version` reports Docker Compose (the Python `podman-compose` provider is refused, because it lacks `--wait` and `service_completed_successfully`; rootless needs `podman.socket` running, and the message says how). Podman is rootless when `podman info --format '{{.Host.Security.Rootless}}'` is `true`. Init runs as `--userns=keep-id` under rootless Podman and `--user "$(id -u):$(id -g)"` otherwise, including rootless Docker (INS-3). `--dir` is made absolute and created with `mkdir -p` as the user before init, and the mount is always `-v "<absolute dir>:/out:Z"`.
- **ISH-5 (MUST)**: First run, in order: pull `<image>:<version>` only when `<engine> image inspect` does not find it; run `init --out /out` with `--local` or `--domain`, plus `--image`, `--http-port`, and `--public-url` when given; `cd` into the folder; `<engine> compose --profile <local|server> up -d --wait --wait-timeout 300` (the profile read from `.env`, LCL-2); then ISH-6. A failure after init leaves the folder in place and prints the command to see logs (`<engine> compose logs orvano migrate`).
- **ISH-6 (MUST)**: After the stack is healthy, the script runs `<engine> compose exec -T orvano /app/orvano admin setup-token </dev/null`. Exit 0: it prints the link from the command's output under a short "Finish setup" heading, with one line saying the link works once, expires in 24 hours, and can be made again with the same command. Exit 3 (setup complete): it prints "Orvano is running at <ORVANO_PUBLIC_URL>/console", with the URL read from `.env` by `sed` (never sourced). Any other exit fails the script.
- **ISH-7 (MUST)**: A rerun on a folder that has both `.env` and `root.key` skips the pull and init, runs `compose up -d --wait` (which also starts a stopped install), and then ISH-6. It never edits a file in the folder. A folder with only one of the two stops with a message that names the missing file and does nothing else. An empty folder (a first run that failed before init) counts as new.
- **ISH-8 (MUST)**: One install per host: `compose.yaml` keeps `name: orvano` and the fixed subnet `172.30.80.0/24`, so a second folder would reuse the first one's volumes with new passwords. ISH-2 refuses a first run when volumes of project `orvano` exist, naming the fix (rerun in the existing folder, or remove the old install with `compose down -v` there). The guide states the limit.

#### Init bundle (BND)

- **BND-1 (MUST)**: The `Dockerfile` copies `deploy/compose.yaml`, `deploy/Caddyfile`, and `deploy/postgres-init/` into the final image at `/app/install/`. Those files in `deploy/` stay the one source of truth; there is no second copy in the repo.
- **BND-2 (MUST)**: `orvano init --out <dir>` writes `compose.yaml`, `Caddyfile`, and `postgres-init/10-orvano-roles.sh` from `/app/install/`, then `.env` and `root.key`, keeping INS-5's order; its failure cleanup removes every path this run created, `postgres-init/` included, so a retry is not refused by BND-3. Modes: `compose.yaml`, `Caddyfile`, and the init script 0644; `postgres-init/` 0755; `.env` 0600; `root.key` 0644 inside the 0700 folder (INS-5).
- **BND-3 (MUST)**: INS-5's first step widens: if any of the five bundle paths exists, init refuses and changes nothing (not even the folder's mode), and the message names each existing path.
- **BND-4 (MUST)**: Init's flags: `--domain <domain>` or `--local` (exactly one), `--image <repository>` (written to `.env` as `ORVANO_IMAGE` only when it is not the default), `--public-url <url>` (`--domain` only, default `https://<domain>`, must be `https`), `--http-port <n>` (`--local` only, default 80; the public URL is `http://localhost`, plus `:<n>` when not 80). `--domain` refuses an IP literal, `localhost`, and a name without a dot, pointing to `--local`, because Caddy cannot get a public certificate for them. Server mode always uses ports 80 and 443 (PRT-1).

#### Version pin (PIN)

- **PIN-1 (MUST)**: The `Dockerfile` takes `ARG ORVANO_IMAGE_VERSION=dev` and sets it as an `ENV` and as the `org.opencontainers.image.version` label. `image.yml` passes `sha-<full commit>`; feature 59 passes the release version.
- **PIN-2 (MUST)**: Init writes `ORVANO_VERSION=<ORVANO_IMAGE_VERSION>` into `.env`, so `edge` is only ever a pull tag, never what an install runs. A `dev` build (a local build or CI) writes `ORVANO_VERSION=dev`, which works only with a local image of that tag. `compose.yaml` uses `image: ${ORVANO_IMAGE:-ghcr.io/orvanohq/orvano}:${ORVANO_VERSION:?run orvano init first}`; the `edge` default goes away.
- **PIN-3 (MUST)**: Changing `ORVANO_VERSION` by hand is how you move versions until feature 53 designs upgrades. The guide says so and says that `migrate` runs on every `up`, so moving forward migrates and moving back is not supported.

#### Local mode and ports (LCL, PRT)

- **LCL-1 (MUST)**: `compose.yaml` has two Caddy services that share Caddy's fixed address `172.30.80.10` and never run together: `caddy` under the `server` profile, with `ORVANO_SITE_ADDRESS: ${ORVANO_DOMAIN}` and ports `80:80`, `443:443`, `443:443/udp`; and `caddy-local` under the `local` profile, with `ORVANO_SITE_ADDRESS: ":80"` and the one port `127.0.0.1:${ORVANO_HTTP_PORT:-80}:80`. The `Caddyfile` site line becomes `{$ORVANO_SITE_ADDRESS}`.
- **LCL-2 (MUST)**: Init writes `COMPOSE_PROFILES=server` or `COMPOSE_PROFILES=local` into `.env`, so a plain `docker compose up -d` in the folder picks the right Caddy. `install.sh` also passes `--profile` explicitly. Switching an install between modes is not supported; the guide says to install again. `compose.yaml` declares the `orvano` service's health check itself (`["CMD", "/app/orvano", "healthcheck"]`, the image's interval and timings), so `--wait` never depends on an image format keeping `HEALTHCHECK`.
- **LCL-3 (MUST)**: Local mode's `.env` sets `ORVANO_DOMAIN=localhost`, `ORVANO_PUBLIC_URL=http://localhost[:<port>]`, and `ORVANO_INSECURE_COOKIES=true` (CON-1: allowed because the public URL is HTTP, so Safari keeps the session). `compose.yaml` passes `ORVANO_INSECURE_COOKIES: ${ORVANO_INSECURE_COOKIES:-false}` to the `orvano` service.
- **LCL-4 (MUST)**: Local mode is for one machine: Caddy binds `127.0.0.1` only. Reaching it from another device on the network is not supported in this feature; the guide points to a server install with a real domain.
- **PRT-1 (MUST)**: Server mode always publishes 80 and 443, because Caddy's automatic certificates need the HTTP or TLS challenge on the standard ports (a different port needs a DNS challenge, which this feature does not configure). `ORVANO_HTTP_PORT` exists for local mode only. The guide notes that rootless Podman cannot bind ports below 1024 unless `net.ipv4.ip_unprivileged_port_start` allows it, and that a rootless install needs `loginctl enable-linger` to come back after a reboot.

#### Setup token (STP)

- **STP-1 (MUST)**: The token is `orv_setup_` plus base64url of 32 random bytes from `RandomNumberGenerator`. `platform.instance.setup_token_hash` holds its SHA-256 (a fast hash is right for a 256 bit random value); `setup_token_expires_at` is the issue time plus 24 hours from `TimeProvider`.
- **STP-2 (MUST)**: Issuing is one `UPDATE platform.instance SET setup_token_hash = $1, setup_token_expires_at = $2, updated_at = now() WHERE id = 1 AND setup_completed_at IS NULL`. Zero rows updated means setup is complete; no token is printed. The last issue wins, so with several `api` replicas only the newest printed link works (the guide says to use `admin setup-token` then).
- **STP-3 (MUST)**: `serve`, in a process with the `api` role, runs once at start, after the platform database answers and no migrations are pending (the LOG-6 ready check), and before it reports ready: when setup is open and no valid token exists (hash null or expired), it issues one with STP-2's update plus `AND (setup_token_hash IS NULL OR setup_token_expires_at <= now())`, so concurrent replicas issue at most one, and prints STP-4's banner; when a valid token exists, it leaves it and prints the short banner (STP-4). A restart therefore never breaks a setup in progress. If the `platform` schema is missing or behind, or the issue throws, it logs a Warning (without the token) and carries on starting.
- **STP-4 (MUST)**: The banner is written to standard output with `Console.Out`, not `ILogger`, so it never reaches OpenTelemetry export (LOG-2 and LOG-4 stay true for logs; this is CON-6's named exception to SCR-5). It is four plain lines: a blank line, `Finish setting up Orvano: <ORVANO_PUBLIC_URL>/console/setup#token=<token>`, `This link works until setup is done or <UTC time, ISO 8601>. Make a new one with: orvano admin setup-token`, and a blank line. The short banner, when a valid token already exists, is one line: `Orvano setup is waiting. Make a setup link with: orvano admin setup-token`.
- **STP-5 (MUST)**: `orvano admin setup-token` reads the same configuration as `serve` (it needs the database, not the root key), always issues a new token per STP-2 (replacing any valid one), prints only the link line to standard output, and exits 0; it exits 3 with "Setup is already complete." on standard error when STP-2 updates no row, and 1 on any other error. Host access is the gate: it is not reachable over HTTP.
- **STP-6 (MUST)**: The submitted token is trimmed of surrounding white space first. A token is valid when its SHA-256 equals `setup_token_hash` (compared with `CryptographicOperations.FixedTimeEquals`), `setup_token_expires_at` is in the future, and `setup_completed_at` is null. Feature 13's owner form checks the same, and in the same transaction that creates the owner sets `setup_completed_at` and clears both token columns.
- **STP-7 (MUST)**: `POST /v1/console/setup/token` is public (`AllowPublicAccess()`, spec 0012 EPT-3 and EPT-5), in the console contract, and changes nothing. It answers `204` for a valid token, `401 setup_token_invalid` for a wrong, expired, or replaced token (one answer for all three), `409 setup_complete` after setup, and `400 validation_failed` for a missing or malformed body. CON-2's `Origin` check applies (it is a POST), so a foreign origin gets `403`; no `Orvano-Csrf` header is needed or checked, because no session exists yet (an amendment to CON-2, recorded in spec 0006). `Cache-Control: no-store` per HDR-5.
- **STP-8 (MUST)**: The check endpoint has a per process limit of 10 requests per minute per client address (ASP.NET Core's built in fixed window limiter, the address from the trusted proxy rule of NET-1), answered `429` with `Retry-After`. Feature 23 may replace it with the shared engine. The token's entropy is the real guard; the limit keeps logs and CPU quiet.

#### First run in the console (FRS)

- **FRS-1 (MUST)**: `GET /v1/console/config` (CPX-3) is public, the same for every caller, `Cache-Control: no-store`, and in this feature returns `{ "setupRequired": boolean }`, where `setupRequired` is `setup_completed_at IS NULL`. Feature 13 adds the instance name and feature switches to the same payload.
- **FRS-2 (MUST)**: The console boot (spec 0003 RTE-10, step 2) loads the config before the first render; feature 13 adds the session call beside it. A failed config load shows the boot error page (spec 0015 AC-21).
- **FRS-3 (MUST)**: The root route's `beforeLoad` sends every path except `/setup` to `/setup` while `setupRequired` is true, and sends `/setup` to `/` once it is false. The existing welcome page stays the `/` route until feature 13 replaces it.
- **FRS-4 (MUST)**: `routes/setup.tsx` renders inside `AuthLayout` (spec 0015): a heading, one sentence on where the token comes from (the install output, the server log, or `orvano admin setup-token`), a token field, and a "Continue" button, built from `src/ui/form` primitives.
- **FRS-5 (MUST)**: On load the page reads `token` from `location.hash` once, then calls `history.replaceState` to drop the hash before anything else runs (spec 0003 URL-5), fills the field, and submits it. The read lives in a module level or ref guarded step so React StrictMode's double effect neither loses the token nor submits it twice. The token lives only in component state or a ref; it is never put in storage, the query cache, a query key, or a log.
- **FRS-6 (MUST)**: States: idle; checking (button busy, field read only); accepted (a success message that the token is accepted, and the next step, the owner account form, which feature 13 adds here); rejected (`setup_token_invalid` shows "This setup link or token is not valid any more. Make a new one with `orvano admin setup-token`." on the field); limited (`429` shows the retry time from `Retry-After`, in seconds, as a relative time); wrong origin (`403` says to open the console at its own address, `ORVANO_PUBLIC_URL`, not an IP or another name); setup complete (`409` reloads config, which redirects to `/`).
- **FRS-7 (MUST)**: Every string goes through Lingui (spec 0003 I18N) and is in the `ar` catalogue as well, the page works right to left, and an axe test passes in both themes. The CSP end to end test (spec 0015 AC-23) loads `/setup` too.

#### CI smoke (SMK)

- **SMK-1 (MUST)**: `_deploy.yml` gets a job `install` with a matrix: `ubuntu-24.04` + Docker, `ubuntu-24.04-arm` + Docker, `ubuntu-24.04` + rootless Podman. It runs on the same path filter as the `deploy` job and is required by `ci-gate` (spec 0008 CI-2). Timeout 30 minutes per leg.
- **SMK-2 (MUST)**: Each leg builds the image natively for its own CPU (no emulation) with `--build-arg ORVANO_IMAGE_VERSION=ci`, tags it `orvano:ci` (Podman: built with Docker and loaded through `docker save | podman load`; step 16 confirms compose resolves `orvano:ci` to Podman's `localhost/orvano:ci`, else the leg passes `--image localhost/orvano`; the leg installs the Docker Compose plugin as Podman's provider and starts `podman.socket` for the user), then runs `sh deploy/install.sh --local --http-port 8080 --image orvano --version ci --dir "$RUNNER_TEMP/orvano"` (plus `--engine podman` on that leg). It never pulls the Orvano image from a registry.
- **SMK-3 (MUST)**: Then it checks, failing on the first miss: the script exited 0 and printed a `#token=` link; `GET /` redirects to `/console`; `GET /console/` is `200` with NET-2's CSP header; `GET /v1/console/config` has `setupRequired: true`; the printed token gets `204` from the check endpoint, a changed token `401`, and a foreign `Origin` `403`; `compose down` then `compose up -d --wait` keeps the printed token valid (`204`); then `install.sh` again (AC-10) leaves `platform.instance.created_at` (read with `compose exec -T postgres psql -U postgres -d orvano -tAc`) and the SHA-256 of `root.key` and `.env` unchanged, prints a new link, and the old token now gets `401`; a second `--dir` on the same host is refused (ISH-8); a rerun with `--domain` on the local folder is refused.
- **SMK-4 (MUST)**: INS-5 on Linux: on each leg, after init, a second user created for the test cannot read `root.key` (`sudo -u <user> cat` fails), and on the Docker legs `docker run` of init without `--user` fails with INS-3's message and leaves the target folder unchanged.
- **SMK-5 (MUST)**: The job also runs `shellcheck deploy/install.sh deploy/postgres-init/*.sh`, `install.sh --help`, and two refusal cases (an unknown flag; `--local` and `--domain` together), and runs `docker compose config --quiet` on a `--domain orvano.example` bundle so the server profile is validated too. It uploads `compose logs` as an artifact on failure. Actions are pinned by commit and the job holds `contents: read` only (spec 0008, `github-actions-hardening`).

#### Telemetry and the guide (TEL, GDE)

- **TEL-1 (MUST)**: The install script and the server make no outbound call of their own (pulling images and Caddy's certificate requests are the engine's and Caddy's): no update check, no usage ping, no crash report. The guide states this. Adding any outbound call later needs its own spec and must be opt in.
- **GDE-1 (MUST)**: `docs/self-hosting.md` is the install guide (AC-16's list), written in the README's voice, with each command in its own `bash` block. It opens with the two one line installs (server and laptop), then "Read the script first" (download, read, run), then the manual path, then reference sections.
- **GDE-2 (MUST)**: The README's Self hosting section becomes the two one line installs and a link to `docs/self-hosting.md`. `deploy/.env.example` lists every variable `.env` can hold, including `COMPOSE_PROFILES`, `ORVANO_HTTP_PORT`, `ORVANO_INSECURE_COOKIES`, and `ORVANO_IMAGE`.

### Data model sketch

`platform.instance` (moved from feature 13 to feature 12; spec 0011 CAT-1 gains the expiry column), in `Orvano.Modules.Platform`'s `PlatformDbContext`, schema `platform`:

| Column                   | Type          | Null | Class | Notes                                                      |
| ------------------------ | ------------- | ---- | ----- | ---------------------------------------------------------- |
| `id`                     | `smallint`    | no   |       | PK, CHECK `id = 1` (IDS-1 singleton)                       |
| `setup_token_hash`       | `bytea`       | yes  | S     | SHA-256 of the token (STP-1); cleared when setup completes |
| `setup_token_expires_at` | `timestamptz` | yes  |       | new; issue time + 24 h; cleared with the hash              |
| `setup_completed_at`     | `timestamptz` | yes  |       | set by feature 13 when the owner is created                |
| Std                      |               |      |       | `created_at`, `updated_at`, `xmin` version                 |

The module's first migration creates the schema and the table and inserts the one row (`id = 1`, all nullable columns null) with `ON CONFLICT DO NOTHING`. `Platform` joins DAT-10's `MigrationOrder` after the existing modules. No foreign keys; nothing points at this table.

### State transitions

```
instance: open (no token) → open (token issued) → open (token replaced) … → complete
```

- open, no token: right after the first migration, or after a token's expiry (expiry is checked on read; nothing clears it).
- open, token issued: STP-2 by `serve` start (only when no valid token exists, STP-3) or by `admin setup-token` (always, replacing the previous one).
- complete: feature 13 sets `setup_completed_at` and clears both token columns. There is no way back in this feature (feature 13's `reset-operator` covers a lost password, not a second setup).

### API surface

Both endpoints are in `api/console.openapi.json` (spec 0014 CTR-1), under `/v1/console`, with PRB-1 problem bodies.

| Endpoint                  | Method | Key inputs                            | Key outputs              | Auth                                                      | Key errors                                                                                    |
| ------------------------- | ------ | ------------------------------------- | ------------------------ | --------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `/v1/console/config`      | GET    | none                                  | `setupRequired: boolean` | public (`AllowPublicAccess`)                              | 503 while migrations are pending (as `/health/ready`)                                         |
| `/v1/console/setup/token` | POST   | `token: string` (req, 1 to 100 chars) | `204`, no body           | public; CON-2 `Origin` check, no CSRF header; STP-8 limit | 400 `validation_failed`, 401 `setup_token_invalid`, 403 bad origin, 409 `setup_complete`, 429 |

Commands (not HTTP): `orvano init` (BND-2 to BND-4), `orvano admin setup-token` (STP-5).

### Value sourcing

| Action                      | Value produced or displayed                                                         | Source                                                                                                      |
| --------------------------- | ----------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `init`                      | `ORVANO_VERSION`                                                                    | the image's `ORVANO_IMAGE_VERSION` env, set at build (PIN-1)                                                |
| `init`                      | `COMPOSE_PROFILES`, `ORVANO_DOMAIN`, `ORVANO_PUBLIC_URL`, `ORVANO_INSECURE_COOKIES` | `--local` or `--domain`, `--public-url`, `--http-port` flags (BND-4, LCL-3)                                 |
| `init`                      | `ORVANO_HTTP_PORT`                                                                  | `--http-port` (`--local` only), written only when not 80                                                    |
| `init`                      | `ORVANO_IMAGE`                                                                      | `--image`, passed through by `install.sh`, written only when not the default (BND-4)                        |
| `init`                      | database passwords, root key                                                        | generated as today (INS-3, SCR-2)                                                                           |
| `init`                      | `compose.yaml`, `Caddyfile`, `postgres-init/`                                       | `/app/install/` in the image (BND-1)                                                                        |
| setup token issue           | token, hash, expiry                                                                 | `RandomNumberGenerator`, SHA-256, `TimeProvider` + 24 h (STP-1)                                             |
| banner and command output   | setup link                                                                          | `ORVANO_PUBLIC_URL` + `/console/setup#token=` + token (STP-4)                                               |
| `install.sh`                | the printed link                                                                    | the output of `admin setup-token` (ISH-6), never parsed from logs                                           |
| `install.sh`                | "running at" URL after setup                                                        | `ORVANO_PUBLIC_URL` read from `.env` with `sed`, never sourced                                              |
| `install.sh`                | profile on a rerun, conflict check                                                  | `COMPOSE_PROFILES` and `ORVANO_IMAGE` read from `.env` with `sed`                                           |
| `install.sh`                | another install on the host                                                         | `<engine> volume ls --filter label=com.docker.compose.project=orvano` (ISH-8)                               |
| `GET /v1/console/config`    | `setupRequired`                                                                     | `platform.instance.setup_completed_at IS NULL`                                                              |
| token check                 | valid or not                                                                        | STP-6 against `setup_token_hash`, `setup_token_expires_at`, `setup_completed_at`                            |
| token check                 | client address for STP-8                                                            | `HttpContext.Connection.RemoteIpAddress` after the forwarded headers rule (NET-1, `ORVANO_TRUSTED_PROXIES`) |
| setup page                  | token in the field                                                                  | `location.hash` `token` once (FRS-5), or what the operator types                                            |
| setup page                  | retry time on 429                                                                   | the `Retry-After` header, through `ApiError` (spec 0003 FLT-1)                                              |
| CI SMK-3                    | `created_at` before and after restart                                               | `compose exec -T postgres psql -U postgres -d orvano -tAc 'select created_at from platform.instance'`       |
| tests of the complete state | `setup_completed_at` set (AC-5 `false`, AC-6 exit 3, AC-7 `409`)                    | a SQL `UPDATE` in the test fixture, until feature 13 sets it for real                                       |

### Key invariants

- At most one setup token is valid at any time, and none once `setup_completed_at` is set.
- A restart never replaces a valid token (STP-3); only `admin setup-token` does.
- One install per host (ISH-8).
- The token is never stored, logged through `ILogger`, put in a URL query, sent to the server in a URL, or kept in browser storage. It appears only in the banner, the command output, `install.sh`'s output, and the URL fragment.
- Init never overwrites a file, and a refused init changes nothing (BND-3, INS-5).
- `install.sh` never changes a file in an existing install folder (ISH-7).
- The files init writes are exactly the image's `/app/install/` files, which are exactly `deploy/` at the image's commit (BND-1).
- A restart never changes `ORVANO_VERSION` (PIN-2).
- Local mode never listens on a network address (LCL-4).

### Security model

- The setup token is the only thing that stands between a fresh internet facing install and whoever reaches it first. It has 256 bits of entropy, a 24 hour life, is issued at start only when none is valid, is replaced on demand, and is stored hashed. Printing it to standard output is CON-6's design; anyone who can read the container's output already has host access (spec 0006 places a hostile host user out of scope).
- The fragment form keeps the token out of Caddy's and Kestrel's access logs and out of `Referer` headers; the page removes it from the address bar at once (URL-5).
- Both new endpoints are public and run with no caller (EPT-5). The check endpoint answers the same for every wrong token, is rate limited (STP-8), checks `Origin` (CON-2, without a CSRF header since no session exists), and changes nothing, so it cannot be used to lock the real operator out.
- `install.sh` is fetched over HTTPS from the repo's `main` branch. Everything else it runs is the signed image (IMG-3, SUP-8), but the script does not verify that signature; the guide shows an optional `cosign verify` and the download, read, then run path for anyone who will not pipe a script to `sh`.
- Local mode uses insecure cookies only on a loopback HTTP address bound to `127.0.0.1` (CON-1, LCL-4).
- Orvano sends no telemetry (TEL-1).

### Configuration required

New or changed `.env` keys (all written by init; `.env.example` documents them, GDE-2):

- `COMPOSE_PROFILES`: `server` or `local`, picks the Caddy service (LCL-2).
- `ORVANO_VERSION`: now required and pinned by init; no `edge` default (PIN-2).
- `ORVANO_IMAGE`: optional image repository, default `ghcr.io/orvanohq/orvano` (CI and mirrors).
- `ORVANO_HTTP_PORT`: optional published port for local mode, default 80 (PRT-1).
- `ORVANO_INSECURE_COOKIES`: `true` in local mode, passed to the `orvano` service (LCL-3, CON-1).

Build: `ORVANO_IMAGE_VERSION` build argument (PIN-1). No new server setting: the setup token lifetime is fixed at 24 hours.

### Critical test scenarios

- Happy path: on a clean runner, `install.sh --local` exits 0, the console redirects to setup, the printed token gets `204`; verifies **AC-1**, **AC-5**, **AC-6**, **AC-7**, **AC-15**.
- Restart: `compose down` and `up` keep the token valid; a rerun keeps `created_at`, `root.key`, and `.env`, prints a new link, and the old token gets `401`; a second install folder is refused; verifies **AC-9**, **AC-10**, **AC-11**.
- Token edge cases (integration tests): a wrong token, an expired token (`FakeTimeProvider` past 24 h), a replaced token, and a token after `setup_completed_at` is set each get the right answer; a restart with a valid token issues none, with an expired one issues one, and two concurrent starts issue at most one; the 11th check in a minute gets `429`; a foreign `Origin` gets `403`; verifies **AC-6**, **AC-7**.
- Init refusals (init CLI tests): each existing bundle path makes init refuse and change nothing; the written files match `/app/install/` byte for byte; `.env` has the pinned version and the right profile per mode; verifies **AC-3**, **AC-4**, **AC-13**.
- Setup page (component and end to end tests): the hash is read once and removed, a typed token works, rejected and limited states show, nothing lands in storage, axe passes in both themes; verifies **AC-8**.
- Script refusals (CI): unknown flag, both modes, no engine on `PATH`, busy port, `--domain` with an IP or a port flag, a rerun whose flags conflict with `.env`; verifies **AC-11**, **AC-12**, **AC-13**.
- Permissions: another Linux user cannot read `root.key`; init without `--user` fails safely; verifies **AC-15** (INS-5).

## Build plan

Tracer Bullet inside the feature: milestone 1 runs one thin thread from the image to the browser (bundle in the image, `--local` install, the instance row, the config endpoint, the console redirect, and the CI leg that proves it). Later milestones thicken the token, the page, the server mode, and the proof.

**Milestone 1: the thread, `--local` on one machine**

1. [x] Confirm the tool assumptions on the CI runner and Docker Desktop: `compose up --wait` treats `migrate`'s exit 0 as success on Compose 2.24, directly and as Podman's provider (else `install.sh` waits on `orvano`'s health with a poll instead); two services under different profiles may declare the same fixed IP. Record the results in ISH-2, raising the minimum versions if needed; satisfies **AC-1**, **AC-11**.
2. [x] `Dockerfile`: `ARG ORVANO_IMAGE_VERSION`, its `ENV` and version label, and the copy of the bundle files to `/app/install/`; `image.yml` passes `sha-<commit>` (PIN-1, BND-1); satisfies **AC-3**, **AC-4**.
3. [x] `compose.yaml`: the pinned image line, the two Caddy profiles, the local port variable, the `orvano` health check, `ORVANO_INSECURE_COOKIES`; `Caddyfile` site line; `.env.example` (PIN-2, LCL-1 to LCL-3, PRT-1, GDE-2); satisfies **AC-4**, **AC-13**.
4. [x] `orvano init`: write the bundle, the widened refusal and cleanup, `--local`, `--image`, `--http-port`, the `--domain` refusals, the pinned version and profile, with init CLI tests (BND-2 to BND-4, LCL-3); satisfies **AC-3**, **AC-4**, **AC-13**.
5. [x] Create `Orvano.Modules.Platform` (log block 9000, its `PlatformDbContext` in schema `platform`, `UseOrvanoConventions`), the `platform.instance` entity and first migration with the seeded row, add it to `MigrationOrder`, the `Dockerfile` restore layer, and the architecture tests; satisfies **AC-5**, **AC-6**.
6. [x] `GET /v1/console/config` with `setupRequired` (FRS-1), its contract entry, and integration tests; the console boot's config load and the root redirect to a first `routes/setup.tsx` in `AuthLayout` with its heading only (FRS-2, FRS-3); satisfies **AC-5**.
7. [x] `deploy/install.sh` for `--local` with Docker: the `main "$@"` shape and stdin handling, flag parsing, the checks of ISH-2, ISH-4, ISH-5, ISH-7, ISH-8, and for now a final line pointing to `/console/setup` (ISH-6 comes in step 10); satisfies **AC-1**, **AC-10**, **AC-11**.
8. [x] The CI `install` job, amd64 Docker leg only, with `shellcheck` and SMK-3's checks that exist so far (redirect, CSP, `setupRequired`, restart persistence); join it to `ci-gate` (SMK-1, SMK-2, SMK-5); satisfies **AC-9**, **AC-15**.

**Milestone 2: the setup token**

9. Token issue (STP-1, STP-2) in the Platform module, the start hook in `serve` for the `api` role with the banner on `Console.Out` (STP-3, STP-4), and `orvano admin setup-token` with its exit codes (STP-5); integration tests with `FakeTimeProvider`; satisfies **AC-6**.
10. `install.sh` ISH-6 (print the link from `admin setup-token`, or the running URL after setup); satisfies **AC-1**, **AC-10**.
11. `POST /v1/console/setup/token` with STP-6's check, the error codes, the `Origin` check, and STP-8's limiter; contract entry; integration tests for each answer; satisfies **AC-7**.
12. CI: add the token checks of SMK-3 (valid `204`, changed `401`, foreign origin `403`, still valid after `down` and `up`, old token `401` after a rerun); satisfies **AC-7**, **AC-9**, **AC-15**.

**Milestone 3: the setup page**

13. The setup page: the fragment read and `replaceState`, the token field and states of FRS-4 to FRS-6, the API call through the generated console client, Lingui strings with the `ar` catalogue; component tests; satisfies **AC-8**.
14. End to end and accessibility: a Playwright test from `/console/` to the accepted state, axe in both themes and right to left, `/setup` added to the CSP test (FRS-7); satisfies **AC-5**, **AC-8**.

**Milestone 4: server mode, Podman, both CPUs**

15. `install.sh --domain` (server profile, the port checks for both ports, the `--domain` refusals) and `--engine podman` with the provider check, rootless detection, and rootless Docker (ISH-4); the `compose config` check of a server bundle in CI; satisfies **AC-2**, **AC-12**.
16. CI: the `ubuntu-24.04-arm` Docker leg and the rootless Podman leg (Docker Compose as the provider, `podman.socket`, the `localhost/orvano:ci` name check of SMK-2), and SMK-4's INS-5 checks on every leg; satisfies **AC-12**, **AC-15**.
17. TEL-1: an integration test that `serve` with no `OTEL_EXPORTER_OTLP_ENDPOINT` opens no outbound connection during start and the setup flow (an `HttpClient` handler that fails the test on any send), and a grep in CI that `install.sh` calls no URL; satisfies **AC-14**.

**Milestone 5: the guide and sign off**

18. `docs/self-hosting.md` (GDE-1, AC-16's list) and the README pointer (GDE-2); spec 0007's INS-3 and INS-5 Linux follow up closed with the CI results; satisfies **AC-16**.
19. Run [verify.md](verify.md)'s manual steps (macOS with Docker Desktop, a real domain on a Linux server, rootful Podman, WSL2) and record the results there; satisfies **AC-1**, **AC-2**, **AC-12**.

## Consequences

**Positive**:

- One command installs Orvano on a laptop or a server, and a second run is safe.
- The install files always match the image that runs them, which is the base feature 53 needs for upgrades.
- Nobody can claim a fresh internet facing install without host access.
- CI proves the install on both CPUs and on rootless Podman, closing spec 0007's open Linux follow up.
- No telemetry: nothing to explain or opt out of.

**Negative / tradeoffs**:

- Piping a script from GitHub to `sh` is a habit some operators reject; the guide must keep the manual path working and documented.
- The setup token appears in container output, which some log shippers collect; the 24 hour expiry and replacing it on demand limit the damage, and the guide says to finish setup promptly.
- Two Caddy services under profiles is less obvious than one; the compose comments must explain it.
- The CI job adds two native image builds and a Podman leg to every server, console, or deploy pull request (several minutes of runner time each).
- Pinning `ORVANO_VERSION` means `compose pull` alone never upgrades; until feature 53, moving versions is a hand edit.
- macOS and Windows are proven by a person, not by CI.
- One install per host, and no switching an install between local and server mode.

**Neutral**:

- `Orvano.Modules.Platform` and `platform.instance` arrive in feature 12, not 13. Spec 0011's CAT-1 and its feature 13 follow up line change accordingly.
- `GET /v1/console/config` arrives in feature 12 with one field; feature 13 grows it (CPX-3).
- The `edge` default for `ORVANO_VERSION` goes away.

## Follow-up

- [ ] Spec 0011: add an `Updated` line: `platform.instance` is built by feature 12 and gains `setup_token_expires_at`; CAT-1's table header changes from (13) to (12); the feature 13 follow up no longer creates `Orvano.Modules.Platform` (it already exists) but still creates the other CAT-1 tables.
- [ ] Spec 0007: add an `Updated` line: INS-5's refusal covers the whole bundle (BND-3); INS-3's init gains `--local` and writes the bundle (BND-2); IMG-1 copies `/app/install/` and takes `ORVANO_IMAGE_VERSION` (BND-1, PIN-1); TLS-1's Caddy site comes from `ORVANO_SITE_ADDRESS` under two profiles (LCL-1). Close the "prove INS-3 and INS-5 on a Linux host" follow up with SMK-4's results, and the feature 12 guide follow up with GDE-1.
- [ ] Spec 0006: add an `Updated` line: CON-6's token lifecycle is STP-1 to STP-6 here (24 hour expiry, issued at start only when none is valid, replaced by `orvano admin setup-token`), the banner is CON-6's exception to SCR-5 and LOG-2 (STP-4), and CON-2 gains an exception: the setup token check, with no session, needs the `Origin` check only, no `Orvano-Csrf` header (STP-7).
- [ ] Spec 0014: CPX-3's config call ships in feature 12 with `setupRequired` only; feature 13 adds the rest.
- [ ] Spec 0012: EPT-5's list of public endpoints gains `GET /v1/console/config` (now from feature 12) and `POST /v1/console/setup/token`.
- [ ] Spec 0003: RTE-10's boot loads only the config in feature 12; feature 13 adds the session call beside it.
- [ ] Spec 0015: AC-21's boot error page now also covers a failed config load (FRS-2), and AC-23's CSP test loads `/setup` (FRS-7).
- [ ] Feature 13: the owner form on `/console/setup` after the accepted state, using the token in memory, STP-6's check and the same transaction that sets `setup_completed_at` and clears the token columns.
- [ ] Feature 23: replace STP-8's local limiter with the shared rate limit engine.
- [ ] Feature 53: design moving `ORVANO_VERSION` (and refreshing the bundle files from the new image) as the upgrade step.
- [ ] Feature 59: once releases exist, `install.sh`'s default `--version` becomes the latest release, and the one line install should point at a release tag's `install.sh` rather than `main`.
- [ ] Spec 0007 HSH-2 on the x86 reference box stays open (not part of this feature's proof).
- [ ] Rootful Podman on Linux and native Windows (without WSL2) are not CI proven; reconsider after alpha.
