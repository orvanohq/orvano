# 0001. Orvano stack and architecture, with its own Postgres job queue

**Date**: 2026-11-27
**Status**: Accepted

## Summary

This spec puts the stack from the design spec on record and closes the gaps it left. Orvano is a modular monolith (one program made of separate modules) on .NET 10 and PostgreSQL 17, with one separate executor for sandboxed code. Background jobs and events run on Orvano's own Postgres queue, because the event must commit together with the data change on every write path, including tenant writes. For building, this means one .NET solution at the repo root, one pnpm workspace, no Valkey in v1, and module projects that appear only when their first feature lands.

## Decision

**Chosen option**: Option 1: the locked stack with Orvano's own Postgres queue

Build Orvano on the stack locked in `docs/design/spec.md`, with a durable job queue and outbox (a table of events written in the same transaction as the change) that Orvano owns in Postgres and claims with `FOR UPDATE SKIP LOCKED` (a Postgres feature that lets several workers take different rows without blocking each other).

**Implementation skills**: `aspire` (`microsoft/aspire-skills`, `.claude/skills/aspire/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `dotnet-aot-compat` (`dotnet/skills`, `.claude/skills/dotnet-aot-compat/`) · `postgresql-best-practices` (`microsoft/postgres-skills`, `.claude/skills/postgresql-best-practices/`) · `testcontainers-dotnet` (`testcontainers/claude-skills`, `.claude/skills/testcontainers-dotnet/`) · `docker-compose-patterns` (`docker/skills`, `.claude/skills/docker-compose-patterns/`) · `shadcn` (`shadcn-ui/ui`, `.claude/skills/shadcn/`) · `playwright-cli` (`microsoft/playwright-cli`, `.claude/skills/playwright-cli/`)

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Proposed stack

Rows marked **settled here** are calls this spec makes. Every other row is recorded from the design spec as it stands, with no comparison.

| Layer | Choice | Reason |
|---|---|---|
| Architecture pattern | Modular monolith: one `orvano` process (API, realtime, worker, scheduler, MCP, console files) plus a separate `orvano-executor` | One thing to run and debug on a single server. Only the executor is apart, because it controls Docker. |
| Language and runtime | .NET 10, C# 14 | Long term support until November 2028. |
| Web framework | ASP.NET Core minimal APIs | Fast, and the OpenAPI document is generated straight from the endpoints. |
| Local orchestration | Aspire AppHost, major version 13 pinned, for contributors only | One command starts Postgres and every process. Users get Docker Compose instead. Pinning one major is **settled here**. |
| Primary database | PostgreSQL 17 | One engine for platform data and tenant data, with row level security (the database itself filters rows per caller). |
| Platform data access | EF Core 10, one `DbContext` per module, each with its own schema and its own migrations | Matches the rule that a module never touches another module's tables. **Settled here.** |
| Tenant data access | Npgsql with generated, parameterized SQL, run under the project's own Postgres role | Tenant tables change at runtime, so a typed ORM does not fit. |
| Background jobs and events | Orvano's own outbox table and jobs table in Postgres, claimed with `FOR UPDATE SKIP LOCKED`, run by hosted services inside `orvano` | The outbox row can commit inside any transaction, EF Core or raw Npgsql, and no extra broker is needed. **Settled here.** |
| Cache, pub/sub, rate limits | No Valkey in v1. Rate limits use ASP.NET Core's built in rate limiter. No cache or pub/sub interface exists until a feature needs one | Valkey only helps with several `orvano` instances, which the scope defers. **Settled here.** |
| Object storage | Local disk by default, with an S3 API adapter | Self hosters start with a volume and can move to S3, R2 or MinIO. |
| Function sandbox | Docker containers reached through `docker-socket-proxy`, gVisor as an optional setting | Already chosen in the design spec's open decisions list. Its older "containerd" wording is reconciled here. |
| Auth crypto | Argon2id for passwords, ES256 for access tokens (JWT), ASP.NET Core Data Protection keys kept in Postgres | Argon2id follows the locked Auth section. The older "Identity password hasher" wording no longer applies. **Settled here.** |
| MCP | `ModelContextProtocol.AspNetCore`, the official C# SDK, mounted at `/mcp` inside the host | Agent calls land in the same modules and permission checks as SDK calls. |
| API contract | OpenAPI 3.1 from `Microsoft.AspNetCore.OpenApi`, committed at `contract/openapi.json` | One source of truth for SDKs, CLI, MCP tools and docs. |
| Observability | `/healthz` and `/readyz`, JSON logs to stdout from the built in .NET logging, optional Prometheus `/metrics`, OpenTelemetry export off by default | Standard parts and no extra logging library. The built in logger choice is **settled here**. |
| Console | Next.js static export, React, Tailwind, shadcn/ui, TanStack Query, served by `orvano` at `/console` | Uses only the public API through the TypeScript SDK. |
| Docs site | Astro Starlight in `website/` | Static, with machine readable `llms.txt` output. |
| CLI | .NET 10 Native AOT (compiled ahead of time to one native binary) with System.CommandLine, binary name `orvano` | Starts fast, needs no runtime. Shares wire types with the server through `Orvano.Wire`. |
| Code shared with the CLI | `Orvano.Wire`: IDs, the error shape, event payloads and the JSON source generator context. MIT licensed, safe for Native AOT | Keeps server only code out of the MIT CLI binary. **Settled here.** |
| SDK generator | `tools/Orvano.SdkGen` with Scriban templates | Output reads like hand written code. Arrives with feature 17. |
| Testing | xUnit v3, Testcontainers (real Postgres in tests), Playwright for the console, contract tests from the OpenAPI document | Tests run against the real database, not a fake. |
| .NET build | One `Orvano.slnx` at the repo root with every .NET project, central package versions, nuget.org as the only package source | One restore and build. A change in shared code breaks the CLI at compile time. **Settled here.** |
| Product version | One `<Version>` in `Directory.Build.props`, starting at `0.1.0-dev`. The release pipeline overrides it from the git tag | One number for the CLI, the images and upgrades. **Settled here.** |
| JavaScript build | One pnpm workspace at the repo root | The console uses the local SDK through `workspace:*`, with one lockfile. **Settled here.** |
| Proxy and hosting | Caddy in front, Docker Compose on one Linux server, images on GHCR | Automatic HTTPS and the simplest supported install. |
| CI | GitHub Actions in `.github/workflows/` | The repo lives on GitHub. |

### Where this spec changes the design spec

When this spec and `docs/design/spec.md` or `docs/design/agent-rules.md` disagree on these points, this spec wins.

| Topic | Design spec says | This spec says |
|---|---|---|
| Job queue | Wolverine or a `SKIP LOCKED` queue (open) | Orvano's own `SKIP LOCKED` queue and outbox |
| Valkey | Listed for cache, pub/sub and rate limits | Not in v1 code, not in Compose. Rate limits use ASP.NET Core's limiter |
| AppHost services | Postgres, Valkey, storage and all processes | Postgres 17, `Orvano.Host` and `Orvano.Executor`. No Valkey, no MinIO |
| Password hashing | Identity password hasher (stack table) and Argon2id (Auth section) | Argon2id |
| Function sandbox | containerd (stack table) and Docker through a socket proxy (open decisions) | Docker through `docker-socket-proxy` |
| Solution file | `server/Orvano.sln` | `Orvano.slnx` at the repo root, holding every .NET project |
| Test command | `dotnet test server` (agent rules) | `dotnet test Orvano.slnx` at the repo root |
| Shared server code | Not in the layout | New `server/src/Orvano.Infrastructure/` |
| Code shared with the CLI | Not in the layout | New `server/src/Orvano.Wire/`, MIT licensed |
| Module projects | All ten drawn in the layout | Each is created by its first feature |
| JavaScript packages | Not stated | One pnpm workspace at the repo root |

### Repository layout

Entries marked `later` are not created by the M0 scaffold. The feature that first needs one creates it.

```
orvano/
  Orvano.slnx                  # every .NET project in the repo
  global.json                  # pins the .NET 10 SDK
  Directory.Build.props        # net10.0 default, nullable on, implicit usings on, <Version>
  Directory.Packages.props     # central NuGet package versions
  NuGet.config                 # nuget.org only, with package source mapping
  package.json                 # private root, pins pnpm in "packageManager", Node in "engines"
  pnpm-workspace.yaml          # console, website, later sdks/ts/* and cli/npm
  .nvmrc                       # the Node.js version used to build the console and docs
  .config/dotnet-tools.json    # later: dotnet-ef, with the first DbContext
  contract/                    # openapi.json, events.yaml, errors.yaml (feature 9 fills it)
  server/
    src/
      Orvano.AppHost/          # Aspire, contributors only
      Orvano.ServiceDefaults/  # OpenTelemetry defaults
      Orvano.Host/             # the orvano container, wires everything
      Orvano.Executor/         # the orvano-executor container
      Orvano.Wire/             # IDs, error shape, event payloads, JSON context (MIT)
      Orvano.Contracts/        # interfaces modules use to talk to each other, events
      Orvano.Infrastructure/   # shared server building blocks
      Modules/                 # later: Orvano.Modules.<Name>, one per feature area
    tests/
      Orvano.ArchitectureTests/  # reference rules, from M0
      Orvano.UnitTests/          # later, with the first unit test
      Orvano.IntegrationTests/   # later, with the first Testcontainers test
      Orvano.ContractTests/      # later, with feature 9
  console/                     # Next.js static export, npm name @orvano/console (private)
  cli/
    Orvano.Cli/                # the Native AOT binary
    Orvano.Cli.Tests/          # later, with feature 36
    npm/                       # later: the @orvano/cli wrapper (feature 73)
  website/                     # Astro Starlight, npm name @orvano/website (private)
  deploy/                      # Compose file, Caddyfile, Dockerfiles (feature 4 fills it)
  sdks/                        # later: ts/orvano, dart/orvano, dart/orvano_flutter, dotnet/Orvano.Sdk
  tools/                       # later: Orvano.SdkGen (feature 17)
  runtimes/                    # later: function base images (feature 55)
  .github/workflows/
```

The ten module names stay as the design spec lists them: `Projects`, `Auth`, `Database`, `Storage`, `Realtime`, `Functions`, `Webhooks`, `Sites`, `Email`, `Ai`, each as `server/src/Modules/Orvano.Modules.<Name>/`.

### Project reference rules

These rules are the architecture. "May reference" is an allow list of repo projects. "Must never reference" also names package families, counted even when they arrive through another package.

| Project | May reference | Must never reference |
|---|---|---|
| `Orvano.Wire` | No repo project. Base class library and `System.Text.Json` only | ASP.NET Core (packages or the `Microsoft.AspNetCore.App` framework reference), EF Core, Npgsql |
| `Orvano.Contracts` | `Orvano.Wire` | ASP.NET Core, EF Core, Npgsql, any module |
| `Orvano.Infrastructure` | `Orvano.Wire`, `Orvano.Contracts` | Any module, `Orvano.Host` |
| `Orvano.ServiceDefaults` | No repo project. Aspire and OpenTelemetry packages only | Any other repo project |
| `Orvano.Modules.<Name>` | `Orvano.Wire`, `Orvano.Contracts`, `Orvano.Infrastructure` | Any other module, `Orvano.Host` |
| `Orvano.Host` | Everything under `server/src/` except `Orvano.AppHost` and `Orvano.Executor` | |
| `Orvano.Executor` | `Orvano.Wire`, `Orvano.ServiceDefaults` | `Orvano.Contracts`, `Orvano.Infrastructure`, any module (feature 54 may widen this) |
| `Orvano.Cli` | `Orvano.Wire` | Anything else under `server/` |
| `Orvano.AppHost` | `Orvano.Host`, `Orvano.Executor` as Aspire resources | |
| Test projects | Library projects they test. Never an executable project, with one exception: `Orvano.IntegrationTests` and `Orvano.ContractTests` may reference `Orvano.Host`, to start the real app in memory (added by [spec 0002](../0002-compose-stack-skeleton/index.md)) | |
| Any project not listed | Nothing under `server/src/` until a spec adds its row | |

`Orvano.Wire` sets `IsAotCompatible` to `true`, because the Native AOT CLI compiles it. Feature 3 gives `server/src/Orvano.Wire/` its own MIT `LICENSE` file.

**How the rules are checked.** `Orvano.ArchitectureTests` references no repo project. It reads every `.csproj` for `ProjectReference`, `PackageReference` and `FrameworkReference`, and each project's `obj/project.assets.json` (written by restore) for packages that arrive indirectly. Reading compiled assemblies alone is not enough: the compiler drops a reference the code never uses, so a forbidden reference would stay hidden until someone used it.

### Module rules

1. **Two entry points per module.** Each module exposes one method that registers its services and one that maps its endpoints. `Orvano.Host` calls both. Everything else in the module is `internal`.
2. **One schema, one `DbContext`, own migrations.** A module's EF Core migrations and its migrations history table live in its own schema.
3. **Startup migrates in a fixed order.** `Orvano.Host` takes a Postgres advisory lock (a lock that stops two startups from migrating at once), migrates `Orvano.Infrastructure` first, then each module in registration order, releases the lock, and only then starts hosted services.
4. **Modules talk through `Orvano.Contracts` only.** A module that offers something to others implements an interface from `Orvano.Contracts` and registers it. No mediator library, and no reading another module's tables.
5. **Side effects across modules travel as events.** One transaction never spans two modules' tables. A change in one module that must cause a change in another writes an outbox event, and the other module reacts to it. Feature 7 may add a shared unit of work (one transaction several modules write through) only if a real case needs it.
6. **Every state change writes its event in the same transaction.** One outbox writer in `Orvano.Infrastructure` takes the caller's open connection and transaction, so it works the same for EF Core and for raw Npgsql.
7. **Tenant SQL never goes through EF Core or the platform role.** It runs through Npgsql under the project's role with row level security on.
8. **Background work runs as hosted services in `Orvano.Host`.** The worker, the scheduler and realtime are `BackgroundService` classes in the one process. Tests may switch them off. Production has no such switch until scale out.
9. **`Orvano.Infrastructure` has its own `DbContext` and schema** for shared tables (outbox, jobs, Data Protection keys). Feature 7 names the schema.

### What the M0 scaffold contains

The target for `/develop stack & architecture`. It derives its own steps from this.

- The root files from the layout above, except entries marked `later`.
- Seven server projects that build: `Orvano.AppHost`, `Orvano.ServiceDefaults`, `Orvano.Host`, `Orvano.Executor`, `Orvano.Wire`, `Orvano.Contracts`, `Orvano.Infrastructure`. No module projects.
- `Orvano.Host` and `Orvano.Executor` use the Web SDK (`Microsoft.NET.Sdk.Web`). The others are class libraries, except the AppHost.
- `Orvano.AppHost`: a csproj AppHost whose `Sdk` attribute carries the pinned Aspire 13 version. It runs Postgres with an explicit `17` image tag, `Orvano.Host` and `Orvano.Executor`. The documented contributor command stays `dotnet run --project server/src/Orvano.AppHost`. The Aspire CLI is optional.
- `Orvano.ServiceDefaults`: OpenTelemetry setup only. It maps no endpoints. The Aspire template's `/health` and `/alive` are removed, because feature 4 adds `/healthz` and `/readyz`. ([Spec 0002](../0002-compose-stack-skeleton/index.md) later puts JSON logging, the `/healthz` mapping and the container health probe in this project.)
- `Orvano.ArchitectureTests`, with the reference rules above as real tests.
- `cli/Orvano.Cli/`: `AssemblyName` is `orvano`. It sets `PublishAot` but no `RuntimeIdentifier` (that is passed at publish time). It builds, publishes as Native AOT, and prints the version from `Directory.Build.props`.
- `console/`: a Next.js app set to static export with base path `/console`, which builds. The design system mapping is feature 5.
- `website/`: an Astro Starlight site that builds. Theming is feature 6.
- `contract/` and `deploy/`: each with a short README saying what lands there and which feature fills it.
- No `sdks/`, `tools/` or `runtimes/` folders yet.

Lint, format, analyzers, warnings as errors, commit hooks and CI checks belong to feature 2. Health endpoints, Dockerfiles, Compose and the logging setup belong to feature 4.

### Builds from a clean clone

The scope's "done when" for this feature means these commands pass on a fresh clone.

**Prerequisites**: the .NET 10 SDK, Node.js at the `.nvmrc` version, pnpm, and the platform's C toolchain for Native AOT (clang on Linux, Xcode command line tools on macOS, the MSVC build tools on Windows). Docker is needed to run the AppHost, not to build or to run the M0 tests.

**Getting pnpm**: recent Node.js releases no longer bundle Corepack (the tool that reads the pinned pnpm version), so install pnpm yourself with `npm install -g pnpm` or pnpm's standalone installer. pnpm then switches to the version in `packageManager` on its own.

```bash
dotnet build Orvano.slnx
dotnet test Orvano.slnx
dotnet publish cli/Orvano.Cli -c Release -r <your-platform-rid>
pnpm install --frozen-lockfile
pnpm -r build
```

The Native AOT publish is checked on your own platform only in M0. Building for every platform is feature 40.

### Version pins

The web check did not reach these, so they are not verified. `/develop` pins the current stable release at scaffold time and records it in the file named below.

| Tool | Floor from the design spec | Pinned in |
|---|---|---|
| .NET SDK | 10.0, latest patch | `global.json` |
| Aspire | Major 13, latest stable 13.x | The `Sdk` attribute of `Orvano.AppHost.csproj`, plus `Directory.Packages.props` for Aspire packages |
| PostgreSQL image | `17` tag | `Orvano.AppHost`, the Compose file (feature 4) and Testcontainers setups (first integration test) |
| `ModelContextProtocol.AspNetCore` | 2.x | `Directory.Packages.props`, when feature 39 adds it |
| xUnit | v3 | `Directory.Packages.props` |
| Node.js (for building the console and docs) | Current Active LTS | `.nvmrc` and `engines` in the root `package.json` |
| pnpm | Current stable | `packageManager` in the root `package.json` |
| Next.js, React, Tailwind, TanStack Query, Astro, Starlight | Current stable major | The package files and the one lockfile |

The Node.js version here is for building Orvano. The Node.js 22 function runtime in the design spec is a separate thing.

### Handed to later specs

This spec picks the approach. These features design the details, and each already has a scope row.

| Feature | What it must settle |
|---|---|
| 10. Event pipeline | Outbox and jobs table shapes, the lease and its timeout, retries and backoff, what happens to a job that keeps failing, cron parsing, cleanup, and polling versus `LISTEN/NOTIFY` wakeups. Three known hazards: ids from a sequence can commit out of order, so a reader asking for "after the last id" can skip a late row for good (realtime resume reads this order); granting tenant roles insert rights on the outbox would let one project forge events for another, so the recommended path writes outbox rows as the platform role inside the tenant transaction; and busy queue tables need cleanup and autovacuum tuning on a 2 vCPU server |
| 7. Platform data model | Schema names, table naming, the name of the `Orvano.Infrastructure` schema, and separate database logins for schema changes and for runtime |
| 8. API conventions | How the built in rate limiter and the idempotency store are configured |
| 20. Email + password sign in | Which Argon2id library and its cost settings |
| 5. Console shell | How a static export handles page addresses that contain project ids |
| 4. Compose stack skeleton and 54. Function sandbox | How `orvano` and `orvano-executor` talk to each other. Dockerfiles need the repo root as build context (or `pnpm deploy`) because of the shared solution and workspace |
| 40. Release pipeline | Overriding `<Version>` from the git tag, and Native AOT builds for every platform |

## Consequences

**Positive**:
- A self hoster runs Postgres and two Orvano containers. No broker, no cache server.
- One event path for every write, including tenant writes under row level security.
- Module boundaries are checked by tests from the first commit, not by review.
- The CLI stays small and MIT clean, because it only compiles `Orvano.Wire`.
- No licence risk from the queue. Everything in it is Orvano's own `Apache-2.0` code.

**Negative / tradeoffs**:
- You own the queue's correctness: leasing, retries, stuck jobs, event order and cleanup. A bug there can lose or repeat events, so feature 10 needs careful tests, including a restart in the middle of a job.
- No ready made jobs dashboard. Any view of queued and failed jobs is yours to build.
- Work across modules is eventually consistent (the second change happens a moment after the first), because it travels as events, not one transaction.
- The worker shares CPU with the API in one process. On the 2 vCPU minimum server, heavy background work can slow requests, so worker concurrency must be capped.
- `docs/design/spec.md` and `docs/design/agent-rules.md` are now out of date in the places listed above, until you update them.
- Argon2id needs a third party library, where the Identity hasher needed none.
- Two small shared projects (`Orvano.Wire` and `Orvano.Contracts`) where one would look simpler. The line between them must be kept.
- One solution means opening the repo loads every .NET project, and `dotnet test` at the root runs every test project.

**Neutral**:
- The repo tree does not show the full layout on day one. This spec is where to look.
- .NET 10 creates `.slnx` solution files by default, so the file is `Orvano.slnx`.

## Follow-up

- [ ] Run `git init` and create the public `OrvanoHQ/orvano` repo before `/develop stack & architecture` (already on your list in the scope).
- [ ] Update `docs/design/spec.md` and `docs/design/agent-rules.md` in the eleven places from *Where this spec changes the design spec*, so the documents never disagree.
- [ ] Confirm the version pins at scaffold time. The web check ran out of budget before it reached them.
- [ ] Feature 10 (event pipeline) designs the queue details and the three hazards listed in *Handed to later specs*. Before that spec is accepted, add a test plan for a crash in the middle of a job and for a late commit.
- [ ] `aspire` conventions are not yet in an `AGENTS.md`. They belong in the nested `server/AGENTS.md`. Note that the skill prefers the Aspire CLI, while this spec keeps `dotnet run` as the documented command.
- [ ] `dotnet-webapi`, `postgresql-best-practices` and `testcontainers-dotnet` conventions belong in the nested `server/AGENTS.md`.
- [ ] `dotnet-aot-compat` conventions belong in the nested `cli/AGENTS.md`, with a pointer from `server/AGENTS.md` for `Orvano.Wire`.
- [ ] `docker-compose-patterns` conventions belong in the nested `deploy/AGENTS.md`.
- [ ] `shadcn` and `playwright-cli` conventions belong in the nested `console/AGENTS.md`.
- [ ] The registry search listed `mcp-csharp-create` and `mcp-csharp-test`, but they were not in `dotnet/skills` when checked. Look again before feature 39 (MCP server v0).
- [ ] Optional MCP servers, yours to connect. Aspire: run `aspire agent init` once the AppHost exists. Playwright: `claude mcp add playwright npx @playwright/mcp@latest`. Two more to consider later: Next.js DevTools (Vercel) for the console and Astro Docs for the docs site.
- [ ] Community skills found but not installed, for you to read before trusting: `aaronontheweb/dotnet-skills@efcore-patterns`, `lombiq/tailwind-agent-skills@tailwind-4-docs`, `astrolicious/agent-skills@astro`, `tanstack-skills/tanstack-skills@tanstack-query`, `wshobson/agents@nextjs-app-router-patterns`, `currents-dev/playwright-best-practices-skill@playwright-best-practices`.
