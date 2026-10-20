# Orvano scope: Foundations

Part of the [Orvano scope](index.md). The legend, workflow default, and build approach live there.

Features 1 to 4 and 54 are the research you asked for before any code. Each one becomes a spec whose `rationale.md` holds the research, and `/audit` (feature 7) turns them into the conventions every later feature follows.

## Foundation

### 1. .NET backend practices · done

Research and record how Orvano writes .NET 10 code, for both the server and the .NET server SDK library, so every backend feature follows one playbook.
**Done when:** a spec records the chosen practices (project layout, API style, data access, async and cancellation, error handling, logging, configuration, testing, performance, library packaging) with the research behind each, ready for `/audit` to turn into conventions.
spec [0002](../specs/0002-dotnet-backend-practices/index.md)

- [x] Design it (spec): `/architect .NET backend practices`

### 2. React console practices · done

Research and record how the console is written with React and Vite, so every console screen is built the same way.
**Done when:** a spec records the chosen practices (structure, routing, data fetching and caching, state, forms, WCAG 2.2 AA accessibility, translation ready strings, testing, bundle size) with the research behind each.
spec [0003](../specs/0003-react-console-practices/index.md)

- [x] Design it (spec): `/architect React console practices`

### 3. Dart and Flutter practices · done

Research and record how the Flutter client SDK and the Dart server SDK are written, since they share one language and toolchain.
**Done when:** a spec records the chosen practices (package layout, null safety, async and streams, platform support for iOS, Android, web, and desktop, secure session storage, pub.dev package scoring, testing) with the research behind each.
spec [0004](../specs/0004-dart-flutter-practices/index.md)

- [x] Design it (spec): `/architect Dart and Flutter practices`

### 4. TypeScript SDK practices · done

Research and record how the JavaScript client, Next.js, and JavaScript server SDKs are written, so three packages for very different runtimes stay consistent.
**Done when:** a spec records the chosen practices (module formats, tree shaking, runtime targets from browser to Node to edge, typing, Next.js App Router and server components, testing) with the research behind each.
spec [0005](../specs/0005-typescript-sdk-practices/index.md)

- [x] Design it (spec): `/architect TypeScript SDK practices`

### 54. Console UX patterns · done

Research and record how the console behaves, studying consoles like Appwrite, Supabase, and other developer tools: page layout and navigation, a resource list that stays visible while one item is open (the Appwrite style table list), sidebars, tabs, forms, data grids, empty and loading states, and confirming destructive actions. Feature 11 builds its components on these patterns.
**Done when:** a spec records the chosen patterns with the research behind each, so every console screen (tables, users, buckets, functions, sites) follows the same layout and interaction rules.

- [x] Design it (spec): `/architect console UX patterns`
      Spec [0001](../specs/0001-console-ux-patterns/index.md)

### 5. Security baseline · done

The bar every feature meets. Orvano stores other people's users, data, and secrets, and later runs their code, so this is set once before anything is built.
**Done when:** a spec records a verification standard and level, a threat model for many projects on one install, password hashing, token signing and rotation, secrets encrypted at rest and keys shown once, and dependency scanning; later specs point to it for their security criteria.
spec [0006](../specs/0006-security-baseline/index.md)

- [x] Design it (spec): `/architect security baseline`

### 57. Open source license · done

Choose Orvano's license and contributor terms before the first public push. It decides whether others can resell Orvano as a hosted service, what every package's metadata says (spec 0002 is waiting on it), and which dependency licenses are allowed.
**Done when:** a spec records the license for the server, the console, and the SDKs (they may differ), the contributor terms (sign off or contributor agreement), and the dependency licenses that are allowed, and a `LICENSE` file is ready for each repo.
spec [0009](../specs/0009-open-source-license/index.md) · code in `LICENSE`, `NOTICE`, `.github/dependency-review-config.yml`, `deploy/Dockerfile`, `tooling/starters/`

- [x] Design it (spec): `/architect open source license`
- [x] Apply it: `/develop open source license` (spec 0009 Rollout steps 1 to 5; step 6 is feature 58)
- [x] Verify it: `/check verify open source license`

### 6. Stack & architecture · done

Design the system around your given choices (.NET 10, React with Vite, PostgreSQL): service boundaries, how one self hosted install runs, background jobs, caching, file storage, and the names of the SDK repos. Then scaffold the `orvano` core monorepo.
**Done when:** the architecture is recorded in a spec, and the core monorepo scaffold boots the server and console locally and builds clean.
spec [0007](../specs/0007-stack-architecture/index.md) · code in `server/`, `console/`, `deploy/`

- [x] Decide the stack (spec): `/architect stack & architecture`
- [x] Scaffold from the decision: `/develop stack & architecture`
- [x] Smoke check it runs: `/test`

### 7. Coding standards & tooling · done

Capture the conventions from features 1 to 5 and the real scaffold, then install lint, format, analyzers, pre-commit hooks, and CI for the core monorepo, plus a starter setup each SDK repo copies. For the JavaScript side (console and TypeScript SDKs), use Vite+ (`vp`, MIT, 1.0 since 2026-09-28) as the toolchain: Oxfmt for formatting, Oxlint first with a small ESLint config only for rules Oxlint lacks (React Compiler diagnostics, Lingui `no-unlocalized-strings`, TanStack plugins, typescript-eslint strict type checked), `vp run` for tasks, and `vp hooks` / `vp staged` for git hooks. Settle whether type checking uses `vp check` (TypeScript 7) or `tsc` (TypeScript 6, specs 0003 WEB-2 and 0005 SDK-1).
**Done when:** root `AGENTS.md` reflects the real stack and the researched practices, and `pnpm check` and `pnpm test` run clean locally with the hooks working (spec 0008 Rollout step 9). Once feature 58 pushes the repo, CI and the security checks run clean there and the Windows CI job decides whether Vite+ stays or the plan falls back to standalone Vite, Vitest, tsdown, and Lefthook, with the reason recorded (spec 0008 TL-5, step 11).
spec [0008](../specs/0008-coding-standards-tooling/index.md) · code in `package.json`, `vite.config.ts`, `eslint.config.ts`, `tooling/`, `.github/`, `.vite-hooks/`

- [x] Design it (spec): `/architect coding standards & tooling`
- [x] Capture conventions + tooling choices: `/audit`
- [x] Install the tooling: `/develop tooling`
- [x] Check it runs clean: `/test`

### 58. Repo & community setup · done

Set up the GitHub side of every repo in the org from the license spec (feature 57) and the tooling (feature 7): branch protection, code owners, a security policy, a contributing guide, issue and PR templates, and contributor sign off. SDK repos copy the same template.
**Done when:** the `orvano` repo has protected `main`, code owners, `SECURITY.md` (how to report a vulnerability), `CONTRIBUTING.md`, templates, and sign off checks running, and a template repo lets each SDK repo start the same way.
spec [0010](../specs/0010-repo-community-setup/index.md) · code in the root community files, `.github/`, `tooling/scripts/` (`github-settings.mjs`, `export-template.mjs`, `dco.test.mjs`), `tooling/starters/`

- [x] Design it (spec): `/architect repo & community setup`
- [x] Build it: `/develop repo & community setup`
  - [x] Community files, issue forms, CODEOWNERS, and the `dco` job on `main` (AC-1 to AC-5, AC-7)
  - [x] `github-settings.mjs`, `export-template.mjs`, and the SDK starter CodeQL and dependency review workflows (AC-8, AC-16 to AC-19)
  - [x] Clean history: skills license check, rewrite, sweep, repo recreated, org settings (AC-12 to AC-14)
  - [x] Go public and prove it: the flip, settings applied, Discussions, the proof PR (AC-6, AC-9 to AC-11, AC-15)
  - [x] Org `.github` and the three template repos, then the CodeQL gate once uploads are proven (AC-16, AC-17, AC-20)
- [x] Verify it: `/check verify repo & community setup`
- [x] Test it: `/test repo & community setup`
- [x] Review it (fresh model): `/check review repo & community setup`
- [x] Document it: `/document repo & community setup`

### 8. Platform data model · done

Orvano's own records, kept apart from the databases projects create: console accounts, organizations, projects, API keys, settings, and the usage, audit, and messaging records later slices fill.
**Done when:** the entities and relationships support every slice in this scope (team roles, usage, audit, messaging targets) without a breaking redesign.
spec [0011](../specs/0011-platform-data-model/index.md) · code in `server/src/Orvano.SharedKernel/Data/`

- [x] Design it (spec): `/architect platform data model`
- [x] Build it: `/develop platform data model`
  - [x] Conventions code in `Orvano.SharedKernel/Data/` (ids, timestamps, convention plugin, text enums, anchors, data classes, email, `EncryptedValue`, `ISecretProtector`) (AC-1 to AC-5, AC-9)
  - [x] PostgreSQL 18 convention tests on a sample context, incl. index forms (AC-1 to AC-5, AC-11)
  - [x] Architecture tests: anchor only cross schema FKs, data classes, keys, schemas (AC-6 to AC-8) (AC-10 is checked by hand in verify.md)
- [x] Verify it: `/check verify platform data model`
- [x] Test it: `/test platform data model`
- [x] Review it (fresh model): `/check review platform data model`
- [x] Document it: `/document platform data model`

### 9. Access control model · done

One permissions model shared by databases, storage, realtime, and functions: who a caller is (anyone, guest, signed in user, a specific user, a label, a server key), what they may do, and where the check runs. Features 16, 26, 29, 34, and 38 build on it.
**Done when:** one model decides who can read or write any resource for both end user sessions and server API keys, and any database engine can enforce it.
spec [0012](../specs/0012-access-control-model/index.md) · code in `server/src/Orvano.SharedKernel/Access/`

- [x] Design it (spec): `/architect access control model`
- [x] Build it: `/develop access control model`
  - [x] Grant grammar and `GrantSet` (parse, `write` expansion, writer rules, creator defaults, limit) (AC-1, AC-6, AC-7)
  - [x] `Caller`, role sets, `AccessEvaluator`, and the denial rules (AC-2, AC-3, AC-5)
  - [x] Scope catalog and ceilings, console permissions, and their endpoint filters (AC-8, AC-11)
  - [x] Contexts, architecture tests, the PostgreSQL `WhereGranted` test, and the label cache test (AC-4, AC-9, AC-10, AC-12)
    - built: contexts, architecture tests, `WhereGranted` test, and the two host label cache contract test over spec 0013's signals (build plan step 8, `server/tests/Orvano.Host.IntegrationTests/Access/`), 2026-10-19
  - [x] Amend specs 0011 and 0006 for the new columns, CHECK constraints, and closed follow ups (AC-8, AC-9) (done by `/architect`, 2026-10-18)
  - [x] Access layer entry and endpoint default deny: `ICallerResolver` and the resolving middleware, `AllowPublicAccess`, generic endpoint requirements for `MapGroup`, the startup check, and the endpoint list test (AC-9, AC-13; build plan steps 10 and 11, added by `/architect` 2026-10-19)
- [x] Verify it: `/check verify access control model`
- [x] Test it: `/test access control model`
- [x] Review it (fresh model): `/check review access control model`
- [x] Document it: `/document access control model`

### 61. Background runtime · in-progress

The shared machinery spec 0007 designed but no feature built yet: cross replica signals over `LISTEN/NOTIFY`, cache invalidation that reaches every replica, the PostgreSQL job queue with schedules, and the per module outbox. Features 9 (the label cache test), 13 (CORS cache), 14 (session revocation, signing key rotation), and every later feature that queues work or announces events build on it.
**Done when:** a cache tag invalidated or a job enqueued on one server instance reaches a second instance through `orvano_signal`, a lost signal is recovered by SIG-2's bypass, poll, and clear, jobs run at least once with leases, backoff, and schedules firing once across workers, and an outbox event reaches its subscriber as a job.
spec [0013](../specs/0013-background-runtime/index.md) (builds spec [0007](../specs/0007-stack-architecture/index.md) SIG, CCH, JOB, EVT) · code in `server/src/Orvano.Infrastructure/`

- [x] Design it (spec): `/architect stack & architecture` (spec 0007 SIG, CCH, JOB, EVT); signal and cache contracts in spec 0013 (2026-10-19); job queue contracts, tables, runner, and schedules in spec 0013 _Job queue design_ (2026-10-19); job queue review settlement in spec 0013 steps 21 to 23 (2026-10-20); module outbox contracts, table, publish, dispatcher, delivery jobs, and cleanup in spec 0013 _Outbox design_ (2026-10-20)
- [ ] Build it: `/develop background runtime`
  - [x] Signals and cache invalidation: `ISignalBus`, `ICacheInvalidator`, the listener, heartbeat, disconnect bypass, and reconnect clear (SIG-1 to SIG-3, CCH-1, CCH-2; spec 0013 AC-1 to AC-13, build plan steps 1 to 7); unblocks feature 9's AC-10
    - built: spec 0013 build plan steps 1 to 7 (contracts and codec, transaction join, listener, cache, two host test, failure handling, telemetry), 2026-10-19
  - [x] Review settlement: handler limit and dispatch lag, reconnect floor, boot announce, confirmed commit count, joined context refusal, module isolation (spec 0013 AC-3, AC-8 to AC-13, build plan steps 8 to 12)
    - built: spec 0013 build plan steps 8 to 12 (`HandlerTimeout` and the LSN-8 lag disconnect, the LSN-5 stable period and boot announce, `AddOnCommitted`, the SND-5 joined refusal, the object inner cache key and the wider modules ban list), 2026-10-19
  - [x] Job queue, runner, leases, backoff, and schedules (JOB-1 to JOB-6; spec 0013 AC-14 to AC-30, build plan steps 13 to 20)
    - built: spec 0013 build plan steps 13 to 20 (job contracts, `JobsDbContext` and the `jobs` migration, `IJobQueue` in and out of a transaction, the runner with leases, renewal, backoff, timeouts, and shutdown release, `CancelProjectJobsAsync`, schedules on `Cronos` 0.13.0 with the `jobs.cleanup` schedule, telemetry, and `JobsDriver`), 2026-10-19
  - [x] Job queue review settlement: dedupe key scoped per kind and project, one pool connection kept for the runner, dead marking on the poll (spec 0013 AC-16, AC-20, AC-26, build plan steps 21 to 23)
    - built: spec 0013 build plan steps 21 to 23 (the `ScopeJobDedupeKey` migration on `(kind, project_id, dedupe_key)` with `NULLS NOT DISTINCT`, the `PoolSize` / 2 − 1 limit with `PoolSize` at least 4, `MarkExpiredDeadAsync` on the poll tick and once per `JobsDriver` pass), 2026-10-20
  - [ ] Module outbox and dispatcher: `IOutbox`, `IOutboxRegistry`, `HasOutbox()`, publish in the caller's transaction, the dispatcher on `WorkerWake`, one delivery job per subscriber, `outbox.cleanup`, and the pool limit at half less two (EVT-1 to EVT-3; spec 0013 AC-31 to AC-43, build plan steps 24 to 30); EVT-4's readers are features 33 to 36
- [ ] Verify it: `/check verify background runtime`
- [ ] Test it: `/test background runtime`
- [ ] Review it (fresh model): `/check review background runtime`
- [ ] Document it: `/document background runtime`

### 11. Design system & console shell · needs a decision

Visual language, base components, layout, navigation, and light and dark themes for the console, so every screen feels the same and works for everyone.
**Done when:** `design.md` covers type, color, spacing, and components; the console shell meets WCAG 2.2 AA with keyboard and screen reader use; every string lives in message files.

- [ ] Design it (spec): `/architect design system & console shell`
