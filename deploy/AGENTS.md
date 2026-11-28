# Deploy

## Overview

What a self hoster runs: the Docker Compose file, the Caddyfile and the Dockerfiles for the `orvano` and `orvano-executor` images (published to GHCR). Feature 4 (Compose stack skeleton, spec 0002) built the first version; `README.md` here is the self hoster documentation.

## Conventions

- One Linux server, Caddy in front with automatic HTTPS, Postgres 17 behind `orvano`, the executor beside it. No Valkey, no MinIO in v1.
- Pin the Postgres image to the `17` tag, the same as the AppHost and Testcontainers setups.
- The executor reaches Docker only through `docker-socket-proxy`; never mount the raw Docker socket into `orvano`.
- Logs are structured JSON on stdout; health is `/healthz` and readiness is `/readyz`.
- `compose.yaml` names images only and never holds `build`; `compose.build.yaml` holds nothing but `build`. Required settings use `${NAME:?NAME is required}`, and no setting has a default password.
- Only `caddy` publishes ports. Three networks: `edge` (caddy, orvano), `data` and `executor` (both `internal: true`); the executor never joins `data`.
- The Orvano images are chiseled (no shell), run as `$APP_UID`, and get `read_only`, a `/tmp` tmpfs and `cap_drop: [ALL]`. Their final Dockerfile stage has no `RUN` step, which keeps the arm64 build free of emulation.
- After any change here, run `deploy/smoke-test.sh` (needs Docker, bash, curl, jq, openssl; publishes ports 80 and 443). CI runs it on every push and pull request.

## Gotchas

- Dockerfiles build with the repo root as context (or `pnpm deploy`), because the server shares one .NET solution and one pnpm workspace.

## Agent skills

- [docker-compose-patterns](../.claude/skills/docker-compose-patterns/): `docker/skills`, services, health checks, dependency order, volumes, dev overrides
- [caddy-reverse-proxy](../.claude/skills/caddy-reverse-proxy/): `dimdasci/vps-setup` (community, not the Caddy project), Caddyfile, reverse proxy, automatic HTTPS, DNS challenge, TLS troubleshooting. Spec 0002 wins where they differ; its certificate reset steps delete `caddy-data`

Declined: docker-buildx and docker-cicd skills (`full-stack-skills/docker-skills`, no `build-push-action` coverage)

## Related specs

- [0001 Stack and architecture](../docs/specs/0001-stack-architecture/index.md)
- [0002 Compose stack skeleton](../docs/specs/0002-compose-stack-skeleton/index.md)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
