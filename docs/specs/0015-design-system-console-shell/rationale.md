# 0015. Design system and console shell: rationale

The decision record behind [index.md](index.md). `/develop` builds from index.md and skips this file.

## Context

Spec 0001 fixed how every console screen behaves (UX-1 to UX-41) and named about 45 primitives the design system must provide. Spec 0003 fixed how the code is written: React Aria Components for accessible building blocks, only `src/ui` may import it (DIR-3), every string through Lingui, a 200 KB first load budget (BDL-1). Both left the visual language, the styling technology, the grid library, the CSS budget, and the workbench to feature 11. Today the console is the feature 6 scaffold: a router, i18n, a query client, and one welcome page, with no styles at all.

Feature 13 (console accounts and projects) is the first feature with real screens, and it needs a shell to put them in plus forms, sheets, dialogs, tables, copy fields, a secret shown once, and delete confirms. Without a design system first, feature 13 would style its own screens, and every later service would inherit or fork those choices. Spec 0001's promise (the rules live inside the primitives, so following them is the easy path) only holds if the primitives exist before the screens.

Several hard constraints shape the options. Spec 0006 NET-2 serves the console with `style-src 'self'` and `font-src` falling back to `'self'`: no inline style elements, no third party fonts. Spec 0009 DEP-1 allows permissive licenses and MPL-2.0, but not OFL-1.1, the license of nearly every open web font. The console must meet WCAG 2.2 AA in both themes and both densities, work in forced colors mode, at 400% zoom, and right to left (UX-3, UX-34, UX-39). One build serves every install, so nothing install specific can be baked in (WEB-8). The team is small and builds with AI agents, so whatever is chosen must be well known, hard to misuse, and checkable by tools.

The APIs the shell displays (the CPX-3 session and config endpoints, org and project lists, project capabilities) are feature 13's and are not in `console.openapi.json` yet. The search, active jobs, and bundled docs behind the command palette, activity indicator, and help pane come even later (features 25, 46, 32).

## Options considered

### Option 1: Own design system on React Aria Components, styled with Tailwind CSS v4 tokens

Write Orvano's primitives in `src/ui` on React Aria Components, with Tailwind v4 as the styling layer. Tailwind's default scales are reset so only Orvano's semantic tokens exist, and themes swap CSS variables.

**Pros**:

- Fits spec 0003 exactly (React Aria Components, `src/ui` boundary), with full control of markup, tokens, and behavior per spec 0001.
- Tailwind v4's `@theme` maps tokens straight to CSS variables, its logical utilities cover right to left, and React Aria's data attributes style states with no JavaScript. The output is one build time file, so the CSP holds.
- Resetting the palette makes off token styling impossible, not just discouraged, which matters when agents write most screens.
- Tailwind and React Aria are both widely known, well documented, and have agent skills.

**Cons**:

- The most work: every primitive is built and tested by hand.
- Utility classes in markup are dense to read, and Tailwind v4 needs modern CSS (OKLCH, `@property`, cascade layers), so very old browsers aren't supported (fine for a developer console).

### Option 2: Adopt a styled kit built on React Aria (React Spectrum S2, HeroUI, or a Tailwind starter kit)

Install or copy a finished component library on React Aria and theme it to Orvano's colors.

**Pros**:

- Fastest start: dozens of styled, accessible components on day one.
- Upstream fixes arrive for free (for an installed kit).

**Cons**:

- Spectrum S2 carries Adobe's look and its own style macro build step; HeroUI adds its own theme system and a large dependency; both fight the token reset and the "no className" rule.
- None of them implement spec 0001's specifics (tier mapped `ConfirmDelete`, UX-21 skeleton timing, one announcer, the resource layout), so the important primitives are still hand built, now around someone else's conventions.
- A copied starter kit is Option 1 with someone else's first draft: useful as reference, not as a decision.

### Option 3: shadcn/ui (Radix primitives) with Tailwind

The popular copy in component approach on Radix UI.

**Pros**:

- A very large ecosystem, excellent agent familiarity, and a ready token convention.

**Cons**:

- It contradicts spec 0003, which chose React Aria Components (DIR-3, A11Y-2) for its stronger APG keyboard models, internationalized date and number handling, and `@react-aria/live-announcer` (A11Y-4). Switching would reopen an accepted spec.
- Two accessibility foundations in one console (Radix for most, React Aria where it is better) would double the patterns to test.

### Option 4: Own system on React Aria with CSS Modules and custom properties

Like Option 1, but each primitive has a plain CSS module, with tokens as CSS custom properties.

**Pros**:

- No new styling dependency; built into Vite; plain CSS is easy to read and fully encapsulated.

**Cons**:

- Nothing stops a module from writing `color: #e11` or `margin-left`: token and right to left discipline depends on review, not tooling.
- More files and more CSS per primitive; feature code layout needs either its own modules or a utility layer anyway.

## Rationale

Option 1 is the only one that keeps both earlier decisions intact (spec 0001's behavior inside the primitives, spec 0003's React Aria boundary) while making the visual rules enforceable by tools. The forces that settle it: agents write most of the console, so a rule that review must catch will drift, while a utility that doesn't exist can't be misused. Resetting Tailwind's scales gives that for color, type, radius, and shadow; the lint rules give it for direction and arbitrary values; the contrast test and the `design.md` check give it for the tokens themselves. Option 4 is a reasonable runner up and would be the choice if adding a styling dependency were off the table, but it moves all of that enforcement to review. Option 2 saves less than it seems, because spec 0001's distinctive primitives aren't in any kit, and Option 3 would reopen spec 0003.

The cost is the build effort, which the build plan contains by doing only what feature 13 needs plus the shell, and deferring `DataGrid`, `LogStream`, `JsonEditor`, and `RelationPicker` to the features that need them. The Tracer Bullet milestone 1 proves the whole pipeline (tokens, theme boot, CSS budget, lint, gallery, axe in three engines) on one primitive before the rest are built.

### Sub decisions

Each was asked of the engineer with a recommendation; all were accepted as recommended.

| Decision                   | Choice                                                  | Why                                                                                                      | Runner up                              |
| -------------------------- | ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- | -------------------------------------- |
| Design source              | No design yet; this spec proposes the direction         | No design tool, screenshots, or existing UI to work from                                                 |                                        |
| Build scope                | Shell, foundations, and the primitives feature 13 needs | The rest need APIs that don't exist yet (search, jobs, dependents for rows)                              | Every listed primitive now             |
| Shell data                 | Props (`ShellModel`); feature 13 wires routes           | The CPX-3 and capabilities payloads aren't in the contract yet; guessing them would fork from feature 13 | Hand typed MSW handlers now            |
| Styling                    | Tailwind CSS v4                                         | Token to variable mapping, logical utilities, build time output, enforceable reset                       | CSS Modules                            |
| Typeface                   | System stacks                                           | Zero bytes, every script, no OFL license change                                                          | Self hosted Inter (needs DEP-1 change) |
| Icons                      | Lucide (ISC)                                            | Large, consistent, per icon imports                                                                      | Phosphor                               |
| Tone                       | Quiet neutral tool                                      | Content and status stand out                                                                             | Brand forward                          |
| Accent                     | Indigo                                                  | Distinct from Supabase green and Appwrite pink, clear of the status hues                                 | Teal                                   |
| Neutrals                   | Cool zinc                                               | Pairs with indigo, crisp in both themes                                                                  | Pure neutral                           |
| Themes                     | System (default), Light, Dark                           | Follows the OS; forced colors and `prefers-contrast` covered by CSS                                      | Extra high contrast themes             |
| Radius                     | 4, 6, 8 px                                              | Precise, tool like, compact in grids                                                                     | 8 to 12 px                             |
| Base size                  | 14 px                                                   | The norm for dense consoles; rem based, so zoom works                                                    | 16 px                                  |
| `design.md` home           | `console/design.md`                                     | Next to the code and `console/AGENTS.md`                                                                 | Repo root                              |
| Grid library               | react-data-grid (MIT)                                   | Small, ARIA grid keyboard, virtualization, editing, right to left                                        | TanStack Table plus Virtual            |
| Sidebar look               | Tinted neutral surface with a border                    | Calm, separates regions for low vision users                                                             | Dark sidebar                           |
| Top bar extras             | Built now with local data                               | Fixes positions (UX-31) and behavior; server parts plug in later                                         | Reserved empty slots                   |
| Screen reader test         | The engineer runs the script in both pairs              | Spec 0001 requires both pairs                                                                            | VoiceOver only for now                 |
| Theme and density switches | Account menu (and the palette)                          | One place, always reachable                                                                              | Settings page                          |
| Org and account pages      | Same shell, scoped nav                                  | Layout never changes shape                                                                               | No sidebar outside projects            |
| Syntax highlighting        | None for now                                            | Short snippets, no bytes; editors bring their own later                                                  | Shiki, lazy loaded                     |
| CSS budget                 | 30 KB gzip                                              | Room above typical Tailwind output, still caught when it grows                                           | 20 KB                                  |
| Brand mark                 | Text wordmark and a simple mark                         | No logo yet; swappable in one file                                                                       | A logo you provide                     |
| Gallery                    | Development only                                        | No bytes or surface on real installs                                                                     | Shipped, hidden                        |
| Right to left proof        | A right to left switch, a browser test, a lint rule     | Proves UX-34 before a right to left language ships                                                       | Lint only                              |

### Decisions made while writing (not asked)

- **Gallery as a second Vite entry, not `routes/_dev`.** The engineer chose a development only gallery. Route files ignored only in production would make the committed `routeTree.gen.ts` differ between dev and build, breaking the drift check, so the gallery is `gallery.html` with its own code based router instead. Runner up: a `_dev` route guarded at runtime, which ships its code.
- **No class merging library.** Primitives take no `className`, so `tailwind-merge` and `tailwind-variants` have nothing to do; a 10 line `cx` is enough. Runner up: `tailwind-variants`, worth it only if primitives start accepting outside classes.
- **No React Aria Tailwind plugin.** Tailwind v4's built in data variants (`data-focus-visible:`) cover React Aria's states. Runner up: `tailwindcss-react-aria-components`, which only shortens the class names.
- **`unavailableReason` instead of `isDisabled` for UX-30.** React Aria's disabled state removes focus, but UX-30 needs focusable, explained, unavailable actions.
- **Theme without JavaScript.** A media query fallback on `:root:not([data-theme])` means a failed boot script still follows the OS, so the worst case is the right theme a moment late, not a white flash in a dark room.
- **The palette.** Values were computed to meet AA in both themes (the TKN-7 checks were run while writing: every text token on every surface passes 4.5:1, the tightest being white on the danger button at 4.76:1, and every boundary and focus pair passes 3:1, the tightest being the dark focus ring on `accent-subtle` at 4.47:1 and the dark strong border on `bg-muted` at 3.15:1).

### Agent skills

The engineer asked for a registry search. Installed: `react-aria` (served from react-aria.adobe.com, Apache-2.0) and `tailwind-4-docs` (`lombiq/tailwind-agent-skills`). Not offered as too thin or off target: the Lucide and data grid skills (single digit installs or not specific to react-data-grid), shadcn skills (Radix, not React Aria). Declined as overlapping `design.md`: `wshobson/agents@tailwind-design-system`. No MCP server fit these tools.

### Cross check (2026-10-26)

A read only pass on a different model (Sonnet) reviewed the first draft for decisions it left to the builder. The engineer chose to apply every recommended fix. What changed:

- **Token source flipped.** The draft kept tokens in both `design.md` and `theme.css` and compared them. Parsing markdown both ways was fragile, so `theme.css` is now the only source and `design.md`'s tables are generated from it (TKN-6).
- **Tailwind v4 details.** Layers, durations, and row height are not Tailwind theme namespaces, so they became plain CSS variables (TKN-1). The stylesheet now scans only `console/src` (`source(none)` plus `@source`), sets a base layer (Preflight's border and placeholder colors would otherwise fall back to `currentColor`), and the build sets `cssCodeSplit: false` so one CSS file is really one (STY-1, STY-2).
- **CSP tested, not assumed.** A Playwright check serves the built console under NET-2's header (STY-6, AC-23).
- **Shell props.** `Breadcrumbs` can't call query hooks from `src/ui` (DIR-2), so feature 13 passes `crumbs`; link options stay loosely typed until feature 13 registers the shell routes (SHL-1).
- **Right to left.** `pseudo-RTL` is not a valid locale tag for Lingui, `Intl`, or React Aria, so the proof uses a direction switch with React Aria's `ar-XB` locale instead (RTL-1). Slides use `translate-x` with an `rtl:` mirror, and the lint pattern is exact (MOT-1, RTL-2).
- **React Aria gaps made explicit.** `unavailableReason` (PRM-4), toast merging, history, and three visible toasts through one `notify()` (PRM-8), the docs pane's focus handling (SHL-9), and the resize separator's keys (SHL-13) are Orvano's own code.
- **Smaller gaps closed.** The breakpoint in rem with one `useIsWide()` (SHL-6), density applied at boot through one layout store (SHL-11), switcher behavior (SHL-12), lazy primitives (PRM-10), the clipboard fallback (PRM-9, AC-24), `StatusChip` statuses, `Avatar`, tooltip tokens, overlay widths, button recipes, `RelativeTime` and `Kbd` details (PRM-11), the browser floor (STY-7), and the test harness for each scenario.
- **Contrast coverage widened** from a hand kept pair list to every text token on every surface (TKN-7); the extra 40 pairs it named were computed and all pass.

### AC-3 first load budget (2026-10-27)

Milestone 5 left this open: with `AppShell` in the first load, the console went over spec 0003 BDL-1's 200 KB. The options were loading the account menu, switcher, and breadcrumb menu popovers lazily, or raising the budget. `/architect` measured before deciding. Each number is gzip, from `pnpm size` on `main` at 8533e18, and counts the entry chunk, the `__root` route, and the `/` route chunk that renders the page, plus their static imports (the same set BDL-1 counts for the `_shell` routes). The shell was rendered from the welcome route in project scope, as the probe in STY-9 does.

| Build                                                             | First load |
| ----------------------------------------------------------------- | ---------- |
| Today, welcome page in `AuthLayout`, route chunk counted          | 209.4 KB   |
| Today, plus a `sideEffects` list in `console/package.json`        | 181.3 KB   |
| `AppShell` rendered                                               | 235.7 KB   |
| `AppShell`, plus `sideEffects`                                    | 236.8 KB   |
| `AppShell`, plus `sideEffects` and lazy popovers                  | 230.3 KB   |
| `AppShell`, plus `sideEffects` and `zod/mini` (chosen)            | 218.9 KB   |
| `AppShell`, plus `sideEffects`, `zod/mini`, and lazy popovers     | 211.9 KB   |
| Last line, plus a chunk group for React Aria's list and menu code | 214.8 KB   |

What the measurements showed:

- **The CI number hides the route chunk.** `pnpm size` reports 187.8 KB today because BDL-1's route list has no `/` route, so the welcome route's own chunk is left out. BDL-1 counts the `__root` and shell routes, not `/`, so a shell measured from `/` has to count that chunk itself, which the probe does (STY-9).
- **The `@/ui` index pulled in the whole kit.** Without `sideEffects`, Rolldown keeps every module the index re-exports that has top level code (a `lazy()` call, a context), so even the welcome page loaded the shell, the palette actions, and the drawer. The list removes about 28 KB from today's first load. With the shell rendered it gains nothing (1 KB more from a different chunk layout), because the shell uses nearly all of those modules.
- **Classic Zod costs about 17 KB more than `zod/mini`.** Built alone with the console's storage schemas, classic Zod is 23.5 KB and `zod/mini` 6.6 KB. Spec 0003's rationale had estimated 5 KB for Zod, which was the core only.
- **Lazy popovers save only about 7 KB.** React Aria's selection and collection code stays in the first load through `Breadcrumbs` and the toast region, so moving the three panels out moves only the menu, list box, and autocomplete parts. A chunk group for that code made it worse, because the group is then imported by the first load. Getting under 200 KB would also mean deferring `Tooltip`, `Drawer`, the activity popover, and possibly the toast region, which is unproven and touches AC-7 to AC-11 the most.

**Decision.** The engineer chose 230 KB with the popovers eager, plus `sideEffects` and `zod/mini` (STY-8, STY-9). That leaves about 11 KB for feature 13's session, config, and API client code. Runner up: 220 KB with lazy popovers (about 8 KB of headroom), whose cost is a first open that waits on a chunk unless it preloads, and a keyboard test to prove the first ArrowDown still lands on the first item. Keeping 200 KB was rejected for the reasons above.

**Decided while writing (not asked).** Zod goes through `zod/mini` everywhere, not only in the first load: one API, a lint rule that can enforce it, and no way for a shared module to bring classic Zod back into the first load. TanStack Router and TanStack Form take `zod/mini` schemas through Standard Schema, the same as classic ones. Runner up: classic Zod allowed on lazy routes, which costs those routes 17 KB and needs a rule nobody can lint.

**Cross check (2026-10-27).** A read only pass on Sonnet looked for decisions step 25 would still leave to its builder. The engineer chose to apply every recommended fix: how the probe alias and build run, where `size-entries.mjs` finds the probe manifest and what it counts, the size script test, the full list of files that move to `zod/mini` with conversion examples, a manifest comparison that checks the `sideEffects` list, the lint rule's home and fixture, and the probe's catalog and lint exemptions.
