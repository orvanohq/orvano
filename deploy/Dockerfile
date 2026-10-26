# syntax=docker/dockerfile:1
# Spec 0007 IMG-1: one multi stage image for linux/amd64 and linux/arm64. Build from the repository root:
#   docker buildx build --platform linux/amd64,linux/arm64 -f deploy/Dockerfile -t ghcr.io/orvanohq/orvano:edge .
# Build stages run on the build machine and cross compile, so no stage needs emulation.

FROM --platform=$BUILDPLATFORM node:24.22.1-bookworm-slim@sha256:55fb5f5a527ea2c5c4a95f963e48ef90aa38c7b346054fdc8ee9957a2b55d205 AS console
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
# Spec 0009 LIC-6: the license travels with the image.
LABEL org.opencontainers.image.licenses="Apache-2.0" \
      org.opencontainers.image.source="https://github.com/orvanohq/orvano"
WORKDIR /app
COPY --from=server /out ./
COPY LICENSE NOTICE /app/
# 1654 is the chiseled image's non root `app` user, so a fresh named volume inherits that owner.
COPY --from=server --chown=1654:1654 /rootfs/var/lib/orvano /var/lib/orvano
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080 8443
VOLUME ["/var/lib/orvano/storage"]
# Spec 0007 IMG-4: no curl; the server binary probes itself.
HEALTHCHECK --interval=10s --timeout=6s --start-period=30s --retries=3 CMD ["/app/orvano", "healthcheck"]
ENTRYPOINT ["/app/orvano"]
CMD ["serve"]
