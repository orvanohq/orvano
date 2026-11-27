# deploy

Everything a self hoster runs: the Docker Compose file, the Caddyfile and the Dockerfiles for the `orvano` and `orvano-executor` images.

Feature 4 (Compose stack skeleton) fills this folder: Caddy in front, `orvano` with `/healthz` and `/readyz`, Postgres 17 behind it and the executor beside it.

Dockerfiles build from the repo root as context, because the server shares one .NET solution and one pnpm workspace.

Contributors run the same pieces with the Aspire AppHost instead (`dotnet run --project server/src/Orvano.AppHost`).
