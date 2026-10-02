# Orvano

Open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, managed from one console that holds many projects.

> Orvano is at the very start. The scaffold runs (API, worker, realtime, console), but there are no product features yet.

## What makes it different

- **Notification Center**: in app inbox widgets, workflows, preferences, and digests.
- **Webhooks platform**: signed outbound webhooks with retries and replay, plus inbound webhooks.
- **Jobs, queues, and cron**: durable background jobs with dead letters and schedules.
- **Backups and environments**: free scheduled backups, point in time restore, and dev, staging, and prod.

## Planned stack

A .NET 10 modular monolith on PostgreSQL 18, run as one image in several roles behind Caddy, with a React console. SDKs for JavaScript/TypeScript, Next.js, Flutter, Dart, and .NET are generated from one TypeSpec contract. It runs on one small server with Docker Compose.

## Install on your server

One command installs Orvano on a Linux server with HTTPS: `curl -fsSL https://github.com/orvanohq/orvano/releases/latest/download/install.sh | sudo sh`.
[Install on a server](https://orvano.dev/docs/self-hosting/install/) covers what you need, the flags, and the master key backup, and [Upgrade and repair](https://orvano.dev/docs/self-hosting/upgrade/) covers the rest. To try it on your own computer, see [Run Orvano locally](https://orvano.dev/docs/local/).

## Work on Orvano itself

You need the .NET 10 SDK, Docker, Node 24, and pnpm through Corepack (`corepack enable pnpm`).

```bash
pnpm install
dotnet run --project dev/Orvano.AppHost
```

This starts Postgres 18, applies the platform migrations, then starts the API, worker, realtime roles, and the console, with the Aspire dashboard for logs and traces. To try the production shape behind Caddy on `http://localhost`, copy `deploy/compose/.env.example` to `deploy/compose/.env`, fill in the passwords, and run `docker compose -f docker-compose.yml -f docker-compose.build.yml up --build` from `deploy/compose/`.

## Where to read more

- Docs: [orvano.dev](https://orvano.dev), with a quickstart for each SDK
- [Scope and roadmap](docs/scope/index.md)
- [Spec 0001: API contract and SDK pipeline](docs/specs/0001-api-contract-sdk-pipeline/index.md)
- [Spec 0002: Stack and architecture](docs/specs/0002-stack-architecture/index.md)

## License

[Apache 2.0](LICENSE)
