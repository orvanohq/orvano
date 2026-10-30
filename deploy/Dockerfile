# syntax=docker/dockerfile:1
# Spec 0007 IMG-1: one multi stage image for linux/amd64 and linux/arm64. Build from the repository root:
#   docker buildx build --platform linux/amd64,linux/arm64 -f deploy/Dockerfile -t ghcr.io/orvanohq/orvano:edge .
# Build stages run on the build machine and cross compile, so no stage needs emulation.

FROM --platform=$BUILDPLATFORM node:24.22.0-bookworm-slim@sha256:a5f4366e834695d5f363b920c104953a535d3a9864d1bdf74566a0ebe01daee5 AS console
# Spec 0008 TL-7: the console builds from the root pnpm workspace. pnpm comes from corepack, which reads the one pin
# in the root package.json (packageManager). The node tag matches .node-version.
WORKDIR /src
RUN corepack enable
# No git hooks in an image build (HOK-1).
ENV ORVANO_SKIP_HOOKS=1
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml .node-version ./
COPY tooling/scripts/prepare.mjs tooling/scripts/
COPY console/package.json console/
# The console and its dependencies only, so the root lint tools never enter the image build.
RUN pnpm install --frozen-lockfile --filter @orvano/console...
COPY console/ console/
RUN pnpm --filter @orvano/console build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS server
ARG TARGETARCH
WORKDIR /src/server
COPY server/global.json server/Directory.Build.props server/Directory.Packages.props server/BannedSymbols.txt server/.editorconfig ./
COPY server/src/Orvano.Host/Orvano.Host.csproj server/src/Orvano.Host/packages.lock.json src/Orvano.Host/
COPY server/src/Orvano.SharedKernel/Orvano.SharedKernel.csproj server/src/Orvano.SharedKernel/packages.lock.json src/Orvano.SharedKernel/
COPY server/src/Orvano.Infrastructure/Orvano.Infrastructure.csproj server/src/Orvano.Infrastructure/packages.lock.json src/Orvano.Infrastructure/
COPY server/src/Orvano.Modules.Auth/Orvano.Modules.Auth.csproj server/src/Orvano.Modules.Auth/packages.lock.json src/Orvano.Modules.Auth/
COPY server/src/Orvano.Modules.Platform/Orvano.Modules.Platform.csproj server/src/Orvano.Modules.Platform/packages.lock.json src/Orvano.Modules.Platform/
# Spec 0002 GEN-4: locked mode, so the image uses exactly the tested dependency graph.
RUN dotnet restore src/Orvano.Host/Orvano.Host.csproj --locked-mode -p:PublishReadyToRun=true
COPY server/src/ src/
RUN RID="linux-$([ "$TARGETARCH" = amd64 ] && echo x64 || echo "$TARGETARCH")" \
 && dotnet publish src/Orvano.Host/Orvano.Host.csproj -c Release -r "$RID" --self-contained false --no-restore \
      -p:PublishReadyToRun=true -o /out
COPY --from=console /src/console/dist /out/wwwroot/console
# The final image has no shell, so the storage folder is made here (spec 0007 IMG-2).
RUN mkdir -p /rootfs/var/lib/orvano/storage

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled@sha256:c89a6a61f494f20a5ae302990e814627a8d91b332fd4a8e7c1707c2d4da9555d
# Spec 0016 PIN-1: image.yml passes sha-<commit>; a local or CI build stays dev (or what it passes). `orvano init` pins
# .env's ORVANO_VERSION to this value (PIN-2).
ARG ORVANO_IMAGE_VERSION=dev
# Spec 0009 LIC-6: the license travels with the image.
LABEL org.opencontainers.image.licenses="Apache-2.0" \
      org.opencontainers.image.source="https://github.com/orvanohq/orvano" \
      org.opencontainers.image.version="${ORVANO_IMAGE_VERSION}"
ENV ORVANO_IMAGE_VERSION=${ORVANO_IMAGE_VERSION}
WORKDIR /app
COPY --from=server /out ./
COPY LICENSE NOTICE /app/
# Spec 0016 BND-1: the install bundle `orvano init` writes, straight from deploy/, so it always matches this image.
COPY deploy/compose.yaml deploy/Caddyfile /app/install/
COPY deploy/postgres-init/10-orvano-roles.sh /app/install/postgres-init/
# 1654 is the chiseled image's non root `app` user, so a fresh named volume inherits that owner.
COPY --from=server --chown=1654:1654 /rootfs/var/lib/orvano /var/lib/orvano
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080 8443
VOLUME ["/var/lib/orvano/storage"]
# Spec 0007 IMG-4: no curl; the server binary probes itself.
HEALTHCHECK --interval=10s --timeout=6s --start-period=30s --retries=3 CMD ["/app/orvano", "healthcheck"]
ENTRYPOINT ["/app/orvano"]
CMD ["serve"]
