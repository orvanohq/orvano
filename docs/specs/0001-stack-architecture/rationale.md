# 0001. Rationale: Orvano stack and architecture

The decision record for [index.md](index.md). A build never needs this file. Read it when you want to know why, or before you change the decision.

## Context

Orvano is a new, self hosted backend as a service for app developers and the coding agents that build for them. There is no code yet. The design spec (`docs/design/spec.md`) locks nearly the whole stack, and the scope asks this spec to record it with no comparison of settled parts, settle what is still open, and give the scaffold a clear target.

The forces that shape the open parts:

- **One small server.** v1 targets a single node with 2 vCPU and 4 GB of memory. Every extra container costs memory and adds one more thing for a self hoster to keep alive.
- **Events must never drift from data.** Realtime, webhooks, function triggers and email all hang off one event pipeline. An event that is lost, or sent for a change that rolled back, breaks all four at once.
- **Two write paths.** Platform data goes through EF Core. Tenant data goes through raw Npgsql transactions under a restricted Postgres role with row level security. Both must write their event in the same transaction as the change.
- **Realtime resume.** A client that reconnects asks for events after its last event id, so events need a stable order and a short kept history.
- **A public `Apache-2.0` server.** Everything inside it must be safe to ship under that licence, now and later.
- **A native CLI that shares code with the server.** Whatever the CLI compiles must work with Native AOT and stay small.
- **A small team, with coding agents doing much of the build.** Structure has to be simple to explain and checked by tests, not by habit.

The design spec also disagrees with itself in three places (Valkey, password hashing, the sandbox runtime) and is silent on how the .NET projects outside `server/` and the JavaScript packages are built. Left open, each of these becomes a guess in the middle of the scaffold.

## Options considered

The settled layers are not compared again. The options differ in one layer only, the job queue.

### Option 1: the locked stack with Orvano's own Postgres queue

Orvano owns one outbox table and one jobs table. Workers claim rows with `FOR UPDATE SKIP LOCKED`. A small writer in `Orvano.Infrastructure` adds the outbox row to whatever transaction the caller has open. (basis: transactional outbox pattern · Postgres `SKIP LOCKED` queue pattern)

**Pros**:
- The outbox row commits inside any transaction, including a tenant transaction under the project role.
- The same table can serve as the ordered event log that realtime resume reads (keeping that order correct when commits finish out of order is feature 10's job).
- No new dependency, no licence exposure, no startup code generation.
- Table shapes and migrations follow the same module rules as everything else.

**Cons**:
- You write and test leasing, retries, backoff, cleanup and cron yourself.
- No dashboard, no community to find your bugs first.
- It is easy to get subtly wrong: a lease that expires while a job still runs sends the job twice.

### Option 2: the locked stack with Wolverine

Wolverine 6.65.1 is an MIT library from JasperFx with a durable inbox and outbox, durable queues, retries and scheduled messages, all on Postgres alone and with EF Core 10 support. (basis: web check on 2026-11-27, see Links)

**Pros**:
- The hard parts are already written and used in production by others.
- Retries, scheduling and error handling come as configuration, not code.
- Active project, with paid support available if you ever want it.

**Cons**:
- Its outbox is documented for EF Core and Marten. Support for a transaction your own code opened with Npgsql could not be confirmed from the docs, and that is the tenant write path. It may exist, but the decision does not rest on this point alone.
- It generates code at runtime by default. Avoiding that needs an extra build step and a startup check.
- It creates and manages its own schemas, outside the one schema per module rule.
- Its envelope tables are a delivery queue, not an ordered event log, so realtime resume would still need Orvano's own table.
- Its handler conventions would sit beside the module and `Orvano.Contracts` rules as a second way to wire things.

### Option 3: the locked stack with Hangfire on Postgres

Hangfire is a long established job runner with a dashboard. Postgres storage comes from a separate third party package. (basis: web check on 2026-11-27, see Links)

**Pros**:
- Very widely used, with a jobs dashboard included.
- Recurring and delayed jobs with little code.

**Cons**:
- No outbox. Orvano would still own its event table and the code that moves events into jobs.
- The core is LGPL and the Postgres storage is LGPLv3, inside an `Apache-2.0` server.
- Postgres support depends on a third party package, not on the Hangfire team.

Ruled out before comparison: TickerQ 11 (its README now describes a source available licence, not open source) and MassTransit 9 (commercial licence). Quartz.NET is a scheduler only, so it does not answer the outbox question.

## Rationale

**The queue.** The deciding force is the second write path. Tenant writes run in a raw Npgsql transaction under a restricted role, and the event must commit in that same transaction. Orvano's own writer does that by design. For Wolverine it is not a documented path, so choosing it would mean either proving it works in source and tests, or writing Orvano's own outbox for tenant writes anyway and running two event systems. The realtime resume force points the same way: the outbox has to be an ordered, briefly kept log, which is a product feature and not something a delivery library provides. Once Orvano owns the outbox for those two reasons, what is left for a library is the job runner, and that part is small: claim a row, run it, retry with backoff, give up after a limit. That is not enough to justify a framework with its own conventions and schemas, or LGPL parts in the server.

This goes against the usual advice to pick the proven library, so the cost is stated plainly in Consequences. The engineer accepted it knowing that the queue's correctness is now Orvano's job. The mitigation is that feature 10 designs it on its own, with tests for a crash in the middle of a job.

**The smaller calls.** Each was asked, and each answer matched the recommendation.

| Question | Picked | Runner up | Why |
|---|---|---|---|
| Valkey in v1 | None. In process behind interfaces | Optional adapters now | Scale out is deferred in the scope, and the scope's own rule says the out of scope list wins. One less container on a 4 GB server. (basis: `docs/scope/index.md`, Planning notes and Deferred) |
| Password hashing | Argon2id | Identity hasher (PBKDF2) | The Auth section is the locked, more specific text. Argon2id is memory hard, so it resists GPU cracking better. (basis: `docs/design/spec.md`, Auth · OWASP password storage guidance) |
| .NET solution | One `Orvano.slnx` at the root | Server solution plus small ones | The CLI shares code with the server. One build turns a break across them into a compile error on the same change. (basis: `docs/design/spec.md`, CLI and Repositories sections) |
| Shared server code | New `Orvano.Infrastructure` | Inside `Orvano.Contracts` | Shared contracts must stay free of EF Core and ASP.NET Core. After the cross check, the part the CLI compiles moved to `Orvano.Wire`. (basis: installed `dotnet-aot-compat` skill) |
| Scaffold depth | Add each module with its first feature | All ten shells now | Tracer Bullet builds real, narrow threads. Empty projects are placeholders. (basis: `docs/scope/index.md`, build approach) |
| JavaScript setup | One pnpm workspace | Separate installs | The console must build on the SDK from the same repo. (basis: `docs/design/agent-rules.md`, console command · `docs/design/spec.md`, Dashboard section) |

**Calls made without asking**, because expertise settles them. Each can be overturned in the spec review.

| Call | Pick | Runner up | Why |
|---|---|---|---|
| Sandbox runtime wording | Docker through `docker-socket-proxy` | containerd | The design spec's own open decisions list ticks this one, and the Functions section agrees. |
| EF Core layout | One `DbContext` and one migration set per module | One shared `DbContext` | Follows directly from one schema per module and no shared tables. |
| Calls between modules | Interfaces in `Orvano.Contracts`, no mediator library | A mediator library | Plain interfaces are easier to follow and to check with reference tests. The best known mediator library is no longer free for all uses. |
| Boundary checks | Tests that read assembly references | An architecture test library | The rules are simple reference rules. No dependency needed. |
| Logging | Built in .NET JSON console logging | Serilog | The design spec asks only for JSON to stdout plus OpenTelemetry. The built in parts cover that. |
| Background work hosting | Hosted services inside `orvano` | Separate worker process | The design spec locks one process. A switch per role can come with scale out. |
| Solution file format | `.slnx` | `.sln` | It is what .NET 10 creates by default, and it is far easier to read in a diff. |

**Calls added after the cross check.** An independent read on a different model found decisions the scaffold would otherwise have invented. The engineer approved every recommended fix on 2026-11-27.

| Call | Pick | Runner up | Why |
|---|---|---|---|
| Code shared with the CLI | Separate `Orvano.Wire` (MIT, safe for Native AOT); `Orvano.Contracts` stays server only | One `Orvano.Contracts` for both | Wire types ship inside the MIT CLI binary. Module interfaces would pull server types toward it. (basis: `docs/design/spec.md`, licence table · installed `dotnet-aot-compat` skill) |
| Product version | One `<Version>` in `Directory.Build.props`, overridden from the git tag on release | Versions computed from git tags by a tool | One plain number, no extra build dependency. |
| Aspire pin | Major 13 pinned in the AppHost `Sdk` attribute, `dotnet run` as the documented command | Float to the latest major, Aspire CLI as the command | Aspire changes shape between majors. `dotnet run` matches the agent rules and needs no extra tool. (basis: installed `aspire` skill) |
| M0 AppHost and ServiceDefaults | Postgres 17 with a pinned tag, Host, Executor; ServiceDefaults maps no endpoints | Aspire template defaults | Template endpoints `/health` and `/alive` clash with `/healthz` and `/readyz`, and the default image tag drifts. |
| Work across modules | Always through outbox events | A shared unit of work from day one | Separate contexts cannot share a transaction without extra machinery. Events keep modules apart, at the cost of a short delay. (basis: modular monolith practice) |
| Startup migrations | Infrastructure first, then modules in order, under a Postgres advisory lock, before hosted services | Migrate each module lazily | Parallel tests and restarts must not race, and the worker must not start against a half migrated schema. |
| Interfaces with no user | None until a feature needs one; rate limits use ASP.NET Core's built in limiter | Cache and pub/sub interfaces now | An interface shaped without a user tends to be wrong. |
| How reference rules are checked | Read csproj files and `project.assets.json` | Read compiled assembly references | The compiler drops unused references, so assembly metadata alone hides violations. |

## References

**Project sources** (verifiable, in this repo):
- `docs/design/spec.md`: Architecture and tech stack, Stack choices, Auth, Functions, CLI, Dashboard, Repositories and domains, Open decisions
- `docs/design/agent-rules.md`: stack line, architecture rules, commands
- `docs/scope/index.md`: build approach, Planning notes, Deferred
- `docs/scope/foundation.md` feature 1 and `docs/scope/platform.md` feature 10
- Installed skills in `.claude/skills/`: `aspire`, `dotnet-aot-compat`
- `docs/.agent-cache/research/job-queue-landscape.md`: the full web check notes

**Practices & standards**:
- Transactional outbox pattern
- Postgres `FOR UPDATE SKIP LOCKED` queue pattern
- Modular monolith with enforced reference rules
- Database backed queue first, a broker only when throughput demands it
- OWASP password storage guidance (Argon2id as first choice)

**Links** (web verified on 2026-11-27):
- WolverineFx on NuGet (version, licence, target frameworks): https://www.nuget.org/packages/WolverineFx
- WolverineFx.EntityFrameworkCore on NuGet (EF Core 10 dependency): https://www.nuget.org/packages/WolverineFx.EntityFrameworkCore
- Wolverine repository (MIT licence): https://github.com/JasperFx/wolverine
- Wolverine PostgreSQL durability guide: https://wolverinefx.net/guide/durability/postgresql.html
- Wolverine durability overview: https://wolverinefx.net/guide/durability/
- Wolverine code generation guide: https://wolverinefx.net/guide/codegen.html
- Hangfire.PostgreSql on NuGet: https://www.nuget.org/packages/Hangfire.PostgreSql
- TickerQ on NuGet: https://www.nuget.org/packages/TickerQ
- MassTransit v9 announcement: https://masstransit.io/introduction/v9-announcement
- Aspire MCP server: https://aspire.dev/get-started/aspire-mcp-server/
- Playwright MCP: https://github.com/microsoft/playwright-mcp
