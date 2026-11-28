# Scope: Console

The shell and the shared pages of the dashboard. Each service's own pages live in that service's file and ship with it. The look comes from `docs/design/design-system/` and `docs/design/mockups/`. Part of the [Orvano scope](index.md).

## M0 (Phase 1: Foundation)

### 5. Console shell & design foundation · in-progress
The design system wired into the real console, and the empty app shell served by the host. It closes the M0 thread at the UI end.
**Done when:** the console static export is served at `/console` through Caddy; the tokens from `docs/design/design-system/` are mapped onto the component library's variables; the app shell (service rail, top bar with a project switcher slot and an approvals badge slot, theme toggle) renders in light and dark; base components (button, badge, table, form, side sheet, code block) handle focus and keyboard.
Spec [0003](../specs/0003-console-shell-design-foundation/index.md) · code in `console/`, `server/src/Orvano.Host/ConsoleFiles/`, `server/src/Orvano.AppHost/`, `server/tests/Orvano.IntegrationTests/`, `deploy/orvano.Dockerfile`, `deploy/smoke-test.sh`, `.github/workflows/ci.yml`
- [x] Design it (spec): `/architect console shell & design foundation`
- [x] Build it: `/develop console shell & design foundation`
  - [x] The thread: the host serves the export at `/console` (redirects, not found, missing files, headers, caching), the Node stage in the image, smoke and host tests (AC-1, AC-2, AC-3, AC-4, AC-15)
  - [x] Design foundation: generated tokens and colour check, shadcn mapping, fonts, favicon, theme menu, providers (AC-5, AC-6, AC-7)
  - [x] Shell and routes: rails, top bar, drawer, skip link, page titles, project guard, stubs, not found (AC-8, AC-9, AC-10, AC-11)
  - [x] Base components and the kit page (AC-12)
  - [x] Playwright and axe tests, the console CI job, the AppHost console resource (AC-13, AC-14, AC-15)
- [x] Verify it: `/check verify console shell & design foundation`

## M9 (Phase 3: Launch)

### 86. Project overview page · needs a decision
The first page of a project: how it is doing and how to connect to it.
**Done when:** the overview shows requests, errors, users, storage and function runs from per project usage counts; quick start snippets for Flutter, Next.js and TypeScript are there; the projects list shows health and usage as in the `Projects` mockup.
- [ ] Design it (spec): `/architect project overview page`

### 87. Unified logs page · needs a decision
One place to search every log a project produces.
**Done when:** one page searches logs across API, functions and sites, and filters by request id.
- [ ] Design it (spec): `/architect unified logs page`

### 88. Command palette · needs a decision
`⌘K` to reach any page or action from the keyboard.
**Done when:** the palette opens any page and runs any action the signed in role is allowed to use.
- [ ] Design it (spec): `/architect command palette`

### 89. Console completeness pass · needs a decision
The sweep that makes the dashboard complete: fill the gaps, then hold every page to the design system.
**Done when:** every v1 page in the spec exists, including project settings (platforms, API keys, members, export `orvano.json`, delete project) and instance settings; a Viewer sees a read only console; empty, loading and error states follow the design system; keyboard and contrast checks pass; browser tests cover the main flows.
- [ ] Design it (spec): `/architect console completeness pass`

## Deferred
- **Orvano Assistant**: a chat panel grounded in the project's schema, logs and docs
