# Deploy

The image and the compose files. Every rule here comes from [spec 0007](../docs/specs/0007-stack-architecture/index.md): IMG-1 to IMG-4 (image), INS-1 to INS-5 (install), TLS-1 and TLS-2, DEV-1 (local dev). Security rules are [spec 0006](../docs/specs/0006-security-baseline/index.md).

## Files

- `Dockerfile`: multi stage. Console with Node and pnpm, server published with ReadyToRun for `linux-x64` and `linux-arm64`, final stage `aspnet:10.0-noble-chiseled` (no shell, user 1654). Build from the repo root: `docker buildx build --platform linux/amd64,linux/arm64 -f deploy/Dockerfile -t ghcr.io/orvanohq/orvano:edge .`
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
- For throwaway test stacks, use `docker compose -p <test-name>` so they never reuse a real install's named volumes.

## Agent skills

Spec 0007 wins where a skill disagrees.

- [containerize-aspnetcore](../.claude/skills/containerize-aspnetcore/): `github/awesome-copilot`, ASP.NET Core images (not its curl health checks or full base images: IMG-2, IMG-4)
- [docker-compose-patterns](../.claude/skills/docker-compose-patterns/): `docker/skills`, compose files
- Declined (2026-10-16): a Caddy skill, and the community Caddy MCP

_Drafted by /sync from the introducing change, worth a quick human pass._
