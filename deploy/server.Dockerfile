# syntax=docker/dockerfile:1
# The one Orvano image. Every role runs from it: `orvano api|worker|realtime|migrate`.
# Build from the repo root: docker build -f deploy/server.Dockerfile .

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
ARG TARGETARCH
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props VERSION ./
COPY server/src/Orvano.Core/Orvano.Core.csproj server/src/Orvano.Core/
COPY server/src/Orvano.Server/Orvano.Server.csproj server/src/Orvano.Server/
RUN dotnet restore server/src/Orvano.Server/Orvano.Server.csproj -a $TARGETARCH

COPY server/ server/
RUN dotnet publish server/src/Orvano.Server/Orvano.Server.csproj -c Release -a $TARGETARCH --no-restore -o /app

# The storage volume mount point, owned by the non root app user (UID 1654 in .NET images).
RUN mkdir -p /storage

# chiseled-extra keeps ICU and tzdata, which scheduling and Npgsql need. No shell, non root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=1654:1654 /storage /var/lib/orvano/storage
ENTRYPOINT ["/app/orvano"]
