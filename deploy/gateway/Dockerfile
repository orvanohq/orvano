# syntax=docker/dockerfile:1
# Caddy plus the console build. Build from the repo root: docker build -f deploy/gateway/Dockerfile .

FROM --platform=$BUILDPLATFORM node:24.21.0-bookworm-slim AS console
ENV COREPACK_ENABLE_DOWNLOAD_PROMPT=0
RUN corepack enable pnpm
WORKDIR /src
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml ./
# The console builds against the private console client, which builds on the JS runtime.
COPY console/package.json console/
COPY sdks/js/package.json sdks/js/
COPY sdks/console-client/package.json sdks/console-client/
RUN pnpm install --frozen-lockfile --filter @orvano/console...
COPY sdks/js/ sdks/js/
COPY sdks/console-client/ sdks/console-client/
COPY console/ console/
RUN pnpm --filter @orvano/console... build

FROM caddy:2.11.4-alpine
COPY deploy/gateway/Caddyfile /etc/caddy/Caddyfile
COPY --chmod=755 deploy/gateway/entrypoint.sh /usr/local/bin/orvano-gateway-entrypoint
COPY --from=console /src/console/dist /srv/console
ENTRYPOINT ["/usr/local/bin/orvano-gateway-entrypoint"]
CMD ["caddy", "run", "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile"]
