# Deploy

## Overview

What a self hoster runs: the Docker Compose file, the Caddyfile and the Dockerfiles for the `orvano` and `orvano-executor` images (published to GHCR). Empty until feature 4 (Compose stack skeleton) fills it.

## Conventions

- One Linux server, Caddy in front with automatic HTTPS, Postgres 17 behind `orvano`, the executor beside it. No Valkey, no MinIO in v1.
- Pin the Postgres image to the `17` tag, the same as the AppHost and Testcontainers setups.
- The executor reaches Docker only through `docker-socket-proxy`; never mount the raw Docker socket into `orvano`.
- Logs are structured JSON on stdout; health is `/healthz` and readiness is `/readyz`.

## Gotchas

- Dockerfiles build with the repo root as context (or `pnpm deploy`), because the server shares one .NET solution and one pnpm workspace.

## Agent skills

- [docker-compose-patterns](../.claude/skills/docker-compose-patterns/): `docker/skills`, services, health checks, dependency order, volumes, dev overrides

## Related specs

- [0001 Stack and architecture](../docs/specs/0001-stack-architecture/index.md)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
