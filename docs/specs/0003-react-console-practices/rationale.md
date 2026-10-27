# 0003. React console practices: rationale and research

Decision record for [index.md](index.md). `/develop` reads index.md; this file is for people and for `/architect` on a later update.

## Context

The Orvano console is a React single page app built with Vite (given in `docs/scope/index.md`). It is the main way developers manage a self hosted Orvano: auth users, databases and table rows, storage, functions, sites, messaging, and settings. Spec 0001 already fixed how it behaves (UX-1 to UX-41): a resource list that stays mounted beside the open item, tabs as routes, nearly all view state in the URL with exact push and replace rules, data kept on screen while paging, cancelled requests, versioned cell saves with undo, versioned browser storage with safe fallbacks, and WCAG 2.2 AA with every string translation ready. Spec 0001's follow ups hand four code questions to this feature: nested route layouts (UX-2, UX-5), URL query state with the UX-27 history rules, request cancelling (UX-21), and safe browser storage (UX-28).

No console code exists yet. The workflow tier is GA and the build approach Tracer Bullet, so the first slice already runs through server, console, and SDKs, and every later screen copies whatever the first one does. The team is small, so the practices must be learnable from one canonical example and checked by tools rather than memory. Self hosting adds constraints: one build serves every install, nothing calls third party services, and a strict Content Security Policy should be possible.

The React ecosystem moves fast. Since 2025, Vite moved to Rolldown (Vite 8), `@vitejs/plugin-react` 6 dropped its built in Babel option, the React Compiler reached 1.0, TypeScript 7 shipped as a native compiler, Vitest's browser mode became stable, MSW reached 3.0, and Lingui reached 6. Choosing from memory would lock in stale choices. Without a recorded standard, each screen would pick its own fetching, state, and form approach, and spec 0001's rules would be rebuilt (or missed) per screen.

## Options considered

The libraries were chosen one area at a time (see _Research by practice area_). The options here are how the standard is enforced.

### Option 1: Document and enforce from the first line of code

Write the rules and canonical pattern, and turn on type checks, lint rules, drift checks, axe checks, and the bundle budget in CI before the first screen.

**Pros**: rules hold from commit one; no cleanup later; reviews spend time on design.
**Cons**: more setup in features 6 and 7 before the first screen; strict checks slow quick experiments.

### Option 2: Document, build the first slice, then enforce

Write the rules now, add the CI gates after Slice 1 lands, and fix what doesn't comply in one PR.

**Pros**: faster first screen; gates are tuned on real code.
**Cons**: the first screens become the pattern others copy, compliant or not; a migration PR touches every early screen.

### Option 3: Document only

Rely on review and the canonical pattern.

**Pros**: no tooling cost.
**Cons**: drifts fastest; GA tier and WCAG 2.2 AA can't rest on memory; literal strings and import boundaries are exactly what reviewers miss.

## Rationale

Option 1, chosen by the engineer and matching specs 0001 and 0002. There is no code to migrate, so enforcing now costs only setup, while Tracer Bullet means the first screen is copied by every later one. Spec 0001 already requires an import rule, a literal string rule, and automated accessibility checks, so most of the lint and test wiring is owed anyway. The extra gates here (API, route tree, and catalog drift checks, plus the bundle budget) are cheap and catch the failures that hurt a self hosted product most: a console that disagrees with its server, untranslated strings, and slow first loads.

## Research by practice area

Each pick lists the main alternatives and why it won. Versions are from the npm registry on 2026-10-15.

### Routing: TanStack Router 1

- **Chosen** because spec 0001 puts filters, sort, cursor, page size, open row, and open sheet in the URL, with exact push and replace rules. TanStack Router validates search params with a schema (Zod 4 directly, through Standard Schema), types them end to end, has search middlewares to keep or strip params per route (`retainSearchParams`, `stripSearchParams`), lets each navigation choose replace, supports nested and pathless layouts, and has loaders built to prefetch into TanStack Query. Its Vite plugin generates the typed route tree from files and splits each route's code automatically, which serves the bundle budget. Its `pendingMs` and `pendingMinMs` options match UX-21's 300 ms and 500 ms skeleton timing exactly.
- **React Router 8 (data mode)**: the most used router, with nested layouts and loaders, but search params are untyped strings, so UX-15 and UX-27 would need a hand built typed layer on top.
- **React Router 8 (framework mode)**: typed route params through type generation, but it brings its own build pipeline, more than an SPA needs.
- **Custom serializer**: TanStack's default JSON search encoding would turn spec 0001's repeated `filter=column:operator:value` params into one encoded JSON array. URL-2 plugs in a `URLSearchParams` based serializer so links stay readable and match UX-15.

### Data fetching and caching: TanStack Query 5 with a generated client

- **TanStack Query** is the established server cache for React. It passes an `AbortSignal` to every query and aborts it when the key changes or nothing uses it (UX-21 cancelling), and `placeholderData` keeps old data on screen while paging. `refetchOnWindowFocus` accepts a function, which lets the interaction lock honor UX-19's "never while editing". Mutation `scope` runs writes to one row in order. No stable v6 exists yet (5.104 is current).
- **Client: openapi-typescript 7 plus openapi-fetch 0.17**, chosen by the engineer. It generates types only, plus a tiny fetch wrapper, so the generated code is small and readable, and the TanStack Query factories stay hand written and explicit. Hey API (0.99, still 0.x with frequent breaking changes) and Orval (8.43, large generated code) generate hooks for you, but tie the console's data layer to a generator's output. The public JS SDK (Appwrite's approach) was set aside because console only endpoints would have to live in it or a side package, and the console would wait on SDK releases.
- Option factories (`queryOptions`) with hierarchical, project first keys are TanStack's recommended way to share one definition between loaders, components, and invalidation.

### Client state: React state plus small stores

Server state belongs in the query cache and view state in the URL, which leaves little else. Zustand (5.0) is the usual pick for what's left, but it would be one more dependency and invites copying server data into a store. A `createStore` helper on React's `useSyncExternalStore` is about 30 lines and covers toasts, the announcer, session status, the interaction lock, and preferences. Redux Toolkit duplicates TanStack Query.

### Forms: TanStack Form 1 with Zod 4

- **TanStack Form 1.33** has separate per field `onChange`, `onBlur`, and `onBlurAsync` validators, which map onto UX-8 (check on leave, recheck on change after an error, server checks on leave only). It is fully typed, accepts Standard Schema validators, can set field errors from the form's submit validator (server errors landing on fields), and is built on a selector based store that suits the React Compiler. The form composition API (`createFormHook`) lets the design system own every field component.
- **React Hook Form 7.90** is the most used and its `onTouched` mode matches UX-8's timing, but server checks on leave need a helper, it has had friction with the React Compiler, and the v8 that addresses this is still in beta.
- **Zod 4.6** over Valibot 1.5: Zod works directly with TanStack Router search validation and TanStack Form, has the larger ecosystem, and costs about 5 KB gzip (1.9 KB for `zod/mini`), small inside a 200 KB budget. One schema library serves forms, URL search, and stored preferences. `jitless` mode keeps it working under a CSP without `unsafe-eval`.

### Accessible components: React Aria Components 1.22

Chosen by the engineer to sit under the design system. It implements the ARIA Authoring Practices patterns for dialogs, menus, combobox, listbox, tabs, table, and grid list, has built in RTL handling, locale aware number and date fields, and built in strings translated into dozens of languages, under Apache 2.0 with frequent releases. That covers WCAG 2.2 AA and translation readiness together. Its `@react-aria/live-announcer` region lets UX-41's one announcer include React Aria's own announcements. Base UI (MIT, v1.9 stable, from the MUI team) is a strong modern option, but has fewer complex widgets and less i18n built in. Radix Primitives (MIT) has a huge community, but its pace has slowed, and it leaves i18n and date fields to you. Ariakit has strong APG coverage but is still 0.x. The data grid itself stays with feature 11 (decided in this conversation), since picking it needs a hands on trial against UX-11 to UX-19.

### Translation: Lingui 6

Lingui 6.9 uses ICU messages (plural, select), compiles `.po` catalogs on import through its Vite plugin (so there are no compiled files to manage), has an extract CLI and a `check sync` drift check, and ships a lint plugin with a no unlocalized strings rule (spec 0001's literal string rule). Its runtime is a few KB. FormatJS (react-intl 12) also uses ICU and needs no build plugin, but has a larger runtime and needs explicit IDs or a separate extraction step. i18next (26) has the biggest ecosystem but its own plural key format by default (ICU only through a plugin) and a larger runtime. Lingui's cost is a Babel pass for its macros, but the React Compiler needs one anyway. Lingui 6 requires Node 22.19 or later, which pushed WEB-3 to Node 24 LTS.

### Testing: Vitest 5 browser mode, MSW 3, Playwright 1.64, axe

- **Vitest browser mode** (stable since Vitest 4, current 5.0) runs component tests in real Chromium, Firefox, and WebKit through Playwright, so focus order, keyboard events, layout, and axe results match real browsers. jsdom can pass a keyboard or accessibility test that fails in a browser, which matters for a WCAG 2.2 AA target.
- **MSW 3** mocks the API at the network level in the browser worker, so components run their real client code. `openapi-msw` (2.1, supports MSW 3) types handlers from the same generated `paths`.
- **Playwright against a real server**, chosen by the engineer, matches Tracer Bullet: each slice proves console and server agree end to end. Containers run under Podman on this machine (spec 0002 TST-5a).
- **axe**: `vitest-axe` is about four years old and unmaintained, so the console calls `axe-core` (4.14) directly through a small helper, and uses `@axe-core/playwright` in E2E. Both are MPL 2.0, allowed as dev dependencies only (WEB-7).
- No coverage gate, by the engineer's choice: required tests by kind are checkable and avoid shallow tests written to hit a number.

### Bundle size: 200 KB gzip first load

Chosen by the engineer. A rough sum of the chosen runtime (React and React DOM, the router, TanStack Query and Form, React Aria for the shell's primitives, Lingui, Zod) fits under 200 KB gzip with each service lazy loaded per route, leaving room for the shell and the overview page. Measuring from Vite's manifest makes the check independent of hashed file names. size-limit (14.2) and `rollup-plugin-visualizer` (7.1, supports Rolldown) are current options for the runner and the treemap.

**Amended 2026-10-27 (spec 0015 STY-8).** Measured with the shell rendered, the first load was 235.7 KB. The 5 KB estimate for Zod above was the core only; classic Zod with the console's schemas is 23.5 KB, and `zod/mini` 6.6 KB. With `zod/mini` and a `sideEffects` list the shell measures about 218.9 KB, and lazy popovers would save only about 7 KB more, so the engineer raised the first load to 230 KB. The measurements are in [spec 0015's rationale](../0015-design-system-console-shell/rationale.md#ac-3-first-load-budget-2026-10-27).

### Calls made without a question

Smaller calls settled while writing, each with its runner up:

| Call                                                                                          | Why                                                                                                                                                           | Runner up                                                               |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| TypeScript 6.0, not 7 (WEB-2)                                                                 | typescript-eslint 8.71 supports `<6.1.0` and `eslint-plugin-lingui` `^5 \|\| ^6`, so 7 would break type aware lint                                            | TypeScript 7 for `tsc` plus 6 for lint, two compilers that can disagree |
| Node 24 LTS (WEB-3)                                                                           | Lingui 6 needs 22.19 or later; Node 24 is the active LTS, Node 20 is out of support                                                                           | Node 22                                                                 |
| React Compiler through Babel (WEB-6)                                                          | The plugin's native Rust `compiler` option is marked experimental                                                                                             | The Rust option, revisit when stable                                    |
| Lingui macros and the compiler in one Babel pass (I18N-2)                                     | Babel runs plugins before presets, so macros still expand first, and each file is parsed once; two passes stay as the fallback                                | Two passes, as the Lingui setup skill shows                             |
| `erasableSyntaxOnly`, no `enum` (WEB-4)                                                       | Oxc strips types without transforming; unions plus `as const` cover enums                                                                                     | Allow `const enum`                                                      |
| No `exactOptionalPropertyTypes` (WEB-4)                                                       | Clashes with Zod 4, TanStack, React Aria, and openapi-typescript optional types for little gain (changed after the cross check, with the engineer's approval) | Keep it and fight the library types                                     |
| Loaders: layouts await frame data, leaves only prefetch (RTE-4)                               | Awaiting grid data would swap the grid for the pending skeleton on every page change, breaking UX-21                                                          | Await everything, simpler                                               |
| Repeated `filter` params via a custom serializer (URL-2)                                      | Matches UX-15's URL format and keeps links readable                                                                                                           | TanStack's JSON encoding                                                |
| Columns named by name in the URL (URL-2)                                                      | Readable links; a rename shows UX-15's ignored filter chip instead of failing silently                                                                        | Column IDs, which survive renames but are opaque                        |
| Filter rules kept as text in the route schema (URL-1)                                         | A navigation validates the search again, and parsed objects would fail a text schema and be wiped by `.catch`                                                 | Parsing in the schema                                                   |
| Adding or removing a filter rule pushes; typing a value replaces (URL-3)                      | UX-27 lists typing as replace but is silent on adding rules; a rule is a meaningful step Back should undo                                                     | Replace for every filter change                                         |
| `staleTime` 30 s, reads retried twice on network and 5xx gateway errors only (QRY-4)          | UX-19's 30 second rule and UX-22's retry rule                                                                                                                 | Shorter staleTime with more refetching                                  |
| `networkMode: 'always'` (QRY-4)                                                               | The default `'online'` pauses requests offline: skeletons hang, and paused writes fire by themselves on reconnect, against UX-22                              | The default, plus custom pause handling                                 |
| Interaction lock held through a counting hook (QRY-4)                                         | UX-19 forbids reloads while editing, selecting, or dirty; a hook with cleanup can't leave the lock stuck                                                      | A plain acquire and release API                                         |
| Placeholder data scoped by resource (QRY-5)                                                   | Plain `keepPreviousData` would show the previous table's rows when switching tables                                                                           | Clearing data on every key change                                       |
| No optimistic updates; patch the cache on success (QRY-6)                                     | In Query 5 a scoped mutation's `onMutate` runs before its turn, so rollbacks of queued edits can clobber each other; UX-12 asks for a saving state anyway     | Optimistic updates with hand ordered rollbacks                          |
| Mutation scope per row (QRY-6)                                                                | Keeps cell saves and undo in order                                                                                                                            | Disabling a row while it saves                                          |
| Focus stays on tab and resource list links (RTE-7)                                            | Moving focus to the `h1` would pull keyboard users off the list they're using; spec 0001 UX-35 amended to match                                               | Literal UX-35, focus to `h1` on every path change                       |
| Sign out does a full page load (QRY-9)                                                        | Clears every cache and store in one step, no leaks between accounts                                                                                           | Clearing each cache by hand                                             |
| `theme-boot.js` file reads one key (STO-6)                                                    | Right theme on first paint without an inline script under a strict CSP                                                                                        | Accept a flash of the wrong theme                                       |
| Announcer built on `@react-aria/live-announcer` (A11Y-4)                                      | One live region for both Orvano and React Aria messages (UX-41)                                                                                               | A separate region, risking double reads                                 |
| PO catalogs with generated IDs, pseudo locale in dev (I18N-3, I18N-7)                         | PO is what translation tools expect; generated IDs need no naming effort; the pseudo locale exposes UX-34 text growth                                         | Explicit message IDs                                                    |
| `Intl` only, no date library; binary units via a message (I18N-5)                             | `Intl` covers dates, relative times, numbers, and lists; Temporal isn't Baseline widely available yet; `Intl` has no binary units                             | date-fns                                                                |
| Zod `jitless` (SEC-2)                                                                         | Keeps a strict CSP possible                                                                                                                                   | Allowing `unsafe-eval`                                                  |
| Secret creating mutations with `gcTime: 0` (SEC-3)                                            | The secret leaves memory once its component unmounts (UX-33)                                                                                                  | Default cache time                                                      |
| pnpm `minimumReleaseAge` 3 days (SEC-5)                                                       | Supply chain defense against just published malicious versions                                                                                                | No delay                                                                |
| One test environment (browser mode); primitives in three engines, features in Chromium (QA-1) | Real browser behavior where it matters, CI time where it doesn't                                                                                              | jsdom for logic tests                                                   |
| E2E on Chromium per PR, three engines nightly (QA-5)                                          | Keeps PR feedback fast                                                                                                                                        | Three engines on every PR                                               |
| 80 KB gzip per lazy route chunk (BDL-1)                                                       | Keeps any one navigation quick; screens with editors can raise it in their spec                                                                               | No per route limit                                                      |
| Commit `routeTree.gen.ts` and `schema.gen.ts`                                                 | Type checks work on a fresh clone; drift checks catch staleness                                                                                               | Generate in CI only                                                     |
| Named exports, `kebab-case` files (WEB-9)                                                     | Greppable names, safe renames, no casing trouble on Windows                                                                                                   | PascalCase component files                                              |

### Installed agent skills

Installed into `.claude/skills/` on 2026-10-15, at the engineer's pick: `antfu/skills` (`vite`, `vitest`, `pnpm`), `vercel-labs/agent-skills@vercel-react-best-practices`, `lingui/skills` (`lingui-best-practices`, `lingui-framework-setup`), `microsoft/playwright-cli@playwright-cli`, and `addyosmani/web-quality-skills@accessibility`. The Lingui setup skill shaped I18N-2 (the Babel pass that `@vitejs/plugin-react` 6 needs, the `@babel/types` peer, the two pass fallback), I18N-3 (`lingui check sync`), and I18N-9 (a green build doesn't prove the macros ran). The official TanStack Router skills listed in the registry could not be installed from the GitHub repo, because TanStack ships them inside its npm packages; see the Follow-up in index.md. The engineer chose to note the React Aria docs MCP server for later. Full discovery notes: `docs/.agent-cache/tool-discovery/react-console-practices.md`.

### Cross check, 2026-10-15

An independent read of the draft on a different model (Sonnet) raised 30 findings. Code and wording bugs were fixed directly: `row` and `sheet` leaking into the grid's query key, a skeleton primitive that couldn't hold its 500 ms minimum (now `useDelayedPending`), type errors in the canonical code, the grid dropping its rows when a background refetch failed, filters parsed inside the route schema, enforcement claims no listed tool checks (now backed by a `signal` required client type, restricted syntax rules, and axe `best-practice` in E2E), focus timing after lazy routes, one component per crumb, StrictMode, and one error reporter. The engineer accepted the recommended fix for each of 24 decision gaps: the pending component and which routes await, session and capability guards with a same origin redirect, React Aria links through `createLink` and `RouterProvider`, a fixed base path and boot order, a counting interaction lock, columns named by name with sort and filter limits and draft handling, sheet naming and the row sheet's data source, the guard asking on row changes, focus staying on tab and list links (amending UX-35), not found mapping and error boundary placement, mutation error surfacing, the storage key catalog, the undo stack and cache patching, the test setup (merged Vite config, `createAppRouter`, E2E global setup), the budget measurement, smaller owners (Referrer-Policy, scroll restoration, shortcuts, signing in again as another user, sign out across tabs, no preload on list links, tsconfig gaps), `networkMode: 'always'`, dropping optimistic updates, parsing form values before submit, dropping `exactOptionalPropertyTypes`, one Babel pass, catalog and React Aria locale size, MSW in browser mode, and the browser API floor.

### Landscape facts, checked 2026-10-15

- React 19.3 (2026-09-09); React Compiler 1.0 stable since 2025-10-07.
- Vite 8.3 (8.0 stable 2026-03-12): Rolldown is the single bundler, Oxc transforms and minifies, Node 20.19+ or 22.12+. `@vitejs/plugin-react` 6.1 removed the `babel` option; the React Compiler runs through `reactCompilerPreset` with `@rolldown/plugin-babel`, or (experimental) a native `compiler` option.
- TanStack Router 1.170 and router plugin 1.168; React Router 8.4 (declarative, data, and framework modes). TanStack Query 5.104; TanStack Form 1.33; React Hook Form 7.90 (v8 in beta).
- Zod 4.6 (`zod/mini` core about 1.9 KB gzip, full about 5.4 KB); Valibot 1.5.
- Lingui 6.9 (ESM only, Node 22.19+); react-i18next 17.0 with i18next 26.4; react-intl 12.1.
- React Aria Components 1.22 (Apache 2.0); Base UI 1.9 (MIT); Radix UI 1.8; Ariakit 0.4.
- Vitest 5.0 (browser mode stable since 4.0); Playwright 1.64; MSW 3.0; axe-core 4.14 (MPL 2.0); `vitest-axe` 0.1.0, unmaintained.
- TypeScript 7.0 (native) and 6.0.3; typescript-eslint 8.71 supports TypeScript below 6.1; ESLint 10.12; `eslint-plugin-jsx-a11y` 6.10 declares ESLint up to 9.
- pnpm 12.11; openapi-typescript 7.13; openapi-fetch 0.17; Hey API 0.99; Orval 8.43; Zustand 5.0; size-limit 14.2.

Full notes: `docs/.agent-cache/research/react-console-practices.md` (reusable for 30 days).

## References

**Project sources**:

- `docs/scope/index.md`: given stack (React with Vite), GA tier, Tracer Bullet, self hosted first
- `docs/scope/foundations.md`, feature 2 row and the boundaries of features 5 to 11
- Spec 0001 (console UX patterns): UX-1 to UX-41 and its feature 2 follow up
- Spec 0002 (.NET backend practices): GEN-5 license allow list, TST-5a Podman, the rule ID and deviation conventions
- Installed agent skills in `.claude/skills/` (listed in index.md `## Decision`)

**Practices & standards**:

- WCAG 2.2 AA; WAI ARIA Authoring Practices Guide patterns
- ICU MessageFormat; CLDR plural rules
- Baseline (web platform feature availability)
- Rules of React; "you might not need an effect"
- Server state versus client state separation; URL as the source of truth for view state
- Content Security Policy without `unsafe-eval` or inline scripts
- Testing by role and accessible name (Testing Library guiding principles)

**Links** (opened during the 2026-10-15 check):

- Announcing Vite 8: https://vite.dev/blog/announcing-vite8
- React Compiler installation: https://react.dev/learn/react-compiler/installation
- React blog: https://react.dev/blog
- TanStack Router search params guide: https://tanstack.com/router/latest/docs/framework/react/guide/search-params
- React Router modes: https://reactrouter.com/start/modes
- Zod 4: https://zod.dev/v4
- Vitest blog: https://vitest.dev/blog/
- Base UI: https://base-ui.com/
- Hey API openapi-ts releases: https://github.com/hey-api/openapi-ts/releases
- Zustand releases: https://github.com/pmndrs/zustand/releases

Versions and licenses not on those pages come from the npm registry (`npm view <package> version license`, 2026-10-15), and the `@vitejs/plugin-react` compiler notes from its published README.
