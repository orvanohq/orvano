# Orvano

Open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, managed from one console that holds many projects.

> Orvano is at the very start. There is no runnable code yet, only the plan and the first design decisions.

## What makes it different

- **Notification Center**: in app inbox widgets, workflows, preferences, and digests.
- **Webhooks platform**: signed outbound webhooks with retries and replay, plus inbound webhooks.
- **Jobs, queues, and cron**: durable background jobs with dead letters and schedules.
- **Backups and environments**: free scheduled backups, point in time restore, and dev, staging, and prod.

## Planned stack

A .NET 10 modular monolith on PostgreSQL 18, run as one image in several roles behind Caddy, with a React console. SDKs for JavaScript/TypeScript, Next.js, Flutter, Dart, and .NET are generated from one TypeSpec contract. It runs on one small server with Docker Compose.

## Where to read more

- Website: [orvano.dev](https://orvano.dev) (coming later)
- [Scope and roadmap](docs/scope/index.md)
- [Spec 0001: API contract and SDK pipeline](docs/specs/0001-api-contract-sdk-pipeline/index.md)
- [Spec 0002: Stack and architecture](docs/specs/0002-stack-architecture/index.md)

## License

[Apache 2.0](LICENSE)
