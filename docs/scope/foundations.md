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
   - [x] Row 7 slice: platform tables, domain rules, console sign up rules, project and org lifecycle jobs, API keys and platforms, events and fixtures: `/develop console accounts, orgs & projects` (AC-1 to 5, 7 to 15, 17 to 19)
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

### 5. Design system & console shell · done
Visual language, layout, and base components for the console, plus the empty shell: navigation, org and project switcher, light and dark themes.
**Done when:** `design.md` covers type, color, spacing, and components; the shell renders with working navigation; base components handle focus and keyboard.
- [x] Design it (spec): `/architect design system & console shell`
- [x] Build it: `/develop design system & console shell`
   - [x] Thin thread: Tailwind v4 and shadcn (Base UI) set up, dark tokens, fonts, theme bootstrap, the session guard and org switcher on real orgs, the AppHost fixtures switch, browser tests with axe in CI (AC-8, 10, 13, 20, 25)
   - [x] Design system: full tokens in both themes and densities, theme and density menu, contrast and color literal tests, `console/design.md` (AC-1 to 4, 7)
   - [x] Components and catalog: the full inventory, keyboard scripts, form, toast, table, and role patterns, the dev only `/dev/components` catalog (AC-5, 6, 7, 9, 21, 22)
   - [x] Shell complete: org and project routes, nav registry, sidebar rail and drawer, both switchers with paging, status gating, not found, errors, landing, titles and focus, 360 px layout (AC-10 to 23)
   - [x] Production shape: Caddy security and cache headers, the `console` compose profile with the gateway, end to end tests in CI (AC-8, 9, 24, 26)
- [x] Verify it: `/check verify design system & console shell`
- [x] Test it: `/test design system & console shell`
Spec [0005](../specs/0005-console-design-system-shell/index.md) · code in `console/`, `deploy/gateway/Caddyfile`, `dev/Orvano.AppHost/`, `tests/scenarios/compose.yml`

### 6. Self host installer · in-progress
A one command install on a single server that sets secrets, pulls containers, and starts Orvano. Designed so later versions upgrade in place.
**Done when:** on a clean Linux server, one command brings up a working Orvano reachable at your domain, and running it again is safe.
- [x] Design it (spec): `/architect self host installer`
- [ ] Build it: `/develop self host installer`
   - [x] Thin thread: image only compose file with log rotation and Postgres tuning keys, `orvano install` writing secrets and files, `install.sh` running Compose, CI installing twice on amd64 and arm64 (AC-2, 8 to 14, 16, 17, 25, 27, 30)
   - [ ] Interactive and preflight depth: prompts, domain and email checks, DNS check, Docker install offer, warnings, master key confirmation, the gateway's ACME email and HSTS (AC-1 to 7, 15, 18, 26, 28)
   - [ ] First admin gate: setup token on the server, `consoleInstall.getSetup`, `setup-status`, then the console `/setup` route and sign in notice once console sign up exists (AC-19 to 24)
   - [ ] Publishing: images to GHCR, the GitHub Release with `install.sh` and its checksum, the README install section (AC-1, 29)
- [ ] Verify it: `/check verify self host installer`
- [ ] Test it: `/test self host installer`
Spec [0006](../specs/0006-self-host-installer/index.md) · code in `deploy/`, `server/src/Orvano.Server/`, `server/src/Orvano.Platform/`, `contract/platform/install.tsp`, `console/`, `.github/workflows/`
