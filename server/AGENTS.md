# Server

## Overview

The Orvano server: one .NET 10 program, built as separate modules, shipped as one image that starts as `api`, `worker`, `realtime`, or `migrate` (spec 0002). `Orvano.Core` is the shared kernel, `Orvano.Server` is the host. Product modules (`Orvano.Platform`, `Orvano.Auth`, ...) arrive as their scope rows are built.

## Key files

| File | Owns |
|---|---|
| `src/Orvano.Server/Hosting/OrvanoProgram.cs` | Role selection and startup |
| `src/Orvano.Server/Hosting/StartupChecks.cs` | Config validation; a role refuses to start on a bad value |
| `src/Orvano.Server/Modules/OrvanoModules.cs` | The explicit module list (no assembly scanning) |
| `src/Orvano.Core/Modules/IOrvanoModule.cs` | The module contract: services, API, work, realtime hooks |
| `src/Orvano.Core/Data/ProjectScope.cs` | The only code path allowed to `SET LOCAL ROLE p_<id>` |
| `src/Orvano.Core/Events/Outbox.cs`, `Jobs/JobQueue.cs` | Events and jobs written in the caller's transaction |
| `src/Orvano.Contract/` | Generated API records and route constants for every audience, plus the embedded `openapi.json` (spec 0001); never packed |
| `src/Orvano.Server/Hosting/ContractValidation.cs`, `ContractValidator.cs` | `Test` environment only: checks every `/v1` response against the contract |
| `src/Orvano.Server/Hosting/Problems.cs` | Problem details normalized to the contract's `Problem`, `Problems.Result` for handlers, and `X-Request-Id` on every response |
| `src/Orvano.Server/Hosting/OrvanoVersion.cs` | The server version (from `VERSION`) and `X-Orvano-Version` on every response, which SDKs compare to their own |
| `src/Orvano.Server/Hosting/ConsoleSessions.cs`, `OrvanoHeaders.cs` | The `/v1/console` rule (401 `console_session_required`) and the temporary auth names, defined only here |
| `src/Orvano.Server/Modules/TestingModule.cs` | The fixed answers of the test only operations; registered only in `Test` |
| `migrations/platform/` | Platform SQL migrations, embedded into the binary |
| `tests/Orvano.ModelDriftCheck/` | Fails when the EF model and the SQL migrations disagree |

## Commands

```bash
# Tests (Docker must be running; Testcontainers starts Postgres 18)
dotnet test --solution Orvano.slnx

# EF model drift check (needs an empty Postgres bootstrapped by deploy/postgres/initdb)
ORVANO_DB_ADMIN_URL="Host=localhost;Port=5432;Username=orvano_admin;Password=...;Database=orvano" dotnet run --project server/tests/Orvano.ModelDriftCheck
```

## Conventions

- A module is one csproj. Its public types live in its `Contracts` namespace; everything else is `internal sealed`.
- `Orvano.Server` (the host) has no public API: its types are `internal`, and `InternalsVisibleTo` exposes them to `Orvano.Server.Tests` and `Orvano.ModelDriftCheck` only. Every public member elsewhere needs an XML doc comment (CS1591 is an error).
- Inside a module, keep business rules in plain types with no ASP.NET, EF, or Npgsql references. Endpoints, event consumers, and job handlers are thin adapters around them.
- EF Core only for platform tables in schema `orvano`, mapped with fluent config in `OrvanoDbContext` (no attributes on domain types). Per project tables use raw Npgsql through `ProjectScope`.
- Module tables carry the module name as a prefix (`orvano.platform_projects`); the kernel's own tables (`orvano.events`, `orvano.jobs`, `orvano.schema_migrations`) have none. Migrations are `NNNN_<name>.sql` and are checksummed when applied, so never edit one after it merges; add a new file instead.
- API errors are problem details (`AddOrvanoProblems` in `Hosting/Problems.cs`); never let a raw exception message reach a client. A handler returns `Problems.Result(status, ErrorCode.X, detail)` with a generated error code and a short safe `detail`.
- `/v1` endpoints use the generated `Orvano.Contract` types, never handwritten request or response records: `v1.MapGet(HealthOperations.Get.Route, ...).WithName(HealthOperations.Get.Id)`. The endpoint name must be the operationId, or contract validation rejects the response.
- Tests: one Postgres container per run through `PostgresFixture`, one fresh database per test through `TestDatabase`. No database mocks.

## Gotchas

- Every event and job is written in the same transaction as the change it describes. Never write one outside that transaction.
- Test only routes (`/v1/test/*`, `/v1/console/test/*`) exist only in `Test`, because `OrvanoModules` adds `TestingModule` only there. Outside `Test` no console session is valid yet, so every console route answers 401 until the auth rows land.
- In the `Test` environment a response that breaks the contract (extra, missing, or mistyped field, undeclared 2xx status, unnamed endpoint) becomes a 500 `contract_violation`. `ORVANO_TEST_FIXTURES` is refused outside `Test`.
- `Orvano.Contract` embeds `contract/dist/openapi.json`, so `deploy/server.Dockerfile` copies that file too; a new server project also needs its csproj copied before restore there.
- Every job handler must be idempotent, and no consumer may rely on event order. A consumer that throws is retried later through the `events.redispatch` job.
- Consumers stay small and do no IO: a hanging consumer still stalls the dispatcher.
- `orvano_app` never holds DDL rights. Only `migrate` and `worker` receive `ORVANO_DB_ADMIN_URL`.
- Changing a platform table means a new SQL migration AND the matching EF model change, or the drift check fails in CI.

## Agent skills

- [testcontainers-integration-tests](../.claude/skills/testcontainers-integration-tests/): `aaronontheweb/dotnet-skills`, xUnit integration tests against real Postgres in Docker

## Related specs

- [0002 Stack and architecture](../docs/specs/0002-stack-architecture/index.md) (roles, Postgres layout, events, invariants)
- [0001 API contract and SDK pipeline](../docs/specs/0001-api-contract-sdk-pipeline/index.md) (generated `Orvano.Contract` types)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
