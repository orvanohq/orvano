# Server

The .NET 10 modular monolith: one executable, `orvano`, built from `Orvano.Host`. Rules live in [spec 0002](../docs/specs/0002-dotnet-backend-practices/index.md) (code), [spec 0007](../docs/specs/0007-stack-architecture/index.md) (runtime, install, image), and [spec 0006](../docs/specs/0006-security-baseline/index.md) (security). Cite rule IDs in reviews.

## Commands

Run from `server/`: `global.json` here pins the SDK and picks the Microsoft.Testing.Platform runner, so `dotnet test --solution` fails from the repo root. The root scripts `pnpm check:dotnet` and `pnpm test` change into `server/` for you. Integration tests and local runs need Docker (or Podman) running.

- Build: `dotnet build Orvano.slnx`. CI strictness (warnings as errors): `dotnet build Orvano.slnx -p:CI=true --no-incremental`
- Format: `dotnet format Orvano.slnx` (whitespace, style, analyzer fixes); CI runs it with `--verify-no-changes`. The pre-commit hook formats staged `.cs` files (whitespace only)
- Coverage: `dotnet test --solution Orvano.slnx --coverage --coverage-output-format cobertura --results-directory "$PWD/TestResults"`
- Test everything: `dotnet test --solution Orvano.slnx` (starts one `postgres:18.6` container). One project: `dotnet test --project tests/<Project>`
- Dev database: `docker compose -f ../deploy/compose.dev.yaml up -d --wait` (trust auth on `127.0.0.1:5432`, user `orvano`)
- Dev root key, once: `dotnet user-secrets set ROOT_KEY "$(openssl rand -base64 32)" --project src/Orvano.Host`
- Migrate, then run on `http://localhost:5080`: `dotnet run --project src/Orvano.Host -- migrate`, then `dotnet run --project src/Orvano.Host`
- Password hash benchmark (HSH-2): `dotnet run -c Release --project benchmarks/Orvano.Benchmarks -- --filter '*PasswordHashing*'`

## Layout

- `src/Orvano.Host`: composition, middleware, and the commands in `Commands/` (`serve`, `migrate`, `init`, `healthcheck`, `admin selftest`). No feature code.
- `src/Orvano.SharedKernel`: config sources and helpers (`Configuration/`), `LogEventIds`, the shared EF Core data conventions (`Data/`, spec 0011), the access control core (`Access/`, spec 0012), and `Error`, `Result`, and `ErrorProblems`. References no other Orvano project.
- `src/Orvano.Infrastructure`: root key, TLS options, and later the job runner, outbox, signals, and file stores (spec 0007 REP-2). References only SharedKernel; modules never reference it (architecture test).
- `src/Orvano.Modules.<Name>`: one project per module. Auth holds `IPasswordHasher` (Argon2id via NSec) in `Contracts/`.
- `tests/`: `<Project>.Tests` for unit tests, `Orvano.Host.IntegrationTests` (HTTP and CLI), `Orvano.ArchitectureTests`, and shared helpers in `Orvano.Testing`.

## Conventions

- Configuration comes from `ORVANO_<SECTION>__<KEY>` variables, and any key also takes a `_FILE` form. Options classes have a `Section` constant, a source generated `[OptionsValidator]`, and XML docs. Bad config fails startup with a message naming the variable (`ConfigurationNames`, `OptionsErrors`).
- Checks that must pass before serving (PostgreSQL 18, no pending migrations, hasher self test) go in `Startup/StartupChecks.cs`. New module `DbContext`s go in `Database/MigrationOrder.cs`.
- A module `DbContext` calls `UseOrvanoConventions(schema, services)` in place of its own `UseNpgsql`: it sets the schema, snake case names, `<schema>.__ef_migrations_history`, the retry, and the `TimestampInterceptor` (spec 0011 AC-3). Pass Npgsql tweaks through its `npgsql` callback; don't register your own `IModelCacheKeyFactory`. Read [spec 0011](../docs/specs/0011-platform-data-model/index.md) before adding a table: its catalog already designs it.
- Entities derive from `Entity` (app made uuid v7 `Id`; an `OwnsMany` item too, keyed with `HasKey(x => x.Id)`), and implement `ITimestamped` and `IVersioned` (xmin) where needed. Store enums with `HasTextEnum()` (snake case text plus a `ck_<table>_<column>` CHECK). Point into another module's schema only with `HasAnchor(Anchor.X, fk, onDelete)`. Mark every text, bytes, address, or JSON property `[DataClass(...)]`; ciphertext is an `EncryptedValue` marked `DataClass.Secret` with a `Purpose`. The rules in `tests/Orvano.ArchitectureTests/Data/` fail the build otherwise.
- An endpoint (or a whole `MapGroup`) declares its access check with `.RequireScope(...)` (server routes), `.RequireClientApi(...)` (client routes), or `.RequireConsolePermission(...)` (control plane routes), from `Access/AccessEndpointFilters.cs`. Don't check access in a handler or your own endpoint filter: these add endpoint metadata that `UseAccessRequirements()` enforces before binding and validation, so a refused caller gets 403 or 404, never a 400 with field names (spec 0012 DEN-5). Decide grants with `AccessEvaluator`, and filter rows with `WhereGranted` (in `Data/`, over a GIN indexed `text[]` column) and the caller's `MatchTokens(action)`.
- Every endpoint declares exactly one kind of requirement or `.AllowPublicAccess()`, with a comment saying why it is public (as on the health routes in `ServeCommand`). Never use `AllowAnonymous`: it does not count as a declaration. At startup `AccessEndpointCheck` fails the host and names each endpoint that declares nothing, mixes kinds, or pairs a requirement with `AllowPublicAccess()`; an undeclared endpoint added after startup answers 404 (spec 0012 EPT-3 to EPT-5).
- A credential resolver implements `ICallerResolver` (returning a `CallerResolution`: `NotPresent`, `Resolved`, or `Failed`), takes its `Order` from `CallerResolverOrder`, and is registered with `services.AddCallerResolver<T>()`. `UseAccessRequirements()` runs the chain; a resolver never sets `ICallerContext` or `IProjectContext` itself (spec 0012 RSV-1 to RSV-4).
- Read the caller from `ICallerContext` and the project from `IProjectContext`. Only the access layer sets them (spec 0012 PRJ-3); the rules in `tests/Orvano.ArchitectureTests/Access/` fail the build if another type implements or sets them, or if `Access/` uses an Npgsql, EF Core, or SQL type.
- A failure is an `Error` (built with its factories, such as `Error.NotFound(code, message)`), returned in a `Result` or `Result<T>`. An endpoint answers it with `error.ToProblem()`, which picks the status from `ErrorKind`, writes problem+json, and sets `Retry-After` on a 429 (spec 0002 ERR-4). Shared access errors live in `AccessErrors`.
- A module that maps endpoints calls the plain `services.AddValidation()` in its `Add<Module>Module`; without it, its request types are never validated (spec 0002 VAL-1). Each endpoint's invalid body test (TST-4) catches a missing call.
- Log through `[LoggerMessage]` methods in `<Module>Log`, with event IDs from that module's block in `LogEventIds`.
- Package versions live only in `Directory.Packages.props`. Every project has a `packages.lock.json` covering `linux-x64` and `linux-arm64`. After changing packages, run `dotnet restore Orvano.slnx --force-evaluate` and commit the lock files: the image restores in locked mode. A new `src/` project also needs its `.csproj` and lock file added to the restore layer in `deploy/Dockerfile`.
- The image builds with a newer SDK (10.0.402) than an older local SDK, and it can raise analyzer warnings yours misses. Keep the local SDK current.
- Style rules raised to warning (file scoped namespaces, braces, `var` only when the type is apparent, `_camelCase` private fields, unused usings) live in `.editorconfig`; they fail the CI build (spec 0008 FMT-6).
- Banned APIs are in `BannedSymbols.txt` (every project) and `src/<Project>/BannedSymbols.<Name>.txt` (one project, for example Auth's). Each line names its rule and the replacement. The one allowed use gets `#pragma warning disable RS0030 // <rule>: <reason>`, as in `MigrateCommand` (spec 0008 LNT-5, LNT-6).
- Test projects (`OutputType` Exe) get the coverage package and `XUNIT_GENERATED_DISABLE_WARNINGS` from `tests/Directory.Build.props`. Key a condition there on `OutputType`, not `IsTestProject`, which isn't known yet at restore.

## Gotchas

- `UseConsoleFiles` (static files) must run before `UseRouting`, or the `/console/{**path}` fallback swallows every asset.
- In `ServeCommand`, `UseAccessRequirements()` runs right after `UseRouting` and resolves the caller itself. Register a new credential (the API key and session resolvers of features 13 and 14) with `AddCallerResolver<T>()`, not as middleware between the two: the access layer is the only place that sets the caller.
- ASP.NET Core's forwarded headers middleware trusts everyone when both known lists are empty. `UseForwardedHeaders` is registered only when `ORVANO_TRUSTED_PROXIES` has entries; keep it that way.
- System.CommandLine 2 has no parser for `Uri`. Take a string option and validate it with `Uri.TryCreate`.
- `init`'s file and folder modes follow spec 0007 INS-5 (folder 0700, `root.key` 0644, `.env` 0600), in that order of steps.

## Tests

- Names are `<Behavior>_<expected>_when_<condition>` (CA1707 is off for `tests/` in `.editorconfig`). Tag rule coverage with `[Trait("Spec", "NNNN:RULE-ID")]`.
- `PostgresFixture` is an assembly fixture, pinned to the same tag as the compose files. A test class that needs its own database calls `CreateDatabaseAsync(prefix)` and `ConnectionString(name)` on it; never start a second container (TST-5). `OrvanoApiFactory` passes settings with `UseSetting`, which reaches config before the host builds. `ConsoleBuild` fakes `wwwroot/console`.
- Test startup failures and CLI behavior through `OrvanoCli` (the real process). `WebApplicationFactory` cannot observe them, because `serve` catches startup errors and disposes the app.

## Agent skills

Load only what the task needs. Spec 0002 wins where a skill disagrees (GEN-0).

- [dotnet-webapi](../.claude/skills/dotnet-webapi/): `dotnet/skills`, Minimal API endpoints (not its interface per service or `Middleware/` advice)
- [ef-core](../.claude/skills/ef-core/): `github/awesome-copilot`, EF Core models and migrations
- [csharp-xunit](../.claude/skills/csharp-xunit/): `github/awesome-copilot`, xUnit tests (use Shouldly, not its assertion picks)
- [testcontainers](../.claude/skills/testcontainers/): `wshaddix/dotnet-skills`, PostgreSQL containers in tests
- [caching](../.claude/skills/caching/): `codewithmukesh/dotnet-claude-kit`, HybridCache (no Redis backplane, spec 0007 CCH-1)
- [opentelemetry](../.claude/skills/opentelemetry/): `codewithmukesh/dotnet-claude-kit`, traces and metrics (no Aspire ServiceDefaults, spec 0007 DEV-2)
- [dotnet-system-commandline](../.claude/skills/dotnet-system-commandline/): `wshaddix/dotnet-skills`, System.CommandLine 2 commands and options for `Commands/`
- Declined (2026-10-16): a BenchmarkDotNet skill

_Drafted by /sync from the introducing change, worth a quick human pass._
