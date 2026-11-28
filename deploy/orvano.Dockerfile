# syntax=docker/dockerfile:1
# The orvano image: API, realtime, worker, scheduler, MCP and console files in one process.
# Build with the repo root as context: docker build -f deploy/orvano.Dockerfile .
# Spec 0002, "Images". The final stage has no RUN step, so arm64 builds without emulation.

# Console stage (spec 0003, "Docker image"). The static export is the same for every processor, so it builds
# once on the builder's own platform. Keep the Node version in step with .nvmrc and pnpm with package.json.
FROM --platform=$BUILDPLATFORM node:26.13.0-slim AS console
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm install -g pnpm@12.23.0
WORKDIR /src
# Every workspace manifest, so the frozen lockfile matches. --ignore-scripts: the root prepare script
# installs git hooks, and there is no .git here.
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY console/package.json console/
COPY website/package.json website/
RUN pnpm install --frozen-lockfile --ignore-scripts --filter @orvano/console...
COPY console ./console
RUN pnpm --filter @orvano/console build

# Build stage. Always runs on the builder's own processor and cross publishes for the target.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
ARG TARGETARCH
ARG VERSION
WORKDIR /src

# Restore first, from the build files and project files only, so this layer is cached until they change.
COPY global.json Directory.Build.props Directory.Packages.props NuGet.config .editorconfig ./
COPY server/src/Orvano.Wire/Orvano.Wire.csproj server/src/Orvano.Wire/
COPY server/src/Orvano.Contracts/Orvano.Contracts.csproj server/src/Orvano.Contracts/
COPY server/src/Orvano.Infrastructure/Orvano.Infrastructure.csproj server/src/Orvano.Infrastructure/
COPY server/src/Orvano.ServiceDefaults/Orvano.ServiceDefaults.csproj server/src/Orvano.ServiceDefaults/
COPY server/src/Orvano.Host/Orvano.Host.csproj server/src/Orvano.Host/
RUN dotnet restore server/src/Orvano.Host/Orvano.Host.csproj -a $TARGETARCH

COPY server/src ./server/src
# An empty VERSION leaves <Version> from Directory.Build.props in force.
RUN dotnet publish server/src/Orvano.Host/Orvano.Host.csproj -c Release -a $TARGETARCH --no-restore -o /app -p:UseAppHost=false ${VERSION:+-p:Version=$VERSION}

# Final stage. Ubuntu chiseled (no shell, no package manager), extra variant (culture and time zone data).
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled-extra AS final
ARG VERSION
LABEL org.opencontainers.image.source="https://github.com/orvanohq/orvano" \
      org.opencontainers.image.licenses="Apache-2.0" \
      org.opencontainers.image.version="${VERSION}"

WORKDIR /app
COPY --from=build /app ./
COPY --from=console /src/console/out /app/console

USER $APP_UID
EXPOSE 8080

# Liveness only, from the binary itself (no shell or curl in the image). Exec form, because there is no shell.
HEALTHCHECK --interval=10s --timeout=3s --retries=5 --start-period=20s \
  CMD ["dotnet", "Orvano.Host.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "Orvano.Host.dll"]
