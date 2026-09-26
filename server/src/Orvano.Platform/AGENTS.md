# Orvano.Platform

## Overview

The Platform module (spec 0003): install settings and admins, orgs, memberships, projects, API keys, and platforms, with the jobs that provision and purge projects. Other modules reach it only through `Contracts/`. Row 7 built it; row 8 (Auth) calls its sign up hooks and key verifier, and row 15 adds invitations.

## Layout

| Folder | Owns |
|---|---|
| `Contracts/` | The only public types: `IProjectDirectory`, `IApiKeyVerifier`, `IConsoleAccess`, `IConsoleSignupPolicy`, `IConsoleAccountCreated`, `IConsoleAccountGuard` and their records |
| `Domain/` | Business rules as plain types: project IDs, lifecycles, the permission matrix, the owner rule, key secrets, web origin patterns, platform identifiers, names |
| `Application/` | Use cases (`OrgService`, `ProjectService`, `ApiKeyService`, `PlatformService`, `InstallService`, `ConsoleAccounts`, `PlatformDirectory`), `PlatformStore`, `Outcome`/`Failure`, `PlatformEvents` |
| `Data/PlatformDbContext.cs` | EF Core mapping of the `platform_` tables; internal, checked by the drift check |
| `Endpoints/` | Thin console endpoints and the row to contract model mapping (`ApiMapping`) |
| `Jobs/` | `platform.project.provision`, `platform.project.purge`, `platform.org.purge`, `platform.remove_memberships`, all on queue `platform` |
| `Fixtures/PlatformFixtures.cs` | `Test` only seeding of fixture projects and keys, through the same code as the console |

## Conventions

- Every write goes through `PlatformStore.WriteAsync`: one connection and transaction, an EF context bound to it, and the Npgsql transaction for `Outbox` and `JobQueue`. It commits only when the use case returns success, so a `Failure` rolls back everything.
- Use cases return `Outcome<T>` (a value or a `Failure` with a `FailureKind` and a contract `ErrorCode`); they know no HTTP. `ApiMapping.Problem` maps the kind to a status.
- Timestamps come from the database clock (`UnitOfWork.NowAsync`), so they agree with the jobs' `now()` checks.
- State transitions are one conditional `ExecuteUpdateAsync ... WHERE status = expected` that checks the row count, never a read then a write. Org changes lock the org `FOR UPDATE`; project create, restore, and changes lock it `FOR SHARE` (`UnitOfWorkExtensions`). Locking queries use `FromSql(...).ToListAsync()` so EF never wraps the lock clause.
- Every change writes its `platform.*` event through `PlatformEvents.WriteAsync`: affected IDs, the `Actor` (a console user, or `system` in jobs), and changed field names. Never a secret, hash, or token.
- Console access: a caller who is not a member of the org gets 404 (`project_not_found` or `not_found`), so existence never leaks; a member whose role lacks the action gets 403 `forbidden`. Project scoped endpoints read `X-Orvano-Project`; a missing header is 400.
- Lists are keyset paged on `(created_at, id)` with `PageCursor` from `Orvano.Core.Paging`.
- `ORVANO_DELETE_GRACE_DAYS` (0 to 90, default 7) is read in `PlatformModule.ConfigureServices`, so every role refuses to start on a bad value.

## Gotchas

- Jobs that issue DDL use the `OrvanoDb.Admin` data source, only in the worker. Each locks the project row first and is idempotent; on its last failing attempt it records `failed` or `purge_failed_at` in a separate transaction, then rethrows.
- The Contract's generated `OrgRole`, `ProjectStatus`, and `PlatformType` share names with this module's own types, and the contract model `Platform` clashes with this namespace: alias them (`using Api = Orvano.Contract;`).
- Invitations are row 15's: until then an invite token admits no one, and member management endpoints don't exist.
- The `auth.user.deleted` consumer reads the console user ID from the event's `subject` and only acts when `project_id` is `console`.

## Tests

`server/tests/Orvano.Server.Tests/Platform/`: domain unit tests, `PlatformHarness` (the module over a fresh database, running queued jobs through the registered handlers on demand), lifecycle and key tests on real Postgres, and HTTP tests against the real binary in `Test`.

## Related specs

- [0003 Platform data model](../../../docs/specs/0003-platform-data-model/index.md) (with `verify.md`)

_Drafted by /sync from the introducing change, worth a quick human pass._
