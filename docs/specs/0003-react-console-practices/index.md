# 0003. React console practices

**Date**: 2026-10-15
**Status**: Accepted
**Updated**: 2026-10-16, verify at scaffold results recorded (RTE-3, RTE-7, FRM-5, I18N-2, I18N-6); RTE-6 and QA-2 retargeted. 2026-10-25, reconciled with spec 0014: FLT-1 maps the PRB-1 body; QRY-1 reads `api/console.openapi.json`; RTE-10 boot calls are CPX-3; QRY-6 row versions follow CNC; RTE-9 allowed actions are CPX-1 (dependents counts are CPX-2); the feature 10 follow up is closed. 2026-10-27, spec 0015 STY-8: BDL-1's first load raised to 230 KB with the shell in it, and Zod used through its `zod/mini` API (WEB-1, URL-1, the search schema example). 2026-10-28, reconciled with spec 0015 after feature 11: RTE-6's `Link` verified; DIR-1 and DIR-3 widened for the gallery, Lucide, and react-data-grid; A11Y-4 names the allowed live regions; I18N-7's pseudo locale grows text by 30%; SEC-2 names the raw HTML sinks; styling and the grid recorded as decided. 2026-10-30, for spec 0016: RTE-10's boot loads only the config in feature 12; feature 13 adds the session call beside it. 2026-10-30, for spec 0017: I18N-3 requires every shipped catalog to be complete, now that `ar` ships.

## Summary

This is the one playbook for writing the Orvano console in React and Vite: spec 0001 says how the console behaves, and this spec says how the code makes it behave that way. Routes come from TanStack Router (typed URL state for filters, sort, and the open row), server data lives only in TanStack Query, forms use TanStack Form with Zod, accessible building blocks come from React Aria Components, and every string goes through Lingui. Tests run in real browsers (Vitest browser mode, then Playwright against a real server), and CI holds the first load to 230 KB. Every rule has an ID so `/audit` can lift it into `AGENTS.md`, tools can enforce it, and reviews can cite it.

## Requirements

This is a cross cutting standard, so it has no build plan of its own. The rules in **Standard definition** are the contract: all console code meets every rule that applies to it, and `/check review` and `/check verify` cite the IDs. The console scaffold (feature 6) and tooling (feature 7) set up what the rules need. Each screen's spec lists the UX IDs it touches; these code rules apply to every screen without being listed.

**User stories**:

- As a console developer, I want one way to route, fetch, hold state, and build forms, so any screen reads like the last one I worked on.
- As a developer using the console, I want links, Back, and refresh to restore my exact view, and paging to keep my rows on screen.
- As a keyboard or screen reader user, I want every screen built from tested accessible primitives.
- As a translator, I want every string in one catalog with real plural rules and named placeholders.
- As a self hoster, I want one console build that works for my install, loads fast, and calls no third party service.

## Decision

**Chosen option**: Option 1: Document the standard and enforce it from the first line of console code (type checks, lint rules, accessibility checks, catalog and API drift checks, and the bundle budget fail CI; review covers the rest).

Console code follows the numbered rules below. **MUST** rules fail the build or the review. **SHOULD** rules need a written reason to skip. Rules marked _verify with the first API screen_ name a current library API that work must confirm; if it differs, that work records the working form here (feature 11 confirmed RTE-6's `Link`, 2026-10-28). The feature 6 scaffold confirmed the rest (see Follow-up).

**Implementation skills**: `vite` (`antfu/skills`, `.claude/skills/vite/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `pnpm` (`antfu/skills`, `.claude/skills/pnpm/`) · `lingui-framework-setup` (`lingui/skills`, `.claude/skills/lingui-framework-setup/`) · `lingui-best-practices` (`lingui/skills`, `.claude/skills/lingui-best-practices/`) · `playwright-cli` (`microsoft/playwright-cli`, `.claude/skills/playwright-cli/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `vercel-react-best-practices` (`vercel-labs/agent-skills`, `.claude/skills/vercel-react-best-practices/`)

**This spec wins over any installed skill where they disagree** (rule **WEB-0**). Known conflicts: `vercel-react-best-practices` recommends SWR (use **QRY-2**), `next/dynamic` (use **BDL-2**), and hand written `memo` (use **WEB-6**), and its server rules don't apply to an SPA. `vitest` covers jsdom and happy-dom setups (use **QA-1**, browser mode only). `lingui-framework-setup` shows two Babel passes for Vite 8 (use **I18N-2**, one pass, with two passes only as the fallback).

## Standard definition

### Scope and boundaries

Applies to all code in the console app in the core monorepo: screens, the design system folder `src/ui`, shared modules, unit and component tests, and end to end tests. It does not cover the TypeScript SDKs (feature 4) or the public docs site (feature 32).

Decided elsewhere, not here (this spec points to them and must not be read as deciding them):

| Topic                                                                                                                                                                                                    | Owner                                                                                                                                                       |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| How every screen behaves: layout, forms, grid, states, deletes, messages, URL contents, focus, shortcuts (UX-1 to UX-41)                                                                                 | Spec 0001, Console UX patterns                                                                                                                              |
| Visual language, tokens, themes, density values, **styling technology**, the **data grid library**, the primitives' look, CSS budget, any component workbench                                            | Feature 11, decided in [spec 0015](../0015-design-system-console-shell/index.md): Tailwind CSS v4 (STY-1), react-data-grid (PRM-5), the dev gallery (GAL-1) |
| Wire format: error body and codes, field errors, request ID, cursors, row versions, allowed actions, dependents counts, the session and runtime config endpoints, the API contract file and its location | Feature 10, API contract & SDK generation                                                                                                                   |
| The console's folder in the monorepo, its fixed base path, how the server serves it (same origin), the dev proxy, container images                                                                       | Feature 6, Stack & architecture (decided in [spec 0007](../0007-stack-architecture/index.md): REP-1, CSL-1 to CSL-3, IMG-1)                                 |
| ESLint, format, and CI wiring; which lint plugins carry each rule below; pre commit hooks                                                                                                                | Feature 7, Coding standards & tooling                                                                                                                       |
| Content Security Policy and `Referrer-Policy` headers, session cookie and CSRF design, dependency vulnerability policy, the final license policy                                                         | Feature 5, Security baseline (interim license list: spec 0002 GEN-5)                                                                                        |
| Console sign in flow, the account's locale setting                                                                                                                                                       | Feature 13, Console accounts & projects                                                                                                                     |
| Column naming rules (this spec needs: no `:` in a column name)                                                                                                                                           | Feature 24, Schema management                                                                                                                               |
| Realtime client protocol and how it updates the cache                                                                                                                                                    | Features 33 and 34                                                                                                                                          |
| Upload transport and progress                                                                                                                                                                            | Features 29 and 30                                                                                                                                          |
| Reporting browser errors to the server                                                                                                                                                                   | Feature 47                                                                                                                                                  |
| Shipping more languages                                                                                                                                                                                  | Deferred item, Console translations                                                                                                                         |

### General rules: toolchain and language

- **WEB-0 (MUST)**: Where an installed agent skill, a tutorial, or generated scaffold disagrees with this spec, this spec wins. A deviation needs a comment `// Deviation <RULE-ID>: <reason>` at the spot and a mention in the PR.
- **WEB-1 (MUST)**: Versions (as of 2026-10-15; minor and patch updates are fine, a new major needs a spec update): React 19.3, Vite 8 (Rolldown), `@vitejs/plugin-react` 6, React Compiler 1.0 (`babel-plugin-react-compiler`), TanStack Router 1, TanStack Query 5, TanStack Form 1, Zod 4 (through its `zod/mini` API only, imported from `@/shared/zod`, spec 0015 STY-8), React Aria Components 1, Lingui 6, Vitest 5, Playwright 1.64, MSW 3, openapi-typescript 7 with openapi-fetch 0.17.
- **WEB-2 (MUST)**: TypeScript 6.0 (`typescript@~6.0`), not 7. typescript-eslint and the Lingui lint plugin don't support TypeScript 7 yet (their peer ranges end at 6.x). Move to 7 in one PR once they do.
- **WEB-3 (MUST)**: Node 24 LTS, pinned in `.node-version` and `engines`. (Lingui 6 needs Node 22.19 or later, and Node 24 is the active LTS.) pnpm is the package manager, pinned in the `packageManager` field. The lockfile is committed, and CI installs with `pnpm install --frozen-lockfile`.
- **WEB-4 (MUST)**: `tsconfig.json` (for `src/`) sets `strict`, `noUncheckedIndexedAccess`, `noImplicitOverride`, `noFallthroughCasesInSwitch`, `verbatimModuleSyntax`, `erasableSyntaxOnly` (no `enum` or `namespace`), `skipLibCheck`, `target` and `lib` at `ES2023` plus `DOM` and `DOM.Iterable`, `moduleResolution: "bundler"`, `jsx: "react-jsx"`, `noEmit`, and `types: ["vite/client"]` explicitly. A separate `tsconfig.node.json` covers the tool configs (`vite.config.ts` and the rest) with `types: ["node"]`. The only path alias is `@/` for `src/` (tsconfig `paths` plus Vite `resolve.alias`). No `any` (use `unknown` and narrow). No `@ts-ignore`; tests may use `@ts-expect-error` with a reason. The `!` non null assertion is a lint error outside tests; a disable comment must say why the value can't be null.
- **WEB-5 (MUST)**: Vite's default build target decides the syntax the build emits. A web API may be used without a fallback when it is Baseline widely available (supported in every major browser for at least 30 months) on the day the code is written; newer APIs need feature detection and a working fallback. (`AbortSignal.any` qualifies as of October 2026.)
- **WEB-6 (MUST)**: The React Compiler is on for all of `src/`, through the stable Babel path (`reactCompilerPreset` from `@vitejs/plugin-react`, run by `@rolldown/plugin-babel`), not the experimental Rust `compiler` option. Don't write `useMemo`, `useCallback`, or `memo` by hand. Where a component can't be compiled (a library the compiler can't analyze), opt it out with the `'use no memo'` directive and a comment saying why. Code follows the Rules of React, which `eslint-plugin-react-hooks` (recommended config, compiler rules included) checks; its compiler diagnostics are errors, so a component the compiler silently skips fails lint.
- **WEB-7 (MUST)**: A new runtime or dev dependency follows spec 0002 GEN-5 (a release or commit in the last 12 months, no commercial or source available license), and its license follows spec 0009 DEP-1 to DEP-5 (MPL 2.0 is allowed for any console dependency, used unmodified, DEP-3). A runtime dependency's size and reason go in the PR (BDL-3).
- **WEB-8 (MUST)**: One console build serves every install. Its base path is `/console`, fixed at build time (Vite `base: '/console/'`, spec 0007 CSL-1) and the same for every install; `config.basePath` is derived from `BASE_URL`. No install specific value (instance URL, instance name, feature switches) is baked in; runtime values come from the API at boot (RTE-10). `import.meta.env` is read in exactly one module, `src/shared/config.ts`, and only for build facts (`DEV`, `BASE_URL`, the app version).
- **WEB-9 (MUST)**: Named exports only, no default exports (except where a tool requires one, such as `vite.config.ts`). File names are `kebab-case`: `rows-page.tsx` exports `RowsPage`, `use-rows-view.ts` exports `useRowsView`. Route files follow the router plugin's naming (`$tableId`, `_shell`, `route.tsx`, `index.tsx`).
- **WEB-10 (MUST)**: The app renders inside `<StrictMode>`, so effects must survive running twice (cleanups undo what setups did). `createRoot` passes `onCaughtError` and `onUncaughtError` to the one error reporter (FLT-4).

### Structure

- **DIR-1 (MUST)**: The console app is laid out like this (it is the top level `console/` folder of the core monorepo, spec 0007 REP-1):

  ```
  console/
    lingui.config.ts · vite.config.ts · vitest.config.ts · playwright.config.ts
    tsconfig.json · tsconfig.node.json
    index.html                    includes <meta name="referrer" content="same-origin"> (SEC-6)
    gallery.html                  the dev only gallery entry, never built (spec 0015 GAL-1 to GAL-3)
    public/theme-boot.js          the only code outside src/ (STO-6)
    src/
      main.tsx                    boot (RTE-10), providers, render
      router.ts                   createAppRouter() and the router and query type registrations
      routeTree.gen.ts            generated by the router plugin, committed, never edited
      routes/                     route files only (RTE-1)
      features/<area>/            one folder per console area: org, project, account,
                                  auth, databases, storage, realtime, functions, sites,
                                  messaging, usage, logs, settings
        api/                      queries.ts (option factories), mutations.ts, types.ts
        components/  hooks/  schemas/
        index.ts                  the only file other areas may import
      shared/
        api/                      client.ts, schema.gen.ts (generated), errors.ts,
                                  query-client.ts, placeholder.ts
        access/                   useCan, <Can> (RTE-9)
        url/                      search.ts (serializer, searchList, sortParam), set-search.ts
        storage/                  stored.ts, items.ts (every stored item)
        stores/                   create-store.ts, interaction-lock.ts, toasts, announcer,
                                  session, messages
        i18n/  format/  config.ts  report.ts (the one error reporter)
      ui/                         the design system (spec 0015), the only importer of react-aria-components;
                                  subfolders form, shell, toast, confirm-delete, and the lazy
                                  primitives (PRM-10); data-grid comes with the rows feature
      gallery/                    the dev gallery's code; imports ui and shared, nothing imports it
      locales/<locale>/messages.po
      test/                       render helpers, MSW handlers, axe helper
    e2e/                          Playwright tests, fixtures, global setup
  ```

- **DIR-2 (MUST)**: Imports flow one way: `routes` → `features` → `shared` and `ui`; `ui` → `shared` only; `shared` imports neither. A route file imports its feature's modules by direct path (the schema from `schemas/`, the component from `components/`), so code splitting keeps components out of the main chunk. One area imports another area only through its `index.ts` (keep that file small: only what other areas need). No import cycles. Generated files (`routeTree.gen.ts`, `schema.gen.ts`) are excluded from lint and format.
- **DIR-3 (MUST)**: Only `src/ui` imports `react-aria-components`, `react-aria`, `react-stately`, `@react-aria/*`, `lucide-react`, or `react-data-grid` (the import rule spec 0001 requires, widened by spec 0015 ICN-1 and PRM-5). Feature code composes `ui` primitives and never renders raw `<input>`, `<select>`, `<dialog>`, or `<table>`.
- **DIR-4 (SHOULD)**: Tests sit next to the code they test (`rows-page.test.tsx` beside `rows-page.tsx`). End to end tests live only in `e2e/`.

### Routing

- **RTE-1 (MUST)**: Routes are file based, through `tanstackRouter({ target: 'react', autoCodeSplitting: true })` from `@tanstack/router-plugin/vite`, listed before `react()` in the Vite config. A route file holds only route options (`validateSearch`, `search` middlewares, `loaderDeps`, `loader`, `beforeLoad`, `staticData`, `errorComponent`) and points `component` at a component from `features/`. Logic lives in features. The root route is `createRootRouteWithContext<RouterContext>()`, and the router type is registered once in `src/router.ts`.
- **RTE-2 (MUST)**: The route tree mirrors the shell's nesting (UX-1, UX-2, UX-5). A layout route renders its persistent frame plus `<Outlet />`, so the frame doesn't mount again when only a child changes:

  ```
  routes/__root.tsx                         providers that need the router, announcer, root error and not found
  routes/_auth.tsx, _auth/sign-in.tsx ...   pages without the shell (0001 exceptions); no session required
  routes/_shell.tsx                         AppShell; beforeLoad requires a session (RTE-9)
  routes/_shell/$orgId/route.tsx            org context
  routes/_shell/$orgId/$projectId/route.tsx project context; beforeLoad puts capabilities in context (RTE-9)
  .../$projectId/databases/route.tsx        ResourceLayout + ResourceList (UX-2): the list stays mounted
  .../tables/$tableId/route.tsx             PageHeader + RouteTabs + <Outlet /> (UX-5, UX-36)
  .../tables/$tableId/index.tsx             redirects to rows, replacing history (UX-5)
  .../tables/$tableId/rows.tsx              validateSearch for the grid's query state (URL-1)
  .../tables/$tableId/columns.tsx ...       one file per tab
  ```

  The resource list's query lives in the service layout route's component, so switching the open resource changes only child params. A link to a resource points at its root, so switching resources lands on the default tab.

- **RTE-3 (MUST)**: Router defaults, set once in `createAppRouter` (`src/router.ts`): `context: { queryClient }`, `basepath` from `shared/config.ts`, `parseSearch` and `stringifySearch` from `shared/url` (URL-2), `defaultPreload: 'intent'`, `defaultPreloadStaleTime: 0` (TanStack Query decides freshness), `defaultPendingComponent` (the content area skeleton), `defaultPendingMs: 300` and `defaultPendingMinMs: 500` (the UX-21 timing), `scrollRestoration: true`, and the default error and not found components. The shell's scrolling panes (content, resource list) carry scroll restoration IDs, since only the window is restored otherwise (the attribute is `data-scroll-restoration-id`, confirmed 2026-10-16 on `@tanstack/router-core` 1.171; `useElementScrollRestoration` covers a pane without the attribute).
- **RTE-4 (MUST)**: Loaders feed the query cache; they never return data for `useLoaderData`, since there is one cache and it's TanStack Query's.
  - **Layout routes** (`_shell`, org, project, service, resource) await (`queryClient.ensureQueryData`) only the small frame data the frame renders: the session, the project and its capabilities, that the resource exists, and its name for the heading, crumbs, and title. Each one owns the pending skeleton for its content area; the shell and resource list never go blank while a child loads.
  - **Leaf routes** never await data that changes with search params (grid pages, filtered lists). They prefetch it (`void queryClient.prefetchQuery(...)`), and the component reads it with `useQuery`, so paging keeps the current rows on screen (UX-21).
  - `loaderDeps` and query keys include only the search params that change the data (for the grid: filter, sort, cursor, size), never `row` or `sheet`, so opening a row or sheet doesn't refetch the page.
- **RTE-5 (MUST)**: Not found and error routing:
  - A loader that gets an `ApiError` with kind `not_found` for the route's own resource throws `notFound()` (the `rethrowNotFound` helper in `shared/api`). An error component that receives a `not_found` error (a resource deleted elsewhere, found by a later refetch) renders the not found view, not the error view.
  - `errorComponent` sits on each **tab leaf route** and on each service layout, so a failed tab keeps the tab bar, and a failed resource keeps the resource list (UX-22).
  - The default tab redirect is `beforeLoad: () => { throw redirect({ to: ..., params, replace: true }) }`.
  - A delete removes the deleted resource's cached queries (`queryClient.removeQueries`) before it navigates away, then invalidates the list, so nothing refetches a 404.
- **RTE-6 (MUST)**: Links and navigation:
  - `src/ui` exports one `Link`, made with TanStack's `createLink` over React Aria's `Link`, so links keep typed `to`, `params`, and `search` and the router's preload behavior (verified by feature 11: `console/src/ui/link.test.tsx` proves preload on hover and on keyboard focus, spec 0015 AC-15). Feature code uses only this `Link` or `useNavigate`.
  - The root route renders React Aria's `RouterProvider` with the router's `navigate` and `buildLocation` based `useHref`, so React Aria widgets that take `href` (menu items, crumbs, list items) navigate through the router.
  - Never build URL strings by hand or assign `window.location`; the exception is the full reload in QRY-9.
  - `RouteTabs` is a `nav` of links (UX-5), not React Aria `Tabs`. Its links never pass `search: true`, so the Rows tab's filters don't carry over to other tabs. A key that really is shared across tabs is kept with the `retainSearchParams` middleware on the parent route.
  - `ResourceList` links set `preload={false}`, so a keyboard user moving through 200 tables doesn't prefetch each one.
- **RTE-7 (MUST)**: Crumbs, title, and focus (UX-4, UX-35):
  - Each route that adds a crumb declares it in `staticData.crumb` (a Lingui message, or a function of params that names the query holding the resource's name), typed by augmenting `StaticDataRouteOption`. `Breadcrumbs` renders one `Crumb` component per match from `useMatches()`, so each crumb can call its own query hook. The document title is built the same way, once, in the root; no route sets `document.title` itself.
  - Focus after navigation: on the router's `onResolved` event (skipping the first load, when the event has no `fromLocation`), the root waits for the next frame and then, if `pathChanged` is true, moves focus to the page's `h1` (`tabIndex={-1}`). Links in `RouteTabs` and `ResourceList` navigate with history state `{ keepFocus: true }` (typed by augmenting `HistoryState`), and then focus stays on the link, because the frame around it didn't change. A change to the search only leaves focus where it is, except paging, which moves focus to the grid (the view hook does this). Spec 0001 UX-35 says the same.
- **RTE-8 (MUST)**: The `UnsavedChangesGuard` primitive is the only user of the router's `useBlocker` (with `withResolver` and `enableBeforeUnload`). Every dirty form registers with it, and one navigation asks once, however many forms are dirty. It blocks a navigation when a dirty form would be unmounted (a different path, or its `sheet` param removed) and when the row sheet is dirty and `row` changes (Previous, Next, or clicking another row, UX-13). It asks with the design system dialog, never `window.confirm` (UX-9). It also holds the interaction lock while any form is dirty (QRY-4).
- **RTE-9 (MUST)**: Guards and access (UX-30):
  - `_shell`'s `beforeLoad` ensures the session query. Without a session it throws `redirect({ to: '/sign-in', search: { redirect } })`, where `redirect` is the current path and search. The sign in page follows `redirect` only when it is a same origin path (starts with a single `/`), else goes to the default page.
  - The project route's `beforeLoad` ensures the project's capabilities (the services and actions the API allows) and returns them in route context.
  - `useCan(kind, action, resource?)` and `<Can>` in `shared/access` read that context and each resource's allowed actions (its `allowedActions` array, spec 0014 CPX-1). Nothing hard codes role names (UX-30).
  - A service route the project can't open throws an `ApiError` with kind `forbidden`, and its error component renders the "not allowed" state inside the shell.
- **RTE-10 (MUST)**: Boot order, in `main.tsx`, before the first render: (1) apply Zod's `jitless` setting (SEC-2); (2) load the runtime config (`GET /v1/console/config`) and the session (`GET /v1/console/session`) in parallel (spec 0014 CPX-3), where a 401 on the session just means signed out; (3) resolve the locale (I18N-6) and activate its catalog, falling back to `en` if it fails to load; (4) render the router. If the config request fails, or the session request fails for any reason other than 401, render the plain boot error page (FLT-2) instead of the router. The boot results are put in the query cache, so routes read them through their queries. Until feature 13 adds the session call, boot loads only the config (spec 0016 FRS-2).

### URL state (UX-27)

- **URL-1 (MUST)**: Every route with query state declares `validateSearch` as a Zod schema. Every field has a `z.catch(...)` fallback, so a hand edited or stale URL never errors. Defaults are removed from the URL with the `stripSearchParams(defaults)` middleware. Search params are flat (strings, numbers, booleans, and string arrays) and keep their URL form: a filter rule stays the text `column:operator:value` in the route schema, and the feature parses it into a rule object (`parseFilterRule`) where it's used. That way a navigation that validates the search again can't misread an already parsed value.
- **URL-2 (MUST)**: The router uses Orvano's own `parseSearch` and `stringifySearch` (built on `URLSearchParams`) instead of TanStack's JSON encoding. A key that appears more than once becomes an array, so each filter rule is its own repeated `filter` param (UX-15), percent encoded. Values stay strings, and schemas coerce them (`z.coerce.number()`). Columns are named by their **name** in the URL, so links stay readable; a renamed or deleted column shows UX-15's "ignored filter" chip. A filter rule splits at its first two colons (column names can't contain `:`, a rule feature 24 must keep), so a value may contain colons. `sort` is `column:asc` or `column:desc`, split at its last colon (the `sortParam` helper). `searchList(itemSchema, { max })` accepts one value or many, drops malformed items one by one, and keeps at most `max` (10 for filters, UX-15).
- **URL-3 (MUST)**: Components never write search params directly. Each route with query state exposes one `use<Route>View()` hook (for example `useRowsView()`) whose setters apply the UX-27 history rules through `setSearch(navigate, patch, mode)` in `shared/url`:

  | Change                                                                                 | History |
  | -------------------------------------------------------------------------------------- | ------- |
  | path, sort, page cursor, page size, open a row, open or close a sheet                  | push    |
  | add or remove a filter rule, Clear filters                                             | push    |
  | edit an applied rule's value (applied 300 ms after typing stops, on blur, or on Enter) | replace |
  | Previous or Next inside the row sheet                                                  | replace |
  | default tab redirect, dropping an expired cursor or missing row                        | replace |

  Changing filters, sort, or page size also clears the cursor (UX-14). Setting a param to its current value adds no history entry. A filter rule that's still being built (no column, or no value for an operator that needs one) lives in the filter builder's local state and reaches the URL only when complete. The pending 300 ms commit is cancelled when the component unmounts or the history moves (Back or Forward), and the builder's draft resyncs from the URL whenever the URL changes from outside.

- **URL-4 (MUST)**: When the API reports an expired or invalid cursor, the view drops `cursor` with replace and shows the UX-27 toast. A `row` that doesn't exist is dropped the same way. Neither is an error page.
- **URL-5 (MUST)**: The URL holds IDs, names, and view state only, never tokens, keys, or passwords. A token that arrives in a link (for example a console invite) is read once and removed from the URL with replace. The sign in `redirect` param (RTE-9) is a path, never a full URL.
- **URL-6 (MUST)**: Sheets in the URL: `sheet` values are named for what they open (`create-table` on the service layout, `create-row` on the rows route), and each route that opens a sheet declares its own values in its schema. `row` and `sheet` never coexist: the rows route's search middleware drops `row` when `sheet` is set. A detail sheet (the row sheet) reads its own detail query, seeded from the page in the cache with `initialData` and `initialDataUpdatedAt`, so a link to a row on another page still opens.

### Data fetching and caching

- **QRY-1 (MUST)**: All HTTP goes through one client in `shared/api/client.ts`, built on `createClient<paths>()` from openapi-fetch and typed by `schema.gen.ts`. openapi-typescript generates that file from `api/console.openapi.json` (spec 0014 CTR-3, CTR-5) with `pnpm api:gen`, and it is committed. CI regenerates it and fails on any difference. The client exports `get(path, { params, signal })`, where `signal` is **required** by the type, and `post`, `put`, `patch`, and `del` for writes. Each returns the data or throws an `ApiError` (FLT-1). No other code calls `fetch` or openapi-fetch directly, and no other HTTP library is added. The base URL is same origin and comes from `shared/config.ts`.
- **QRY-2 (MUST)**: Each area defines its queries as **option factories** in `features/<area>/api/queries.ts`, built with `queryOptions`. Keys are hierarchical and project first: `[area, projectId, resource, ...ancestorIds, resourceId, params]`, with the resource's own ID second to last and the params object last. Each factory exposes an `all(...)` prefix for invalidation. Only factories create keys; components and loaders always call a factory (lint: `queryKey` only inside `api/queries.ts`). `@tanstack/eslint-plugin-query` (recommended) is on.
- **QRY-3 (MUST)**: Every `queryFn` passes the `signal` it receives to `get` (the type makes it required). TanStack Query then aborts the request when its key changes (new filter, sort, or page) or nothing on screen uses it any more (navigation), which covers UX-21 cancelling. The client also adds a 30 second timeout (`AbortSignal.any([signal, AbortSignal.timeout(30_000)])`). A timeout becomes an `ApiError` with kind `timeout` and shows the error state. An abort is silent and never shows an error. Long running work (uploads, job streams) uses the paths features 29, 30, and 46 define, not this timeout.
- **QRY-4 (MUST)**: The query client defaults, set once in `shared/api/query-client.ts`:
  - `staleTime: 30_000`, so coming back to the tab refetches only data older than 30 seconds (UX-19).
  - `networkMode: 'always'` for queries and mutations, so an offline request fails at once as an `ApiError` with kind `network` instead of pausing. A paused write would otherwise fire by itself when the network returns, which UX-22 forbids. The offline banner reads `onlineManager` (UX-22).
  - Reads retry at most twice with the default growing delay, and only on kind `network` and on 502, 503, and 504 (UX-22). Never on other 4xx, never on 429 (the UI shows when to try again), never on timeouts.
  - Mutations never retry automatically (`retry: false`).
  - `refetchOnWindowFocus` and `refetchOnReconnect` are functions that return false while the **interaction lock** is held (UX-19). The lock lives in `shared/stores/interaction-lock.ts` and is held only through `useInteractionLock(active)`, which counts holders and releases in the effect cleanup, so a holder that unmounts can't leave it stuck. The grid holds it while a cell is being edited or rows are selected; the unsaved changes guard holds it while a form is dirty.
  - `ApiError` is registered as Query's default error type, and mutation `meta` is typed (`{ surface?: 'toast' }`).
- **QRY-5 (MUST)**: A list or grid that pages, filters, or sorts keeps its data on screen with `placeholderData: keepPreviousForSameResource(resourceId)` from `shared/api/placeholder.ts`. It returns the previous data only when the previous query's key has the same resource ID in the second to last position (QRY-2), so opening another table never shows the last table's rows (UX-21). The component marks the region `aria-busy` while `isPlaceholderData` or `isFetching` is true. A failed refetch while data is on screen keeps the data and shows an inline error with Retry next to it; the full error state shows only when there is no data at all.
- **QRY-6 (MUST)**: Writes:
  - Mutations live as hooks in `features/<area>/api/mutations.ts`.
  - **No optimistic cache updates.** A write shows a saving state where it happened (the cell keeps the edited value with a saving indicator, UX-12). On success the hook patches the affected cached entries from the response with `setQueryData` (the edited row inside its page). It invalidates only queries a patch can't cover (counts, other lists), never the grid page on screen after an inline edit, so rows never move under the cursor (UX-12, UX-19). On failure the cell goes back to its old value with the error inline.
  - Writes to one row share `scope: { id: rowId }`, so they run in order.
  - Every write that carries a version sends it as `If-Match` with the resource's ETag (its `version` string, spec 0014 CNC-1 to CNC-4); the `412 version_conflict` answer maps to an `ApiError` with kind `conflict`, which drives the Reload or Overwrite choice (UX-12).
  - Mutation hooks never toast. The caller handles errors where the action happened (`mutate` with `onError`, or `mutateAsync` in a try). The global `MutationCache` `onError` shows a toast only for mutations with `meta.surface === 'toast'`, used when the source may be gone (FLT-3).
  - The submit control reads `isPending`, so a second press does nothing (UX-8).
- **QRY-7 (MUST)**: Data the page frame needs is read with `useSuspenseQuery`, after the layout loader's `ensureQueryData`. Everything else uses `useQuery`. Never fetch in `useEffect`, and never copy query data into `useState` or a store. The one exception is a form's starting values, which are a deliberate snapshot (FRM-5).
- **QRY-8 (MUST)**: Session expiry (UX-22): the client turns a 401 with the session expired code into an event on the session store, and the shell opens the sign in dialog over the page. After sign in: if the signed in user is a different user, do the QRY-9 full reload. Otherwise call `router.invalidate()`, failed queries refetch, and a mutation wrapped in `withReauth` retries **once**, since the person already asked for it. Forms keep their values throughout.
- **QRY-9 (MUST)**: Sign out calls the API, posts `signed-out` on a `BroadcastChannel` named `orvano:session` (other tabs then reload too), and does a full page load of the sign in page. That drops the query cache, every store, and all in memory state in one step.
- **QRY-10 (SHOULD)**: Polling is only for job status until realtime ships: `refetchInterval` of 2 seconds or more that stops once the job reaches a final state (UX-29). Streams (logs, realtime events) are not stored as queries; features 34 and 46 define their hooks.
- **QRY-11 (MUST)**: The grid's undo stack (UX-12) is component state in the grid feature, cleared when you leave the table. Each entry holds the row ID, column, old value, new value, and the row version the save returned. Undo is itself a versioned write, so a newer change by someone else refuses it (UX-12).

### Client state

- **STA-1 (MUST)**: Put state in the first place that fits:
  1. The URL, for anything a refresh, Back, or a shared link must restore (UX-27).
  2. TanStack Query, for anything the server owns.
  3. Component state (`useState`, `useReducer`), for anything one component tree owns.
  4. TanStack Form, for form values, touched state, and errors.
  5. A shell store in `shared/stores`, for client state many trees read: toasts, the announcer, message history, session status, the interaction lock, and preferences (STO).
- **STA-2 (MUST)**: Shell stores use the small `createStore` helper in `shared/stores` (built on `useSyncExternalStore`, read with selectors). React context carries only stable dependencies (the query client, i18n, the router), never values that change often. No global state library (Zustand, Redux, Jotai, and the like) without a spec update.
- **STA-3 (MUST)**: Compute derived values during render (the compiler memoizes them). `useEffect` is only for syncing with something outside React (subscriptions, DOM APIs, `ResizeObserver`), never for copying one piece of state into another.

### Browser storage (UX-28)

- **STO-1 (MUST)**: Only `src/shared/storage` touches `localStorage`, `sessionStorage`, or IndexedDB (lint: restricted globals everywhere else).
- **STO-2 (MUST)**: Every stored item is declared once, in `shared/storage/items.ts`, with `defineStored({ key, schema, fallback })`. The keys:

  | Item                                                           | Key                                              |
  | -------------------------------------------------------------- | ------------------------------------------------ |
  | Table layout (column visibility, width, order by column ID)    | `orvano:v1:{userId}:{projectId}:table:{tableId}` |
  | Last used index for those tables (`{ [tableId]: lastUsedMs }`) | `orvano:v1:{userId}:{projectId}:tables`          |
  | Resource list collapsed state and width, per service           | `orvano:v1:{userId}:{projectId}:svc:{service}`   |
  | Primary sidebar collapsed state, density, last SDK             | `orvano:v1:{userId}:layout`                      |
  | Command palette recents                                        | `orvano:v1:{userId}:palette`                     |
  | Theme (read before sign in)                                    | `orvano:v1:theme`                                |

  `userId` comes from the session, which boot loads before any screen renders (RTE-10), so only the theme is read without a user. Every read and every write is wrapped in try and catch. A read that fails to parse JSON or fails its Zod schema returns the fallback and removes the bad entry. A new item is added to `items.ts`, never declared elsewhere.

- **STO-3 (MUST)**: Availability is probed once at startup (write and remove a test key). If storage is blocked or the probe throws, every item lives in memory for the session, and the console works the same apart from forgetting on reload (UX-28). The last used index is written at most once a minute per table, and keeps 50 tables per project; a write that hits the quota first evicts the least recently used tables, then retries once, then falls back to memory.
- **STO-4 (MUST)**: A change that isn't backward compatible bumps the key version (`v1` to `v2`). Old versions are ignored, and at startup keys from older versions are removed.
- **STO-5 (MUST)**: The storage module listens for the `storage` event, so a preference changed in another tab updates this tab (through the store's subscription).
- **STO-6 (MUST)**: Storage never holds secrets, tokens, project data, row contents, or form drafts, only IDs and layout preferences (UX-28). The single exception to STO-1 is `public/theme-boot.js`, a few lines loaded from `index.html` before the app. It reads the global theme key inside try and catch, so the first paint (the sign in page included) uses the right theme without an inline script.
- **STO-7 (SHOULD)**: "Don't ask again for this table this session" (UX-23) uses `sessionStorage` through the same module, so it lasts through a reload but not a new browser session.

### Forms

- **FRM-1 (MUST)**: Forms use TanStack Form through one app wide hook made with `createFormHookContexts` and `createFormHook` in `src/ui/form`. Its field components (`TextField`, `NumberField`, `Select`, `Checkbox`, `Switch`, `RelationPicker`, `JsonEditor`, and the rest) and form components (`SubmitButton`, `ErrorSummary`) are the design system's. Screens call `useAppForm` and render `form.AppField`. Never call `useForm` directly, and never wire a raw input.
- **FRM-2 (MUST)**: The UX-8 timing is implemented once, in the shared field wiring in `src/ui/form`, and covered by browser component tests. A field is checked when it loses focus after a change. Once it shows an error, it's checked on every change, and the error clears the moment it passes. Screens never set validator timing themselves.
- **FRM-3 (MUST)**: Each form has one Zod schema for its values in `features/<area>/schemas/`, and one mapper function `to<Action>Request(values)` whose return type is the generated request type, so the compiler checks that form and API agree. Validation messages use the `t` macro from `@lingui/core/macro` inside the schema's `error` functions, so they're translated when validation runs, in the current locale. Checks that need the server (for example "name already taken") are a field's `onBlurAsync` validator. It passes on the `signal` it's given, says "Checking" through the announcer (UX-41), and is never the final word: the server decides on submit.
- **FRM-4 (MUST)**: Submitting runs in the form's `onSubmitAsync` validator: `mutateAsync(toRequest(schema.parse(values)))`, so trims, coercions, and defaults in the schema apply. Server validation errors come back from that validator as `{ form, fields }`, so they land in the same error state as client errors: errors that name a field show at the field, and the rest go to the `ErrorSummary` (UX-8). After a failed submit, focus moves to the summary. The submit button is never disabled for invalid input. It shows the busy state while submitting and ignores a second press.
- **FRM-5 (MUST)**: A form starts from the last saved values (a snapshot of the query data) as its default values. It counts as changed while its values differ from those defaults (the form's `isDefaultValue` state is false), not `isDirty`, which stays true after a change is reverted (UX-9; the state is `isDefaultValue`, confirmed 2026-10-16 on TanStack Form 1.33 for both form and field state). After a successful save, the form resets to the saved values. The `UnsavedChangesGuard` and settings card Update buttons read that changed state.
- **FRM-6 (MUST)**: A failed submit keeps every value, wrong ones included (UX-10). The one exception is the password after a failed sign in, which is reset on purpose. Forms never persist drafts (STO-6).

### Accessibility (WCAG 2.2 AA)

- **A11Y-1 (MUST)**: The target is WCAG 2.2 AA. Spec 0001 sets the behavior. These rules set how the code gets there.
- **A11Y-2 (MUST)**: Interactive widgets come from `src/ui` primitives built on React Aria Components, which follow the ARIA Authoring Practices patterns. Feature code doesn't hand roll widgets, add `role` to a `div`, or put click handlers on non interactive elements. Links go somewhere, buttons do something.
- **A11Y-3 (MUST)**: Pages use landmarks (`header`, `nav`, `main`), exactly one `h1`, and headings in order. The design system's components require an accessible name in their types (for example `IconButton` won't compile without `aria-label`).
- **A11Y-4 (MUST)**: Every announcement goes through the shell's announcer store (UX-41). The `Announcer` primitive is built on `@react-aria/live-announcer`, the region React Aria's own components already announce through, so Orvano and React Aria messages share one region and nothing is read twice. The only other live regions are React Aria's toast region, `Banner`'s role, and `InlineError`'s `role="alert"` (spec 0015 AC-12); anything else that must be heard goes through the announcer.
- **A11Y-5 (MUST)**: Every component test runs axe (`axe-core`, tags `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa`) on what it rendered, through the `expectNoAxeViolations` helper in `src/test`, and fails on any violation. Every E2E step that reaches a new page state runs `@axe-core/playwright` with the same tags plus `best-practice` (which checks one `h1`, heading order, and landmarks for A11Y-3). Keyboard behavior is tested with real key presses in browser mode, never by calling handlers.
- **A11Y-6 (MUST)**: Lint runs the JSX accessibility rules through Oxlint's `jsx-a11y` plugin, every rule at error, which covers the plugin's strict preset (spec 0008 LNT-4, VER-3).
- **A11Y-7 (MUST)**: Keyboard shortcuts (UX-26) are registered only with `useShortcut` from `src/ui`, which adds them to the shell's shortcut registry and sets `aria-keyshortcuts` on the target. The `?` list reads the same registry, so it can't drift from what's bound.

### Translation ready strings

- **I18N-1 (MUST)**: Every user facing string is a Lingui message. In components, use `<Trans>`, `<Plural>`, and `useLingui()` from `@lingui/react/macro`. Outside components, use the `t` macro from `@lingui/core/macro` only inside functions that run when the text is shown or validated (never at module level). For text stored before it's shown (crumbs, store messages), keep a lazy `msg` descriptor and translate at display time with `i18n._(descriptor)`. Resource names, user content, and code never go through translation.
- **I18N-2 (MUST)**: The Lingui macros and the React Compiler run in **one** Babel pass: `babel({ plugins: ['@lingui/babel-plugin-lingui-macro'], presets: [reactCompilerPreset()] })` from `@rolldown/plugin-babel`. Babel runs plugins before presets, so the macros expand before the compiler reads the code. `lingui()` compiles `.po` catalogs on import. Install `@babel/types` beside `@lingui/babel-plugin-lingui-macro` (a peer that `lingui extract` needs). The scaffold confirmed on 2026-10-16 that both transforms run in this single pass (the built page carries the compiler's memo cache and compiled message IDs). If a later upgrade breaks it, fall back to two passes, `linguiTransformerBabelPreset()` first, and record it here; I18N-9 keeps checking.
- **I18N-3 (MUST)**: Catalogs are PO files at `src/locales/<locale>/messages.po`, with `en` as the source locale and IDs generated from the source text (add `context` when one English word means two things). Start with one catalog. The `en` catalog is imported statically by the entry, since the boot error page needs it (it counts in the BDL-1 budget). Other locales load lazily. Split catalogs by area only if `en` passes 30 KB gzip. CI runs `lingui check sync` and fails when a message was added or changed without extraction. Every shipped catalog is required to be complete (spec 0017 LOC-1), so `lingui check sync` fails on a missing `ar` translation too.
- **I18N-4 (MUST)**: Plurals and choices use ICU through the macros (`plural`, `select`, `<Plural>`). Never build a sentence from fragments, concatenate translated strings, or pick a plural with `count === 1 ? ... : ...`. Name placeholders (assign to a well named variable, or use `ph()`), so translators see `{tableName}`, not `{0}`.
- **I18N-5 (MUST)**: Dates, times, relative times, numbers, and lists are formatted only through `shared/format`, which wraps `Intl.DateTimeFormat`, `Intl.RelativeTimeFormat`, `Intl.NumberFormat`, and `Intl.ListFormat` for the active locale (cached per locale). No date library is added. Sizes use binary units (UX-34): `shared/format` picks the unit and formats the number, and a message supplies the unit label (`{size} KiB`), because `Intl` has no binary units.
- **I18N-6 (MUST)**: The locale comes from the account's setting (feature 13), then `navigator.languages` matched to the shipped catalogs, then `en`. One locale module in `shared/i18n` owns `resolveLocale` and `getDirection`. Changing locale sets `<html lang dir>` and React Aria's `I18nProvider` locale. React Aria's built in strings are pruned to the shipped locales at build time (`@react-aria/optimize-locales-plugin`, listed before `react()` as `{ ...optimizeLocales.vite({ locales }), enforce: 'pre' }`, where `locales` is derived from the shipped catalogs in `shared/i18n` and mapped to the region tags React Aria uses (`en` becomes `en-US`; a bare `en` would prune the English strings themselves); confirmed 2026-10-16 under Vite 8.3 with Rolldown, where it removed the other locales' strings and saved about 16 KB gzip on a DatePicker bundle). Layout uses logical properties only (feature 11 enforces the CSS side).
- **I18N-7 (SHOULD)**: Development builds include a pseudo locale (`pseudoLocale` in `lingui.config.ts`) that grows every message by about 35% with its placeholders intact, so untranslated strings and text that breaks at 30% longer (UX-34) show up while building (spec 0015 GAL-2).
- **I18N-8 (MUST)**: Lint flags literal text in JSX and in user facing props outside message calls (`eslint-plugin-lingui` `no-unlocalized-strings`, with a short allow list: code snippets, brand names, test files). This is spec 0001's literal string rule.
- **I18N-9 (MUST)**: A green build doesn't prove the macro pass ran (a miswired macro transform leaves strings untranslated without an error). One E2E test loads the console in the pseudo locale and checks that a known string is pseudo translated.

### Errors

- **FLT-1 (MUST)**: The client turns every failed response into one `ApiError` (in `shared/api/errors.ts`): `kind` (`network`, `timeout`, `unauthenticated`, `forbidden`, `not_found`, `conflict`, `validation`, `rate_limited`, `server`), `status`, `code`, `message`, `fieldErrors`, `requestId`, `requiredRole`, `retryAfter`. A thrown non HTTP error (a failed `fetch`) becomes kind `network`; an abort is rethrown unchanged so TanStack Query treats it as a cancel. The wire format is spec 0014 PRB-1, and this type maps it: `kind` from the status (KND-2), `code` from `code`, `message` from `detail`, `fieldErrors` from `errors` (PRB-3), `requestId` from `traceId`, `requiredRole` and `retryAfter` from the members of the same name. UI code switches on `kind` (and `code` for specific cases), never on status numbers or message text.
- **FLT-2 (MUST)**: Error boundaries are route level (RTE-5). The boot error page and the root error component are plain pages with Reload and the request ID, for when even the shell can't run. Tab leaf and service layout error components render `ErrorState` (what failed, Retry, the copyable request ID) inside the shell. Retry calls `reset()` from `useQueryErrorResetBoundary`, then `router.invalidate()`.
- **FLT-3 (MUST)**: Action errors show where the action happened (UX-24): a form, card, cell, or panel, handled by the caller (QRY-6). A toast is used only when that place may be gone (`meta.surface: 'toast'`).
- **FLT-4 (MUST)**: Errors are never swallowed: no empty `catch`, and no `catch` that only logs and carries on as if it worked. The console sends nothing to third party services. Unexpected errors go to the one reporter in `shared/report.ts` (React's root error callbacks and the error components call it), which writes `console.error` until feature 47 defines a reporting endpoint. Lint bans `console` everywhere else.

### Security

- **SEC-1 (MUST)**: Credentials and tokens are never readable from JavaScript storage (STO-6) or the URL (URL-5). The session transport (cookie and CSRF design) is feature 5's and feature 13's. The client sends same origin credentials and adds whatever header they define, in its one middleware.
- **SEC-2 (MUST)**: No `dangerouslySetInnerHTML`, `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`, `eval`, `new Function`, or inline event handler strings. Rendering trusted HTML (for example bundled docs) needs a spec. Zod is set to `z.config({ jitless: true })` at boot, so it never uses `new Function` and the console runs under a strict CSP without `unsafe-eval`.
- **SEC-3 (MUST)**: A secret shown once (UX-33) stays only in the component that shows it: the mutation that creates it sets `gcTime: 0`, and the secret is never put in a store, the query cache, storage, the URL, or a log.
- **SEC-4 (MUST)**: Links to URLs that come from users or the API render only for `http:` and `https:` schemes. External links use `rel="noopener noreferrer"`.
- **SEC-5 (MUST)**: Dependency install scripts stay blocked (pnpm's default). One is allowed only by name in `pnpm-workspace.yaml`, with a reason. pnpm's `minimumReleaseAge` is set to 3 days (4320 minutes), so a just published (possibly compromised) version isn't installed at once. Feature 5 may tighten this.
- **SEC-6 (MUST)**: The server sends `Referrer-Policy: same-origin` (feature 5 owns the header, UX-27), and `index.html` also carries `<meta name="referrer" content="same-origin">` in case the console is served without it.

### Testing

- **QA-1 (MUST)**: Unit and component tests run in **Vitest browser mode** with the Playwright provider (`@vitest/browser-playwright`) and `vitest-browser-react`. There is one environment and no jsdom. `vitest.config.ts` builds on the app's Vite config (`mergeConfig`), so tests run the same router plugin, Lingui macros, and compiler as the app. It defines two projects: features and shared code in Chromium, and `src/ui` primitives in Chromium, Firefox, and WebKit.
- **QA-2 (MUST)**: Component tests mock the API with MSW 3 (the browser worker, its script served from the test public folder), with handlers typed from the generated `paths` (via `openapi-msw`). An unhandled request to an API path fails the test; Vite and Vitest's own requests pass through. Shared default handlers live in `src/test/handlers` (including session, config, org, and project, so ancestor route loaders resolve), and each test adds what it needs with `worker.use(...)`. Timing tests (UX-21's 300 and 500 ms) use real timers with a handler the test releases, not fake timers.
- **QA-3 (MUST)**: Each test renders through `renderWithProviders(url)` in `src/test`. It creates a fresh query client (`retry: false`) and calls `createAppRouter({ queryClient, history: createMemoryHistory({ initialEntries: [url] }) })` from `src/router.ts`, so the test runs the real generated route tree at that URL, with the `en` catalog and the MSW worker. Tests find elements by role and accessible name, falling back to label and text. Test IDs are only for internals that have no accessible name. No DOM snapshot tests.
- **QA-4 (MUST)**: Required tests, by kind (no coverage percentage gate):
  - **Every route**: loading (skeleton after the delay), each UX-20 empty state that applies, error with request ID (UX-22), not allowed (UX-30), and success.
  - **Every form**: leave field validation, the error summary and its focus, server field errors mapped to fields, a second press doing nothing, and the unsaved changes guard.
  - **Every `ui` primitive**: its APG keyboard model, focus return, and axe, in all three engines.
  - **Every shared module** (search serializer and history helper, storage, error mapping, formatters, the placeholder helper, the interaction lock): edge and corrupt inputs (bad URLs, corrupt or blocked storage, unknown error bodies).
  - **Every slice**: at least one Playwright path through the real console and server (QA-5).
  - **Every bug fix**: a regression test.
- **QA-5 (MUST)**: End to end tests use Playwright against the built console served by a **real Orvano server and PostgreSQL in containers** (the `ghcr.io/orvanohq/orvano` image per spec 0007 IMG-3, run under Docker or Podman per spec 0002 TST-5a and spec 0007 DEV-1). A Playwright `globalSetup` starts the containers, waits for the server to be healthy, creates the first console admin through the API, and saves its signed in state. Each test then creates its own org and project through the API, so tests never share data. Traces are kept on failure. PRs run Chromium; a nightly run covers Chromium, Firefox, and WebKit.
- **QA-6 (MUST)**: A test that proves an acceptance criterion starts its name with the spec qualified ID (`'0013 AC-2: shows the first use empty state'`), so `/check verify` can trace it.

### Bundle size and performance

- **BDL-1 (MUST)**: Budgets, in gzip, JavaScript only (the `en` catalog counts; CSS is feature 11's):
  - **First load: 230 KB.** (Raised from 200 KB by spec 0015 STY-8 once the shell was measured.) The entry chunk, plus the chunks Vite's manifest lists for the `__root`, `_shell`, org, project, and project overview route files, plus everything those import statically.
  - **Any one emitted JavaScript file: 80 KB.** A screen spec may raise this for its own route's files with a reason (editors, for example).
  - The build writes Vite's manifest (`build.manifest: true`), and a size check reads it, matching route files by source path, and fails CI over budget. Feature 7 picks the runner (for example size-limit with a script that lists the files).
- **BDL-2 (MUST)**: Code is split by route automatically (RTE-1). Heavy primitives (`DataGrid`, `JsonEditor`, code editors, charts) are imported by their own path (`@/ui/data-grid`), never through the `@/ui` index, and anything rarely used loads through `lazy()` or a lazy route.
- **BDL-3 (MUST)**: A new runtime dependency's gzip size and reason go in the PR description. Over 10 KB gzip, review must agree it's worth it. Prefer what the platform or an existing dependency already provides.
- **BDL-4 (SHOULD)**: `pnpm build:analyze` produces a bundle treemap on demand (for example `rollup-plugin-visualizer`, which supports Rolldown). Look at it whenever a budget check fails.
- **BDL-5 (SHOULD)**: Lists that can grow past about 200 items (the resource list, logs, files) render only the visible rows (`@tanstack/react-virtual`, or react-data-grid's own virtualization, spec 0015 PRM-5). Measure with the React profiler before adding any other optimization.

### Canonical pattern

The shape every screen follows. API paths are placeholders until feature 10 fixes them, and any API a rule still marks _verify with …_ may change form.

```ts
// vite.config.ts
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";
import react, { reactCompilerPreset } from "@vitejs/plugin-react";
import babel from "@rolldown/plugin-babel";
import { tanstackRouter } from "@tanstack/router-plugin/vite";
import { lingui } from "@lingui/vite-plugin";

export default defineConfig({
  plugins: [
    tanstackRouter({ target: "react", autoCodeSplitting: true }), // RTE-1: before react()
    react(),
    lingui(), // I18N-2: .po compiles on import
    babel({
      // WEB-6, I18N-2: one pass;
      plugins: ["@lingui/babel-plugin-lingui-macro"], // plugins run before presets,
      presets: [reactCompilerPreset()], // so macros expand first
    }),
  ],
  base: "/console/", // WEB-8, spec 0007 CSL-1
  server: {
    port: 5173,
    proxy: {
      // spec 0007 CSL-3: one origin in dev
      "^/(?!console(/|$))": { target: "http://localhost:5080" }, // every path outside /console
    },
  },
  resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  build: { manifest: true }, // BDL-1
});
```

```tsx
// src/router.ts
import { createRouter, type RouterHistory } from "@tanstack/react-router";
import type { QueryClient } from "@tanstack/react-query";
import { routeTree } from "./routeTree.gen";

export interface RouterContext {
  queryClient: QueryClient;
}

export function createAppRouter(opts: { queryClient: QueryClient; history?: RouterHistory }) {
  return createRouter({
    routeTree,
    history: opts.history, // QA-3: memory history in tests
    context: { queryClient: opts.queryClient },
    basepath: config.basePath, // WEB-8: fixed, same for every install
    parseSearch, // URL-2
    stringifySearch,
    defaultPreload: "intent",
    defaultPreloadStaleTime: 0, // RTE-3: Query owns freshness
    defaultPendingComponent: ContentSkeleton, // UX-21
    defaultPendingMs: 300,
    defaultPendingMinMs: 500,
    scrollRestoration: true,
    defaultErrorComponent: RouteError, // FLT-2
    defaultNotFoundComponent: RouteNotFound,
  });
}

declare module "@tanstack/react-router" {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
  interface StaticDataRouteOption {
    crumb?: Crumb;
  } // RTE-7
  interface HistoryState {
    keepFocus?: boolean;
  }
}
declare module "@tanstack/react-query" {
  interface Register {
    defaultError: ApiError;
    mutationMeta: { surface?: "toast" };
  } // QRY-4
}
```

```tsx
// src/main.tsx
const rootElement = document.getElementById("root");
if (!rootElement) throw new Error("index.html has no #root element");
const root = createRoot(rootElement, {
  onCaughtError: reportConsoleError, // WEB-10, FLT-4
  onUncaughtError: reportConsoleError,
});
const queryClient = createQueryClient(); // QRY-4
const boot = await bootConsole(queryClient); // RTE-10: jitless, config + session, locale

root.render(
  <StrictMode>
    {boot.ok ? (
      <AppProviders queryClient={queryClient}>
        <RouterProvider router={createAppRouter({ queryClient })} />
      </AppProviders>
    ) : (
      <BootError error={boot.error} /> // FLT-2
    )}
  </StrictMode>,
);
```

```ts
// src/features/databases/schemas/rows-search.ts
import { z } from "@/shared/zod"; // zod/mini, jitless (SEC-2, spec 0015 STY-8)
import type { SearchMiddleware } from "@tanstack/react-router";
import { searchList, sortParam } from "@/shared/url/search";
import { filterRuleText } from "./filter-rule";

export const rowsSearchSchema = z.object({
  // URL-1: every field falls back
  filter: z.catch(searchList(filterRuleText, { max: 10 }), []), // URL-2: repeated, kept as text
  sort: z.catch(z.optional(sortParam), undefined), // column:asc|desc, split at the last colon
  cursor: z.catch(z.optional(z.string()), undefined),
  size: z.catch(
    z.pipe(z.coerce.number(), z.union([z.literal(25), z.literal(50), z.literal(100)])),
    50,
  ),
  row: z.catch(z.optional(z.string()), undefined),
  sheet: z.catch(z.optional(z.literal("create-row")), undefined), // URL-6: named for what it opens
});
export type RowsSearch = z.output<typeof rowsSearchSchema>;

export const rowsSearchDefaults: Pick<RowsSearch, "filter" | "size"> = { filter: [], size: 50 };

// Only what changes the data goes in loaderDeps and the query key (RTE-4)
export type RowsKey = Pick<RowsSearch, "filter" | "sort" | "cursor" | "size">;
export const toRowsKey = ({ filter, sort, cursor, size }: RowsSearch): RowsKey => ({
  filter,
  sort,
  cursor,
  size,
});

export const dropRowWhenSheetOpen: SearchMiddleware<RowsSearch> = ({ search, next }) => {
  const result = next(search);
  return result.sheet ? { ...result, row: undefined } : result; // URL-6
};
```

```tsx
// src/routes/_shell/$orgId/$projectId/databases/$databaseId/tables/$tableId/rows.tsx
import { createFileRoute, stripSearchParams } from "@tanstack/react-router";
import {
  dropRowWhenSheetOpen,
  rowsSearchDefaults,
  rowsSearchSchema,
  toRowsKey,
} from "@/features/databases/schemas/rows-search";
import { rowQueries } from "@/features/databases/api/queries";
import { RowsPage } from "@/features/databases/components/rows-page"; // DIR-2: direct path, split out
import { TabError } from "@/features/databases/components/tab-error";

export const Route = createFileRoute(
  "/_shell/$orgId/$projectId/databases/$databaseId/tables/$tableId/rows",
)({
  validateSearch: rowsSearchSchema,
  search: { middlewares: [stripSearchParams(rowsSearchDefaults), dropRowWhenSheetOpen] },
  loaderDeps: ({ search }) => toRowsKey(search), // RTE-4: row and sheet never refetch
  loader: ({ context: { queryClient }, params, deps }) => {
    void queryClient.prefetchQuery(rowQueries.page(params, deps)); // RTE-4: prefetch, never await grid data
  },
  errorComponent: TabError, // RTE-5: the tab bar stays
  component: RowsPage,
});
```

```ts
// src/features/databases/api/queries.ts
import { queryOptions } from "@tanstack/react-query";
import { get } from "@/shared/api/client";
import { keepPreviousForSameResource } from "@/shared/api/placeholder";
import type { RowsKey } from "../schemas/rows-search";
import { toRowsQuery, type TableParams } from "./types";

export const rowQueries = {
  all: (projectId: string) => ["databases", projectId, "rows"] as const, // QRY-2
  page: (p: TableParams, k: RowsKey) =>
    queryOptions({
      // resource ID second to last, params last (QRY-2, QRY-5)
      queryKey: [...rowQueries.all(p.projectId), p.databaseId, p.tableId, k] as const,
      queryFn: ({ signal }) =>
        get("/projects/{projectId}/databases/{databaseId}/tables/{tableId}/rows", {
          params: { path: p, query: toRowsQuery(k) },
          signal, // QRY-3: required by get's type
        }),
      placeholderData: keepPreviousForSameResource(p.tableId), // QRY-5: never another table's rows
    }),
};
```

```tsx
// src/features/databases/components/rows-page.tsx
import { getRouteApi } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useLingui } from "@lingui/react/macro";
import { DataGrid } from "@/ui/data-grid"; // BDL-2: heavy primitive by its own path
import { ErrorState, GridSkeleton, InlineError, useDelayedPending } from "@/ui";
import { rowQueries } from "../api/queries";
import { toRowsKey } from "../schemas/rows-search";
import { useRowsView } from "../hooks/use-rows-view";

const route = getRouteApi("/_shell/$orgId/$projectId/databases/$databaseId/tables/$tableId/rows");

export function RowsPage() {
  const params = route.useParams();
  const search = route.useSearch();
  const view = useRowsView(); // URL-3: the UX-27 push or replace rules
  const rows = useQuery(rowQueries.page(params, toRowsKey(search)));
  const showSkeleton = useDelayedPending(rows.isPending); // UX-21: shows after 300 ms, stays 500 ms
  const { t } = useLingui();

  if (showSkeleton) return <GridSkeleton />;
  if (rows.data === undefined) {
    return rows.isError ? (
      <ErrorState error={rows.error} onRetry={() => void rows.refetch()} />
    ) : null;
  }
  return (
    <>
      {rows.isError && <InlineError error={rows.error} onRetry={() => void rows.refetch()} />}{" "}
      {/* QRY-5 */}
      <DataGrid
        aria-label={t`Rows`} // I18N-1
        rows={rows.data.items}
        busy={rows.isPlaceholderData || rows.isFetching} // QRY-5: aria-busy
        sort={search.sort}
        onSortChange={view.setSort} // push, clears the cursor
        onPageChange={view.setPage} // push, then focus moves to the grid
        onOpenRow={view.openRow} // push
      />
    </>
  );
}
```

```ts
// src/features/databases/hooks/use-rows-view.ts (excerpt)
export function useRowsView() {
  const navigate = route.useNavigate();
  const focusGrid = useGridFocus();
  return {
    setSort: (sort: string | undefined) => setSearch(navigate, { sort, cursor: undefined }, "push"),
    setPage: (cursor: string | undefined) => {
      setSearch(navigate, { cursor }, "push");
      focusGrid(); // RTE-7, UX-35
    },
    openRow: (row: string) => setSearch(navigate, { row }, "push"),
    stepRow: (row: string) => setSearch(navigate, { row }, "replace"), // Previous or Next in the sheet
  };
}
```

```ts
// src/shared/storage/items.ts (excerpt)
export const tableLayout = defineStored({
  // STO-2
  key: (ids: { userId: string; projectId: string; tableId: string }) =>
    `orvano:v1:${ids.userId}:${ids.projectId}:table:${ids.tableId}`,
  schema: tableLayoutSchema, // column IDs, widths, order
  fallback: defaultTableLayout,
});
// reads and writes never throw: a parse or schema failure returns the fallback (STO-2),
// and blocked storage falls back to memory (STO-3)
```

### Replaces

These common patterns are wrong in the Orvano console, even when a tutorial or an agent skill suggests them:

- React Router, Next.js, or Remix patterns: `useSearchParams` string parsing, `useLoaderData` as the data cache, `next/dynamic`
- `useEffect` plus `fetch`, axios or ky, SWR, RTK Query, or calling `fetch` or openapi-fetch outside the API client
- Query keys written as literals outside factories; copying query data into `useState` or a store; optimistic cache writes
- Zustand, Redux, Jotai, or a context that holds changing state
- React Hook Form, Formik, or hand wired `<input>` elements; disabling submit until valid; submitting unparsed form values
- `localStorage.getItem` scattered in components; storing tokens or drafts in the browser
- i18next, react-intl, hard coded strings, string concatenation for sentences, `count === 1` plurals
- date-fns, Day.js, Moment, or hand formatted numbers and sizes
- Hand written `useMemo`, `useCallback`, and `memo`
- Radix, Headless UI, Base UI, or MUI imported anywhere; React Aria imported outside `src/ui`
- jsdom or happy-dom test environments, Enzyme, DOM snapshot tests, `getByTestId` as the first choice
- Default exports, `enum`, `any`, `@ts-ignore`, `console.log`

### Enforcement

Feature 7 installs and wires these; this table says what each rule is checked by.

| Mechanism                                                                                                                                                                                                                                                                                | Rules                                                                                                                                                                                                                                                                                                                                                               | Where it lives                              |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------- |
| TypeScript (`tsc --noEmit`, the WEB-4 options)                                                                                                                                                                                                                                           | WEB-4, A11Y-3 (required accessible names), FRM-3 (the mapper's return type), QRY-3 (`signal` required by `get`), URL-1 (typed search output)                                                                                                                                                                                                                        | `tsconfig.json`, `tsconfig.node.json`, CI   |
| ESLint and Oxlint (spec 0008 LNT-1a): typescript-eslint strict type checked (including no non null assertion), react-hooks recommended (compiler diagnostics as errors), TanStack Query and Router plugins, JSX accessibility rules (Oxlint `jsx-a11y`, LNT-4), Lingui rules, no console | WEB-4, WEB-6, WEB-9, QRY-7, A11Y-2, A11Y-6, I18N-4, I18N-8, FLT-4                                                                                                                                                                                                                                                                                                   | ESLint flat config (feature 7)              |
| ESLint restricted imports, globals, and syntax, plus an import boundary rule                                                                                                                                                                                                             | DIR-2, DIR-3, STO-1, QRY-1 (`fetch` and openapi-fetch outside `shared/api`), QRY-2 (`queryKey` outside `api/queries.ts`), URL-3 (`useSearch` writes and `navigate({ search })` outside view hooks and `shared/url`), WEB-8 (`import.meta.env` outside `shared/config`), WEB-1 (`zod` outside `shared/zod`, spec 0015 STY-8), SEC-2, Replaces list (banned packages) | ESLint flat config                          |
| Drift checks: regenerate and `git diff --exit-code`                                                                                                                                                                                                                                      | QRY-1 (`schema.gen.ts`), RTE-1 (`routeTree.gen.ts`), I18N-3 (`lingui check sync`)                                                                                                                                                                                                                                                                                   | CI                                          |
| Vitest browser mode with axe (WCAG tags)                                                                                                                                                                                                                                                 | A11Y-5, QA-1 to QA-4, FRM-2                                                                                                                                                                                                                                                                                                                                         | `vitest.config.ts`, CI                      |
| Playwright with axe (WCAG tags plus `best-practice`) against a real server                                                                                                                                                                                                               | QA-5, A11Y-3, A11Y-5, I18N-9                                                                                                                                                                                                                                                                                                                                        | `playwright.config.ts`, CI (PR and nightly) |
| Bundle size check on the Vite manifest                                                                                                                                                                                                                                                   | BDL-1 (and spec 0015 STY-9's shell probe)                                                                                                                                                                                                                                                                                                                           | CI                                          |
| pnpm settings                                                                                                                                                                                                                                                                            | WEB-3, SEC-5                                                                                                                                                                                                                                                                                                                                                        | `package.json`, `pnpm-workspace.yaml`       |
| Review (`/check review`, people)                                                                                                                                                                                                                                                         | Everything else: loader await versus prefetch (RTE-4), history modes (URL-3), state placement (STA-1), dependency weight and license (WEB-7, BDL-3), cache patching after writes (QRY-6), test quality                                                                                                                                                              | PR template checklist from feature 7        |

### Rollout

New code immediately. No console code exists, so every rule holds from the first commit of the console scaffold (feature 6), and the lint, type, drift, and size checks are on before the first screen lands.

### Exceptions

- `public/theme-boot.js` reads browser storage directly (STO-6).
- `vite.config.ts` and other tool configs may use default exports and `import.meta.url`.
- Tests may use `!`, `@ts-expect-error` with a reason, literal strings, and direct storage access when the test is about storage.
- Generated files (`routeTree.gen.ts`, `schema.gen.ts`) are exempt from lint and style rules.
- The SQL editor and other code editors (feature 27) manage their own keyboard and may hold their own editor state; the page around them follows this spec.
- Anything else: a `// Deviation <RULE-ID>: <reason>` comment and a PR mention (WEB-0).

## Consequences

**Positive**:

- One cache (TanStack Query) and typed URL state (TanStack Router) make UX-21 and UX-27 the default behavior instead of something each screen rebuilds.
- Generated API types plus drift checks mean the console and server can't silently disagree, and E2E tests against a real server catch what types can't.
- React Aria brings the APG keyboard models, RTL, and locale aware fields, so WCAG 2.2 AA and translation readiness rest on tested primitives.
- Lingui's compiled catalogs and route level splitting keep the first load inside a 230 KB budget that CI defends.
- No commercial dependencies and no third party telemetry, which suits self hosters.

**Negative / tradeoffs**:

- Three TanStack libraries plus React Aria and Lingui is a lot to learn for new contributors. The canonical pattern and the installed skills are the on ramp.
- The custom search serializer, the history helper, the storage module, the placeholder helper, the interaction lock, and the boot sequence are Orvano code to write and maintain (a few hundred lines with tests).
- A Babel pass (Lingui macros and the React Compiler) makes builds slower than a pure Oxc pipeline, and a wrong wiring fails silently, so the scaffold and I18N-9 must prove both transforms ran.
- No optimistic updates means every write shows a brief saving state instead of an instant change.
- TypeScript stays on 6.0 until the lint ecosystem supports 7, which gives up 7's much faster type checking for now.
- Browser mode tests in three engines and E2E tests against real containers are slower and need Playwright browsers plus a container engine (Docker or Podman, spec 0007 DEV-1) on every machine and runner.
- `eslint-plugin-jsx-a11y` doesn't declare ESLint 10 support, so feature 7 runs those rules in Oxlint instead (spec 0008 LNT-4).
- One rule names a library API still marked _verify with the first API screen_ (QA-2); its exact form may change then. RTE-6 was verified by feature 11.

**Neutral**:

- The data grid library, styling, and the visual design were decided by feature 11 in spec 0015 (react-data-grid, Tailwind CSS v4, `console/design.md`); this spec only fixes how they get data and strings.
- Contributors used to React Hook Form, React Router, or Radix need to adjust; the Replaces list names the swaps.
- One installed skill (`vercel-react-best-practices`) partly conflicts with this spec (see `## Decision`); agents apply WEB-0.
- This spec amended spec 0001's UX-35 (focus stays on tab and resource list links), and UX-28's key example gained the `table:` segment.

## Follow-up

- [x] Feature 6 scaffold (spec 0007 Follow-up, CSL-1 to CSL-3): verified 2026-10-16 and recorded in place: the single Babel pass (I18N-2), the scroll restoration attribute (RTE-3), the `onResolved` payload (RTE-7), TanStack Form's `isDefaultValue` (FRM-5), and `@react-aria/optimize-locales-plugin` under Rolldown (I18N-6).
- [x] Feature 11: confirm `createLink` over React Aria's `Link` keeps preloading on hover and focus (RTE-6) when `src/ui` builds the `Link`; record any changed form here. (Confirmed with no change to the form: `console/src/ui/link.test.tsx`, spec 0015 AC-15.)
- [ ] The first screen that calls the API (features 10 and 13): confirm MSW 3 in browser mode in Chromium, Firefox, and WebKit (QA-2); record any changed form here.
- [x] Feature 6: decide the console's folder in the monorepo, its fixed base path, how the server serves it (same origin), and the dev proxy, so `shared/config.ts` and `basepath` have values. Closed by spec 0007: folder `console/` (REP-1), base path `/console` (CSL-1), served same origin by the server with an `index.html` fallback (CSL-2), and the Vite dev server on 5173 proxying every path outside `/console` to the server on 5080 (CSL-3).
- [ ] Feature 7 (`/audit`): lift these rules into the console's `AGENTS.md`; record the 8 installed skills under `## Agent skills` with the WEB-0 overrides; note the React Aria docs MCP (`@react-aria/mcp`) as an MCP server to connect.
- [x] (Done in spec 0008: ESLint 10 with Oxlint's `jsx-a11y`, LNT-4; the restriction rules and import boundary, LNT-3; size-limit over the Vite manifest, CI-2.) Feature 7: choose ESLint 9 or 10 given `eslint-plugin-jsx-a11y`'s peer range, pick the import boundary plugin, write the restricted syntax rules (QRY-2, URL-3), and pick the bundle size runner.
- [ ] After the console installs its packages: run `npx skills experimental_sync` to pull the official TanStack skills that ship inside the TanStack npm packages (they couldn't be installed from the GitHub repo on 2026-10-15).
- [ ] Move to TypeScript 7 when typescript-eslint and `eslint-plugin-lingui` support it (WEB-2).
- [x] Feature 10: the error body must carry what `ApiError` maps (FLT-1): kind or code, field errors, request ID, required role, retry after, a session expired code (QRY-8), and an expired cursor code (URL-4). Also the version field or header for writes (QRY-6), the session endpoint (returning the user ID and locale), and the runtime console config endpoint (RTE-10). Settled in spec 0014 (2026-10-25): PRB-1 and PRB-8 (`session_expired`, `invalid_cursor`), CNC for versions, CPX-3 for the session and config endpoints (feature 13 builds them).
- [x] Feature 11: build `src/ui/form` (FRM-1, FRM-2), `Link` with `createLink` (RTE-6), `Announcer` on `@react-aria/live-announcer` (A11Y-4), `useDelayedPending`, `GridSkeleton`, and `ContentSkeleton` with the UX-21 timing, `UnsavedChangesGuard` on `useBlocker` (RTE-8), `RouteTabs` (RTE-6), and `useShortcut` (A11Y-7); pick the styling technology and the grid library against these rules. (Built by feature 11, spec 0015 milestones 1 to 6: Tailwind CSS v4 for styling, react-data-grid for the grid, built by the rows feature.)
- [ ] Feature 5: send `Referrer-Policy: same-origin` and a CSP that allows everything this spec needs (no inline scripts, `theme-boot.js` as a file, Zod jitless); confirm or tighten SEC-5's `minimumReleaseAge`.
- [ ] Feature 13: the account locale setting that I18N-6 reads first.
- [ ] Feature 24: keep `:` out of column names (URL-2).
- [ ] Optional: install a TanStack Query skill later if the TanStack sync doesn't include one.

## Rationale

Reasoning, options, research, and sources: see [rationale.md](rationale.md).
