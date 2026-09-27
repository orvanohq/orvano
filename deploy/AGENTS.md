# Deploy

## Overview

Everything that ships Orvano to a server: the two images (the server image every role runs from, and the gateway, which is Caddy plus the console build), the Compose file for one Linux host, the Postgres bootstrap script, and the installer. The Compose file and the bootstrap script are also built into the server image, because `orvano install` writes them to every install (spec 0006).

## Key files

| File | Owns |
|---|---|
| `server.Dockerfile` | The one server image (`ghcr.io/orvanohq/orvano`): `orvano api|worker|realtime|migrate`, plus `install`, `setup-status`, and `healthcheck` |
| `gateway/Dockerfile` | The gateway image (`ghcr.io/orvanohq/orvano-gateway`): builds the console with pnpm, then serves it from Caddy |
| `gateway/Caddyfile` | The only public entry point: routing to `api` and `realtime`, the console's security headers, asset caching, and HSTS |
| `gateway/entrypoint.sh` | Writes `/etc/caddy/global.caddy` (the ACME email) at start, then runs Caddy |
| `compose/docker-compose.yml` | The production shape on one host; installer managed |
| `compose/docker-compose.build.yml` | Repo only overlay that builds both images from the checkout |
| `compose/.env.example` | Every setting the Compose file reads, with how to generate each secret |
| `compose/initdb/10-orvano-roles.sh` | One time Postgres bootstrap: the `orvano_admin` and `orvano_app` roles and the `orvano` database |
| `install/` | The self host installer; see [install/AGENTS.md](install/AGENTS.md) |

## Commands

```bash
# Build the images from the repo root (CI tags them with the VERSION file)
docker build -f deploy/server.Dockerfile -t ghcr.io/orvanohq/orvano:dev .
docker build -f deploy/gateway/Dockerfile -t ghcr.io/orvanohq/orvano-gateway:dev .

# Check the Caddyfile without starting anything
docker run --rm -v "$PWD/deploy/gateway/Caddyfile:/etc/caddy/Caddyfile:ro" -e ORVANO_PUBLIC_URL=https://orvano.example.com caddy:2.11.4-alpine sh -c ': >/etc/caddy/global.caddy && caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile'
```

The production shape command (`docker-compose.yml` plus `docker-compose.build.yml`) is in the root AGENTS.md.

## Conventions

- Both Dockerfiles build from the repo root and pin their base images to exact versions. The build stages run on the build platform and cross compile (`$BUILDPLATFORM`, `$TARGETARCH`), so arm64 builds stay fast.
- The server image is .NET's chiseled image: no shell, non root (UID 1654). Health checks call `/app/orvano healthcheck`, never `curl` or `sh`, and you cannot `docker exec` a shell into it.
- Only the gateway publishes ports (80, 443, 443/udp). Postgres and every role stay on Compose's internal network.
- A new `ORVANO_*` setting goes in three places: the service's `environment` in `docker-compose.yml`, `compose/.env.example` with a comment, and `orvano install` when the installer must generate or keep it. A required value uses `${X:?set in .env}` so Compose refuses to start without it.
- Shared settings live in `x-` anchors (`x-server`, `x-app-db`, `x-logging`, `x-healthcheck`). Users override services in `docker-compose.override.yml`, never the anchors, because anchors are resolved before files merge.
- Every service uses the `x-logging` anchor (json file, 10 MB, 3 files) and sets a memory limit.
- Only `migrate` and `worker` get `ORVANO_DB_ADMIN_URL`; the other roles connect as `orvano_app`.
- In the Caddyfile, `/internal/*` is always a 404 (internal calls go straight to `api:8080`), and `/v1/realtime` is matched before `/v1/*`. HSTS is sent only over HTTPS, without `includeSubDomains`.

## Gotchas

- Editing `compose/docker-compose.yml` or `compose/initdb/` changes what the next release installs on every server: the installer rewrites the Compose file on each run.
- `compose/initdb/10-orvano-roles.sh` is also used by the Aspire AppHost, the Testcontainers `PostgresFixture`, the EF drift check in CI, `tests/scenarios/compose.yml`, and `sdks-nightly.yml`. A change here reaches all of them. Never create project roles in it; only `orvano_admin` creates those.
- A new server project needs its csproj copied before `dotnet restore` in `server.Dockerfile`. A new package the console build needs needs its `package.json` copied in `gateway/Dockerfile`, which copies only `console`, `sdks/js`, and `sdks/console-client`.
- `tests/scenarios/compose.yml`, `ci.yml`, `sdks.yml`, and `release.yml` build these same Dockerfiles, so a broken Dockerfile fails all of them.
- Caddy refuses an empty `email`, which is why `entrypoint.sh` writes the line only when `ORVANO_ACME_EMAIL` is set. Keep the escaping of `\` and `"` if you touch it.
- `.dockerignore` keeps every `.env` out of the build context, so your local `compose/.env` never ends up in an image. ESLint and Prettier skip `deploy/`.
- The Postgres 18 image keeps its data under `/var/lib/postgresql/18/docker`, so the volume mounts `/var/lib/postgresql`, not `.../data`. The installer checks for the volume `orvano_orvano-pg` by that exact name.

## Related specs

- [0002 Stack and architecture](../docs/specs/0002-stack-architecture/index.md) (containers on one server, request routing, the Postgres roles)
- [0005 Console design system and shell](../docs/specs/0005-console-design-system-shell/index.md) (the console's security headers)
- [0006 Self host installer](../docs/specs/0006-self-host-installer/index.md) (the installer, the image only Compose file, the gateway's ACME email and HSTS)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
