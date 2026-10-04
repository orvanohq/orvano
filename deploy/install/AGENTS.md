# Self host installer

## Overview

`install.sh` installs, repairs, or upgrades Orvano on one Linux server (spec 0006). It is split in two on purpose:

- `install.sh` (this folder, POSIX `sh`, runs on the host as root) owns only what needs the host: flags, the lock, root, architecture, distro, memory, firewall, clock, Docker and Compose checks, the data volume, ports, pulling images, running Compose, the health poll, the failure report, and the summary.
- `orvano install` (`server/src/Orvano.Server/Install/`, inside the server image of the version being installed) makes every other decision and writes every file in the install directory (`.env`, `.env.previous`, `docker-compose.yml`, `initdb/`, `.install-result`). It never talks to Docker.

## Key files

| File | Owns |
|---|---|
| `install.sh` | The host side; `main` at the bottom shows the whole run in order |
| `../compose/docker-compose.yml`, `../compose/initdb/` | Embedded in the server image and written by `orvano install`, so the compose file always matches the image that wrote it |
| `../compose/docker-compose.build.yml` | Repo only overlay that builds the images from this checkout; the installer never ships it |
| `../../server/src/Orvano.Server/Install/` | `orvano install` and its unit tested rules |
| `../../.github/workflows/ci.yml` (`Install with install.sh`) | ShellCheck, then two installs with `--no-pull` on amd64 and arm64 |
| `../../.github/workflows/release.yml` | Stamps the version into `STAMPED_VERSION` and attaches `install.sh` plus its `.sha256` to the GitHub Release |

## Commands

```bash
# Lint, as CI does (ShellCheck in a container if it is not installed)
docker run --rm -v "$PWD/deploy/install:/mnt:ro" koalaman/shellcheck:stable --shell=sh /mnt/install.sh

# Try it without touching this machine: a throwaway docker:28-dind container (Alpine warns; --yes continues).
# Alpine has no curl (the installer refuses without it) and no ss (the port check is skipped), so add both.
docker run -d --privileged --name orv-try -v "$PWD/deploy/install:/src:ro" docker:28-dind
docker exec orv-try apk add curl iproute2
docker exec orv-try sh /src/install.sh --version <V> --domain localhost --yes
```

## Conventions

- Exit codes are the contract: `0` success, `1` unexpected error, `2` refused (bad input, failed preflight, downgrade, lost `.env`, lock held), `3` started but unhealthy or unreachable. `orvano install` uses `0`, `1`, `2`, and `install.sh` passes a `2` through.
- Use the helpers, not bare `echo` and `exit`: `refuse` (exit 2), `fail` (exit 1), `warn_and_ask` (warning, then a default no question), `confirm` (`--yes` answers yes; with no terminal it takes the default and says so).
- Every message is one plain sentence that names the thing and what to do next. Docker's raw errors go to `install.log` through `log`, not to the screen.
- `log` writes to `<dir>/install.log` only once the directory exists. Never pass it a secret, a token, or `.env` content.
- Questions read `/dev/tty`, because under `curl ... | sh` standard input is the script. `orvano install` gets `-it </dev/tty` only when there is a terminal.
- Flags reach `orvano install` exactly as the user typed them (`"$@"`), after `--existing-data` and `--version`. The container parser ignores script only flags and lets a later repeat win, so a new flag must be safe to repeat.
- `orvano install` hands its result back in `<dir>/.install-result`, never on stdout. `install.sh` deletes a stale one before the run and after reading it, and shows the master key block when it cannot read one.
- A folder is a server install or a local one (`orvano install --local`, spec 0011, marked `ORVANO_LOCAL=true` in `.env`), never converted either way: `install.sh` (`check_not_local`) and `orvano install` both refuse the other kind with exit 2. A local install skips `install.sh` entirely; the reader runs the image with `docker run ... install --local` and then `docker compose up -d --wait`.
- The installer image is pulled before `docker run`, which then uses `--pull never`. With `--no-pull`, every image must already be local, or the run exits 2 naming it.

## Gotchas

- Keep it POSIX `sh` (Debian's `dash`), no bashisms: CI runs ShellCheck with `--shell=sh`.
- Never run it on a machine with a real Orvano install. Use a throwaway VM or the dind container above.
- The install directory is root only (`umask 077`); `docker-compose.yml` is 0644 and `initdb/10-orvano-roles.sh` is 0755 because containers read them.

## Related specs

- [0006 Self host installer](../../docs/specs/0006-self-host-installer/index.md) (the design, exit codes, and `verify.md` with the host steps)
- [0011 Docs site and quickstarts](../../docs/specs/0011-docs-site-quickstarts/index.md) (`--local` and `--port`, `LocalRule.cs`)

_Drafted by /sync from the introducing change, worth a quick human pass._
