# Orvano

Open source, self hosted backend (auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, backups) run from one console. Apache 2.0.

## Stack

- **Server**: C# 14 on .NET 10, ASP.NET Core Minimal APIs, a modular monolith started in roles (`api`, `worker`, `realtime`, `migrate`)
- **Data**: PostgreSQL 18 only (data, outbox events, job queue); EF Core 10 for platform tables, raw Npgsql for per project tables
- **Console**: React, Vite, TypeScript, TanStack Router and Query, served static behind Caddy 2
- **Contract and SDKs** (spec 0001; `contract/`, `tools/sdkgen/`, `sdks/`): TypeSpec to OpenAPI 3.1, generated JS/TS, Next.js, Flutter, Dart, and .NET SDKs
- **Tooling**: pnpm workspaces via Corepack, Node 24, Aspire 13 for local dev, Docker Compose in production, GitHub Actions

## Build approach

Tracer Bullet: each version ships one capability end to end through every layer (backend, console, SDKs, docs), working, then later versions thicken it.

## Commands

```bash
corepack enable pnpm && pnpm install                    # install
flutter pub get                                         # install the Dart workspace (SDKs and scenario runners)
dotnet run --project dev/Orvano.AppHost                 # dev: Postgres, every role, console, Aspire dashboard
dotnet build Orvano.slnx && pnpm -r build               # build
dotnet test --solution Orvano.slnx                      # test (Docker must be running)
dotnet format Orvano.slnx && pnpm lint:fix && pnpm format   # fix lint and format (CI checks all three)
docker compose -f deploy/compose/docker-compose.yml up --build   # production shape on http://localhost
pnpm --filter @orvano/contract build && dotnet run --project tools/sdkgen   # after a contract change: regenerate, then commit both
docker compose -f tests/scenarios/compose.yml up -d --build --wait   # Test server on :8080 for the shared scenarios
```

## Specs

Stored in `docs/specs/NNNN-title/` (`index.md`, `rationale.md`, optional `verify.md`). Scope lives in `docs/scope/`.

## Rules

- Clean Architecture inside each module: business rules live in plain types with no ASP.NET, EF, or Npgsql references. Endpoints, event consumers, and job handlers are thin adapters. Infrastructure implements interfaces the module owns. Generated contract types or DTOs cross boundaries, never domain types.
- Module boundaries are spec 0002's: one csproj per module, public types only in its `Contracts` namespace, no module touches another module's tables.
- Folders match the scaffold: feature folders inside each project (`Jobs/`, `Events/`); console code beside the route that uses it.
- Strict types: C# nullable and analyzer warnings are errors; TS `strict: true`, no `any` (use `unknown` and narrow), exhaustive switches.
- One error pattern: the API returns RFC 9457 problem details; the console and SDKs surface typed errors.
- Contract first: the TypeSpec in `contract/` is the only source of the public API. Never hand edit a `generated/` or `Generated/` folder; change the contract and run SdkGen (CI fails on stale output).
- Validate config at startup: every `ORVANO_*` setting is checked when a role starts, and the role refuses to run on a bad value.
- Never log event payloads, secrets, tokens, connection strings, or the master key.
- Document public APIs: XML doc comments on public C# members, TSDoc on exported TS symbols, `///` docs on public Dart members.
- Naming: .NET casing with an `Async` suffix; TS camelCase, PascalCase components, kebab-case files, named exports only; Dart snake_case files; SQL snake_case.
- Tests: unit tests for domain and use cases, integration tests against real Postgres 18 via Testcontainers, never a mocked database. The console meets WCAG AA. CI must be green to merge.

## Tooling

Installed (scope row 2):
- Lint and format: `.editorconfig` with `dotnet format`, .NET analyzers (`AnalysisLevel` latest, `TreatWarningsAsErrors`) in `Directory.Build.props`; ESLint flat config (typescript-eslint, react hooks) plus Prettier for every pnpm workspace.
- Pre commit: Lefthook runs `dotnet format` and ESLint/Prettier on staged files only, then restages what they fixed. `pnpm install` installs the hooks; skip once with `LEFTHOOK=0`. Builds, typecheck, and tests stay in CI.
- Tests: xUnit v3 plus Testcontainers (server, in place), xUnit for SdkGen (`tools/tests/`) and the .NET SDK (`sdks/dotnet/tests/`); Vitest for the JS SDKs (`sdks/js/test/`, `sdks/nextjs/test/`), plus Testing Library for the console when row 5 lands; `package:test` for the Dart SDKs (`sdks/dart/*/test/`).
- CI: `.github/workflows/ci.yml` runs lint and format checks (`dotnet format --verify-no-changes`, `pnpm lint`, `pnpm format:check`), build, tests, the EF drift check, and the compose smoke test.
- SDK CI: `.github/workflows/sdks.yml` fails when `contract/dist/openapi.json` or generated code is stale, then runs the shared scenarios on Node, Bun, Deno, Chromium, workerd, Next.js, Dart, Flutter (Chrome, Android), and .NET (`net10.0`, `netstandard2.0`). `sdks-nightly.yml` runs Flutter on the iOS simulator. `sdks.yml` also runs the TS and Dart SDK unit tests and reports breaking changes since the last release tag (oasdiff).
- Release: pushing the tag `v<VERSION>` runs `.github/workflows/release.yml`, which runs `sdks.yml` and then publishes to npm, pub.dev, and NuGet and updates the `orvano-js`, `orvano-dart`, and `orvano-dotnet` mirrors. It is a dry run until 0.1, and a manual run is always a dry run. Flutter 3.44.2 is pinned in `.tool-versions` and both workflows so `dart format` output matches everywhere.

## Git

- integration: on
- branch prefix: by change type (`feat/`, `fix/`, `chore/`, `docs/`)
- commit: per-milestone, conventional commits (`feat(server): ...`, `fix(console): ...`)
- Never commit to `main`; open a PR so CI runs. Ask before merging, force pushing, or deleting branches.
- One branch at a time: never create a new branch while an earlier one still has an unmerged PR (no stacked PRs). Add follow up work to the open branch, or wait until its PR merges, then branch from the updated `main`.

## Agent skills

- [dotnet-webapi](.claude/skills/dotnet-webapi/): `dotnet/skills`, Minimal API endpoints, OpenAPI metadata, error handling
- [ef-core](.claude/skills/ef-core/): `github/awesome-copilot`, EF Core practices for platform tables
- [optimizing-ef-core-queries](.claude/skills/optimizing-ef-core-queries/): `dotnet/skills`, fast EF Core queries
- [configuring-opentelemetry-dotnet](.claude/skills/configuring-opentelemetry-dotnet/): `dotnet/skills`, traces, metrics, logs
- [aspire](.claude/skills/aspire/): `microsoft/aspire-skills`, the AppHost and Aspire CLI
- [aspire-monitoring](.claude/skills/aspire-monitoring/): `microsoft/aspire-skills`, logs, traces, and resource state in dev
- [aspire-deployment](.claude/skills/aspire-deployment/): `microsoft/aspire-skills`, publishing to Compose and beyond
- [multi-stage-dockerfile](.claude/skills/multi-stage-dockerfile/): `github/awesome-copilot`, `deploy/server.Dockerfile` and `deploy/gateway/Dockerfile`
- [pnpm](.claude/skills/pnpm/): `antfu/skills`, workspaces, catalogs, overrides
- [supabase-postgres-best-practices](.claude/skills/supabase-postgres-best-practices/): `supabase/agent-skills`, Postgres schema, roles, migrations, query performance (ignore its Supabase product parts)
- [lefthook](.claude/skills/lefthook/): `fandhe-ai/agent-reference-skills`, `lefthook.yml` pre commit hooks (written in Japanese)
- [flutter-add-integration-test](.agents/skills/flutter-add-integration-test/): `flutter/agent-plugins`, `integration_test` in the Flutter scenario runner
- [dart-write-documentation](.agents/skills/dart-write-documentation/): `flutter/agent-plugins`, `///` API docs for the published Dart packages
- [dart-run-static-analysis](.agents/skills/dart-run-static-analysis/): `flutter/agent-plugins`, `dart analyze` and `dart fix` (CI runs `--fatal-infos`)
- [dart-add-unit-test](.agents/skills/dart-add-unit-test/): `dart-lang/skills`, `package:test` unit tests for the Dart SDK packages
- [dart-test-fundamentals](.agents/skills/dart-test-fundamentals/): `kevmoo/dash_skills`, `package:test` groups, lifecycle, platforms (`-p chrome`), `dart_test.yaml`
- [dart-collect-coverage](.agents/skills/dart-collect-coverage/): `dart-lang/skills`, LCOV coverage for Dart packages
- [nextjs-app-router-patterns](.agents/skills/nextjs-app-router-patterns/): `wshobson/agents`, App Router, server components, route handlers, middleware for `@orvano/nextjs`
- [workers-best-practices](.agents/skills/workers-best-practices/): `cloudflare/skills`, Workers runtime rules (the edge target the JS SDK must run on)
- [playwright-cli](.agents/skills/playwright-cli/): `microsoft/playwright-cli`, driving a real browser with Playwright

Declined: ESLint, typescript-eslint, and Prettier skills (the configs are small; the ESLint MCP covers live linting)

MCP servers: Aspire MCP (recommended), GitHub MCP (recommended), Postgres MCP Pro, dev database only (recommended), ESLint MCP `@eslint/mcp` (recommended), Dart and Flutter MCP `dart mcp-server` (recommended), Playwright MCP (recommended), Next.js DevTools MCP `next-devtools-mcp` (recommended)

## Context files

- [server/AGENTS.md](server/AGENTS.md) (.NET server: modules, Core kernel, migrations, tests)
- [console/AGENTS.md](console/AGENTS.md) (React console: routes, data fetching, UI rules)
- [contract/AGENTS.md](contract/AGENTS.md): the TypeSpec API contract, operation metadata, how to add an endpoint
- [tools/sdkgen/AGENTS.md](tools/sdkgen/AGENTS.md): SdkGen, the C# generator, its templates and type mapping
- [sdks/AGENTS.md](sdks/AGENTS.md): the five SDK surfaces, generated versus handwritten code, audience routing
- [tests/scenarios/AGENTS.md](tests/scenarios/AGENTS.md): the shared scenarios and one runner per SDK surface

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
