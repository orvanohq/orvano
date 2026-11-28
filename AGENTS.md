# Orvano

Self hosted, AI native backend as a service. What to build: `docs/design/spec.md`. Order: `docs/scope/`. Stack, layout and module rules: [spec 0001](docs/specs/0001-stack-architecture/index.md), which wins wherever it disagrees with `docs/design/spec.md` or `docs/design/agent-rules.md`.

## Stack

- **Language / Runtime**: C# 14 on .NET 10 (server, executor, CLI); TypeScript on Node 26 (console, docs site)
- **Framework**: ASP.NET Core minimal APIs in one modular monolith (`orvano`) plus `orvano-executor`; Next.js static export console; Astro Starlight docs
- **Key dependencies**: PostgreSQL 17, EF Core 10 (platform data, one `DbContext` per module), Npgsql (tenant SQL under row level security), Orvano's own Postgres outbox and job queue (no Valkey in v1), Aspire 13 for contributors, Caddy and Docker Compose for self hosters
- **Package manager**: NuGet with central versions (`Directory.Packages.props`); pnpm workspace at the root

## Build approach

Tracer Bullet: prove the whole pipe end to end first, then thicken one strand at a time.

## Commands

```bash
dotnet restore Orvano.slnx && pnpm install       # install
dotnet run --project server/src/Orvano.AppHost   # run Postgres, orvano and the executor (needs Docker)
dotnet build Orvano.slnx && pnpm -r build        # build everything
dotnet test --solution Orvano.slnx               # test (Microsoft.Testing.Platform; plain `dotnet test` also works)
dotnet publish cli/Orvano.Cli -c Release -r <rid> # native CLI binary
```

## Specs

Stored in `docs/specs/`. Format: `docs/specs/NNNN-title.md`, or a folder with `index.md` for larger specs.

## Rules

- Domain driven design: each module is a bounded context with its own model, schema and ubiquitous language. Modules never share domain objects; they talk through `Orvano.Contracts` interfaces and past tense domain events (`ProjectCreated`), never each other's tables.
- Aggregates are the consistency boundary and are referenced across modules by ID only. Value objects (`ProjectId`, `Email`) are immutable and validated on construction. Endpoints and application services orchestrate; domain logic lives in aggregates and domain services.
- Folder by feature: inside a module (and in the console) each feature folder holds its endpoint, request and response types, handler and validation together.
- Every state change writes its outbox event in the same transaction. Tenant SQL is parameterized and runs under the project's role with RLS, never through EF Core.
- Errors are problem+json with a stable `code` and a `hint`. Every mutating endpoint supports `Idempotency-Key` and `Orvano-Dry-Run`. The contract in `contract/` is generated from code and committed (feature 9 onward).
- Strict: nullable on and all warnings as errors in C#; TypeScript `strict` with no `any`.
- Document public APIs: XML docs on public types in `Orvano.Wire` and `Orvano.Contracts` and on every endpoint. Validate every options class at startup (`ValidateOnStart`).
- Console screens meet WCAG AA. Conventional commits (`feat(auth): add sign in`).
- Done means: unit and integration tests pass against real Postgres (Testcontainers), new endpoints have contract tests, SDKs regenerated, a docs page with a compiling sample, no secrets in code, no TODO without an issue link.

## Tooling (installed by `/develop tooling`, feature 2)

- Lint and format: `.editorconfig`, built in .NET analyzers at the latest recommended level, `dotnet format`; ESLint (Next config) and Prettier for `console/` and `website/`.
- Before each commit: format and lint the staged files only. The full build, typecheck and tests run in CI.
- CI: GitHub Actions in `.github/workflows/` on every push and pull request: build, lint, test, `pnpm -r build`, both container images (amd64 and arm64, not pushed) and the Compose smoke test (`deploy/smoke-test.sh`).

## Git

- integration: on
- branch prefix: feat/
- commit: per-milestone
- sign off: every commit carries `Signed-off-by` (`git commit -s`); the DCO check in CI fails the pull request otherwise

## Agent skills

Installed skills are area specific and listed in each area's AGENTS.md below.
MCP servers: Aspire (recommended, `aspire agent init --mcp`), Playwright (recommended, `claude mcp add playwright npx @playwright/mcp@latest`)
Candidates found by `/architect`, not installed (read before trusting): `aaronontheweb/dotnet-skills@efcore-patterns`, `lombiq/tailwind-agent-skills@tailwind-4-docs`, `astrolicious/agent-skills@astro`, `tanstack-skills/tanstack-skills@tanstack-query`, `wshobson/agents@nextjs-app-router-patterns`, `currents-dev/playwright-best-practices-skill@playwright-best-practices`

## Context files

- [server/AGENTS.md](server/AGENTS.md): .NET server, module and reference rules, Aspire, tests
- [cli/AGENTS.md](cli/AGENTS.md): Native AOT CLI, MIT, `Orvano.Wire` only
- [console/AGENTS.md](console/AGENTS.md): Next.js static export dashboard, design system and mockups
- [website/AGENTS.md](website/AGENTS.md): Astro Starlight docs site
- [deploy/AGENTS.md](deploy/AGENTS.md): Compose, Caddy and Dockerfiles for self hosters

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
