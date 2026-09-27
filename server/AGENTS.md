# Server

## Overview

The Orvano server: one .NET 10 program, built as separate modules, shipped as one image that starts as `api`, `worker`, `realtime`, or `migrate` (spec 0002). `Orvano.Core` is the shared kernel, `Orvano.Server` is the host. Product modules (`Orvano.Platform`, `Orvano.Auth`, ...) arrive as their scope rows are built.

## Key files

| File | Owns |
|---|---|
| `src/Orvano.Server/Hosting/OrvanoProgram.cs` | Role selection and startup |
| `src/Orvano.Server/Hosting/StartupChecks.cs` | Config validation; a role refuses to start on a bad value |
| `src/Orvano.Server/Install/` | `orvano install` (spec 0006): plain, unit tested rules (`EnvFile`, `InstallSecrets`, `PgTuning`, `VersionRule`, `DomainRule`, `EmailRule`, `InstallPlan`) behind `InstallCommand`, which writes every file in the install directory and never talks to Docker; see [deploy/install/AGENTS.md](../deploy/install/AGENTS.md) |
| `src/Orvano.Server/Hosting/SetupStatusCommand.cs` | `orvano setup-status`, run inside `api`: prints `required` or `done` |
| `src/Orvano.Server/Modules/OrvanoModules.cs` | The explicit module list (no assembly scanning) |
| `src/Orvano.Core/Modules/IOrvanoModule.cs` | The module contract: services, API, work, realtime hooks |
| `src/Orvano.Core/Data/ProjectScope.cs` | The only code path allowed to `SET LOCAL ROLE p_<id>` |
| `src/Orvano.Core/Events/Outbox.cs`, `Jobs/JobQueue.cs` | Events and jobs written in the caller's transaction |
| `src/Orvano.Contract/` | Generated API records and route constants for every audience, plus the embedded `openapi.json` (spec 0001); never packed |
| `src/Orvano.Server/Hosting/ContractValidation.cs`, `ContractValidator.cs` | `Test` environment only: checks every `/v1` response against the contract |
| `src/Orvano.Server/Hosting/Problems.cs` | Problem details normalized to the contract's `Problem`, `Problems.Result` for handlers, and `X-Request-Id` on every response |
| `src/Orvano.Server/Hosting/OrvanoVersion.cs` | The server version (from `VERSION`) and `X-Orvano-Version` on every response, which SDKs compare to their own |
| `src/Orvano.Server/Hosting/ConsoleSessions.cs`, `src/Orvano.Core/Http/OrvanoHeaders.cs` | The `/v1/console` rule (401 `console_session_required`), which also sets the `ConsoleUser` endpoints act as, and the header names (temporary auth names included), defined only there |
| `src/Orvano.Server/Hosting/TestFixtures.cs` | `Test` only fixtures: console sessions (each a stable console user until row 8), plus projects and API keys the api seeds at startup |
| `src/Orvano.Server/Modules/TestingModule.cs` | The fixed answers of the test only operations; registered only in `Test` |
| `src/Orvano.Platform/` | The Platform module: orgs, projects, API keys, platforms, install settings, and their jobs (spec 0003); see its AGENTS.md |
| `src/Orvano.Core/Http/`, `src/Orvano.Core/Paging/PageCursor.cs` | What module endpoints share: `ApiProblem.Result`, `ConsoleUser`, and the keyset cursor and limit rule for list operations |
| `migrations/platform/` | Platform SQL migrations, embedded into the binary |
| `tests/Orvano.ModelDriftCheck/` | Fails when the EF model and the SQL migrations disagree |

## Commands

```bash
# Tests (Docker must be running; Testcontainers starts Postgres 18)
dotnet test --solution Orvano.slnx

# EF model drift check (needs an empty Postgres bootstrapped by deploy/compose/initdb)
ORVANO_DB_ADMIN_URL="Host=localhost;Port=5432;Username=orvano_admin;Password=...;Database=orvano" dotnet run --project server/tests/Orvano.ModelDriftCheck
```

## Conventions

- A module is one csproj. Its public types live in its `Contracts` namespace; everything else is `internal sealed`.
- `Orvano.Server` (the host) has no public API: its types are `internal`, and `InternalsVisibleTo` exposes them to `Orvano.Server.Tests` and `Orvano.ModelDriftCheck` only. Every public member elsewhere needs an XML doc comment (CS1591 is an error).
- Inside a module, keep business rules in plain types with no ASP.NET, EF, or Npgsql references. Endpoints, event consumers, and job handlers are thin adapters around them.
- EF Core only for platform tables in schema `orvano`, mapped with fluent config (no attributes on domain types): the kernel's tables in `OrvanoDbContext`, each module's in its own internal context (`PlatformDbContext`). Per project tables use raw Npgsql through `ProjectScope`.
- Module tables carry the module name as a prefix (`orvano.platform_projects`); the kernel's own tables (`orvano.events`, `orvano.jobs`, `orvano.schema_migrations`) have none. Migrations are `NNNN_<name>.sql` and are checksummed when applied, so never edit one after it merges; add a new file instead.
- API errors are problem details (`AddOrvanoProblems` in `Hosting/Problems.cs`); never let a raw exception message reach a client. A handler returns `Problems.Result(status, ErrorCode.X, detail)` (in a module, `ApiProblem.Result` from `Orvano.Core.Http`) with a generated error code and a short safe `detail`.
- `/v1` endpoints use the generated `Orvano.Contract` types, never handwritten request or response records: `v1.MapGet(HealthOperations.Get.Route, ...).WithName(HealthOperations.Get.Id)`. The endpoint name must be the operationId, or contract validation rejects the response.
- Tests: one Postgres container per run through `PostgresFixture`, one fresh database per test through `TestDatabase`. No database mocks.

## Gotchas

- Every event and job is written in the same transaction as the change it describes. Never write one outside that transaction.
- Test only routes (`/v1/test/*`, `/v1/console/test/*`) exist only in `Test`, because `OrvanoModules` adds `TestingModule` only there. Outside `Test` no console session is valid yet, so every console route answers 401 until the auth rows land.
- In the `Test` environment a response that breaks the contract (extra, missing, or mistyped field, undeclared 2xx status, unnamed endpoint) becomes a 500 `contract_violation`. `ORVANO_TEST_FIXTURES` is refused outside `Test`.
- `Orvano.Contract` embeds `contract/dist/openapi.json`, so `deploy/server.Dockerfile` copies that file too; a new server project also needs its csproj copied before restore there.
- `Orvano.Server` embeds `deploy/compose/docker-compose.yml` and `deploy/compose/initdb/10-orvano-roles.sh`, which `orvano install` writes to every install, so editing either changes what the next release installs. `install`, `setup-status`, and `healthcheck` are checked before role selection in `OrvanoProgram`, so they run with no `ORVANO_ROLE`.
- In `Production`, `api` refuses to start while no install admin exists and `ORVANO_SETUP_TOKEN` is unset; a malformed token is refused in every environment (spec 0006, AC-21).
- Every job handler must be idempotent, and no consumer may rely on event order. A consumer that throws is retried later through the `events.redispatch` job.
- Consumers stay small and do no IO: a hanging consumer still stalls the dispatcher.
- `orvano_app` never holds DDL rights. Only `migrate` and `worker` receive `ORVANO_DB_ADMIN_URL`.
- Changing a platform table means a new SQL migration AND the matching EF model change, or the drift check fails in CI. A new module's `DbContext` must be added to the drift check's context list in `tests/Orvano.ModelDriftCheck/Program.cs`.
- `orvano_admin` can't `SET ROLE` to a role it creates (Postgres 16+ default), so the provision job grants each `p_<id>` to it with `INHERIT FALSE, SET TRUE`, and the purge drops the schema as `p_<id>`. Never grant `INHERIT`.
- Tests that change the schema version use `TestDatabase.SetSchemaVersionAsync` relative to `PlatformSchema.ExpectedVersion`, never a fixed number, so each new migration doesn't break them.

## Agent skills

- [testcontainers-integration-tests](../.claude/skills/testcontainers-integration-tests/): `aaronontheweb/dotnet-skills`, xUnit integration tests against real Postgres in Docker
- [dotnet-cryptography](../.claude/skills/dotnet-cryptography/): `envoydev/claude-stack`, `System.Security.Cryptography` primitives (ECDsa, AesGcm, RandomNumberGenerator) for signing keys and envelope encryption
- [dotnet-api-security](../.claude/skills/dotnet-api-security/): `wshaddix/dotnet-skills`, ASP.NET Core API auth, JWT bearer, CORS, and rate limiting
- [dotnet-jwt-authentication](../.claude/skills/dotnet-jwt-authentication/): `ronnythedev/dotnet-clean-architecture-skills`, JWT issuing and validation in .NET (spec 0004 wins where it assumes a single issuer)
- [libsodium](../.claude/skills/libsodium/): `claude-dev-suite/claude-dev-suite`, libsodium and Argon2id, used through NSec for password hashing (security scan: medium risk, read it before relying on it)

## Related specs

- [0002 Stack and architecture](../docs/specs/0002-stack-architecture/index.md) (roles, Postgres layout, events, invariants)
- [0003 Platform data model](../docs/specs/0003-platform-data-model/index.md) (orgs, projects, keys, platforms, module contracts)
- [0001 API contract and SDK pipeline](../docs/specs/0001-api-contract-sdk-pipeline/index.md) (generated `Orvano.Contract` types)
- [0004 App user sign up, sign in, and sessions](../docs/specs/0004-app-user-auth/index.md) (`Orvano.Auth`, tokens, sessions, signing keys)
- [0006 Self host installer](../docs/specs/0006-self-host-installer/index.md) (`orvano install`, `setup-status`, the setup token)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
