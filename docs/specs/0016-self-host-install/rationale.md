# 0016. Self host install: rationale

The decision record for [index.md](index.md). `/develop` builds from `index.md`; read this only when you need the why.

## Context

Feature 12 has to make Orvano something a person can install, on a server or on a laptop, with one command, and land in the console's first run setup with data that survives a restart. A lot of the ground is already laid. Spec 0007 settled one image with an `orvano` binary (`serve`, `migrate`, `init`, `healthcheck`, `admin`), a reference `deploy/compose.yaml` (PostgreSQL, a one shot `migrate`, Orvano, Caddy) with named volumes and health checks, and `orvano init`, which writes `root.key` and `.env` into an operator owned 0700 folder (INS-3, INS-5). Spec 0006 CON-6 already says the first owner is created only with a one time setup token printed at first start. Spec 0011 already sketches the `platform.instance` singleton that holds it.

What is missing is the glue and the proof. Today an install is three manual steps: copy three files and a folder out of the repo, run init through a long `docker run` with the right user flags for your engine, then `compose up`. There is no laptop path: the Caddyfile wants a real domain, and a `localhost` site gets Caddy's internal certificate authority, which every browser warns about. The image tag defaults to `edge`, so a `compose pull` and restart can silently migrate an install to a newer main build. Nothing in CI starts the stack; spec 0007 still owes a proof of INS-3 and INS-5 on a real Linux host. And the console has no first run route yet.

The forces: Orvano holds other people's data, so a fresh internet facing install must not be claimable by whoever reaches it first. Operators run Docker or Podman, rootful or rootless, on amd64 or arm64, and spec 0007 promises the same files work on all of them. The install files and the image must not drift, because feature 53 (upgrades) will build on whatever this feature leaves. Feature 13 owns console accounts and sign in, so the line between "the instance is set up" and "an operator exists" has to be drawn cleanly. And the proof has to run in CI on a clean machine, not on a maintainer's laptop.

## Options considered

### Option 1: An install script around an `init` that writes the whole bundle from the image (chosen)

`orvano init` gains the job of writing `compose.yaml`, `Caddyfile`, and `postgres-init/` (copied into the image from `deploy/` at build time) beside `.env` and `root.key`. A short POSIX `install.sh` checks the host, picks the engine and its user flags, runs init, runs `compose up --wait`, and prints the setup link. The manual path (init, then compose) stays documented.

**Pros**:

- The install files always match the image version that wrote them, which is what feature 53 needs.
- The script holds no install content of its own, so it is small enough to read before running, and everything substantive comes from the signed image.
- The engine and user flag logic of INS-3, which people get wrong by hand, lives in one tested place.
- A rerun is naturally safe: init refuses, and the script skips it.

**Cons**:

- Two moving parts (script and init) instead of one.
- The one line form pipes a script from GitHub to `sh`, which some operators reject.
- Init's refusal and cleanup rules widen to five paths.

### Option 2: Keep compose only, two documented commands

No script. The guide shows `docker run ... init` and `docker compose up -d`, with a table of user flags per engine.

**Pros**:

- Nothing new to build or test beyond the guide.
- Every step is visible to the operator.

**Cons**:

- Fails the feature's "one command" bar literally.
- The INS-3 user flag choice stays a manual step that fails confusingly when wrong.
- Operators still have to fetch the compose files from the repo, at a ref that may not match the image.

### Option 3: A script that downloads the install files from GitHub

`install.sh` fetches `compose.yaml` and friends from `raw.githubusercontent.com` at a ref, checks their checksums, then runs init and compose.

**Pros**:

- Init stays as it is.
- The files are visible as plain URLs.

**Cons**:

- The files and the image can drift (a ref and an image tag are two pins to keep in step).
- Checksums need publishing and a place to live; feature 59 does not exist yet.
- More network fetches in the install, each a failure point.

### Option 4: A self initialising compose file

A one shot `init` service inside compose generates the passwords and root key into a named volume on first `up`, so `docker compose up -d` alone installs.

**Pros**:

- Truly one compose command, no script.

**Cons**:

- The root key lands in a volume, not in the operator's folder, which breaks INS-5 and makes the key easy to lose and hard to back up apart from the database.
- The domain still has to come from somewhere (an env var on the command line), so it is not really one command either.
- The compose file still has to be fetched first.

## Rationale

Option 1 is the only one that meets "one command" without giving up what spec 0007 already got right. INS-5 deliberately keeps `root.key` in an operator owned folder so it can be backed up apart from the database; option 4 throws that away. Options 2 and 3 leave the install files as something fetched separately from the image, which is the drift that will hurt most when feature 53 arrives: an upgrade needs to know exactly which compose file goes with which image, and the simplest answer is that the image carries its own. With the files in the image, the script has almost nothing to do except the engine detection that people get wrong by hand, which is exactly the part worth automating.

The setup gate goes to feature 12 and the owner form to feature 13 because that split matches the data: feature 12 needs the instance record and the token to make "the console opens to first run setup" real and checkable, while the operator, password hashing, sessions, and sign in are all feature 13's. A token step that is checked by the server but not used up keeps feature 12's acceptance testable on its own and lets feature 13 add one form behind it without reworking the gate.

Local mode serves plain HTTP on `127.0.0.1` rather than Caddy's internal HTTPS because a browser warning on the very first screen is the worst first impression, and CON-1 already allows insecure cookies on an HTTP public URL. Binding to loopback only keeps that relaxation off the network. Pinning `ORVANO_VERSION` to the image's own version trades convenience (no silent `edge` upgrades) for safety: a restart must never migrate data, and upgrades get their own design in feature 53. No telemetry, because there is nothing to collect it yet and a self hosted product earns trust by not calling home.

### Sub decisions

| Decision           | Pick                                                                                                      | Runner up                  | Why                                                                            |
| ------------------ | --------------------------------------------------------------------------------------------------------- | -------------------------- | ------------------------------------------------------------------------------ |
| Platforms          | Linux amd64 and arm64 with Docker or Podman; macOS; Windows through WSL2, documented                      | Linux only                 | The feature says server or laptop; WSL2 covers Windows without a second script |
| First run boundary | 12: instance, token, config, token step; 13: owner form                                                   | 12 builds the full setup   | Matches the data each feature owns; keeps 12 testable alone                    |
| Token lifecycle    | Issued at start when none is valid, replaced on demand, 24 h expiry, hashed (narrowed by the cross check) | Issued once, never expires | A lost log or a leaked old token is never a standing risk                      |
| Laptop mode        | `--local`: plain HTTP on `127.0.0.1`, insecure cookies                                                    | Caddy internal HTTPS       | No certificate warning; CON-1 already allows it on HTTP                        |
| Version            | Pin to init's own image version                                                                           | Follow `edge`              | A restart must never migrate data                                              |
| Telemetry          | None                                                                                                      | Opt in ping                | Nothing to collect yet; trust                                                  |
| CI proof           | Linux amd64 and arm64 Docker, plus rootless Podman, on every relevant PR                                  | Nightly full matrix        | A broken install should block the PR that broke it                             |
| Script delivery    | One line pipe from `main`, with a read first path                                                         | Download then run only     | Meets one command; the image does the real work                                |
| Folder and rerun   | `./orvano`; a rerun starts the existing install                                                           | Refuse a rerun             | A safe rerun is the easiest restart instruction                                |
| Ports              | 80 and 443; local mode port settable (server mode fixed after the cross check)                            | Fixed                      | Laptops often have port 80 taken                                               |
| Guide              | `docs/self-hosting.md`, README points to it                                                               | README only                | The guide is long (TLS, SELinux, Podman, sizing); feature 32 moves it          |
| Token in the link  | URL fragment, read once and removed, plus typing                                                          | Typed only                 | A clickable link without the token reaching any server log                     |
| Missing engine     | Explain and stop, never install                                                                           | Offer to install Docker    | Installing system packages needs root and is not ours to do                    |
| Engine preference  | Docker first, `--engine` to override                                                                      | Podman first               | Matches the README's order                                                     |
| Data model         | `platform.instance` moves to feature 12, plus `setup_token_expires_at`                                    | Keep it in 13              | The gate needs it                                                              |

### Decisions made while writing (not asked)

- **Two Caddy services under compose profiles** for server and local mode (LCL-1), chosen over an init written `compose.override.yaml` with `!override` (newer Compose syntax, unclear support across `podman compose` providers) and over one service with a variable bind address (Docker's short port syntax cannot express "all interfaces" and "loopback only" through one variable without losing IPv6). Step 1 confirms the profile and shared IP behaviour.
- **`install.sh` gets the link from `orvano admin setup-token`**, not by parsing logs. Logs vary by engine and log driver; a command's output does not. The cost is that the token `serve` printed at start is replaced at once during an install, which the guide explains.
- **SHA-256 for the token hash**, not Argon2id: the token is 256 random bits, so a slow hash adds nothing, and the check endpoint stays cheap.
- **A small built in rate limiter on the check endpoint** (STP-8) until feature 23 builds the shared engine, because every public endpoint should be limited and this one costs a database read.
- **The check endpoint does not use the token up**, so a page reload or a second tab does not lock the operator out; feature 13's owner form uses it up in its own transaction.
- **Exit code 3** for "setup already complete" from `admin setup-token`, so `install.sh` can tell it from a failure without parsing text.
- **`ORVANO_IMAGE`** as an optional repository override, so CI can run the exact image it built and mirrors work, without touching `compose.yaml`.
- **No References section**, matching the other specs in this repo.

### Cross check (2026-10-29)

A read only cross check on another model found two blockers and eight majors; you chose to apply every recommended fix:

- `--image` now reaches `.env` through init (`ORVANO_IMAGE`), and the script pulls only an image it does not have (ISH-5, BND-4).
- One install per host is now explicit, and a second folder is refused before it can reuse the first install's volumes (ISH-8).
- `curl | sh` safety: the body runs from `main "$@"` on the last line, and engine calls get `-T` and `</dev/null` (ISH-1, ISH-6).
- `--dir` is made absolute and created by the user before the bind mount; init's cleanup covers every bundle path (ISH-4, BND-2).
- The port check uses `nc -z` when present, else maps compose's bind error (ISH-2).
- Podman is accepted only with the Docker Compose provider; the guide covers lingering and low ports for rootless (ISH-4, PRT-1).
- `compose.yaml` declares the `orvano` health check, so `--wait` does not depend on the image format (LCL-2).
- A restart no longer replaces a valid setup token, so a reboot never breaks a setup in progress; only `admin setup-token` replaces it (STP-3). This changed the earlier "new token per start" pick in the narrow way the cross check showed it was needed: a fresh token still appears at first start, as CON-6 asks.
- Server mode is fixed to ports 80 and 443, and `--domain` refuses names that cannot get a public certificate (BND-4, PRT-1).
- The setup check's CSRF exception is recorded against CON-2, and the page has a wrong origin state (STP-7, FRS-6).

Minor fixes: rootless Docker and a `docker` alias to Podman, rerun flag conflicts, `.env` read without sourcing, test fixtures that set `setup_completed_at` with SQL, token trimming, the `Retry-After` format, React StrictMode, AC-14 scoped to Orvano's own calls, the unverified signature stated plainly, and follow ups for specs 0003, 0012, and 0015.

### Found while building

- **The init script is 0755, not 0644 (BND-2, milestone 1, #138).** With 0644, Docker Desktop's file sharing shows the bind mounted `postgres-init/10-orvano-roles.sh` to the postgres entrypoint as executable, so the entrypoint runs it directly instead of sourcing it; that fails with "bad interpreter", the `orvano` and `orvano_provisioner` roles are never made, and `migrate` fails. 0755 makes the entrypoint run it the same way on every host, and the script only reads environment variables, so the executable bit grants nothing new.
- **Rootless Docker runs init as `--user 0:0`, not the operator's uid (ISH-4, AC-12, milestone 4).** Rootless Docker maps root in the container to the operator on the host, and every other uid to a subordinate id. With `--user "$(id -u):$(id -g)"` the operator's folder shows as owned by root and the write fails with "Permission denied". With `--user 0:0` the files land owned by the operator, which is what INS-3 asks for. Tested with the `docker:28.1.1-dind-rootless` image: a full `install.sh --local` reached setup, and uid 1654 read `root.key`.
- **Rootless Podman needs `--user` as well as `--userns=keep-id` (ISH-4, AC-12, milestone 4).** `keep-id` maps the operator's uid to itself, but an image with a `USER` line still runs as that user, here 1654, so init could not chmod the folder. Adding `--user "$(id -u):$(id -g)"` fixes it. Tested on Podman 4.9.4 rootless.
- **`docker save | podman load` keeps Docker's name (SMK-2, milestone 4).** The image lands as `docker.io/library/orvano:ci`, not `localhost/orvano:ci`. Podman's socket answers `200` for `orvano:ci` and `404` for `localhost/orvano:ci`, so `--image orvano` is the right flag.
- **Dynamic addresses stay out of Caddy's way (ISH-8, milestone 4).** Podman hands out addresses in sequence and keeps counting across `compose down`, and with Docker Compose as its provider it recreates containers that have named volumes on every `up`. So reruns walked the `orvano` container up the subnet until it took `172.30.80.10`, and Caddy then failed with "requested ip address 172.30.80.10 is already allocated". The rootless Podman CI leg caught it on the first rerun; an operator would have hit it the same way. `ip_range: 172.30.80.128/25` keeps every address with no fixed value in the upper half, on both engines, and leaves `.10` and `ORVANO_TRUSTED_PROXIES` alone. After the fix, the smoke plus 12 more reruns on Podman 4.9.4 all exited 0 with Caddy on `.10`. Recreating is otherwise harmless: the data lives in the named volumes, so `created_at`, the keys, and a valid token all survive.
- **`podman compose exec` passes the exit code through (ISH-6, milestone 4).** `admin setup-token`'s exit 3 reaches the script, but Podman also prints its own "Error: executing ...: exit status 3" to standard error. The script holds that output back unless the exit is a real failure.

### Agent skills

`docker-compose-patterns` shapes the profiles and health waits; `containerize-aspnetcore` the Dockerfile change (spec 0007 IMG-2 and IMG-4 win over its curl health checks); `authoring-github-workflows` and `github-actions-hardening` the CI job; `aspnet-minimal-api-openapi` the two endpoints (spec 0014 wins). No new tool was chosen, so no skill or MCP search ran. `shellcheck` is preinstalled on GitHub's Ubuntu runners and adds no dependency.
