# 0002. .NET backend practices for the Orvano server and .NET SDK

**Date**: 2026-10-15
**Status**: Accepted
**Updated**: 2026-10-16, PRF-1 allows ReadyToRun (spec 0007); VAL-1 requires `AddValidation()` in each module. 2026-10-18, LAY-3, DAT-1, and DAT-9 point to the built conventions of spec 0011 (`UseOrvanoConventions`, `IVersioned`). 2026-10-18, PRF-2 lists end user labels and resource grants as security related entries (spec 0012). 2026-10-20, DAT-6 allows one spliced identifier, a registered outbox schema (spec 0013 DSP-8). 2026-10-25, reconciled with spec 0014: API-1 points to PTH-1 and PTH-2; API-6 is code first (CTR-1); ERR-4 uses the final KND-1 map with `PreconditionFailed` 412, the PRB-1 body, and `Error.RetryAfter` (PRB-2); LIB-6 names the KND-2 table; LIB-7 names `Idempotency-Key`; LOG-6 paths stay (HLT-5); the feature 10 follow up is closed.

## Summary

This is the one playbook for writing .NET 10 code at Orvano, for both the server and the `Orvano.Server` NuGet library. The server is a modular monolith (one deployable app split into modules like Auth and Databases) with Minimal API endpoints, plain handler classes, EF Core for Orvano's own tables, and raw Npgsql behind an engine adapter for the databases users create. Expected failures travel as a small Result type, telemetry goes through OpenTelemetry, configuration comes from `ORVANO_` environment variables, and tests use xUnit v3, Shouldly, and real PostgreSQL in containers. Every rule below has an ID so `/audit` can lift it into `AGENTS.md`, analyzers can enforce it, and reviews can cite it.

## Decision

**Chosen option**: Option 1: Document the standard and enforce it from the first line of code (analyzers, banned APIs, and architecture tests in CI; review for the rest).

Orvano's .NET code follows the numbered rules in `## Standard definition`; **MUST** rules fail the build or the review, **SHOULD** rules need a written reason to skip.

**Implementation skills**: `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `create-datadriven-aspnetcore` (`dotnet/skills`, `.claude/skills/create-datadriven-aspnetcore/`) · `optimizing-ef-core-queries` (`dotnet/skills`, `.claude/skills/optimizing-ef-core-queries/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `postgresql-optimization` (`github/awesome-copilot`, `.claude/skills/postgresql-optimization/`) · `postgresql-table-design` (`wshobson/agents`, `.claude/skills/postgresql-table-design/`) · `csharp-xunit` (`github/awesome-copilot`, `.claude/skills/csharp-xunit/`) · `testcontainers` (`wshaddix/dotnet-skills`, `.claude/skills/testcontainers/`) · `dotnet-best-practices` (`github/awesome-copilot`, `.claude/skills/dotnet-best-practices/`) · `csharp-expert` (`dotnet/skills`, `.claude/skills/csharp-expert/`) · `msbuild` (`dotnet/skills`, `.claude/skills/msbuild/`) · `analyzing-dotnet-performance` (`dotnet/skills`, `.claude/skills/analyzing-dotnet-performance/`) · `nuget-trusted-publishing` (`dotnet/skills`, `.claude/skills/nuget-trusted-publishing/`)

**This spec wins over any installed skill where they disagree** (rule **GEN-0**). Known conflicts: `dotnet-best-practices` says MSTest, FluentAssertions, and Moq (use **TST-1** instead); `dotnet-webapi` says an interface for every service and exception handlers in a `Middleware/` folder (use **API-7** and **ERR-4** instead); `create-datadriven-aspnetcore` can scaffold controllers (use **API-1**).

## Standard definition

### Scope and boundaries

Applies to every .NET project in the core monorepo's server tree and in the .NET SDK repo, tests and benchmarks included. Rules marked _shared_ apply to both, _server_ only to the server, _library_ only to the SDK.

Decided elsewhere, not here (this spec points to them and must not be read as deciding them):

| Topic                                                                                                                                                                                         | Owner                                                                                         |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| Wire format: JSON naming, error body shape, error code format, status mapping, pagination, API version header, API key header, idempotency keys, which SDK code is generated and by what tool | Feature 10, API contract & SDK generation                                                     |
| Service boundaries, background jobs, cache backing store (Redis or none), hosting, container images, local dev orchestration (including Aspire), monorepo top level folder names              | Feature 6, Stack & architecture (decided in [spec 0007](../0007-stack-architecture/index.md)) |
| Password hashing, token signing, secrets at rest, PII classification and log redaction, dependency vulnerability and license policy                                                           | Feature 5, Security baseline                                                                  |
| Platform entities, ID format, column types beyond the rules below                                                                                                                             | Feature 8, Platform data model                                                                |
| How the caller and project are resolved, permission checks, not found versus forbidden                                                                                                        | Feature 9, Access control model                                                               |
| Engine adapter interface shape                                                                                                                                                                | Features 15 and 16                                                                            |
| Formatter, lint and CI wiring, git hooks                                                                                                                                                      | Feature 7, Coding standards & tooling                                                         |
| NuGet ID prefix reservation, release process, versioning                                                                                                                                      | Feature 52, SDK publishing & versioning                                                       |

### Shared rules: language and build

- **GEN-0 (MUST)**: Where an installed agent skill, a tutorial, or generated scaffold disagrees with this spec, this spec wins. A deviation needs a comment `// Deviation <RULE-ID>: <reason>` at the spot and a mention in the PR.
- **GEN-1 (MUST)**: Target `net10.0` (C# 14, the SDK default). The server moves only between LTS releases (next: .NET 12, November 2027), within six months of its release. `global.json` pins the 10.0 SDK with `"rollForward": "latestFeature"` and sets `"test": { "runner": "Microsoft.Testing.Platform" }` so `dotnet test` runs xUnit v3 on Microsoft.Testing.Platform natively.
- **GEN-2 (MUST)**: Root `Directory.Build.props` sets `Nullable` enable, `ImplicitUsings` enable, `AnalysisLevel` latest-recommended, `EnforceCodeStyleInBuild` true, and `TreatWarningsAsErrors` true when `CI` is `true`. Rule severities change only in the root `.editorconfig`, never per file.
- **GEN-3 (MUST)**: The solution file is `.slnx`. Package versions live only in `Directory.Packages.props` (`ManagePackageVersionsCentrally` and `CentralPackageTransitivePinningEnabled` true); a `PackageReference` never carries `Version`.
- **GEN-4 (SHOULD)**: `RestorePackagesWithLockFile` true, and CI restores with `--locked-mode`, so a self hoster's build uses exactly the tested dependency graph. `NuGetAudit` stays on (feature 5 sets the failure level).
- **GEN-5 (MUST)**: A new third party package needs an OSI approved license compatible with Orvano's license and visible recent maintenance. Licenses: spec 0009 DEP-1 to DEP-5. "Maintained" means a release or commit within the last 12 months. Prefer what ships in the box. Never add a package under a commercial or source available license (today that rules out MediatR 13+, AutoMapper 15+, FluentAssertions 8+, NBomber).
- **GEN-6 (MUST)**: File scoped namespaces, namespace matches the folder path, one top level type per file (small private records next to their only user are fine).
- **GEN-7 (SHOULD)**: Types are `internal` unless another project needs them, and `sealed` unless built for inheritance. Use `record` for requests, responses, and value objects, `init` or `required` properties for immutability, and primary constructors for injected services.
- **GEN-8 (MUST)**: No `!` (null forgiving operator) outside tests unless a comment says why the value cannot be null. No `#nullable disable`.
- **GEN-9 (MUST)**: Read the clock only through an injected `TimeProvider`. Times are `DateTimeOffset` in UTC, normalized at the edge: a shared JSON converter in `Orvano.SharedKernel` turns every incoming `DateTimeOffset` to UTC (Npgsql refuses to write a non zero offset to `timestamptz`, so an unnormalized `+02:00` would become a 500). `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, and `DateTimeOffset.UtcNow` are banned APIs.
- **GEN-10 (MUST)**: Anything machine read (keys, IDs, headers, SQL, log properties) uses invariant culture and ordinal string comparison.
- **GEN-11 (MUST)**: Map between types by hand: a static `ToResponse()` method or a constructor next to the type, or project straight to the response in an EF `Select`. No mapping library.
- **GEN-12 (MUST)**: Register services with the built in container, explicitly, one line per service. No assembly scanning, no third party container.

### Shared rules: async and cancellation

- **ASY-1 (MUST)**: I/O is async from the endpoint down. Never block on async code: `.Result`, `.Wait()`, and `GetAwaiter().GetResult()` are banned. No `async void`.
- **ASY-2 (MUST)**: Every method that does I/O or may wait takes `CancellationToken cancellationToken` as its last parameter and passes it to every call it makes (CA2016 as error). In the server it has no default value; in the library it defaults to `default`. Endpoints get it from the request (bound to `HttpContext.RequestAborted`).
- **ASY-3 (MUST)**: The library calls `ConfigureAwait(false)` on every `await` (CA2007 on in the SDK repo only). The server never does (ASP.NET Core has no synchronization context).
- **ASY-4 (SHOULD)**: Return `Task` or `Task<T>`. Use `ValueTask` only on a measured hot path that usually completes at once, and never await one twice.
- **ASY-5 (MUST)**: No fire and forget work in request code (`Task.Run`, or unawaited tasks). Work that must outlive the request goes through `IJobQueue`, the PostgreSQL job queue of spec 0007 JOB-6.
- **ASY-6 (SHOULD)**: Stream large results with `IAsyncEnumerable<T>`. Use `await using` for async disposables.
- **ASY-7 (MUST)**: Set timeouts with a `CancellationTokenSource` built from the injected `TimeProvider` (`new CancellationTokenSource(timeout, timeProvider)`), linked to the caller's token, so tests can drive it with `FakeTimeProvider`. Never race `Task.Delay`. A request cancelled by the caller is not an error (see ERR-4 for how the server handles it).

### Shared rules: JSON

- **JSN-1 (MUST)**: System.Text.Json only (Newtonsoft is banned). Each server module and the SDK declare a source generated `JsonSerializerContext` for their types and register it in the resolver chain. Property naming and enum format follow feature 10.
- **JSN-2 (MUST)**: Ignore unknown JSON properties on read (the default), so newer servers and older SDKs keep working together.

### Shared rules: testing

- **TST-1 (MUST)**: xUnit v3 on Microsoft.Testing.Platform, Shouldly for assertions, real code or small hand written fakes first (`FakeTimeProvider`, an in memory email sender), NSubstitute only at true external seams. No Moq, no FluentAssertions, no AwesomeAssertions.
- **TST-2 (MUST)**: Test projects mirror production: `tests/<Project>.Tests` for fast unit tests with no I/O (pure logic: domain rules, query builders, mapping), one integration test project per repo (server: `tests/Orvano.Host.IntegrationTests`, with a folder per module; SDK: `tests/Orvano.Server.ConformanceTests`), `tests/Orvano.ArchitectureTests` in the server, and a shared helper project `tests/Orvano.Testing`. Handlers touch the database, so handler tests are integration tests. `Orvano.Testing` holds `OrvanoApiFactory` (a `WebApplicationFactory<Program>` whose `CreateProjectClientAsync` creates a project and server key and returns a signed in `HttpClient`), `ReadProblemAsync`, and the PostgreSQL fixture.
- **TST-3 (MUST)**: Name tests `<Behavior>_<expected>_when_<condition>`, for example `SignUp_returns_409_when_email_already_used`. One behavior per test, arrange, act, assert, no loops or branches in the test body, `[Theory]` for data variations. Pass `TestContext.Current.CancellationToken` to async calls.
- **TST-4 (MUST)**: Every acceptance criterion in a feature spec has at least one test tagged with the spec number and the criterion, `[Trait("AC", "0014:AC-2")]`, so `/check` can trace criteria to tests without collisions between specs. Each endpoint also has one test that sends an invalid body and expects 400 with `code` `validation_failed`, which proves validation really runs for that module and that the host's error shape reaches it (VAL-1).
- **TST-5 (MUST)**: Integration tests use a real PostgreSQL from `Testcontainers.PostgreSql`, with the image pinned to an exact tag: the same `postgres:18.<minor>` tag the compose files use (spec 0007 PG-1, which makes PostgreSQL 18 the only supported major). One container per test run (an xUnit v3 assembly fixture), started with `max_connections=300`, and test connection strings use `Maximum Pool Size=5` so parallel classes do not exhaust connections. The fixture migrates a template database once, then clears every Npgsql pool (`NpgsqlConnection.ClearAllPools()`, since PostgreSQL refuses to clone a template that has open sessions); each test class gets its own database cloned from it (`CREATE DATABASE … TEMPLATE …`), and Respawn wipes that database between the class's tests, configured with `SchemasToInclude` set to the module schemas and `TablesToIgnore` set to every migrations history table (otherwise the next host start sees pending migrations). Classes run in parallel, tests in one class run in order. Never use the EF in memory provider or SQLite as a stand in.
- **TST-5a (MUST)**: Tests and scripts work with any OCI container engine (Docker or Podman) and never assume Docker by name. Testcontainers finds the engine through `DOCKER_HOST` (for Podman, its Docker compatible socket or named pipe); any engine specific setting (for example disabling Testcontainers' Ryuk cleanup container where Podman cannot run it) is read from environment variables documented in the repo README, never hard coded. Docker is the default engine on the development machine (a Mac with Docker installed), so the README documents Docker first; Podman stays supported for contributors.
- **TST-6 (MUST)**: Host the real app in `WebApplicationFactory<Program>` (Host's `Program.cs` ends with `public partial class Program;` so tests can see it) and test through HTTP. Swap only external providers (email, SMS, push) and `TimeProvider`.
- **TST-7 (SHOULD)**: Use Verify snapshot tests for stable outputs worth seeing whole: ProblemDetails bodies, OpenAPI output, SDK request serialization.
- **TST-8 (SHOULD)**: Collect coverage (Microsoft.Testing.Extensions.CodeCoverage) and show it in CI, with no numeric gate. Traceability (**TST-4**) is the gate.

### Server rules: layout

- **LAY-1 (MUST)**: The server is a modular monolith with one project per module:

  ```text
  server/                              (top level folder per spec 0007 REP-1)
    Orvano.slnx  global.json  Directory.Build.props  Directory.Packages.props  .editorconfig  BannedSymbols.txt
    src/
      Orvano.Host/                     ASP.NET Core entry point, composition, middleware, the migrate command. No feature code.
      Orvano.SharedKernel/             Result, Error, IProjectContext, problem mapping, config helpers. No module logic.
      Orvano.Infrastructure/           job runner, outbox dispatcher, signal listener, cache invalidation, file stores, root key, project database connections (spec 0007 REP-2). No module logic.
      Orvano.Modules.Auth/             one class library per module (Auth, Databases, Storage, …)
      Orvano.Engines.Postgres/         project database engine (implements the adapter interface from Databases' Contracts)
    tests/
      Orvano.Modules.Auth.Tests/
      Orvano.Host.IntegrationTests/
      Orvano.ArchitectureTests/
      Orvano.Testing/
    benchmarks/
      Orvano.Benchmarks/
  ```

- **LAY-2 (MUST)**: Inside a module, code is grouped by feature (a vertical slice), not by technical layer:

  ```text
  Orvano.Modules.Auth/
    AuthModule.cs            public: AddAuthModule(...) and MapAuthModule(...)
    Contracts/               public: what other modules may use (interfaces, events, DTOs)
    Features/SignUp/         SignUpEndpoint.cs, SignUpHandler.cs, SignUpRequest.cs, SignUpResponse.cs
    Domain/                  entities and value objects
    Data/                    AuthDbContext.cs, Configurations/, Migrations/
    AuthErrors.cs  AuthLog.cs  AuthOptions.cs  AuthJsonContext.cs  AuthTelemetry.cs
  ```

- **LAY-3 (MUST)**: Only `<Module>Module.cs` and `Contracts/` are public for other modules to use. One carve out: request and response records and options classes are `public` because the validation generator, options binding, and OpenAPI need public types; an architecture test forbids any other module from referencing a module's `Features/` or `Domain/` namespaces. A module references `Orvano.SharedKernel` and other modules' `Contracts` types only. An engine project (`Orvano.Engines.*`) references only `Orvano.SharedKernel` and the Databases module's `Contracts`; `Orvano.Host` registers the engines. No module references `Orvano.Host` or `Orvano.Infrastructure`, and module references never form a cycle. `Orvano.Infrastructure` references only `Orvano.SharedKernel` (plus the packages spec 0007 REP-2 lists), and modules use it only through the interfaces REP-2 places in `Orvano.SharedKernel`. Each module grants `InternalsVisibleTo` to its own test project only. `Orvano.SharedKernel` may reference the ASP.NET Core shared framework, EF Core relational, Npgsql, and `EFCore.NamingConventions` (DAT-2, spec 0011), and nothing else.
- **LAY-4 (MUST)**: Modules talk through `Contracts` interfaces (in process calls). Events between modules go through each module's transactional outbox (spec 0007 EVT-1 to EVT-3), written in the same `SaveChanges` as the change and handled by `IEventHandler<TEvent>` in the subscribing module. A module never reads or writes another module's tables.
- **LAY-5 (MUST)**: `Orvano.Host` wires modules with one call each: `builder.Services.AddAuthModule(builder.Configuration)` and `app.MapAuthModule()`.

### Server rules: API

- **API-1 (MUST)**: Endpoints are Minimal APIs. Each module maps one route group; each feature has one static `<Feature>Endpoint` class with a `Map(IEndpointRouteBuilder)` method. No controllers. Route prefixes and versioning are spec 0014 PTH-1, PTH-2, and VRS: `Orvano.Host` hands each module its group under `/v1`, and a module never writes `/v1`.
- **API-2 (MUST)**: Endpoint delegates return `TypedResults` through a `Results<…>` union, which documents the success responses. Failure statuses are declared with `.ProducesProblem(<status>)` for each `ErrorKind` the handler can return (a `ProblemHttpResult` in the union documents none of them). Each endpoint sets `WithSummary` and `WithName("<Module>_<Feature>")`, for example `Auth_SignUp`; names are the OpenAPI operation IDs and must be unique across the app (a startup test checks).
- **API-3 (MUST)**: Endpoints are thin: bind, call one handler, map the Result. No business logic, no `DbContext`, no `try`/`catch`.
- **API-4 (MUST)**: Deny by default. A fallback authorization policy requires an authenticated caller; every endpoint states `RequireAuthorization(<policy>)` or `AllowAnonymous()` explicitly, on the endpoint or on its route group. A startup test walks `EndpointDataSource` and fails on any endpoint with neither authorization nor anonymous metadata. Policies come from feature 9.
- **API-5 (MUST)**: Requests and responses are records in the feature folder. Never bind or return EF entities.
- **API-6 (MUST)**: OpenAPI is code first: the server is the source of the contract, through the built in `AddOpenApi` (OpenAPI 3.1), per spec 0014 CTR-1.
- **API-7 (MUST)**: Each feature has one `internal sealed` handler class, registered scoped by its module, with one public method `Task<Result<T>> HandleAsync(<Request>, CancellationToken)`. No interface for a handler, no mediator library, no handler calling another handler (extract a shared service instead).
- **API-8 (MUST)**: Concerns shared across endpoints live in endpoint filters or middleware, never copied into handlers. Validation applies to every endpoint (`AddValidation()`); the project context filter goes on each module route group whose endpoints need a project; metering is added by feature 45.
- **API-9 (MUST)**: The current project comes from a scoped `IProjectContext` (in `Orvano.SharedKernel`, exposing `Guid Id`) that middleware fills once per request; handlers read it and never trust a project ID from the body. Reading `Id` on a route that has no project (console or instance level endpoints) throws, so a missing filter fails loudly. How the project is resolved is feature 9's call.

### Server rules: data access

- **DAT-1 (MUST)**: Orvano's own tables use EF Core 10 with `Npgsql.EntityFrameworkCore.PostgreSQL`. Each module has its own `DbContext`, its own PostgreSQL schema, and its own migrations history table in that schema (`<schema>.__ef_migrations_history`; a default schema alone leaves every module sharing one history table). A module gets its schema and history table (plus snake case names and the DAT-8 retry) by calling `UseOrvanoConventions(schema, services)` from `Orvano.SharedKernel` instead of `UseNpgsql` and `HasDefaultSchema` itself; spec 0011 (AC-3, MOD-1) owns how that call works and the architecture test that fails a context which skips it.
- **DAT-2 (MUST)**: Names are snake_case in the database (`UseSnakeCaseNamingConvention()` from `EFCore.NamingConventions`). Times map to `timestamptz`. Entity shape, IDs, and keys follow feature 8.
- **DAT-2a (MUST)**: Project isolation is automatic, not hand written. Every platform entity that belongs to a project implements `IProjectScoped` (`Guid ProjectId`), and each `DbContext` applies an EF Core 10 named query filter `"project"` to all of them, comparing `ProjectId` with `IProjectContext.Id`. Bypassing it (`IgnoreQueryFilters(["project"])`) needs a `// Deviation DAT-2a` comment. Each module has a cross project leak integration test: data created in project A is invisible from project B. Feature 9 may refine the mechanism, not remove it.
- **DAT-3 (MUST)**: Handlers use their module's `DbContext` directly. No generic repository. Reused queries are `IQueryable<T>` extension methods inside the module.
- **DAT-4 (MUST)**: Reads project straight to response records with `Select`, or use `AsNoTracking()`. No lazy loading. Load related data on purpose (`Include` or projection), and watch for N+1 queries in review.
- **DAT-5 (MUST)**: Every list query is bounded (`Take`), with pagination per feature 10 (keyset preferred).
- **DAT-6 (MUST)**: SQL is always parameterized. Raw SQL uses `FromSql` or `ExecuteSql` with interpolation (which parameterizes). `FromSqlRaw` or `ExecuteSqlRaw` with built strings is banned (EF1002 as error). The one exception is an identifier, never a value: the outbox dispatcher and cleanup name each module's `outbox` table from a schema registered in code and pattern checked, quoted, through `FormattableStringFactory`, with a `// Deviation DAT-6` comment (spec 0013 DSP-8).
- **DAT-7 (MUST)**: Bulk changes use `ExecuteUpdateAsync` or `ExecuteDeleteAsync`, not load, change, save loops. These bypass change tracking and the DAT-9 `xmin` check, so never use them on a single row a caller is editing concurrently.
- **DAT-8 (MUST)**: One `SaveChangesAsync` is one transaction. Several saves that must succeed together go in `BeginTransactionAsync` inside `CreateExecutionStrategy().ExecuteAsync(...)`, because connection resiliency (`EnableRetryOnFailure`) is on.
- **DAT-9 (SHOULD)**: Entities edited by more than one caller use optimistic concurrency on PostgreSQL's `xmin` (the entity implements `IVersioned` from `Orvano.SharedKernel`, whose `uint Version` the convention plugin maps to `xmin`, so no real column and no `[Timestamp]` attribute; spec 0011 COL-1 makes this required for every editable platform entity). A conflict returns a `Conflict` error.
- **DAT-10 (MUST)**: Migrations are EF Core migrations per module in `Data/Migrations`, reviewed as code, never edited once released. The host binary's `migrate` command applies every module's migrations in the order of one explicit list in `Orvano.Host` (`MigrationOrder`, a module before any module whose tables depend on it) and exits; install and upgrade run it before starting the server, and locally you run `dotnet run --project src/Orvano.Host -- migrate`. At startup the server checks for pending migrations and refuses to start, naming the command to run. Each module has an `IDesignTimeDbContextFactory` so `dotnet ef migrations add` works on the class library, and a test asserting `HasPendingModelChanges()` is false, so a model change without its migration fails CI. `Database.Migrate` and `MigrateAsync` are banned outside the migrate command. After beta, schema changes that break older code use expand then contract (add, backfill, switch, remove later).
- **DAT-11 (MUST)**: Project databases (tables your users create) never use EF Core. Code reaches them only through the engine adapter (features 15 and 16 shape it), which follows these rules (defaults that features 15 and 16 may tune):
  - One `NpgsqlDataSource` per project database, held in a bounded cache (at most 500 entries, least recently used evicted, idle entries disposed after 10 minutes, disposed at once when the project is deleted), each with `Maximum Pool Size=10`. Never `new NpgsqlConnection(string)` (banned).
  - Identifiers from users must match `^[a-z][a-z0-9_]{0,62}$`, must not be a PostgreSQL reserved word, and must not start with `pg_` or `orvano_`; one adapter function validates and quotes them.
  - Values are always parameters.
  - Every command has a timeout (default 30 s, `ORVANO_DATABASES__COMMANDTIMEOUT`) and the request's cancellation token.
- **DAT-12 (MUST)**: Npgsql's `Include Error Detail`, EF Core's `EnableSensitiveDataLogging`, and `EnableDetailedErrors` are on only in Development. Connection strings are never logged.

### Server rules: errors

- **ERR-1 (MUST)**: `Orvano.SharedKernel` defines the one error vocabulary:

  ```csharp
  public enum ErrorKind { Validation, Unauthorized, Forbidden, NotFound, Conflict, RateLimited, Unavailable }

  public sealed record Error(string Code, string Message, ErrorKind Kind,
      IReadOnlyDictionary<string, string[]>? Fields = null,
      IReadOnlyDictionary<string, object?>? Extensions = null)   // e.g. retryAfterSeconds for RateLimited
  {
      public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
      public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
      // one factory per kind
  }

  public readonly struct Result<T>
  {
      private readonly bool _isSuccess;                 // false for default(Result<T>), which is never valid

      public T? Value { get; }
      public Error? Error { get; }

      [MemberNotNullWhen(true, nameof(Value))]
      [MemberNotNullWhen(false, nameof(Error))]
      public bool IsSuccess => _isSuccess && Error is null;

      public static implicit operator Result<T>(T value) => new(value);
      public static implicit operator Result<T>(Error error) => new(error);
      // private constructors, plus a non generic Result for commands with no value
  }
  ```

- **ERR-2 (MUST)**: Handlers return a Result for expected failures (validation that needs data, not found, conflict, forbidden, quota). They never throw for those. Exceptions are for bugs and outages only.
- **ERR-3 (MUST)**: Each module lists its errors once, as static members of `<Module>Errors`. Codes are stable strings in the format feature 10 sets; messages are English, plain, and safe to show to callers.
- **ERR-4 (MUST)**: Endpoints turn an `Error` into ProblemDetails (RFC 9457) with one shared `error.ToProblem()` in `Orvano.SharedKernel`, adding `code`, `traceId`, field errors, and `Extensions`; the body is spec 0014 PRB-1. The kind to status map is final in spec 0014 KND-1: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, PreconditionFailed 412, RateLimited 429, Unavailable 503. A wait is `Error.RetryAfter` (a `TimeSpan?`), written as `retryAfter` in the body and the `Retry-After` header on 429 and 503 only (spec 0014 PRB-2, PRB-6). One `IExceptionHandler` in `Orvano.Host` plus `AddProblemDetails()` turns anything unhandled into a 500 ProblemDetails with a generic message and the trace ID, never a stack trace or exception message outside Development. That handler logs the exception at Error itself, because in .NET 10 the exception middleware no longer logs exceptions a handler has handled. It treats cancellation in two ways: an `OperationCanceledException` while `HttpContext.RequestAborted` is cancelled means the caller left, so it writes no body and logs at Debug; any other `OperationCanceledException` is a server side timeout, so it returns 503 (`Unavailable`, code `timeout`) and logs at Error.
- **ERR-5 (MUST)**: Catch only specific, known exceptions, and only to turn them into a Result: a unique violation (`PostgresException` with `SqlState` `23505`) becomes `Conflict`, checked with the shared `IsUniqueViolation(string constraintName)` helper so a handler knows which index fired (constraint names are module constants, snake_case from DAT-2); a foreign key violation (`23503`, `IsForeignKeyViolation(constraintName)`) becomes `Validation` on the referencing field; `DbUpdateConcurrencyException` becomes `Conflict`. No catch all blocks in handlers.

### Server rules: validation

- **VAL-1 (MUST)**: `AddValidation()` (built into .NET 10) checks request records before the handler runs. `Orvano.Host` always calls `builder.Services.AddValidation()` (it is the one place for validation options and for the `CustomizeProblemDetails` hook below), and **every module that maps endpoints also calls the plain `services.AddValidation()`, with no options, in its `Add<Module>Module`**, added with the module's first endpoint: the validation source generator covers only request types in the project that makes the call, so without it a module's endpoints silently skip validation (confirmed 2026-10-16 on SDK 10.0.100; `[ValidatableType]` alone does not fix it, and is not needed). The TST-4 invalid body test per endpoint is what catches a module that forgets the call. Shape rules use DataAnnotations (`[Required]`, `[StringLength]`, `[Range]`, `[EmailAddress]`); rules across fields use `IValidatableObject`. Failures return the built in 400 ValidationProblem, which `AddProblemDetails(o => o.CustomizeProblemDetails = …)` in `Orvano.Host` extends with `code` `validation_failed` and `traceId`, so it matches the ERR-4 shape from day one.
- **VAL-2 (MUST)**: Rules that need data or state (uniqueness, quotas, permissions, references) live in the handler and return a `Validation` or `Conflict` error with field details.
- **VAL-3 (MUST)**: Row values checked against a user's table schema (feature 16) return the same `Validation` error with `Fields`, so callers see one error shape everywhere.

### Server rules: logging and telemetry

- **LOG-1 (MUST)**: Log through `ILogger<T>` with source generated `[LoggerMessage]` methods, collected in one `internal static partial class <Module>Log` per module as extension methods. Each module owns a block of 1000 event IDs, recorded as constants in `Orvano.SharedKernel/LogEventIds.cs`: Host 0, Auth 1000, Databases 2000, Storage 3000, Realtime 4000, Functions 5000, Sites 6000, Messaging 7000, Operations 8000, Platform (console accounts and projects) 9000; new modules take the next free block. No string interpolation in log calls (CA2254 and CA1848 as errors).
- **LOG-2 (MUST)**: Property names are PascalCase (`{ProjectId}`, `{UserId}`). Never log passwords, tokens, API keys, secrets, connection strings, or request or response bodies. Feature 5 decides how other personal data is redacted.
- **LOG-3 (MUST)**: Levels: Error means an operator should act; Warning means unexpected but handled; Information means lifecycle events (startup, migration, project created); Debug means diagnostics. No Information log per request; requests show up as traces.
- **LOG-4 (MUST)**: OpenTelemetry for traces and metrics (ASP.NET Core, HttpClient, Npgsql, and runtime instrumentation), with logs bridged through `builder.Logging.AddOpenTelemetry()`. The OTLP exporter turns on only when the standard `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Console logs are JSON outside Development.
- **LOG-5 (MUST)**: Each module has one `ActivitySource` and one `Meter`, both named `Orvano.<Module>`, in `<Module>Telemetry`. Metric names follow OpenTelemetry conventions (`orvano.<module>.<name>`, with units). `Orvano.Host` registers them all with `AddSource("Orvano.*")` and `AddMeter("Orvano.*")`. Metric tags stay low in cardinality: never user IDs, emails, or raw paths (use the route template); a project ID tag appears only on metering meters. Usage metering (feature 45) builds on these meters.
- **LOG-6 (MUST)**: Built in health checks at `/health/live` (the process is up) and `/health/ready` (the platform database answers and no migrations are pending), using one small check written in `Orvano.Host` on the shared `NpgsqlDataSource` (no extra package). Spec 0014 kept these paths for container checks, outside `/v1` and the contract (HLT-5).

### Server rules: configuration

- **CFG-1 (MUST)**: Each module binds an options class with a `Section` constant, validated at startup: `AddOptions<T>().BindConfiguration(T.Section).ValidateOnStart()` plus a source generated `[OptionsValidator]`. Handlers take `IOptions<T>` (or `IOptionsMonitor<T>` when the value can change at runtime), never `IConfiguration`. Every option property has an XML doc comment (the docs site, feature 32, can list them).
- **CFG-2 (MUST)**: Keep ASP.NET Core's default sources from `WebApplication.CreateBuilder` (including `ASPNETCORE_ENVIRONMENT`, which is Production when unset, and user secrets in Development), then add Orvano's so they win: an optional JSON file at the path in `ORVANO_CONFIG_FILE` (added right after the builder is created, before anything reads configuration; a set but missing path fails startup), environment variables with the `ORVANO_` prefix, then the command line. Environment names are `ORVANO_<SECTION>__<KEY>`, for example `ORVANO_DATABASE__HOST` or `ORVANO_AUTH__SESSIONLIFETIME` (case does not matter; `__` means nesting). Self hosting docs mention only the `ORVANO_` names.
- **CFG-3 (MUST)**: Any key can instead come from a file: `ORVANO_DATABASE__PASSWORD_FILE=/run/secrets/db_password` reads the file's contents, with one trailing newline trimmed (one configuration source in `Orvano.SharedKernel`, for Docker and Kubernetes secrets). A missing or unreadable file fails startup; setting both `KEY` and `KEY_FILE` fails startup; file values are read once, not reloaded.
- **CFG-4 (MUST)**: No secrets in source control or shipped `appsettings` files. Bad configuration stops startup with a message naming the environment variable to fix: a helper in `Orvano.SharedKernel` turns each `OptionsValidationException` key (`Auth:SessionLifetime`) into its variable name (`ORVANO_AUTH__SESSIONLIFETIME`), and `Orvano.Host` prints that before exiting.
- **CFG-5 (MUST)**: The platform database is configured by `ORVANO_DATABASE__HOST`, `__PORT` (default 5432), `__NAME` (default `orvano`), `__USERNAME`, `__PASSWORD` (or `__PASSWORD_FILE`), and `__SSLMODE` (default `Prefer`). `Orvano.Host` owns these options, builds one `NpgsqlDataSource` from them, and registers it; every module's `DbContext` uses that one data source. Feature 6 may add options; it must not split them per module. Settings a project owner changes in the console live in the database (feature 8), not in configuration.

### Server rules: performance

- **PRF-1 (MUST)**: Ship JIT builds with ReadyToRun precompiled code (spec 0007 stack table and IMG-1), never Native AOT. ReadyToRun shortens container start while EF Core and reflection keep working, and Dynamic PGO still tunes hot paths once the server runs. Keep code AOT friendly where it costs nothing: source generators for JSON, logging, validation, options, and configuration binding; no `Reflection.Emit`; no `dynamic`.
- **PRF-2 (MUST)**: Cache only with a measured need or a spec that asks for it, and then through `HybridCache`. Keys are `orvano:<module>:<entity>:<id>`, tagged by project so a change can invalidate by tag. The spec that adds a cache names its invalidation path. There is no shared second level cache; tag invalidation goes through `ICacheInvalidator`, which reaches every replica by a `cache` signal (spec 0007 CCH-1, CCH-2). So: never cache permission answers or API key validity; cap security related entries (roles, memberships, end user labels, resource grants, project status, API key metadata, signing key lookups) at 60 seconds; cap every other entry at 5 minutes when its invalidation path is named, and at 60 seconds otherwise.
- **PRF-3 (MUST)**: Outbound HTTP uses `IHttpClientFactory` typed or named clients (`new HttpClient()` is banned in the server), each with `AddStandardResilienceHandler()` (timeouts, retry with jitter, circuit breaker). Retries are off for unsafe methods (`Retry.DisableForUnsafeHttpMethods()`) unless the provider accepts an idempotency key and the call sends one.
- **PRF-4 (MUST)**: Hot paths (query building, permission evaluation, token validation, serialization) have BenchmarkDotNet benchmarks with `[MemoryDiagnoser]` in `benchmarks/Orvano.Benchmarks`; a PR that changes one shows before and after numbers.
- **PRF-5 (MUST)**: Before each release, k6 load tests run against core endpoints with thresholds as budgets. A feature spec may set its own budget; until then the default is p95 server time at or under 100 ms for single row reads and writes at 200 requests per second, with errors under 0.1 percent, on the reference box (4 vCPU, 8 GB, PostgreSQL on the same host). Revisit the default after Slice 1 is measured.
- **PRF-6 (SHOULD)**: Reach for `Span<T>`, `ArrayPool<T>`, or pooled streams only where a benchmark or profile shows the need. Readable code otherwise.

### Library rules (the .NET server SDK)

- **LIB-1 (MUST)**: Package ID and root namespace `Orvano.Server`. Target `net10.0` only. Add each new LTS target when it ships and drop a target when it leaves support; skip STS releases.
- **LIB-2 (MUST)**: Project settings: `IsAotCompatible` true (turns on trim, AOT, and single file analyzers), `GenerateDocumentationFile` true with every public member documented (CS1591 as error), `PackageReadmeFile`, `PackageIcon`, `PackageLicenseExpression` (Orvano's license), `RepositoryUrl`, `PublishRepositoryUrl` true, `EmbedUntrackedSources` true, `IncludeSymbols` with `SymbolPackageFormat` snupkg, `Deterministic` true, and `ContinuousIntegrationBuild` true in CI. Source Link comes from the SDK. No strong name signing (no .NET Framework target).
- **LIB-3 (MUST)**: The only dependency is `Microsoft.Extensions.Http` (which brings the DI, logging, and options abstractions that ASP.NET Core apps already have), at the lowest version that works (10.0.0), never forcing consumers onto the newest. The SDK repo turns `CentralPackageTransitivePinningEnabled` off for the packable project so central versions never raise that floor; raise the floor only when an advisory hits it.
- **LIB-4 (MUST)**: One thread safe `OrvanoClient`, meant to be reused. Two ways in: `new OrvanoClient(OrvanoClientOptions, HttpClient? httpClient = null)` (without an `HttpClient` it uses one shared `SocketsHttpHandler` with `PooledConnectionLifetime` of 2 minutes so DNS changes are seen), or `services.AddOrvano(...)`, which registers it as a typed client through `IHttpClientFactory`. Services hang off it as properties (`client.Databases`, `client.Users`). An `HttpClient` you pass in stays yours: `OrvanoClient` never disposes it. The shared handler is the only static, created once and never changed; no other static state.
- **LIB-5 (MUST)**: Every I/O method ends in `Async`, takes an optional `OrvanoRequestOptions? options = null` (per call timeout, retry override, extra headers) just before `CancellationToken cancellationToken = default`, which comes last, and uses `ConfigureAwait(false)`. No synchronous wrappers. Paged lists also offer an `IAsyncEnumerable<T>` that walks every page.
- **LIB-6 (MUST)**: Any error response throws `OrvanoException`, carrying `Kind` (from the status alone, per the spec 0014 KND-2 table), `StatusCode`, `Code`, `Title`, `Detail`, `TraceId`, and the raw body as the SDK's own `OrvanoProblem` model (the ASP.NET Core `ProblemDetails` type is not available under LIB-3). Subclass only when a case carries extra data (`OrvanoRateLimitedException` with `RetryAfter`). Error codes are string constants in `OrvanoErrorCodes`, generated from the contract. Bad arguments throw `ArgumentException` types before any request. Network failures surface as the standard `HttpRequestException`. When the per attempt timeout (LIB-7) runs out on the last attempt, the SDK throws `TimeoutException`, never `OperationCanceledException`, so callers can tell a timeout apart from their own cancellation; their own token's cancellation surfaces as `OperationCanceledException`.
- **LIB-7 (MUST)**: Built in retry, with no Polly dependency: only GET, HEAD, OPTIONS, and requests to operations marked idempotent that carry an `Idempotency-Key` (spec 0014 IDM-1 to IDM-3), on 408, 429, 502, 503, 504, and transient network errors; at most 2 retries with exponential backoff and full jitter from 0.5 s, honoring `Retry-After` up to 30 s. `OrvanoClientOptions.Retry.MaxRetries = 0` turns it off. Each attempt builds a fresh `HttpRequestMessage` from a request factory (a sent message cannot be sent again). Default timeout 30 s per attempt (a linked token source from the options' `TimeProvider`), set in `OrvanoClientOptions.Timeout` and overridable per call through `OrvanoRequestOptions`, for example for uploads.
- **LIB-8 (MUST)**: The API key never appears in logs, exception messages, or `ToString()` output. Logging is optional (an `ILoggerFactory` from DI or options), at Debug only, with method, path, status, and duration, never bodies.
- **LIB-9 (MUST)**: Serialization uses the SDK's own source generated `OrvanoJsonContext` and never changes global options. An enum value the SDK does not know does not throw (model enums as string backed values with an unknown fallback).
- **LIB-10 (MUST)**: Track the public API with `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`, RS0016 and RS0017 as errors) from the first commit. Before beta this only forces every change to be written down in `PublicAPI.Unshipped.txt` (removals and renames are still allowed); blocking breaking changes starts at beta through package validation. `EnablePackageValidation` is on from the start; `PackageValidationBaselineVersion` points at the last release from the first beta onward (feature 52).
- **LIB-11 (MUST)**: Generated code (feature 10) lives in `Generated/`, starts with `// <auto-generated/>`, is never edited by hand, and must still meet these rules: async with cancellation, nullable clean, documented, and tracked by the public API files. Hand written code lives beside it.
- **LIB-12 (MUST)**: SDK repo layout: `Orvano.Server.slnx`, the same root build files as the server, `src/Orvano.Server/`, `tests/Orvano.Server.Tests/` (unit tests with a hand written stub `HttpMessageHandler`, never a mocked `HttpClient`), `tests/Orvano.Server.ConformanceTests/` (feature 10's shared suite against a real server container), and an optional `samples/` folder.

### Canonical pattern

One feature slice end to end. The details (routes, codes, auth) are placeholders until features 9, 10, and 14 decide them; the shape is the standard.

```csharp
// Orvano.Modules.Auth/Features/SignUp/SignUpRequest.cs
namespace Orvano.Modules.Auth.Features.SignUp;

public sealed record SignUpRequest
{
    [Required, EmailAddress, StringLength(320)]
    public required string Email { get; init; }

    [Required, StringLength(256, MinimumLength = 8)]
    public required string Password { get; init; }
}

// Orvano.Modules.Auth/Features/SignUp/SignUpResponse.cs
public sealed record SignUpResponse(Guid UserId, DateTimeOffset CreatedAt);

// Orvano.Modules.Auth/Features/SignUp/SignUpEndpoint.cs
internal static class SignUpEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/users", HandleAsync)
            .WithName("Auth_SignUp")
            .WithSummary("Create an end user with email and password")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AllowAnonymous();

    private static async Task<Results<Created<SignUpResponse>, ProblemHttpResult>> HandleAsync(
        SignUpRequest request, SignUpHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/auth/users/{result.Value.UserId}", result.Value)
            : result.Error.ToProblem();
    }
}

// Orvano.Modules.Auth/Features/SignUp/SignUpHandler.cs
internal sealed class SignUpHandler(
    AuthDbContext db, IPasswordHasher hasher, IProjectContext project,
    TimeProvider time, ILogger<SignUpHandler> logger)
{
    public async Task<Result<SignUpResponse>> HandleAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        var user = User.Create(project.Id, request.Email, hasher.Hash(request.Password), time.GetUtcNow());
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(AuthConstraints.UserEmailPerProject))
        {
            return AuthErrors.EmailTaken;                          // ERR-5: the unique index decides, no race
        }

        logger.UserCreated(project.Id, user.Id);
        return new SignUpResponse(user.Id, user.CreatedAt);
    }
}

// Orvano.Modules.Auth/AuthErrors.cs
internal static class AuthErrors
{
    public static readonly Error EmailTaken =
        Error.Conflict("user_email_taken", "A user with this email already exists in this project.");
}

// Orvano.Modules.Auth/AuthLog.cs
internal static partial class AuthLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "End user {UserId} created in project {ProjectId}")]
    public static partial void UserCreated(this ILogger logger, Guid projectId, Guid userId);
}

// Orvano.Modules.Auth/AuthOptions.cs  (env: ORVANO_AUTH__SESSIONLIFETIME=7.00:00:00)
public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>How long an end user session stays valid.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "365.00:00:00")]
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromDays(30);
}

// Orvano.Modules.Auth/AuthOptionsValidator.cs
[OptionsValidator]
internal sealed partial class AuthOptionsValidator : IValidateOptions<AuthOptions>;

// Orvano.Modules.Auth/AuthModule.cs
public static class AuthModule
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddDbContext<AuthDbContext>(/* Npgsql, snake_case, retry on failure */);
        services.AddValidation();                                   // VAL-1: covers this module's request types
        services.AddScoped<SignUpHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapAuthModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");
        SignUpEndpoint.Map(group);
        return app;
    }
}

// tests/Orvano.Host.IntegrationTests/Auth/SignUpTests.cs
public sealed class SignUpTests(OrvanoApiFactory api) : IClassFixture<OrvanoApiFactory>
{
    [Fact, Trait("AC", "0014:AC-2")]
    public async Task SignUp_returns_409_when_email_already_used()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await api.CreateProjectClientAsync(ct);
        var body = new { email = "ada@example.test", password = "correct horse battery" };
        await client.PostAsJsonAsync("/auth/users", body, ct);

        var response = await client.PostAsJsonAsync("/auth/users", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync(ct)).Code.ShouldBe("user_email_taken");
    }
}
```

The SDK side of the same call:

```csharp
// App code using Orvano.Server
var client = new OrvanoClient(new OrvanoClientOptions
{
    Endpoint = new Uri("https://orvano.example.com"),
    ProjectId = projectId,
    ApiKey = apiKey,
});

try
{
    var user = await client.Users.CreateAsync(new CreateUserRequest { Email = email, Password = password }, cancellationToken);
}
catch (OrvanoException ex) when (ex.Code == OrvanoErrorCodes.UserEmailTaken)
{
    // tell the person the email is taken
}

// Inside the SDK (hand written core)
internal async Task<T> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> type, CancellationToken cancellationToken)
{
    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode)
    {
        throw await OrvanoException.FromResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }
    return (await response.Content.ReadFromJsonAsync(type, cancellationToken).ConfigureAwait(false))!; // Deviation GEN-8: a 2xx body is never null per contract
}
```

### Replaces

These common patterns are wrong at Orvano, even when a tutorial or an agent skill suggests them:

- MVC controllers, FastEndpoints, or endpoint logic written inline in lambdas
- MediatR, Wolverine, or any mediator; an interface per handler or service with one implementation
- Clean Architecture layer projects (Domain, Application, Infrastructure) or global `Services/` and `Repositories/` folders
- Generic repositories over EF Core; one `DbContext` shared by all modules; lazy loading; the EF in memory provider or SQLite in tests
- `Database.Migrate()` at startup
- Exceptions for expected outcomes (`NotFoundException`); `catch (Exception)` in handlers
- FluentValidation alongside built in validation
- Serilog's static `Log`, `Console.WriteLine`, interpolated log messages
- AutoMapper, Mapperly, or other mapping libraries; Newtonsoft.Json
- `DateTime.UtcNow`, `.Result`, `.Wait()`, `new HttpClient()` per call
- MSTest, NUnit, TUnit, FluentAssertions, Moq, NBomber
- Flat environment names mapped by hand (`ORVANO_DB_HOST`)

### Enforcement

Feature 7 installs and wires these; this table says what each rule is checked by.

| Mechanism                                                          | Rules                                                                                                                                                                                                                                                                                                                                                                                                                           | Where it lives                                   |
| ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------ |
| Compiler and .NET analyzers, warnings as errors in CI              | GEN-2, GEN-6, GEN-8, GEN-10 (CA1305, CA1307, CA1309, CA1310), ASY-2 (CA2016), ASY-1 (CA1849), ASY-3 (CA2007, SDK only), LOG-1 (CA2254, CA1848), DAT-6 (EF1002), LIB-2 (CS1591 and trim and AOT warnings)                                                                                                                                                                                                                        | Root `Directory.Build.props` and `.editorconfig` |
| Banned API analyzer (`Microsoft.CodeAnalysis.BannedApiAnalyzers`)  | GEN-9 (clock APIs), ASY-1 (`Task.Wait`, `Task<T>.Result`, `GetResult`), JSN-1 (Newtonsoft types), PRF-3 (`HttpClient` constructor, server only), DAT-10 (`Migrate`, `MigrateAsync` outside the migrate command), DAT-11 (`NpgsqlConnection(string)`), DAT-6 (`ExecuteSqlRaw`, `FromSqlRaw`)                                                                                                                                     | `BannedSymbols.txt` at each repo root            |
| Public API analyzer and package validation                         | LIB-10                                                                                                                                                                                                                                                                                                                                                                                                                          | SDK repo                                         |
| Central package management, lock files                             | GEN-3, GEN-4                                                                                                                                                                                                                                                                                                                                                                                                                    | `Directory.Packages.props`, CI restore           |
| Architecture tests (ArchUnitNET)                                   | LAY-3, LAY-4 (module and engine references, no Host or Infrastructure references from modules, no cycles, no use of another module's `Features/` or `Domain/`, SharedKernel's allowed references), API-3 (endpoints do not depend on `DbContext`), API-7 (handlers are `internal sealed`), DAT-2a (every entity with a `ProjectId` implements `IProjectScoped`), GEN-11 and GEN-12 (no mapping or scanning packages referenced) | `tests/Orvano.ArchitectureTests`                 |
| Endpoint metadata tests (walk `EndpointDataSource` in a test host) | API-2 (unique `<Module>_<Feature>` names), API-4 (every endpoint has authorization or anonymous metadata)                                                                                                                                                                                                                                                                                                                       | `tests/Orvano.Host.IntegrationTests`             |
| Integration tests every module must have                           | DAT-2a (cross project leak test), DAT-10 (`HasPendingModelChanges()` is false), VAL-1 and TST-4 (one invalid body test per endpoint, expecting 400 and `validation_failed`; the only guard against a module that skips `AddValidation()`)                                                                                                                                                                                       | `tests/Orvano.Host.IntegrationTests`             |
| Startup checks                                                     | CFG-1, CFG-3, CFG-4 (`ValidateOnStart`, `_FILE` errors, variable names in messages), DAT-10 (pending migration check)                                                                                                                                                                                                                                                                                                           | `Orvano.Host`                                    |
| Trait report                                                       | TST-4                                                                                                                                                                                                                                                                                                                                                                                                                           | `/check verify` reads the `AC` traits            |
| Review (`/check review`, people)                                   | Everything else: log content (LOG-2), cache justification (PRF-2), N+1 queries (DAT-4), test quality (TST-3), license and maintenance of new packages (GEN-5)                                                                                                                                                                                                                                                                   | PR template checklist from feature 7             |

### Rollout

New code immediately. There is no existing code, so every rule holds from the first commit of the scaffold (feature 6), and analyzers are on before the first feature lands.

### Exceptions

- `Orvano.Host`'s `Program.cs` may read `IConfiguration` directly while bootstrapping.
- The migrate command may call `Migrate` (DAT-10).
- Tests may use `!`, may skip `ConfigureAwait`, and may read `DateTimeOffset.UtcNow` only to seed a `FakeTimeProvider`.
- Benchmarks are exempt from ASY-2 and from the TST rules.
- Test fixtures may open raw `NpgsqlConnection`s to create, clone, and reset test databases (DAT-11's ban covers production code).
- Generated SDK code is exempt from style rules only; LIB-11 still applies.
- Anything else: a `// Deviation <RULE-ID>: <reason>` comment and a PR mention (GEN-0).

## Consequences

**Positive**:

- One feature folder holds everything for a feature, which matches the scope's slices and keeps diffs small and reviewable.
- Analyzers, banned APIs, and architecture tests catch most rule breaks before review, so reviews spend time on design.
- No commercially licensed dependencies; self hosters and forks face no licensing surprises.
- Real PostgreSQL in every integration test means the engine neutral data API and migrations are tested against the real thing.
- OpenTelemetry meters and traces from day one give features 45 to 47 something to build on rather than retrofit.

**Negative / tradeoffs**:

- More projects (one per module) mean slower cold builds and more files to create per module.
- The own Result type is about 150 lines to write and maintain, plus a mapping step in every endpoint.
- No mediator means shared behavior must be added on purpose as endpoint filters; there is no single pipeline to hang it on.
- Integration tests need a container engine (Docker or Podman) on every developer machine and CI runner. Docker works with no extra setup; Podman needs a little extra for Testcontainers (TST-5a).
- Warnings as errors and strict analyzers slow down quick experiments; deviations need written reasons.
- `net10.0` only shuts out apps on .NET Framework or out of support runtimes.
- EF Core 10 has AOT limits, so the server cannot move to Native AOT without a data access rethink.

**Neutral**:

- Contributors used to controllers, MediatR, or FluentAssertions must learn the Orvano way; the canonical pattern above is the reference.
- The default performance budget is a starting guess until Slice 1 is measured.
- Two installed agent skills conflict with this spec (see `## Decision`); agents must apply GEN-0.

## Follow-up

- [x] (Decided in spec 0009: Apache 2.0, and GEN-5 now points to its DEP rules.) Orvano's own open source license is not decided anywhere in the scope, yet LIB-2 package metadata and GEN-5 dependency vetting need it. Spec 0007 REP-1 leaves the `LICENSE` file to feature 57; decide it there.
- [x] Feature 6 scaffold (spec 0007 Follow-up, REP-1 layout): checked whether .NET 10's validation source generator picks up request types for endpoints mapped inside module class libraries. It does not unless the module calls `AddValidation()` itself; VAL-1 and the canonical `AddAuthModule` now say so (2026-10-16).
- [ ] Feature 9: confirm or refine the interim project isolation mechanism (DAT-2a, API-9) and the not found versus forbidden rule.
- [x] Features 6 and 7: confirm Testcontainers.PostgreSql runs under Docker on this Mac with the PG-1 tag, and write the setup into the README beside the spec 0007 DEV-1 dev compose (TST-5a). Confirmed 2026-10-16 by the scaffold's integration tests on `postgres:18.6`; the README covers Docker first and Podman second. Proving it under Podman is optional now and only matters for contributors who use Podman.
- [x] PRF-1 amended 2026-10-16 to allow ReadyToRun, following spec 0007 (which chose it after this spec weighed it as the runner up).
- [x] Feature 6: decide whether Aspire (version 13) runs local development, and if so whether its ServiceDefaults project hosts the LOG-4 and LOG-6 setup. Closed by spec 0007 DEV-1 and DEV-2: no Aspire AppHost or ServiceDefaults; LOG-4 and LOG-6 setup lives in `Orvano.Host`, and the standalone Aspire dashboard is only an optional OTLP viewer in `deploy/compose.dev.yaml`.
- [ ] Feature 7 (`/audit`): lift these rules into root `AGENTS.md`; record the 13 installed skills under `## Agent skills` with the GEN-0 overrides; note `microsoft/postgres-mcp` as an MCP server to connect (read only role) once a local database exists.
- [x] Feature 10: settle JSON naming, error body and code format, the final kind to status mapping (ERR-4 holds interim defaults), health check paths, pagination, the API key header, idempotency keys (LIB-7 depends on it), and confirm the generator can emit LIB-11 compliant C#.
- [ ] Feature 5: set the PII redaction approach (LOG-2) and the NuGet audit failure level (GEN-4).
- [x] Feature 8: confirm schema per module (DAT-1) and choose the ID format. Closed by spec 0011: MOD-1 confirms one schema per module, and IDS-1 chooses app made uuid v7 ids (`Guid.CreateVersion7()`).
- [ ] Feature 12: confirm the reference box used by the PRF-5 budget.
- [ ] Feature 52: reserve the `Orvano` NuGet ID prefix and set up trusted publishing (the installed `nuget-trusted-publishing` skill covers it).
- [ ] The registry still lists `dotnet/skills@configuring-opentelemetry-dotnet`, but the repo no longer has it; check again at `/audit`.

## Rationale

Reasoning, options, research, and sources: see [rationale.md](rationale.md).
