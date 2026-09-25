# Orvano

Open source, self hosted backend (auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, backups) run from one console. Apache 2.0.

## Stack

- **Server**: C# 14 on .NET 10, ASP.NET Core Minimal APIs, a modular monolith started in roles (`api`, `worker`, `realtime`, `migrate`)
- **Data**: PostgreSQL 18 only (data, outbox events, job queue); EF Core 10 for platform tables, raw Npgsql for per project tables
- **Console**: React, Vite, TypeScript, TanStack Router and Query, served static behind Caddy 2
- **Contract and SDKs**: TypeSpec to OpenAPI 3.1, generated JS/TS, Next.js, Flutter, Dart, and .NET SDKs (spec 0001)
- **Tooling**: pnpm workspaces via Corepack, Node 24, Aspire 13 for local dev, Docker Compose in production, GitHub Actions

## Build approach

Tracer Bullet: each version ships one capability end to end through every layer (backend, console, SDKs, docs), working, then later versions thicken it.

## Commands

```bash
corepack enable pnpm && pnpm install                    # install
dotnet run --project dev/Orvano.AppHost                 # dev: Postgres, every role, console, Aspire dashboard
dotnet build Orvano.slnx && pnpm -r build               # build
dotnet test --solution Orvano.slnx                      # test (Docker must be running)
docker compose -f deploy/compose/docker-compose.yml up --build   # production shape on http://localhost
```

## Specs

Stored in `docs/specs/NNNN-title/` (`index.md`, `rationale.md`, optional `verify.md`). Scope lives in `docs/scope/`.

## Rules

- Clean Architecture inside each module: business rules live in plain types with no ASP.NET, EF, or Npgsql references. Endpoints, event consumers, and job handlers are thin adapters. Infrastructure implements interfaces the module owns. Generated contract types or DTOs cross boundaries, never domain types.
- Module boundaries are spec 0002's: one csproj per module, public types only in its `Contracts` namespace, no module touches another module's tables.
- Folders match the scaffold: feature folders inside each project (`Jobs/`, `Events/`); console code beside the route that uses it.
- Strict types: C# nullable and analyzer warnings are errors; TS `strict: true`, no `any` (use `unknown` and narrow), exhaustive switches.
- One error pattern: the API returns RFC 9457 problem details; the console and SDKs surface typed errors.
- Validate config at startup: every `ORVANO_*` setting is checked when a role starts, and the role refuses to run on a bad value.
- Never log event payloads, secrets, tokens, connection strings, or the master key.
- Document public APIs: XML doc comments on public C# members, TSDoc on exported TS symbols.
- Naming: .NET casing with an `Async` suffix; TS camelCase, PascalCase components, kebab-case files, named exports only; SQL snake_case.
- Tests: unit tests for domain and use cases, integration tests against real Postgres 18 via Testcontainers, never a mocked database. The console meets WCAG AA. CI must be green to merge.

## Tooling

Chosen here, installed by `/develop tooling`:
- Lint and format: `.editorconfig` with `dotnet format`, .NET analyzers (`AnalysisLevel` latest, `TreatWarningsAsErrors`) in `Directory.Build.props`; ESLint flat config (typescript-eslint, react hooks) plus Prettier for every pnpm workspace.
- Pre commit: Lefthook runs `dotnet format` and ESLint/Prettier on staged files only. Builds, typecheck, and tests stay in CI.
- Tests: xUnit v3 plus Testcontainers (server, in place); Vitest plus Testing Library (console, when row 5 lands).
- CI: `.github/workflows/ci.yml` already runs build, tests, the EF drift check, and the compose smoke test; add lint and format checks to it.

## Git

- integration: on
- branch prefix: by change type (`feat/`, `fix/`, `chore/`, `docs/`)
- commit: per-milestone, conventional commits (`feat(server): ...`, `fix(console): ...`)
- Never commit to `main`; open a PR so CI runs. Ask before merging, force pushing, or deleting branches.

## Agent skills

- [dotnet-webapi](.claude/skills/dotnet-webapi/): `dotnet/skills`, Minimal API endpoints, OpenAPI metadata, error handling
- [ef-core](.claude/skills/ef-core/): `github/awesome-copilot`, EF Core practices for platform tables
- [optimizing-ef-core-queries](.claude/skills/optimizing-ef-core-queries/): `dotnet/skills`, fast EF Core queries
- [configuring-opentelemetry-dotnet](.claude/skills/configuring-opentelemetry-dotnet/): `dotnet/skills`, traces, metrics, logs
- [aspire](.claude/skills/aspire/): `microsoft/aspire-skills`, the AppHost and Aspire CLI
- [aspire-monitoring](.claude/skills/aspire-monitoring/): `microsoft/aspire-skills`, logs, traces, and resource state in dev
- [aspire-deployment](.claude/skills/aspire-deployment/): `microsoft/aspire-skills`, publishing to Compose and beyond
- [multi-stage-dockerfile](.claude/skills/multi-stage-dockerfile/): `github/awesome-copilot`, `deploy/*.Dockerfile`
- [pnpm](.claude/skills/pnpm/): `antfu/skills`, workspaces, catalogs, overrides
- [supabase-postgres-best-practices](.claude/skills/supabase-postgres-best-practices/): `supabase/agent-skills`, Postgres schema, roles, migrations, query performance (ignore its Supabase product parts)
- [lefthook](.claude/skills/lefthook/): `fandhe-ai/agent-reference-skills`, `lefthook.yml` pre commit hooks (written in Japanese)

MCP servers: Aspire MCP (recommended), GitHub MCP (recommended), Postgres MCP Pro, dev database only (recommended), ESLint MCP `@eslint/mcp` (recommended)

## Context files

- [server/AGENTS.md](server/AGENTS.md) (.NET server: modules, Core kernel, migrations, tests)
- [console/AGENTS.md](console/AGENTS.md) (React console: routes, data fetching, UI rules)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
