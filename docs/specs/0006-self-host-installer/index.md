# 0006. Self host installer

**Date**: 2026-09-26
**Status**: Accepted

## Summary

This spec gives Orvano a one command install on a single Linux server: `curl -fsSL https://github.com/orvanohq/orvano/releases/latest/download/install.sh | sudo sh`. A small shell script checks the server and then hands the real work to `orvano install`, a command inside the server image that asks for your domain, generates every secret, and writes the compose file for its own version into `/opt/orvano`. The script then starts the containers, waits until they are healthy, and prints the console URL, the master key to back up, and a one time setup link. Only the person holding that link can create the first admin account, which closes the race where a stranger signs up first. Running it again never changes a secret: the same version repairs the install, and a newer version upgrades it in place.

## Requirements

**User stories**:
- As someone self hosting Orvano, I want one command on a fresh server to give me a working Orvano at my domain with HTTPS, so that I don't have to read a compose file or generate secrets by hand.
- As that person, I want running the command again to be safe, and a newer release of it to upgrade me in place, so that I never fear breaking my install.
- As that person, I want to be the only one who can create the first admin account, so that nobody who finds my server first can take it over.
- As an operator using cloud init, Ansible, or CI, I want every prompt to have a flag, so that I can install Orvano without a terminal.
- As the Orvano team, I want CI to install Orvano with the real installer on every change, so that the install path never silently breaks.

**Acceptance criteria** (the contract):

Command and preflight
- **AC-1**: On a clean supported server (Ubuntu 22.04 or 24.04, Debian 12 or 13, `amd64` or `arm64`) whose domain resolves to it, `curl -fsSL https://github.com/orvanohq/orvano/releases/latest/download/install.sh | sudo sh` asks for the domain and an optional email, installs the version stamped into that script, and exits 0 once `GET https://<domain>/v1/health` answers 200 through Caddy with a Let's Encrypt certificate. It ends by printing the console URL, the setup link (AC-19), and, on a fresh install, the master key (AC-15).
- **AC-2**: Before writing anything, the installer refuses with exit code 2 and one plain message when: it is not running as root; the architecture is not `amd64` or `arm64`; `MemTotal` is under 1.8 GiB; Docker runs rootless or with `userns-remap` (files would not be owned by host root); a Compose project named `orvano` already exists with a working directory other than `<dir>` (one install per host); port 80 or 443 is held by anything other than this install's gateway container (a container labelled `com.docker.compose.project=orvano` and `com.docker.compose.service=gateway` that publishes the port); the Orvano data volume `orvano_orvano-pg` exists but `<dir>/.env` does not; or another installer run holds the lock (AC-25).
- **AC-3**: It warns and asks before continuing (default no; `--yes` continues) when: the distro is not a supported one but Docker Engine 24 or later with Compose 2.24 or later is present; `MemTotal` is under 3.5 GiB; `ufw` is active and does not allow 80 and 443; `timedatectl` reports the clock is not NTP synchronized (Let's Encrypt fails on a wrong clock); `<dir>/.env` has an `ORVANO_VERSION` but the data volume `orvano_orvano-pg` does not exist ("starting with an empty database").
- **AC-4**: When Docker Engine or the Compose v2 plugin is missing or older than AC-3's minimum on a supported distro, the installer asks to install it with Docker's official install script (`https://get.docker.com`). Yes (or `--yes`) installs it and continues. No exits 2 with a link to Docker's install docs. On an unsupported distro it never offers and exits 2 with that link.
- **AC-5**: The domain comes from `--domain` or a prompt (defaulting to the host of the current `ORVANO_PUBLIC_URL` when one exists). It must be a lowercase hostname with at least one dot, labels of 1 to 63 of `a-z0-9-`, at most 253 characters, not an IP address, no scheme, port, or path; or exactly `localhost`. Anything else is asked again, or exits 2 without a terminal. A domain gives `ORVANO_PUBLIC_URL=https://<domain>`; `localhost` gives `http://localhost` and a "not for production" warning.
- **AC-6**: For a domain (not `localhost`), the installer resolves its A and AAAA records and compares them with this server's addresses: its IPv4 and IPv6 interface addresses, then its public addresses from `https://ipv4.icanhazip.com` and `https://ipv6.icanhazip.com` (each allowed to fail, for example on an IPv6 only host) unless `--no-ip-lookup` is given. No record, or no match, explains that HTTPS will fail until DNS points here, and asks whether to continue (default no; `--yes` continues).
- **AC-7**: The Let's Encrypt account email comes from `--email` or a prompt with an empty default. An empty value is allowed. A non empty value must match `^[^\s@]+@[^\s@]+$`, otherwise it is asked again (exit 2 without a terminal). The gateway uses it as the ACME account email when set, and starts with no account email when empty.

Files and secrets
- **AC-8**: A fresh install (no `ORVANO_VERSION` in `<dir>/.env`, because there is no `.env` or because you wrote one yourself beforehand) creates `/opt/orvano` (or `--dir`), owned by root, with `docker-compose.yml` (0644), `initdb/10-orvano-roles.sh` (0755), `.env` (0600), and `install.log` (0600), and never creates `docker-compose.override.yml`. `.env` holds the keys in *Install layout*; every key missing from it gets a fresh value from a cryptographic random source, and every key already in a `.env` you wrote beforehand is kept (so you can seed `ORVANO_PG_TUNING=manual` or your own passwords before the first run).
- **AC-9**: Running the installer again never changes the value of a secret already in `.env` (`POSTGRES_PASSWORD`, `ORVANO_ADMIN_PASSWORD`, `ORVANO_APP_PASSWORD`, `ORVANO_MASTER_KEYS`, `ORVANO_SETUP_TOKEN`). A missing one is generated, except a missing database password while the data volume exists, which refuses with exit 2 (a new password would not match the database). Keys, comments, and blank lines the installer does not manage stay as they were, in the same order.
- **AC-10**: Every run rewrites `docker-compose.yml` and `initdb/10-orvano-roles.sh` from the copies embedded in the image of the version being installed, and never reads, writes, or deletes `docker-compose.override.yml`. Every Compose command runs with the install directory as its working directory and no `-f` flag, so Compose merges your override file.
- **AC-11**: Every run sets `ORVANO_PG_SHARED_BUFFERS`, `ORVANO_PG_EFFECTIVE_CACHE_SIZE`, `ORVANO_PG_WORK_MEM`, `ORVANO_PG_MAINTENANCE_WORK_MEM`, and `ORVANO_PG_MEMORY_LIMIT` from `MemTotal` by the rule in *Postgres tuning*, unless `.env` has `ORVANO_PG_TUNING=manual`, in which case it leaves all five untouched. At exactly 4096 MiB the values equal spec 0002's (`512MB`, `1536MB`, `8MB`, `128MB`, `1280M`).
- **AC-12**: `install.log` gets one timestamped line per step (preflight results, version decision, files written, Compose results). It never contains a secret, the setup token, or the master key.

Version, repair, upgrade
- **AC-13**: The version to install is `--version`, else the version stamped into the script at release. The installer writes it to `ORVANO_VERSION`. Compared with the `ORVANO_VERSION` already in `.env`: none means a fresh install; the same means a repair; higher means an upgrade (images pulled, then `up -d`, where the `migrate` role runs before every other role as today); lower refuses with exit 2 ("downgrades are not supported; restore a backup instead").
- **AC-14**: `--no-pull` never pulls: the installer image and every service image must already be present locally (for air gapped servers and CI), otherwise it exits 2 naming the missing image.

Master key
- **AC-15**: When this run generated `ORVANO_MASTER_KEYS`, the installer prints the key, the `.env` path, and a warning that losing it makes stored secrets unrecoverable, then waits until you type `saved`. With `--yes` or no terminal it prints the same block and does not wait. A run that did not generate the key never prints it, only a one line reminder with the `.env` path. If `orvano install` exited 0 but its result file is missing or unreadable, the script cannot tell, so it prints the full block (a lost key is unrecoverable, and root can already read `.env`) and logs `could not read .install-result`. This happens whether or not AC-16 succeeded.

Start and verify
- **AC-16**: After `docker compose up -d --remove-orphans`, the installer waits up to `--timeout` seconds (default 300) until `migrate` has exited 0 and `postgres`, `api`, `worker`, and `realtime` report healthy and `gateway` is running. It then retries `GET <ORVANO_PUBLIC_URL>/v1/health` for up to 120 seconds (time for the first certificate). All passing exits 0.
- **AC-17**: If `migrate` exits non zero, a service is still unhealthy at the timeout, or the public health check fails, the installer exits 3. It names the failing service or check, shows its last 50 log lines, and prints the exact `docker compose` commands to look further (run from the install directory). For a failing public check with healthy services, it names DNS, the firewall, and the gateway log as the likely causes. It never stops, removes, or rolls back anything, and running it again after the fix continues from there.
- **AC-18**: When a run on an existing install (one with `ORVANO_VERSION`) would change `ORVANO_PUBLIC_URL`, including a switch to or from `localhost`, it first warns that every signed in app user and console user must sign in again (tokens name the old URL, spec 0004 AC-6) and asks (default no; `--yes` continues).

First admin (server and console)
- **AC-19**: When the install has no install admin, the installer prints `<ORVANO_PUBLIC_URL>/setup#<ORVANO_SETUP_TOKEN>` as the setup link, reading "no install admin" from the running `api` (AC-24), so it works even when the public check failed. Once an install admin exists it prints no link.
- **AC-20**: While no install admin exists and `ORVANO_SETUP_TOKEN` is set, `consoleAccount.create` without a `setupToken` equal to it (compared in constant time) gets 403 `setup_token_invalid` and creates nothing. With the right token it is admitted as the first account (spec 0003 AC-7, including the race rule). Once an install admin exists, `setupToken` is ignored and spec 0003's invite rules apply.
- **AC-21**: The `api` refuses to start when `ORVANO_SETUP_TOKEN` is set but is not `ost_` followed by exactly 43 base64url characters. In the `Production` environment it also refuses to start when the token is unset and no install admin exists, with a message that says to run the installer or set `ORVANO_SETUP_TOKEN`. In `Development` and `Test`, an unset token leaves the first sign up open, as spec 0003 AC-7 has it today.
- **AC-22**: `consoleInstall.getSetup` (`GET /v1/console/install/setup`) needs no session and answers 200 `{ "setupRequired": true }` exactly while no install admin exists, else `{ "setupRequired": false }`. It is rate limited to 60 requests per minute per connection IP.
- **AC-23**: The console route `/setup` reads the token from the URL fragment, then removes the fragment from the address bar and history (`history.replaceState`) before anything else renders. While `setupRequired` is true it shows a "Create the first admin" form (name, email, password) that calls `consoleAccount.create` with `setupToken`, and on success lands signed in on the new personal org. A 403 `setup_token_invalid` shows "This setup link is not valid. Run the installer again on your server to see the right link." When `setupRequired` is false it redirects to `/sign-in`. When `setupRequired` is true, `/sign-in` shows "Finish setting up Orvano: open the setup link the installer printed on your server" in place of the form. Both screens meet WCAG AA.
- **AC-24**: `orvano setup-status`, run inside the `api` container, calls `http://localhost:8080/v1/console/install/setup` and exits 0 printing `required` or `done`, or exits 1 when the API does not answer.

Gateway and runtime
- **AC-25**: Only one installer run at a time: the script holds an exclusive `flock` on `<dir>/.install.lock` for its whole run, and a second run exits 2 with "another install is running". The lock is released when the process ends, however it ends.
- **AC-26**: Installs on HTTPS send `Strict-Transport-Security: max-age=31536000` on every response from the gateway; `http://localhost` installs do not.
- **AC-27**: Every service in the compose file uses the `json-file` log driver with `max-size: 10m` and `max-file: "3"`.

No terminal
- **AC-28**: Every prompt has a flag (see *Command line*). Without a terminal (no readable `/dev/tty`), a missing required value (`--domain` on a fresh install) exits 2 naming the flag, and every yes or no question takes its default unless `--yes` is given. The installer never waits for input without a terminal. `--help` prints every flag and exits 0.

Release and CI
- **AC-29**: Pushing the tag `v<VERSION>` makes `release.yml` build `ghcr.io/orvanohq/orvano` and `ghcr.io/orvanohq/orvano-gateway` for `linux/amd64` and `linux/arm64`, push the tags `X.Y.Z` and `X.Y`, create the GitHub Release `v<VERSION>` (`gh release create --verify-tag --latest`), and attach `install.sh` (with `X.Y.Z` stamped in) and `install.sh.sha256` to it, so `releases/latest/download/install.sh` serves the newest script. Before 0.1, and on a manual run, it builds everything but pushes, creates, and uploads nothing (the existing dry run gate).
- **AC-30**: On both `ubuntu-24.04` and `ubuntu-24.04-arm` runners, CI builds both images tagged `ghcr.io/orvanohq/orvano:<V>` and `ghcr.io/orvanohq/orvano-gateway:<V>` with `<V>` = the `VERSION` file, runs `deploy/install/install.sh --version <V> --domain localhost --yes --no-pull`, and passes only if: the first run exits 0; `/v1/health` answers 200 through Caddy and `/internal/readyz` answers 404; `setup-status` prints `required`; a second identical run exits 0 with every secret in `.env` byte for byte unchanged. ShellCheck passes on `install.sh` with `--shell=sh`. This job replaces the "Images and compose smoke test" job.

## Decision

**Chosen option**: Option 1: A small host script plus an `install` command inside the server image.

`install.sh` (POSIX `sh`) owns only what needs the host: root and architecture checks, Docker, ports, the data volume, the lock, and running Compose. `orvano install` (C# in the server image, run with `docker run`) owns every decision and every file: prompts, validation, DNS check, secrets, `.env`, the compose file for its own version, and the version rule. The first admin is protected by a one time setup token that the installer generates and the API requires on the first console sign up.

**Implementation skills**: `multi-stage-dockerfile` (`github/awesome-copilot`, `.claude/skills/multi-stage-dockerfile/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `security-and-hardening` (`addyosmani/agent-skills`, `.claude/skills/security-and-hardening/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`)

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### How one run works

```
install.sh (host, root)                         orvano install (container)
1. parse flags, take flock on <dir>/.install.lock
2. root, arch, distro, RAM floor, ufw, NTP
3. Docker + Compose present and new enough? rootless or userns-remap?
   missing → offer get.docker.com (AC-4)
4. data volume exists? .env exists? other `orvano` project elsewhere?
5. ports 80/443 free, or published by our gateway container
6. delete any stale <dir>/.install-result, then
   docker run --rm --user 0:0 --network host \
     -v <dir>:/install [-it </dev/tty] \
     ghcr.io/orvanohq/orvano:<version> install   ──► 7. read /install/.env (if any)
     --existing-data=<yes|no> [flags]                8. version rule (fresh, repair, upgrade, refuse)
                                                      9. prompts or flags: domain, email; validate
                                                     10. DNS check (AC-6), domain change warning (AC-18)
                                                     11. generate missing secrets, PG tuning from /proc/meminfo
                                                     12. write .env (temp + rename), docker-compose.yml,
                                                         initdb/10-orvano-roles.sh
                                                     13. write the result file /install/.install-result
                                                         (temp + rename, 0600): generated_master_key=<0|1>
   exit 0 only: read, then delete
   <dir>/.install-result             ◄──────────────
14. cd <dir>; docker compose pull (unless --no-pull)
15. docker compose up -d --remove-orphans
16. poll `docker compose ps --all --format json` (AC-16)
17. curl <ORVANO_PUBLIC_URL>/v1/health with retries
18. docker compose exec -T api /app/orvano setup-status
19. summary: console URL, setup link if required, master key block (AC-15)
20. exit 0, 2, or 3
```

The container never talks to Docker (the chiseled image has no Docker client and gets no Docker socket). The script makes only the host checks (steps 1 to 5 and the health wait) and never a decision the container could make. Everything about values, files, and versions is unit tested C#; the host checks and their yes or no prompts are proven by ShellCheck and the CI install (AC-30).

`install` and `setup-status` are matched in `OrvanoProgram.RunAsync` before `RoleSelector`, like `healthcheck`, so they need no `ORVANO_ROLE`. `setup-status` finds the port the same way `HealthcheckCommand` does.

### Command line

| Flag | Default | Meaning |
|---|---|---|
| `--domain <host>` | prompt; current domain on a rerun | hostname or `localhost` (AC-5) |
| `--email <addr>` | prompt, empty allowed; current value on a rerun | ACME account email (AC-7) |
| `--version <X.Y.Z>` | stamped at release | version to install (AC-13) |
| `--dir <path>` | `/opt/orvano` | install directory |
| `--yes` | off | answer yes to every question, never wait at the master key block |
| `--no-pull` | off | use local images only (AC-14) |
| `--no-ip-lookup` | off | skip the public address lookup in the DNS check (AC-6) |
| `--timeout <seconds>` | `300` | health wait (AC-16) |
| `--help` | | print flags and exit 0 |

Exit codes: `0` success, `1` unexpected error, `2` refused (bad input, failed preflight, downgrade, lost `.env`, lock held), `3` started but unhealthy or unreachable.

Prompts read from `/dev/tty`, because under `curl ... | sh` standard input is the script itself. The script passes `-it` and `</dev/tty` to `docker run` only when `/dev/tty` is readable.

### Install layout

`/opt/orvano/` (owned by root):

| Path | Owner | Mode | On every run |
|---|---|---|---|
| `docker-compose.yml` | installer | 0644 | rewritten from the image; starts with a comment saying it is managed and to use the override file |
| `docker-compose.override.yml` | you | yours | never created, read, written, or deleted |
| `.env` | shared | 0600 | secrets kept; managed keys updated in place; your keys, comments, and order kept |
| `.env.previous` | installer | 0600 | the `.env` before this run, written only when this run changed `.env` |
| `initdb/10-orvano-roles.sh` | installer | 0755 | rewritten from the image |
| `install.log` | installer | 0600 | appended, never holds secrets |
| `.install.lock` | installer | 0600 | lock file for `flock` |
| `.install-result` | installer | 0600 | written by `orvano install` when it succeeds, read and deleted by `install.sh` in the same run; never left behind after a successful run |

`.env` keys:

| Key | Written | Value |
|---|---|---|
| `POSTGRES_PASSWORD`, `ORVANO_ADMIN_PASSWORD`, `ORVANO_APP_PASSWORD` | when missing (AC-9) | 24 random bytes as lowercase hex (48 characters) |
| `ORVANO_MASTER_KEYS` | when missing | `k<yyyymmdd>:<base64 of 32 random bytes>` (UTC date of generation), one entry |
| `ORVANO_SETUP_TOKEN` | when missing | `ost_` + 32 random bytes as unpadded base64url (43 characters) |
| `ORVANO_PUBLIC_URL` | every run | `https://<domain>` or `http://localhost` |
| `ORVANO_ACME_EMAIL` | every run | email or empty |
| `ORVANO_VERSION` | every run | the version being installed |
| `ORVANO_PG_*` (five keys) | every run unless `ORVANO_PG_TUNING=manual` | *Postgres tuning* |

Random values come from `RandomNumberGenerator`, never `System.Random`. A new `.env` is written to `.env.tmp` (0600) and renamed over `.env`, so a crash never leaves half a file.

### Postgres tuning

With `M` = `MemTotal` in MiB (read from `/proc/meminfo`, which shows the host's memory inside the container), rounded down to whole MB:

| Key | Rule |
|---|---|
| `ORVANO_PG_SHARED_BUFFERS` | `M / 8`, at least 128, at most 8192, in `MB` |
| `ORVANO_PG_EFFECTIVE_CACHE_SIZE` | `M * 3 / 8`, in `MB` |
| `ORVANO_PG_WORK_MEM` | `8MB` below 8192 MiB, else `16MB` |
| `ORVANO_PG_MAINTENANCE_WORK_MEM` | `M / 32`, at least 64, at most 1024, in `MB` |
| `ORVANO_PG_MEMORY_LIMIT` | `M * 5 / 16`, in `M` (the Compose unit) |

The compose file reads each with spec 0002's value as the fallback (`${ORVANO_PG_SHARED_BUFFERS:-512MB}` and so on), so a hand written `.env` keeps working. A change restarts Postgres on `up`.

### Repository changes around the compose file

- `deploy/compose/docker-compose.yml` becomes image only (no `build:` keys) and is the file the installer embeds. The `build:` sections move to `deploy/compose/docker-compose.build.yml`. Local production shape becomes `docker compose -f docker-compose.yml -f docker-compose.build.yml up --build` from `deploy/compose/`.
- `deploy/postgres/initdb/` moves to `deploy/compose/initdb/`, so the repo and `/opt/orvano` share one layout and the compose file mounts `./initdb`. `tests/scenarios/compose.yml` and the Testcontainers step in `ci.yml` follow the move.
- An `x-logging` anchor (AC-27) goes on every service. `postgres` reads the five tuning keys (AC-11).
- `deploy/compose/.env.example` lists every key in *Install layout*, including `ORVANO_SETUP_TOKEN`.
- `Orvano.Server.csproj` embeds `deploy/compose/docker-compose.yml` and `deploy/compose/initdb/10-orvano-roles.sh` as resources, and `deploy/server.Dockerfile` copies `deploy/compose/` into the build stage.

### Gateway

- `deploy/gateway/Caddyfile` opens with a global options block that holds `import /etc/caddy/global.caddy`. A small entrypoint script in the gateway image writes that file before starting Caddy: `email <ORVANO_ACME_EMAIL>` when set, empty otherwise (AC-7). Caddy can't take an empty `email` value, which is why this is written at start and not substituted.
- The site block adds `Strict-Transport-Security "max-age=31536000"` only for requests that match `protocol https` (AC-26). No `includeSubDomains` (other subdomains may not be Orvano's, row 30) and no `preload`.
- The compose `gateway` service gains `ORVANO_ACME_EMAIL`.

### API surface

| Operation | Method and path | Key inputs | Key outputs | Auth | Key errors |
|---|---|---|---|---|---|
| `consoleInstall.getSetup` (new) | GET `/v1/console/install/setup` | none | `setupRequired: boolean` | none (the fifth console route without a session, next to spec 0004's four) | 429 `rate_limited` |
| `consoleAccount.create` (spec 0004, changed) | POST `/v1/console/account` | adds `setupToken?: string` | unchanged | none | 403 `setup_token_invalid` (new), plus spec 0004's |
| `orvano install` (CLI, container) | `docker run ... orvano:<v> install [flags]` | *Command line*, `--existing-data` from the script | files in `/install` (including `.install-result`), exit code | root in the container, only the install dir mounted | exit 2 |
| `orvano setup-status` (CLI, container) | `docker compose exec -T api /app/orvano setup-status` | none | `required` or `done` | runs inside `api` | exit 1 |
| `install.sh` (host) | `curl ... \| sudo sh -s -- [flags]` | *Command line* | running install, summary | root | exit 2, 3 |

`setup_token_invalid` joins `contract/errors.tsp` with its code, 403, and the text "The setup link is not valid for this install."

`IConsoleSignupPolicy.AdmitAsync` gains a `string? setupToken` parameter. The first account branch compares it with the configured token (when set) before admitting, inside the same `platform_install_settings` row lock, and returns a new `SignupAdmission.SetupTokenInvalid` that Auth maps to 403 `setup_token_invalid`.

A new Platform contract, `IInstallSetupState.IsSetupRequiredAsync(CancellationToken)` (true while `platform_install_admins` is empty), serves `consoleInstall.getSetup` and the `api` startup check in AC-21.

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| `orvano install` | version to install | `--version` flag, else the script's stamped version passed as `--version` |
| `orvano install` | installed version | `ORVANO_VERSION` in the mounted `.env` |
| `orvano install` | data volume exists | `docker volume inspect orvano_orvano-pg` in the script, passed as `--existing-data` |
| `orvano install` | domain | `--domain`, else prompt, else the host of `ORVANO_PUBLIC_URL` in `.env` |
| `orvano install` | ACME email | `--email`, else prompt, else `ORVANO_ACME_EMAIL` in `.env` |
| `orvano install` | server addresses for the DNS check | IPv4 and IPv6 interface addresses (the container shares the host network), then `https://ipv4.icanhazip.com` and `https://ipv6.icanhazip.com` |
| `orvano install` | fresh, repair, or upgrade | `ORVANO_VERSION` in `.env` (absent means fresh) compared with the version to install |
| `orvano install` | domain changed | new `ORVANO_PUBLIC_URL` differs from the one in `.env` |
| `orvano install` | Postgres values | `MemTotal` from `/proc/meminfo`, *Postgres tuning* |
| `orvano install` | every secret | `RandomNumberGenerator`, formats in *Install layout* |
| `orvano install` | master key ID date | UTC clock at generation |
| `orvano install` | compose file and init script | embedded resources of the running image |
| `install.sh` | whether a master key was generated | `generated_master_key=<0\|1>` in `<dir>/.install-result`, written by `orvano install` (never printed, so you never see it); missing or unreadable after exit 0 counts as generated (AC-15) |
| `install.sh` | master key and setup token to print | `.env`, read by the script as root |
| `install.sh` | service health | `docker compose ps --all --format json` |
| `install.sh` | setup link needed | `orvano setup-status` inside `api` |
| `install.sh` | port is ours | `docker ps --filter label=com.docker.compose.project=orvano --filter label=com.docker.compose.service=gateway --filter publish=<port>` |
| `install.sh` | other holder of a port | `ss -Htlnp` (process name for the message) |
| `install.sh` | another install on this host | `com.docker.compose.project.working_dir` label on any container of project `orvano` |
| `install.sh` | rootless or remapped Docker | `docker info --format '{{.SecurityOptions}}'` contains `rootless` or `userns` |
| `install.sh` | clock synchronized | `timedatectl show -p NTPSynchronized --value` (skipped when `timedatectl` is absent) |
| `install.sh` | distro | `ID` and `VERSION_ID` in `/etc/os-release` |
| `consoleInstall.getSetup` | `setupRequired` | `platform_install_admins` is empty (`IInstallSetupState`) |
| `consoleAccount.create` | configured setup token | `ORVANO_SETUP_TOKEN`, read once at `api` start |
| console `/setup` | token | URL fragment, held in component state only |
| `release.yml` | stamped version | `VERSION` file (spec 0001) |
| CI install job | image tag and `--version` | `VERSION` file |

### Key invariants

- A secret in `.env` is never changed or removed by the installer.
- The installer never touches `docker-compose.override.yml`, and never deletes a container, volume, or file it did not write in this run (apart from replacing its own files).
- The installed version never goes down.
- The setup token, the master key, and the database passwords never appear in `install.log`, in the API's logs, or in a URL sent to the server (the token lives only in the fragment).
- The setup token admits exactly one account: the first. After that it has no effect.
- The compose file on the server always matches the version of the images it names, because both come from the same image.

### Security model

- The script needs root; it writes only under the install directory, plus Docker's own install when you agree to it. It never changes firewall rules.
- `.env`, `.env.previous`, and `install.log` are 0600 root. The installer container mounts only the install directory, runs as root inside the container (to write root owned 0600 files), gets no Docker socket, and is removed when it ends.
- The setup token is 256 random bits, compared in constant time, and useful only while no install admin exists. Its length is what makes guessing impossible; spec 0004's limit of 60 sign ups per hour per IP only keeps the noise down. The status endpoint reveals only whether an install is still unclaimed, so the README tells you to open the setup link right after installing.
- The script is served from the GitHub release, with a `.sha256` beside it for people who check before running. Images are pulled by tag from GHCR (image signing is a follow up).
- Nothing phones home. The only outside calls are GHCR, Docker's install script (if you agree), Let's Encrypt (through Caddy), and the public address lookup (skippable).

### Configuration required

- `ORVANO_SETUP_TOKEN` (new, `api`): the one time token for the first console account, validated at startup (AC-21).
- `ORVANO_ACME_EMAIL` (new, `gateway`): optional Let's Encrypt account email.
- `ORVANO_PG_SHARED_BUFFERS`, `ORVANO_PG_EFFECTIVE_CACHE_SIZE`, `ORVANO_PG_WORK_MEM`, `ORVANO_PG_MAINTENANCE_WORK_MEM`, `ORVANO_PG_MEMORY_LIMIT` (new, compose only): Postgres memory settings.
- `ORVANO_PG_TUNING` (new, installer only): `manual` stops the installer from retuning.
- Existing keys the installer now generates: `POSTGRES_PASSWORD`, `ORVANO_ADMIN_PASSWORD`, `ORVANO_APP_PASSWORD`, `ORVANO_MASTER_KEYS`, `ORVANO_PUBLIC_URL`, `ORVANO_VERSION`.
- GitHub: the new `release.yml` jobs get job level `permissions:` (`packages: write` for the images job, `contents: write` for the release job), leaving the workflow's top level `contents: read` as it is.

### Critical test scenarios

- Happy path: CI installs with `--domain localhost --yes --no-pull`, health passes through Caddy, `setup-status` says `required`, a second run exits 0 and leaves secrets byte for byte the same, verifies **AC-10**, **AC-14**, **AC-16**, **AC-19**, **AC-24**, **AC-30**.
- Setup token: on a fresh database with the token set, sign up without it gets 403 `setup_token_invalid`, with a wrong one 403, with the right one creates the install admin; a later sign up with the same token gets spec 0003's `signup_closed`; two racing first sign ups with the token give exactly one admin, verifies **AC-20**, **AC-22**.
- Startup refusal: `api` in `Production` with no token and no admin refuses to start; with a malformed token refuses in any environment; in `Development` with no token the first sign up is admitted, verifies **AC-21**.
- Rerun safety: a unit test runs the install plan against a `.env` with custom keys and comments, and every secret, custom key, comment, and line order survives; a missing database password with `--existing-data=yes` refuses, verifies **AC-9**.
- Version rule: fresh, repair, upgrade, and downgrade cases, including `0.10.0` over `0.9.3`, verifies **AC-13**.
- Failure: with a service forced unhealthy (a bad `ORVANO_MASTER_KEYS` in an override file), the installer exits 3, names `api`, prints its log lines, and removes nothing, verifies **AC-17**.
- Console: `/setup` removes the fragment before render, creates the admin, redirects when setup is done, shows the invalid link message, and passes axe; `/sign-in` shows the finish setup notice while `setupRequired`, verifies **AC-23**.
- No terminal: without `/dev/tty` and without `--domain` on a fresh install, it exits 2 naming `--domain` and never blocks, verifies **AC-28**.

## Build plan

Tracer Bullet: the first three tasks make a thin, real thread (repo compose file, then `orvano install`, then `install.sh`, proven by CI on `localhost`). Later tasks thicken it with the checks, the first admin gate, the console screens, and publishing.

1. **Compose file ready to embed**: make `docker-compose.yml` image only and add `docker-compose.build.yml`; move `deploy/postgres/initdb` to `deploy/compose/initdb` and update `tests/scenarios/compose.yml` and `ci.yml`; add the `x-logging` anchor and the five `ORVANO_PG_*` keys with spec 0002 fallbacks; update `.env.example`. Satisfies **AC-10**, **AC-11**, **AC-27**.
2. **`orvano install`, thin**: an `Install/` folder in `Orvano.Server` with plain, unit tested types (no Docker, no ASP.NET): `EnvFile` (parse and write keeping unknown lines and order), `InstallSecrets` (formats above), `PgTuning`, `VersionRule`, `DomainRule`, `EmailRule`, `InstallPlan` (inputs to decisions and files). The `install` subcommand is checked before role selection, like `healthcheck`, and writes files from embedded resources with flags only (no prompts yet), `.env` written atomically, `.env.previous`, `install.log`, and the result file `.install-result`. Embed the resources in the csproj and copy `deploy/compose/` in the Dockerfile. Satisfies **AC-8**, **AC-9**, **AC-11**, **AC-12**, **AC-13**.
3. **`install.sh`, thin, plus CI**: `deploy/install/install.sh` in POSIX `sh` with flag parsing, `flock`, root and architecture checks, Docker and Compose version checks (refusing when missing), the rootless and `userns-remap` refusal, the one install per host check, the data volume and `.env` check, `docker run` of the installer, `pull` (unless `--no-pull`), `up -d --remove-orphans`, the health poll, the public health check, the failure report, and the summary. The CI job from AC-30 (amd64 and arm64 runners, images tagged from `VERSION`, which also proves `--user 0:0` works in the chiseled image), with ShellCheck, replaces the compose smoke test. Satisfies **AC-2** (root, arch, lost `.env`), **AC-14**, **AC-16**, **AC-17**, **AC-25**, **AC-30**.
4. **Interactive and preflight depth**: prompts through `/dev/tty`, `--help`, the no terminal rules; the domain and email prompts with validation; the DNS check with IPv4 and IPv6 interface addresses and `icanhazip.com`; the domain change warning; distro detection, the RAM floor and warning, the port check (our gateway by Compose labels, anything else named through `ss`), the `ufw`, NTP, and missing data volume warnings; the Docker install offer through `get.docker.com`; the master key block with the typed `saved` confirmation. Satisfies **AC-1**, **AC-2**, **AC-3**, **AC-4**, **AC-5**, **AC-6**, **AC-15**, **AC-18**, **AC-28**.
5. **Gateway**: the entrypoint script writing `global.caddy`, the Caddyfile import and the HSTS rule, and `ORVANO_ACME_EMAIL` in compose. Checked by `caddy validate` with and without an email, and by the CI install (no HSTS on `localhost`). Satisfies **AC-7**, **AC-26**.
6. **Setup token on the server**: `ORVANO_SETUP_TOKEN` validation and the `Production` startup refusal; `IInstallSetupState`; `AdmitAsync(…, setupToken)` and `SignupAdmission.SetupTokenInvalid`; `setup_token_invalid` in `contract/errors.tsp`; `consoleInstall.getSetup` in `contract/platform/install.tsp` with its rate limit; `orvano setup-status`; regenerate with SdkGen. Integration tests against real Postgres call `AdmitAsync` directly when the console sign up endpoint is not built yet. The CI install job and the scenarios compose file set the token. Satisfies **AC-19**, **AC-20**, **AC-21**, **AC-22**, **AC-24**.
7. **Setup token end to end**: add `setupToken` to `consoleAccount.create` and pass it to `AdmitAsync`. This lands with spec 0004 task 8 (the console session), or right after it if that task is already built. Then the console `/setup` route and the `/sign-in` notice, with browser tests (axe) and a Playwright test that installs, opens the printed link, and creates the admin. Satisfies **AC-20**, **AC-23**.
8. **Publishing**: in `release.yml`, an images job (Buildx with QEMU; both Dockerfiles already compile on the build platform, so only the final copy runs emulated) pushing `X.Y.Z` and `X.Y` for both images, and a release job that creates the GitHub Release `v<VERSION>` marked latest, stamps the version into `install.sh`, writes `install.sh.sha256`, and attaches both, all behind the existing dry run gate and with job level permissions. A root `README.md` section "Install on your server" covers the command, the flags, where files live, opening the setup link right away, upgrading, backing up `.env` and the master key, reading logs with `docker compose logs`, and using the override file (per service keys only: the compose file's `x-` anchors are resolved before merging, so override the service, not the anchor). Satisfies **AC-1**, **AC-29**.

## Consequences

**Positive**:
- One command takes a fresh server to a working, HTTPS Orvano, and the same command repairs and upgrades it.
- Every decision the installer makes is typed, unit tested C# that ships with the version it installs, so the compose file and images can never drift apart.
- A public server is never open to a stranger taking the first admin account.
- CI proves the real install path, twice, on every change.

**Negative / tradeoffs**:
- Each run pulls or starts the full server image just to run the installer, which adds a little time on a slow link.
- Two pieces must stay in step: the script's flags and the container's flags. The script passes flags through untouched to limit this.
- An upgrade stops the old containers while the new ones start, so there is a short outage. Row 38 owns safe upgrades and backups before them.
- Tags on GHCR can be moved. Until images are signed, you trust the `orvanohq` GitHub org.
- The `Production` startup refusal means a hand written compose file now needs `ORVANO_SETUP_TOKEN` until an admin exists.
- The installer never checks database passwords you change by hand in `.env` against the ones baked into the data volume. A mismatch shows up as an unhealthy `migrate` or `api` (AC-17), not as a preflight refusal.
- One install per host: the fixed Compose project name `orvano` rules out a second install on the same server.
- The public address lookup contacts a third party (Cloudflare's `icanhazip.com`) unless you pass `--no-ip-lookup`.

**Neutral**:
- Amends spec 0003 AC-7 (the first account also needs the setup token when one is configured) and spec 0004 (`consoleAccount.create` gains `setupToken`, a fifth console route works without a session, a new rate limit).
- Settles spec 0002's open follow up: the production compose file stays hand written, not generated by Aspire.
- The local production shape command in `AGENTS.md` changes (the build override file and the initdb path), for `/sync` to record.

## Follow-up

- [x] Spec 0003: note on AC-7 that the first account also needs the setup token when `ORVANO_SETUP_TOKEN` is set (spec 0006 AC-20).
- [x] Spec 0004: add `setupToken?` to `consoleAccount.create`, `consoleInstall.getSetup` to the routes that work without a session, and its limit to *Rate limits*.
- [x] Spec 0002: mark the follow up about generating secrets and the compose file choice as settled by spec 0006.
- [ ] Row 38 (upgrades): back up the database and `.env` before an upgrade, and consider a short maintenance window message.
- [ ] Row 35 (backups): include `.env` (the master key) in backups, as spec 0002 asks.
- [ ] Row 11 (docs site): move the README install section into the docs site.
- [ ] Sign the images and `install.sh` with cosign (keyless, GitHub OIDC) and verify the signature in the installer.
- [ ] Consider a short `get.orvano.dev` alias that redirects to the release asset.
- [ ] Consider RHEL family support (Rocky, Alma, Fedora) once SELinux and firewalld are tested.
