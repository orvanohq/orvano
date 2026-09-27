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

You need a Linux server (Ubuntu 22.04 or 24.04, or Debian 12 or 13, on amd64 or arm64) with at least 2 GB of memory (4 GB recommended), ports 80 and 443 open, and a domain whose DNS points to it. Then run:

```bash
curl -fsSL https://github.com/orvanohq/orvano/releases/latest/download/install.sh | sudo sh
```

It checks the server, offers to install Docker if it is missing, asks for your domain and an optional email for Let's Encrypt, generates every secret, starts Orvano, and waits until `https://<your domain>` answers. To check the script before running it, download `install.sh` and `install.sh.sha256` from the [latest release](https://github.com/orvanohq/orvano/releases/latest) and run `sha256sum -c install.sh.sha256`.

**Open the setup link right away.** At the end the installer prints `https://<your domain>/setup#...`. Only that link can create the first admin account, and only once. Until you use it, anyone who finds your server can see that it is still unclaimed (but can't claim it).

**Back up your master key.** On the first run the installer prints it and waits until you type `saved`. It lives in `/opt/orvano/.env` with the database passwords; losing it makes every secret Orvano stores unrecoverable. Keep a copy of the whole `.env` somewhere safe.

**Flags.** Pass them after `sh -s --`, for example `curl -fsSL .../install.sh | sudo sh -s -- --domain orvano.example.com --email ops@example.com --yes`. Every question has a flag, so it also runs from cloud init, Ansible, or CI:

| Flag | Meaning |
|---|---|
| `--domain <host>` | Your domain, or `localhost` (plain HTTP, not for production) |
| `--email <addr>` | Let's Encrypt account email; empty is allowed |
| `--version <X.Y.Z>` | The version to install; defaults to the script's own |
| `--dir <path>` | Install directory; default `/opt/orvano` |
| `--yes` | Answer yes to every question, never wait at the master key |
| `--no-pull` | Use only images already on the server (air gapped servers) |
| `--no-ip-lookup` | Skip the public address lookup in the DNS check |
| `--timeout <seconds>` | How long to wait for healthy services; default 300 |

Exit codes: `0` success, `2` refused (nothing changed), `3` started but unhealthy or unreachable, `1` anything else.

**Where files live.** Everything is in `/opt/orvano`: `docker-compose.yml` and `initdb/` (rewritten by every run), `.env` (your settings and secrets, `0600`), `.env.previous` (the `.env` before the last run changed it), and `install.log` (what each run did, never a secret).

**Upgrade or repair.** Run the same command again. It never changes a secret: the same version repairs the install, and a newer one pulls the new images and upgrades in place (migrations run first). Downgrades are refused; restore a backup instead. An upgrade restarts the containers, so expect a short outage.

**Logs.** From `/opt/orvano`, `docker compose ps --all` shows every service and `docker compose logs api` (or `worker`, `gateway`, ...) shows its logs. Each container keeps at most 30 MB of logs.

**Your own changes.** Put them in `/opt/orvano/docker-compose.override.yml`; the installer never touches it, and Compose merges it on every start. Override a service's keys (for example `services: api: environment: ...`), not the `x-` anchors at the top of `docker-compose.yml`, because anchors are resolved before the files merge. To keep your own Postgres memory settings, put `ORVANO_PG_TUNING=manual` in `.env`.

## Run it locally

You need the .NET 10 SDK, Docker, Node 24, and pnpm through Corepack (`corepack enable pnpm`).

```bash
pnpm install
dotnet run --project dev/Orvano.AppHost
```

This starts Postgres 18, applies the platform migrations, then starts the API, worker, realtime roles, and the console, with the Aspire dashboard for logs and traces. To try the production shape behind Caddy on `http://localhost`, copy `deploy/compose/.env.example` to `deploy/compose/.env`, fill in the passwords, and run `docker compose -f docker-compose.yml -f docker-compose.build.yml up --build` from `deploy/compose/`.

## Where to read more

- Website: [orvano.dev](https://orvano.dev) (coming later)
- [Scope and roadmap](docs/scope/index.md)
- [Spec 0001: API contract and SDK pipeline](docs/specs/0001-api-contract-sdk-pipeline/index.md)
- [Spec 0002: Stack and architecture](docs/specs/0002-stack-architecture/index.md)

## License

[Apache 2.0](LICENSE)
