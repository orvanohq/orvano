# Orvano

**The AI native backend you host yourself.** Auth, database, storage, realtime, functions, webhooks, sites and email on .NET 10 and PostgreSQL, built for app developers and for the coding agents that build with them.

> [!WARNING]
> **Orvano is pre alpha.** Nothing here is ready for real use yet. There is no release, APIs and data formats will change without notice, there is no upgrade path between builds, and it has not had a security review. Please don't run it with data you care about. You're welcome to follow along, read the code and open issues.

## Where things are

| Folder | What it holds | Licence |
|---|---|---|
| `server/` | The `orvano` host, the `orvano-executor`, shared libraries, the contributor AppHost and tests | Apache-2.0 |
| `server/src/Orvano.Wire/` | Wire types shared with the CLI | MIT |
| `cli/` | The `orvano` command line tool (one native binary) | MIT |
| `console/` | The dashboard, served by `orvano` at `/console` | Apache-2.0 |
| `website/` | The docs site | Apache-2.0 |
| `contract/` | The generated API contract (OpenAPI, events, errors) | Apache-2.0 |
| `deploy/` | Docker Compose, Caddy and Dockerfiles for self hosting | Apache-2.0 |
| `docs/` | Design, scope and specs for building Orvano | Apache-2.0 |

SDKs will live in `sdks/`, each MIT licensed with its own `LICENSE` file.

## Try it from source

You need the .NET 10 SDK, Node.js 26 with pnpm, and Docker (only to run the stack).

```bash
dotnet build Orvano.slnx
dotnet test --solution Orvano.slnx
dotnet run --project server/src/Orvano.AppHost
pnpm install && pnpm -r build
```

[CONTRIBUTING.md](CONTRIBUTING.md) has the full setup and how to send a change.

## Security

Please don't report security problems in public issues. [SECURITY.md](SECURITY.md) explains how to report one privately.

## Licence

The Orvano server is licensed under the [Apache License 2.0](LICENSE). The CLI, `Orvano.Wire` and the SDKs are licensed under the MIT License; each of those folders carries its own `LICENSE` file. See [NOTICE](NOTICE).

Contributions are accepted under the licence of the folder they change, with a [Developer Certificate of Origin](https://developercertificate.org/) sign off on every commit.
