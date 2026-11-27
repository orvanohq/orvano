# Server

## Overview

The .NET side of Orvano: the `orvano` host (API, realtime, worker, scheduler, MCP, console files in one process), the `orvano-executor` for sandboxed code, the shared libraries, the contributor AppHost and the tests. Module projects appear under `src/Modules/` only when their first feature lands.

## Key files

| File | Owns |
|---|---|
| `src/Orvano.Host/Program.cs` | Wires every module into the one `orvano` process |
| `src/Orvano.Executor/Program.cs` | The executor process (sandbox arrives with feature 54) |
| `src/Orvano.Wire/` | IDs, error shape, event payloads, JSON context. MIT, AOT safe, shared with the CLI |
| `src/Orvano.Contracts/` | Interfaces and events modules use to talk to each other |
| `src/Orvano.Infrastructure/` | Shared building blocks: outbox writer, jobs, Data Protection keys |
| `src/Orvano.ServiceDefaults/Extensions.cs` | OpenTelemetry setup only, maps no endpoints |
| `src/Orvano.AppHost/AppHost.cs` | Contributor orchestration: Postgres 17, `orvano`, `orvano-executor` |
| `tests/Orvano.ArchitectureTests/ReferenceRules.cs` | The project reference rules from spec 0001, as code |

## Commands

```bash
dotnet run --project server/src/Orvano.AppHost   # or: aspire start --apphost server/src/Orvano.AppHost/Orvano.AppHost.csproj
dotnet test --solution Orvano.slnx               # run from the repo root
```

## Conventions

- Reference rules (spec 0001) are enforced by `Orvano.ArchitectureTests`: Wire references no repo project and no ASP.NET Core, EF Core or Npgsql; Contracts only Wire; Infrastructure only Wire and Contracts; a module only Wire, Contracts and Infrastructure; the Executor only Wire and ServiceDefaults; only the Host wires modules. A new kind of project needs a spec row and a matching case in `ReferenceRules.cs`.
- A module is `src/Modules/Orvano.Modules.<Name>/` and exposes exactly two public entry points: one that registers services, one that maps endpoints. Everything else is `internal`.
- One schema, one `DbContext` and its own migrations per module. The Host migrates under a Postgres advisory lock: Infrastructure first, then modules in registration order, then hosted services start.
- One transaction never spans two modules. Cross module side effects travel as outbox events; the outbox writer takes the caller's open connection and transaction (EF Core or raw Npgsql alike).
- Background work (worker, scheduler, realtime) runs as `BackgroundService` classes in the Host.
- Package versions live only in the root `Directory.Packages.props`; a `.csproj` lists `PackageReference` without a version. Add every new project to `Orvano.slnx`.
- Tests use xUnit v3 and real Postgres through Testcontainers, never a fake database. Test projects reference library projects only, never an executable.

## Gotchas

- `Orvano.AppHost.csproj` carries the Aspire version in its `Sdk` attribute; keep it equal to `AspireVersion` in `Directory.Packages.props`. Aspire stays on major 13 (17.0 exists; it makes the Aspire CLI mandatory).
- The AppHost has the Aspire CLI bundle on: `dotnet run` uses an `aspire` on PATH, or fetches the matching CLI through `dnx`.
- xunit.v3 4.x runs only on Microsoft.Testing.Platform (`global.json` sets the runner), so `dotnet test` needs `--solution`, not a positional `.slnx`.
- The architecture tests read each project's `obj/project.assets.json`; restore before running them on their own.
- MSBuild XML comments cannot contain `--`, so do not paste command flags into `.csproj` comments.

## Agent skills

- [aspire](../.claude/skills/aspire/): `microsoft/aspire-skills`, AppHost wiring and lifecycle (`aspire start`, `aspire describe`, `aspire wait`); this repo keeps `dotnet run` as the documented command
- [dotnet-webapi](../.claude/skills/dotnet-webapi/): `dotnet/skills`, minimal API endpoints, OpenAPI metadata, error handling
- [postgresql-best-practices](../.claude/skills/postgresql-best-practices/): `microsoft/postgres-skills`, schema, indexes, RLS, JSONB, pooling
- [testcontainers-dotnet](../.claude/skills/testcontainers-dotnet/): `testcontainers/claude-skills`, integration tests against a real Postgres container
- [dotnet-aot-compat](../.claude/skills/dotnet-aot-compat/): `dotnet/skills`, keep `Orvano.Wire` trim and AOT safe (the CLI compiles it)

MCP servers: Aspire (recommended, `aspire agent init --mcp`)

## Related specs

- [0001 Stack and architecture](../docs/specs/0001-stack-architecture/index.md)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
