# Epic: Foundations

The ground every version stands on. Nothing here ships as a release by itself; it makes v0.1 possible. See [index.md](index.md) for the full plan.

### 1. Stack & architecture · done · GA
Decide the server language, the console framework, the database engine setup, the gateway, the worker and event model, and how containers are laid out on one server. Then scaffold a runnable monorepo.
**Done when:** the stack is recorded in a spec, and the empty scaffold (API, worker, console) boots locally with one command and passes build.
- [x] Decide the stack (spec): `/architect stack & architecture`
- [x] Scaffold from the decision: `/develop stack & architecture`
- [x] Verify it: `/check verify stack & architecture`
- [x] Test it: `/test stack & architecture`
- [x] Review it (fresh model): `/check review stack & architecture`
- [x] Document it: `/document stack & architecture`
Spec [0002](../specs/0002-stack-architecture/index.md) · code in `server/`, `console/`, `dev/`, `deploy/`

### 2. Coding standards & tooling · done
Capture conventions from the real scaffold, then install lint, format, type strictness, pre commit hooks, and CI for every package (server, console, and each SDK).
**Done when:** root `AGENTS.md` reflects the real stack, and lint, format, and CI run clean across the monorepo.
- [x] Capture conventions + tooling choices: `/audit`
Code in `.editorconfig`, `Directory.Build.props`, `eslint.config.js`, `.prettierrc.json`, `lefthook.yml`, `.github/workflows/ci.yml`

### 3. Platform data model · in-progress · GA
The internal model that every product hangs off: console accounts, organizations, members and roles, projects, API keys and scopes, platforms (allowed web origins and app bundle IDs), and project scoped app users. Environments must fit in later without a breaking migration.
**Done when:** the model supports many orgs and many projects per install, keeps each project's data isolated, and leaves room for environments, usage metering, and a future cloud.
- [x] Design it (spec): `/architect platform data model`
- [ ] Build it: lands with the rows that first use each part (Tracer Bullet), no separate `/develop` run
   - [ ] Row 7 slice: platform tables, domain rules, console sign up rules, project and org lifecycle jobs, API keys and platforms, events and fixtures: `/develop console accounts, orgs & projects` (AC-1 to 5, 7 to 15, 17 to 19)
   - [ ] Row 8 slice: `auth_users`, request authentication (project lookup, key verification, scopes, origins), console accounts as users of `console`: `/develop app user sign up & sign in` (AC-4 to 8, 12 to 14, 16, 17)
   - [ ] Row 15 slice: invitations and member management: `/develop console team members & roles` (AC-7, 9, 10)
- [ ] Verify it: `/check verify platform data model`
- [ ] Test it: `/test platform data model`
- [ ] Review it (fresh model): `/check review platform data model`
- [ ] Document it: `/document platform data model`
Spec [0003](../specs/0003-platform-data-model/index.md) · code in `server/src/Orvano.Platform/`, `server/src/Orvano.Auth/`, `server/migrations/platform/`

### 4. API contract & SDK pipeline · done
One machine readable description of every public endpoint is the source of truth. From it you generate the API reference and the SDKs: core JS/TS, Next.js (wraps core, adds server components and cookie sessions), Flutter, Dart server, and .NET.
**Done when:** changing one endpoint in the contract regenerates all five SDKs and the reference, and a sample call works from each SDK against the scaffold.
- [x] Design it (spec): `/architect API contract & SDK pipeline`
- [x] Build it: `/develop API contract & SDK pipeline`
   - [x] Thin thread: health operation from TypeSpec through SdkGen to all five SDK surfaces and the server, scenarios green in CI (AC-1, 2, 3, 5, 9, 10, 13, 16)
   - [x] Shared conventions: errors, audience routing and client pattern, the private console client, cursor pagination, typed events, retries and timeouts, test only operations and temporary auth formats (AC-2, 4, 5, 6, 7, 8, 14, 17, 18)
   - [x] Release pipeline: version stamping and headers, publishing and mirrors, docs snippets, public contract file, breaking change check (AC-11, 12, 15, 17)
- [x] Verify it: `/check verify API contract & SDK pipeline`
- [x] Test it: `/test API contract & SDK pipeline`
Spec [0001](../specs/0001-api-contract-sdk-pipeline/index.md) · code in `contract/`, `tools/sdkgen/`, `sdks/`, `server/src/Orvano.Contract/`, `tests/scenarios/`, `.github/workflows/sdks.yml`, `.github/workflows/release.yml`

### 5. Design system & console shell · needs a decision
Visual language, layout, and base components for the console, plus the empty shell: navigation, org and project switcher, light and dark themes.
**Done when:** `design.md` covers type, color, spacing, and components; the shell renders with working navigation; base components handle focus and keyboard.
- [ ] Design it (spec): `/architect design system & console shell`

### 6. Self host installer · needs a decision
A one command install on a single server that sets secrets, pulls containers, and starts Orvano. Designed so later versions upgrade in place.
**Done when:** on a clean Linux server, one command brings up a working Orvano reachable at your domain, and running it again is safe.
- [ ] Design it (spec): `/architect self host installer`
