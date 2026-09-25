# syntax=docker/dockerfile:1
# Caddy plus the console build. Build from the repo root: docker build -f deploy/gateway/Dockerfile .

FROM --platform=$BUILDPLATFORM node:24.21.0-bookworm-slim AS console
ENV COREPACK_ENABLE_DOWNLOAD_PROMPT=0
RUN corepack enable pnpm
WORKDIR /src
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml ./
COPY console/package.json console/
RUN pnpm install --frozen-lockfile --filter @orvano/console...
COPY console/ console/
RUN pnpm --filter @orvano/console build

FROM caddy:2.11.4-alpine
COPY deploy/gateway/Caddyfile /etc/caddy/Caddyfile
COPY --from=console /src/console/dist /srv/console
