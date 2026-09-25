# Epic: Foundations

The ground every version stands on. Nothing here ships as a release by itself; it makes v0.1 possible. See [index.md](index.md) for the full plan.

### 1. Stack & architecture · in-progress · GA
Decide the server language, the console framework, the database engine setup, the gateway, the worker and event model, and how containers are laid out on one server. Then scaffold a runnable monorepo.
**Done when:** the stack is recorded in a spec, and the empty scaffold (API, worker, console) boots locally with one command and passes build.
- [x] Decide the stack (spec): `/architect stack & architecture`
- [x] Scaffold from the decision: `/develop stack & architecture`
- [ ] Verify it: `/check verify stack & architecture`
- [x] Test it: `/test stack & architecture`
- [x] Review it (fresh model): `/check review stack & architecture`
- [ ] Document it: `/document stack & architecture`
Spec [0002](../specs/0002-stack-architecture/index.md) · code in `server/`, `console/`, `dev/`, `deploy/`

### 2. Coding standards & tooling
Capture conventions from the real scaffold, then install lint, format, type strictness, pre commit hooks, and CI for every package (server, console, and each SDK).
**Done when:** root `AGENTS.md` reflects the real stack, and lint, format, and CI run clean across the monorepo.
- [ ] Capture conventions + tooling choices: `/audit`

### 3. Platform data model · needs a decision · GA
The internal model that every product hangs off: console accounts, organizations, members and roles, projects, API keys and scopes, platforms (allowed web origins and app bundle IDs), and project scoped app users. Environments must fit in later without a breaking migration.
**Done when:** the model supports many orgs and many projects per install, keeps each project's data isolated, and leaves room for environments, usage metering, and a future cloud.
- [ ] Design it (spec): `/architect platform data model`

### 4. API contract & SDK pipeline · in-progress
One machine readable description of every public endpoint is the source of truth. From it you generate the API reference and the SDKs: core JS/TS, Next.js (wraps core, adds server components and cookie sessions), Flutter, Dart server, and .NET.
**Done when:** changing one endpoint in the contract regenerates all five SDKs and the reference, and a sample call works from each SDK against the scaffold.
- [x] Design it (spec): `/architect API contract & SDK pipeline`
- [ ] Build it: `/develop API contract & SDK pipeline`
   - [ ] Thin thread: health operation from TypeSpec through SdkGen to all five SDK surfaces and the server, scenarios green in CI (AC-1, 2, 3, 5, 9, 10, 13, 16)
   - [ ] Shared conventions: errors, audience routing and client pattern, the private console client, cursor pagination, typed events, retries and timeouts (AC-2, 4, 6, 7, 8, 14, 17)
   - [ ] Release pipeline: version stamping and headers, publishing and mirrors, docs snippets, public contract file, breaking change check (AC-11, 12, 15, 17)
- [ ] Verify it: `/check verify API contract & SDK pipeline`
- [ ] Test it: `/test API contract & SDK pipeline`
Spec [0001](../specs/0001-api-contract-sdk-pipeline/index.md)

### 5. Design system & console shell · needs a decision
Visual language, layout, and base components for the console, plus the empty shell: navigation, org and project switcher, light and dark themes.
**Done when:** `design.md` covers type, color, spacing, and components; the shell renders with working navigation; base components handle focus and keyboard.
- [ ] Design it (spec): `/architect design system & console shell`

### 6. Self host installer · needs a decision
A one command install on a single server that sets secrets, pulls containers, and starts Orvano. Designed so later versions upgrade in place.
**Done when:** on a clean Linux server, one command brings up a working Orvano reachable at your domain, and running it again is safe.
- [ ] Design it (spec): `/architect self host installer`
