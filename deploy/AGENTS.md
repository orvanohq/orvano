# Deploy

The image, the compose files, and the install script. The rules come from [spec 0007](../docs/specs/0007-stack-architecture/index.md): IMG-1 to IMG-4 (image), INS-1 to INS-5 (install), TLS-1 and TLS-2, DEV-1 (local dev); and [spec 0016](../docs/specs/0016-self-host-install/index.md): ISH (script), BND (init bundle), PIN (version pin), LCL and PRT (local mode and ports), SEL (SELinux), SMK (CI). Security rules are [spec 0006](../docs/specs/0006-security-baseline/index.md). The operator's guide is [docs/self-hosting.md](../docs/self-hosting.md).

## Files

- `Dockerfile`: multi stage. Console with Node and pnpm, server published with ReadyToRun for `linux-x64` and `linux-arm64`, final stage `aspnet:10.0-noble-chiseled` (no shell, user 1654). Build from the repo root: `docker buildx build --platform linux/amd64,linux/arm64 -f deploy/Dockerfile -t ghcr.io/orvanohq/orvano:edge .`
- `install.sh`: the one command install. POSIX `sh`, every line inside a function with `main "$@"` last, so a cut off download runs nothing (ISH-1).
- `compose.yaml`: the reference install (PostgreSQL, one shot `migrate`, Orvano, Caddy).
- `compose.dev.yaml`: PostgreSQL for local development (trust auth, bound to `127.0.0.1`), plus the OpenTelemetry dashboard under the `otel` profile.
- `Caddyfile`, `postgres-init/10-orvano-roles.sh` (creates the `orvano` and `orvano_provisioner` roles on first start), and `.env.example` (every variable, no usable secrets).

## Conventions

- Pin exact versions: `postgres:18.6` here must match `PostgresFixture.Image` in `server/tests/Orvano.Testing`. Build stage images are pinned by digest.
- The `Dockerfile` restore layer copies `BannedSymbols.txt`, `.editorconfig`, and each `server/src` project's `.csproj` and `packages.lock.json`, and restores in locked mode. Add new projects there.
- The console stage builds from the root pnpm workspace (root `package.json`, `pnpm-workspace.yaml`, `pnpm-lock.yaml`, `.node-version`), gets pnpm through corepack, and installs only `--filter @orvano/console...`. `.dockerignore` lets in `tooling/scripts/prepare.mjs` alone, because pnpm runs the root `prepare` script, which exits on `ORVANO_SKIP_HOOKS=1` (spec 0008 TL-7).
- The `node` tag in the `Dockerfile` must match `.node-version`; CI fails when they differ.
- The final stage carries `/app/LICENSE` and `/app/NOTICE` and the `org.opencontainers.image.licenses` and `.source` labels (spec 0009 LIC-6); keep them when you change that stage.
- The image has no shell or curl. Health checks run `/app/orvano healthcheck`, and the storage folder is created in the build stage.
- PostgreSQL health checks use `pg_isready -h 127.0.0.1`, because during first start initialisation the server listens only on its socket.
- `ORVANO_TRUSTED_PROXIES` must equal Caddy's fixed IP on the compose network (`172.30.80.10`). Only the `orvano` service gets the root key secret; `migrate` needs none (INS-2).
- `init` runs as the operator, and the install folder is 0700 with `root.key` at 0644 (INS-3, INS-5). Run compose as the same user.
- The `orvano` service keeps `stop_grace_period: 35s`, longer than `serve`'s 25 s shutdown timeout, so running jobs are released before Docker kills the container (spec 0013 OUT-6).
- `.github/workflows/image.yml` publishes the image on each push to `main`, tagged `edge` and `sha-<commit>` (the full commit, which each SDK's conformance pins), signed with cosign keyless and attested (spec 0007 IMG-3, spec 0006 SUP-8). Its last job sends `contract-updated` to the six SDK repos when `api/` changed, only after the image succeeds, so keep that job last (spec 0014 CNF-6, RGN-5).
- For throwaway test stacks, use `docker compose -p <test-name>` so they never reuse a real install's named volumes.
- `compose.yaml`, `Caddyfile`, and `postgres-init/` here are the one source of `/app/install/` in the image, and `init` writes them out from there (BND-1, BND-2). The image's `ORVANO_IMAGE_VERSION` is what `init` pins in `.env`, so `edge` is only ever a pull tag (PIN-1, PIN-2).
- Every `init` mount uses `:z`, never `:Z`, and `compose.yaml` carries no SELinux option: `:Z` would make a path private to one container (SEL-1, SEL-2). No CI leg applies labels, so `install-checks.sh` fails on `/out:Z`. Labelled hosts stay unproven until VFY-8 and VFY-12 run.
- `install.sh` refuses a first install into a folder that has anything in it, hidden files too, because `:z` relabels everything in `--dir`. Only `.DS_Store` and `lost+found` don't count (ISH-9). `init` has no such check; BND-3 stays its refusal.
- `tooling/scripts/install-checks.sh` needs a host with no other Orvano install, or ISH-8's refusal fires first, and a free `BUSY_PORT` (default 18080). CI's `install` job runs it, then `install-smoke.sh` and `install-permissions.sh`, on amd64 Docker, arm64 Docker, and rootless Podman (SMK-1).

## Agent skills

Spec 0007 wins where a skill disagrees.

- [containerize-aspnetcore](../.claude/skills/containerize-aspnetcore/): `github/awesome-copilot`, ASP.NET Core images (not its curl health checks or full base images: IMG-2, IMG-4)
- [docker-compose-patterns](../.claude/skills/docker-compose-patterns/): `docker/skills`, compose files
- Declined (2026-10-16): a Caddy skill, and the community Caddy MCP

_Drafted by /sync from the introducing change, worth a quick human pass._
