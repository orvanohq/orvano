# 0006. Self host installer: rationale

The decision record behind [index.md](index.md). `/develop` builds from the index; this file explains why.

## Context

Orvano is self hosted first (scope index). Spec 0002 fixed the production shape as Docker Compose on one Linux host, with images on GHCR, and left the step that turns an empty server into a running install to this row. Today that step is manual: copy `.env.example`, generate three passwords and a master key with `openssl`, build the images from source, and run Compose. Nobody outside the team gets past that, and nobody inside it should run production that way.

What the installer must handle. Secrets that must never change once written (the database passwords are baked into the data volume on first start, and the master key protects every stored secret, spec 0002). A compose file that must match the images it names, version by version, since migrations only go forward. HTTPS through Caddy, which only works once DNS points at the server. Servers with no Docker at all, and people running it through cloud init or Ansible with no terminal. And "designed so later versions upgrade in place" (the scope row), which means the second run is as important as the first.

A security gap sits next to it. Spec 0003 AC-7 makes the first console account the install admin. On a server on the public internet, the time between "containers are up" and "the owner signs up" is a window in which anyone who finds the URL owns the install. Spec 0004 makes `ORVANO_MASTER_KEYS` required, so after v0.1 an install without the installer's help does not even start.

Constraints: the server image is chiseled (no shell, no package manager, no Docker client), the minimum host is 2 vCPU and 4 GB on `amd64` or `arm64` (spec 0002), `release.yml` publishes SDKs but no images yet, and the project's rules call for typed, tested code and config checked at startup.

## Options considered

### Option 1: A small host script plus an `install` command in the server image

`install.sh` does only what needs the host (root, Docker, ports, the lock, running Compose). It runs `docker run ghcr.io/orvanohq/orvano:<version> install`, which asks the questions, generates secrets, and writes the compose file embedded in that very image.

**Pros**:
- All decisions are C# with unit tests, following the project's own rules.
- The compose file always matches the image version, because the image carries it.
- Upgrading means running a newer image. No separate upgrade tool.
- The host script stays short enough to read before piping it into `sh`.

**Cons**:
- Two pieces with flags to keep in step.
- Docker must exist before the real installer can run, so the script needs its own Docker check and install offer.
- Pulling the server image just to install adds time on a slow link.

### Option 2: One shell script does everything

A single bash script: prompts, secret generation with `openssl`, writes `.env` and the compose file (downloaded or inline), runs Compose.

**Pros**:
- One file, nothing to pull before it starts working.
- Familiar to every Linux admin.

**Cons**:
- Rules like "never change a secret, keep unknown keys and comments, refuse a downgrade" are hard to write correctly and hard to test in shell.
- The compose file comes from a second place, which can drift from the images.
- Grows into the fragile, untested core of every install.

### Option 3: Container only, no script (Appwrite's model)

The documented command is `docker run -it --rm -v /var/run/docker.sock:... ghcr.io/orvanohq/orvano install`, and the container drives Docker through the socket.

**Pros**:
- Nothing to curl, one artifact.
- Version pinned by the image tag you run.

**Cons**:
- Docker must already be installed, with no help.
- The container needs the Docker socket (root on the host) and a Docker client, which the chiseled image does not have.
- A long command that people copy wrongly.

### Option 4: A standalone host binary

A single file .NET `orvano` CLI per OS and architecture, downloaded to the host, that installs and later manages the server.

**Pros**:
- The richest experience, and room for status, logs, and backup commands later.
- Can drive Compose directly on the host.

**Cons**:
- A second shipped artifact per architecture to build, sign, and version.
- Still needs a download step (a script or manual), so it does not remove Option 1's script.
- More surface than one install command needs today.

## Rationale

Option 1 puts each job where it is easiest to get right. The host checks and their prompts stay in shell, proven by ShellCheck and the CI install on both architectures. The hard rules of this feature (secrets never change, the downgrade refusal, keeping your `.env` edits, tuning from memory) are data rules, and the project already has a typed, tested home for those: the server. Embedding the compose file in the image removes the most dangerous drift in a self hosted product, a compose file from one version naming images from another. Upgrades then need no separate design: a newer installer image writes a newer compose file, and the `migrate` role already runs first. Option 2 is simpler on day 1 and worse by day 180. Option 3 cannot work with the chiseled image and leaves the fresh server case unsolved. Option 4 is Option 1 plus a second artifact to ship, which can wait for row 38 if host management commands earn it.

The container never gets the Docker socket. That keeps the thing you pipe from the internet small (it only checks the host and runs Compose), and keeps the thing with the logic unprivileged towards Docker.

**Sub decisions made in the design conversation** (the engineer chose each):
- **First admin**: a one time setup token in the URL fragment, required by the API while no install admin exists, over having the installer create the admin (a password typed in a terminal, and the installer reaching into Auth) or leaving AC-7 open (the takeover window). `Production` refuses to start without it, so a hand made install cannot stay open by accident, while `Development` and `Test` keep today's behaviour. The token stays valid until the first admin exists, since it is already secret, single use, and only on the server.
- **Running it again**: keep secrets and reconcile (repair, upgrade, refuse a downgrade), over a menu or a refusal. Your changes go in `docker-compose.override.yml`, which the installer never touches, over checksum detection that would block upgrades.
- **Compose source**: hand written and embedded, over Aspire's Compose publisher (spec 0002's open question). The file carries tuned healthchecks, `depends_on` conditions, and the initdb mount that a generator would fight.
- **Scope**: domain with automatic HTTPS only, plus `localhost` for trying it and for CI. No "behind your own proxy" mode yet. Install and run again only, no uninstall or host wrapper command.
- **Everything else asked**: offer to install Docker via `get.docker.com`; `/opt/orvano`; the GitHub release asset URL; flags plus `--yes`; warn and ask on a DNS mismatch; images published on tag behind the SDK dry run gate; Postgres tuned to memory; Ubuntu LTS and Debian supported; an optional ACME email; on failure, leave everything running and explain; the master key shown with a typed `saved` confirmation.

**Calls made while writing the spec** (pick, why, runner up):
- **Where the installer code lives**: an `Install/` folder in `Orvano.Server`, because it is a host command like `healthcheck` and owns no tables. Runner up: its own csproj, worth it only if it grows host management commands.
- **Health wait**: the script polls `docker compose ps --format json`, because `up --wait` treats the one shot `migrate` container exiting as a failure on some Compose versions. Runner up: `up --wait` once the minimum Compose version handles it.
- **Minimum Docker**: Engine 24 and Compose 2.24, both from early 2024 and what `get.docker.com` gives anyway. Runner up: no minimum, which fails later and less clearly.
- **Lock**: `flock` on a lock file, which releases itself when the process dies. Runner up: a `mkdir` lock, which leaves stale locks after a crash.
- **`.env` writes**: temp file plus rename, and one `.env.previous`, so a crash never leaves half a file and the last change can be undone by hand. Runner up: in place writes.
- **Server address for the DNS check**: IPv4 and IPv6 interface addresses first, then `ipv4.icanhazip.com` and `ipv6.icanhazip.com` (run by Cloudflare), skippable with `--no-ip-lookup`, because cloud servers behind 1:1 NAT do not see their public address on an interface, and IPv6 only hosts need their own lookup. Runner up: interface addresses only, which falsely warns on AWS and GCP.
- **Which port holder is ours**: Compose labels on the container that publishes the port, because `ss` only ever shows `docker-proxy`. Runner up: parsing `docker-proxy` arguments, which breaks when Docker changes its proxy.
- **One install per host**: refuse a second `orvano` project in another directory, because the fixed project name makes volumes collide. Runner up: a project name derived from `--dir`, which would break every documented `docker compose` command and the volume check.
- **Rootless Docker**: refuse, because files and ports would not belong to host root and the security model assumes they do. Runner up: support it, which needs a different port and ownership story.
- **CI coverage**: both `amd64` and `arm64` GitHub runners, because the spec promises both and only an install on each proves it.
- **Pre seeded `.env`**: a `.env` without `ORVANO_VERSION` is a fresh install whose keys are kept, so you can set your own tuning or passwords before the first run.
- **ACME email**: the gateway entrypoint writes an imported `global.caddy`, because Caddy rejects an empty `email` value and the Caddyfile has no conditionals. Runner up: make the email required.
- **HSTS**: one year, no `includeSubDomains` (custom domains, row 30, may share a parent), no `preload`. Runner up: six months.
- **Log rotation**: `json-file`, 10 MB times 3 per service, because Docker's default never rotates and a busy install fills the disk. Runner up: the `local` driver, less familiar to people reading logs by hand.
- **Formats**: master key ID `k<yyyymmdd>` so rotations sort and read clearly; setup token `ost_` plus 32 bytes base64url so it is recognisable in a leak scan. Error code `setup_token_invalid` (403), distinct from `signup_closed` so the console can say the right thing.
- **Release object**: `gh release create --verify-tag --latest` in `release.yml`, because `releases/latest/download/...` needs a published release and today's workflow only pushes a tag. Job level permissions keep the rest of the workflow read only.
- **Image build**: one runner with Buildx and QEMU, because both Dockerfiles compile on the build platform and only the final copy runs emulated. Runner up: native `arm64` runners with a manifest merge, if QEMU turns out slow.
- **Docs**: a README section now, moved to the docs site in row 11.
- **Script lint**: ShellCheck with `--shell=sh` in CI (already on GitHub's Ubuntu runners), since the script runs under `dash` on Debian and Ubuntu.
