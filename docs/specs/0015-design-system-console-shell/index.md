# 0015. Design system and console shell

**Date**: 2026-10-26
**Status**: In Progress
**Updated**: 2026-10-27, AC-3 first load budget settled: 230 KB with the shell, popovers stay eager, the barrel tree shakes, Zod through `zod/mini`, and a shell probe in `pnpm size` (STY-8, STY-9, step 25; spec 0003 BDL-1 amended). 2026-10-28, the review triage's questions settled: the top bar below 25rem, the live regions, the palette pattern, `ar` for right to left, user text in an isolate, `RouteTabs` overflow, `unavailableReason` on `Switch`, the gallery's scope, and new screen reader steps (AC-9, AC-12, AC-13, AC-15, AC-18, AC-20, AC-22, THM-6, SHL-4, SHL-12, SHL-14, PRM-4, RTL-1, RTL-3, GAL-2, GAL-3, SRT-1 to SRT-13; reasons in [rationale.md](rationale.md#review-triage-decisions-2026-10-28)). 2026-10-30, for spec 0016: AC-21's boot error page now also covers a failed config load (FRS-2), and AC-23's CSP test loads `/setup` (FRS-7). 2026-10-30, for spec 0017: RTL-1 notes the real `ar` catalog feature 13 adds. 2026-11-01, for spec 0017: feature 13 no longer adds that catalog, so RTL-1 says its screens use the same proof (spec 0017 LOC-5).

## Summary

This spec decides how the Orvano console looks and the frame every screen sits in. The look is a quiet, neutral developer tool: cool zinc grays, one indigo accent, the system fonts, Lucide icons, small 6 px corners, and a 14 px base size, with Light, Dark, and System themes. It is built with Tailwind CSS v4 on top of React Aria Components. The token values live in one stylesheet, and [`console/design.md`](../../../console/design.md) shows them alongside how each component looks and behaves. Feature 11 builds the tokens, the themes, the shell (top bar, sidebar, breadcrumbs, resource list), and the base primitives that feature 13 needs. Each primitive is checked with axe in three browsers and with real screen readers.

## Requirements

Builds on spec [0001](../0001-console-ux-patterns/index.md) (how screens behave, UX-1 to UX-41) and spec [0003](../0003-react-console-practices/index.md) (how the code is written). It does not restate their rules; it cites them. Where this spec and those disagree, those win, and the gap is fixed here or in a Follow-up that amends them.

**User stories**:

- As a developer using the console, I want every screen to look and behave the same, so that a new service feels familiar the first time I open it.
- As a developer who works at night, I want the console to follow my system theme, or a theme I pick, with no white flash on load.
- As a keyboard or screen reader user, I want to reach every part of the shell (skip link, sidebar, breadcrumbs, switchers, menus, dialogs, sheets, the command palette) without a mouse, and to hear what changed.
- As a low vision user, I want readable contrast in both themes, a visible focus ring, and a console that still works at 400% zoom and in forced colors mode.
- As a translator, I want every shell string in the catalog and a layout that holds in right to left languages and with 30% longer text.
- As the engineer of feature 13 and every later screen, I want finished primitives and one `design.md` to build from, so that I compose screens instead of styling them.

**Acceptance criteria**:

- **AC-1**: `console/design.md` covers type, color, spacing, radius, elevation, motion, density, focus, icons, theming, right to left, and, for every primitive feature 11 builds, what it is built on, its variants and states, its keyboard model, and notes. Its token tables are generated from `console/src/ui/theme.css` (the only source of token values), and `pnpm check` fails when the generated blocks are out of date (TKN-6).
- **AC-2**: The console is styled only with Tailwind CSS v4 through `@tailwindcss/vite`, from one stylesheet `src/ui/theme.css`. The default Tailwind color, font, text size, radius, and shadow scales are reset, so only the semantic tokens exist as utilities (`bg-red-500` and `text-black` compile to nothing). The build emits exactly one CSS file (`build.cssCodeSplit: false`), and no code injects a `<style>` element at runtime (STY-1 to STY-3).
- **AC-3**: The emitted CSS is at most 30 KB gzip, checked by `pnpm size` in CI (STY-4). The first load JavaScript with `AppShell` rendered is at most 230 KB gzip, and no emitted JavaScript file passes 80 KB (spec 0003 BDL-1 as amended, STY-8). `pnpm size` in CI checks it on the shell probe build (STY-9) until feature 13's `_shell` routes are measured directly. The primitives in PRM-10 load lazily; the account menu, switchers, and collapsed breadcrumb menu do not (STY-8).
- **AC-4**: The theme setting has three values, System (the default), Light, and Dark, stored in `orvano:v1:theme` (spec 0003 STO-2). `public/theme-boot.js` sets `data-theme` and `color-scheme` on `<html>` before the first paint, so neither the sign in page nor the shell flashes the wrong theme. With System selected, an OS theme change applies at once with no reload. With storage blocked, or with the boot script failed, the console follows the OS theme through CSS alone (THM-1 to THM-5).
- **AC-5**: In both themes, every text color token has a contrast of at least 4.5:1 on every surface token, and the boundary and focus tokens at least 3:1 on every surface token (the sets in TKN-7). A unit test computes these from `theme.css`, clipping colors outside sRGB, and fails below the threshold. axe (spec 0003 A11Y-5 tags) passes on every gallery scene in both themes and both densities.
- **AC-6**: In forced colors mode the focus ring, control borders, the selected sidebar item, and the current tab stay visible. With `prefers-contrast: more`, decorative borders become strong borders and muted text becomes the main text color. With `prefers-reduced-motion: reduce`, sheets, drawers, toasts, and popovers appear without sliding and skeletons don't shimmer (THM-6, MOT-1, spec 0001 UX-39).
- **AC-7**: `AppShell` renders a skip link (the first focusable element, "Skip to content", moving focus to `main`), a `header` top bar, a `nav` labeled "Breadcrumb", a `nav` primary sidebar labeled by the scope, and one `main`, per spec 0001 UX-1 and spec 0003 A11Y-3. The sidebar shows the nav items of the current scope (project services, org pages, or account pages) from the `ShellModel` (SHL-2), with the current one marked `aria-current="page"`.
- **AC-8**: From 64rem (1024 px at the default font size) the sidebar can collapse to a 56 px icon rail whose items keep their accessible names and show them in a tooltip (UX-40); the collapsed state is saved in `orvano:v1:{userId}:layout`. Below 64rem the sidebar and any resource list are labeled drawers that open from a button, trap focus, close with Escape, return focus to the button, and close by themselves when the window widens past 64rem (UX-3, SHL-6). At 320 px wide every shell region reflows with no horizontal scroll.
- **AC-9**: `Breadcrumbs` renders the `crumbs` it is given (SHL-1), the last one `aria-current="page"`. The org and project crumbs show the names from `ShellModel` and open `OrgSwitcher` and `ProjectSwitcher`. Each switcher loads at once with an empty query, searches 150 ms after typing stops, cancels the previous search through its `AbortSignal`, asks for at most 20 results, announces the result count, shows loading, empty, and error (with Retry) states, and ends with "All projects" and "New project" links (or the org equivalents) when the model provides them (SHL-12). The current org or project is marked in its switcher (`aria-current="true"` and a check icon), and the highlight starts on it. Below 64rem every crumb except the first and last collapses into a menu; below 25rem only that menu and the current page crumb show, and every crumb control is at least 24 by 24 px (SHL-4). `useDocumentTitle` builds `Resource · Service · Project · Orvano` from the same crumbs (UX-4).
- **AC-10**: `ResourceLayout` and `ResourceList` meet spec 0001 UX-2: the list stays mounted while a resource is open, the open item is `aria-current="page"`, and the list resizes by pointer and by keyboard on a focusable separator (SHL-13): arrow keys move it 16 px (mirrored in right to left), Home and End jump to 200 and 480 px, and the maximum shrinks so the content keeps at least 480 px. It collapses to a 40 px strip with an expand button. Width and collapsed state are saved per service in `orvano:v1:{userId}:{projectId}:svc:{service}` when a drag ends or a key changes them. Long names truncate visually and keep the full accessible name plus a tooltip.
- **AC-11**: The account menu holds the avatar, the user's name and email, a Theme section (System, Light, Dark), a Density section (Comfortable, Compact), Keyboard shortcuts, and Sign out (which calls the model's `onSignOut`). Changing density sets `data-density` on `<html>`, switches grid and list rows between 40 and 28 px (UX-32), leaves forms unchanged, and is saved in `orvano:v1:{userId}:layout`. The saved density is applied before the first render (SHL-11). Every target stays at least 24 by 24 px in both densities.
- **AC-12**: One `notify()` function feeds the toast queue, the message history, and the announcer (PRM-8). `Toast` meets UX-24: the inline end bottom corner, at most three visible, newest on top, never takes focus, success closes after 6 s with the timer paused while hovered or focused, errors stay until dismissed, identical toasts merge with a count, and the region never covers the focused element. `Banner` shows one at a time under the top bar. `ActivityIndicator` lists the last 50 messages of the session (UX-24 message history) and shows "No running jobs" until feature 46 adds the jobs list. The only live regions in the console are the `Announcer`, React Aria's toast region, `Banner`'s role, and `InlineError`'s `role="alert"` (A11Y-4 as amended). Anything else that must be heard, such as a switcher's search error and its retry result, or an `ErrorState` that replaces content, speaks through `announce()`, once per event.
- **AC-13**: `CommandPalette` opens with Mod+K from anywhere except while a modal is open, follows the APG dialog plus combobox pattern, with a menu of actions (React Aria's `Autocomplete` with a `Menu`, PRM-8), lists local actions only (go to each nav item of the current scope, switch theme, switch density, show shortcuts, open help, a deliberate subset of UX-25 until feature 25), shows recents for an empty query, and on Escape closes and returns focus to where it was. `useShortcut` (spec 0003 A11Y-7) registers every shortcut and sets `aria-keyshortcuts`; `?` (matched on the typed character, outside text inputs) opens `ShortcutHelp`, which lists the registry.
- **AC-14**: The help button sits in the same place in the top bar in every scope (UX-31, WCAG 3.2.6). It opens `DocsPane`, which from 64rem sits beside the content and narrows it (nothing is covered) and below 64rem is a drawer. Opening moves focus to the pane heading; Escape and the close button close it and return focus to the help button. Until feature 32 ships bundled docs, the pane shows a short message and a link to the docs fallback address (SHL-9) that opens in a new tab.
- **AC-15**: Every primitive listed in PRM-1 exists in `src/ui`, meets its APG keyboard model and focus return, has a component test with axe in Chromium, Firefox, and WebKit (spec 0003 QA-1, QA-4), and requires an accessible name in its props where it has no visible label (A11Y-3). A `Button`, `IconButton`, or `Switch` with `unavailableReason` stays focusable, ignores presses, and exposes the reason; a menu item and a switch show it as their description (PRM-4). `Link` is made with `createLink` over React Aria's `Link`, and a test proves the router still preloads on hover and focus.
- **AC-16**: `src/ui/form` provides `useAppForm` with the field and form components (spec 0003 FRM-1), and the UX-8 timing lives there once (FRM-2): a field is checked on blur after a change, then on every change once it shows an error. After a failed submit focus moves to `ErrorSummary`. `UnsavedChangesGuard` is the only `useBlocker` user and asks with the design system dialog (RTE-8).
- **AC-17**: `useDelayedPending`, `GridSkeleton`, and `ContentSkeleton` meet UX-21's timing: nothing for the first 300 ms, then the skeleton for at least 500 ms. `ConfirmDelete` applies the UX-23 tier from its resource kind map (a kind missing from the map fails the type check), loads "what goes with it" through its `loadDependents` prop, and shows the loading, "about", and "Couldn't count" states.
- **AC-18**: Every shell and primitive string is a Lingui message (spec 0003 I18N-1, I18N-8), and `pnpm check` fails on literal JSX text outside `src/gallery`. A browser test renders the project shell with `dir="rtl"` and React Aria's locale set to `ar` (RTL-1) and asserts that the sidebar sits at the inline start (the right edge), drawers open from the right, arrow keys on the resize separator are mirrored, direction icons (chevrons, back arrows) are flipped, `Kbd` keys keep their left to right order, and a long Latin name in the resource list keeps its start visible (RTL-3). The physical direction utilities in RTL-2 fail lint everywhere in `console/src`.
- **AC-19**: Only `src/ui` may import `react-aria-components`, `react-aria`, `react-stately`, `@react-aria/*`, `lucide-react`, and `react-data-grid` (spec 0003 DIR-3, widened here by ICN-1 and PRM-5). Outside `src/ui`, a `style` prop and an arbitrary value class (`w-[13px]`) fail lint. Each rule has a fixture in `tooling/lint-fixtures/` (STY-5).
- **AC-20**: The dev gallery is `console/gallery.html` with its code in `src/gallery/`. `pnpm dev` serves it at `/console/gallery.html`. It shows the shell in all three scopes, every primitive in every state, and toolbar switches for theme, density, locale (`en` and the pseudo locale), direction, width frames (including 320 px), and a fixture scenario (normal, slow, fail, empty), plus a reset control, all on placeholder data. With the pseudo locale on, shell and scene text is at least 30% longer than English. Each scene sets its own document title. Its strings stay out of the production catalog (GAL-2). `pnpm build` never ships it: `tooling/scripts/check-gallery-not-shipped.mjs` fails when the build output has `gallery.html` or a manifest chunk built from `src/gallery` (GAL-1 to GAL-3).
- **AC-21**: The boot error page (which also covers a failed config load, spec 0016 FRS-2), the root error component (spec 0003 FLT-2), and `AuthLayout` (for the pages without the shell: sign in, sign up, first run setup) use the same tokens and themes, render correctly before a session exists, and pass axe in both themes.
- **AC-22**: The screen reader script in this spec (SRT-1 to SRT-13) has been run with VoiceOver and Safari on macOS and with NVDA and Firefox on Windows. Each step's result, the versions used, and any fix are recorded in this spec's `verify.md` before the feature is `done` (spec 0001 Enforcement).
- **AC-23**: The built console, served with spec 0006 NET-2's exact `Content-Security-Policy` header, loads every route the build has (today the welcome page and `/setup` inside `AuthLayout`; feature 13 adds the shell routes to the same test) with zero CSP violations reported by the browser (STY-6).
- **AC-24**: `CopyField` and `SecretReveal` copy through `navigator.clipboard`; where it is unavailable or refused (a self hosted install over plain HTTP), the value is selected and an inline message says how to copy it by keyboard, and nothing claims "Copied" (PRM-9).

## Decision

**Chosen option**: Option 1: Orvano's own design system, built on React Aria Components and styled with Tailwind CSS v4 through semantic tokens, with the shell and the base primitives in feature 11 and the rest built by the feature that first needs them.

`src/ui` is the design system. It holds the tokens, the themes, the shell, and every primitive, and it is the only code that touches React Aria, Lucide, or the grid library. Feature code composes primitives and lays them out with Tailwind's layout utilities. It never chooses a color, font size, or radius, because those utilities don't exist outside the token set.

**Implementation skills**: `react-aria` (Adobe, `.claude/skills/react-aria/`) · `tailwind-4-docs` (`lombiq/tailwind-agent-skills`, `.claude/skills/tailwind-4-docs/`; run its docs sync once locally, and never commit its `references/docs/` snapshot, which `.gitignore` excludes) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `playwright-cli` (`microsoft/playwright-cli`, `.claude/skills/playwright-cli/`) · `lingui-best-practices` (`lingui/skills`, `.claude/skills/lingui-best-practices/`) · `router-plugin` (`.claude/skills/router-plugin/`) · `vite` (`antfu/skills`, `.claude/skills/vite/`)

Specs 0001, 0003, and 0008 win over any skill where they disagree (spec 0003 WEB-0). Known gap: the `react-aria` skill shows `className` and `style` props on wrappers, which PRM-3 forbids on Orvano's primitives.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### Scope and boundaries

In: the tokens and both themes, `design.md`, the Tailwind setup, the shell (`AppShell`, scope navigation, breadcrumbs and switchers, `ResourceLayout`, top bar controls), the primitives in PRM-1, the form wiring (spec 0003 FRM-1, FRM-2), the dev gallery, the lint rules in STY-5 and RTL-2, the CSP check, and the screen reader script.

Out, and who owns it:

| Item                                                                                                                                                                                                                                                                   | Owner                                                     |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| The `_shell` and `_auth` routes, RTE-9's session guard, building `ShellModel` and `crumbs` from the session, org, project, capabilities, and `staticData.crumb`, the switchers' search endpoints, sign out, tightening `ShellLinkOptions` to the registered route tree | Feature 13                                                |
| `DataGrid` (and `FilterBuilder`, `FilterChip`, `ColumnMenu`, `BulkActionBar`, `Pagination`), the non modal row `SideSheet` (UX-13), react-data-grid's CSS                                                                                                              | The rows feature (16), on react-data-grid (PRM-5)         |
| `LogStream`                                                                                                                                                                                                                                                            | Feature 46                                                |
| `JsonEditor`, `RelationPicker`                                                                                                                                                                                                                                         | Features 24 and 16, whichever comes first                 |
| Server search and create actions in the command palette, the active jobs list                                                                                                                                                                                          | Features 25 and 46                                        |
| Bundled docs for `DocsPane`                                                                                                                                                                                                                                            | Feature 32                                                |
| Chart colors and chart primitives                                                                                                                                                                                                                                      | Feature 45                                                |
| Syntax highlighting                                                                                                                                                                                                                                                    | Revisit with `JsonEditor` and the SQL editor (feature 27) |

Every later primitive follows `design.md`, PRM-2 to PRM-4, and the same tests, without a new spec, unless it needs a new token or a new dependency.

### Rules

Each rule has an ID so reviews and `/check verify` can cite it. Values (colors, sizes, durations) live in `theme.css` and show in `design.md`, not here.

**Styling (STY)**

- **STY-1 (MUST)**: Tailwind CSS 4 with `@tailwindcss/vite`. The one stylesheet is `src/ui/theme.css`, imported once by `main.tsx` and once by the gallery entry. In order, it holds:
  1. `@import "tailwindcss" source(none);` then `@source "../";` (scan only `console/src`, so `design.md`, specs, and other text never generate classes);
  2. an `@theme` block that first resets the default scales (`--color-*: initial`, `--font-*: initial`, `--text-*: initial`, `--radius-*: initial`, `--shadow-*: initial`, which also clears `--font-weight-*`) and then declares Orvano's tokens, line heights as `--text-<name>--line-height`; Tailwind's spacing multiplier stays at its default 4 px;
  3. the dark, `prefers-contrast`, and `forced-colors` blocks (THM-3, THM-6) and the `data-density` block;
  4. an `@layer base` block that sets the default border color to `--color-border`, input placeholders to `--color-fg-muted`, `body` to `bg` and `fg` at `text-base` in `--font-sans`, `color-scheme`, and `scroll-padding` (SHL-8);
  5. the plain CSS variables of TKN-1 (layers, durations, row height).
     Step 1 of the build plan confirms `@tailwindcss/vite` supports the installed Vite 8 before anything else; if it doesn't, the fallback is Tailwind's PostCSS plugin through Vite's built in PostCSS, with the same `theme.css`.
- **STY-2 (MUST)**: No runtime CSS in JS, no `<style>` element, no `style` attribute in HTML, and no CSS file other than `theme.css`. `vite.config.ts` sets `build.cssCodeSplit: false`. A primitive that needs a computed size (the resource list width, a virtualized row offset) sets it through React's `style` prop, which writes through the CSS object model and is allowed by `style-src 'self'`. Only `src/ui` may do this (STY-5).
- **STY-3 (MUST)**: Primitives style their states through React Aria's data attributes with Tailwind's data variants (`data-hovered:`, `data-pressed:`, `data-focus-visible:`, `data-selected:`, `data-disabled:`, `data-invalid:`) and `aria-disabled:` for PRM-4, not through JavaScript state. No Tailwind plugin is added for this. Every class name is written whole, as a literal in the source (Tailwind finds classes by scanning text, so a class built by joining pieces never reaches the CSS). Class lists are joined with a small local `cx` helper in `src/ui/cx.ts`; no `clsx`, `tailwind-merge`, or `tailwind-variants`, since primitives don't accept outside classes (PRM-3).
- **STY-4 (MUST)**: The emitted CSS is at most 30 KB gzip. `pnpm size` gains a size-limit entry for the built CSS file, beside the BDL-1 JavaScript entries.
- **STY-5 (MUST)**: Restriction rules in `tooling/config/console-restrictions.ts` (spec 0008 LNT-3), each with a fixture: outside `src/ui`, a JSX `style` attribute, and a class string holding an arbitrary value or property (the pattern `-\[` or `(^|\s)\[[a-z-]+:`), fail. Everywhere in `console/src`, a class string matching RTL-2's pattern fails. Nothing imports `src/gallery`.
- **STY-6 (MUST)**: An end to end test (`e2e/csp.spec.ts`) serves the built console with `vite preview` configured to send spec 0006 NET-2's exact `Content-Security-Policy` header, records `securitypolicyviolation` events, loads every route that exists, and fails on any violation. A tooling test fails when the policy in `vite.config.ts` differs from the one in `server/src/Orvano.Host/Web/SecurityHeaders.cs`. It runs in the PR Playwright job (spec 0003 QA-5 runs the full server later; this check needs only the static build).
- **STY-7 (MUST)**: The supported browsers are the ones Tailwind v4 needs: Safari 16.4, Chrome 111, Firefox 128, and later. Nothing needs a fallback for older engines.
- **STY-8 (MUST)**: The first load budget with the shell is 230 KB gzip (spec 0003 BDL-1, raised from 200 KB on 2026-10-27; the 80 KB per file limit is unchanged). The account menu, the org and project switchers, and the collapsed breadcrumb menu stay in the first load, so a keyboard press on their trigger opens a ready panel (AC-9, AC-11); PRM-10's lazy set is unchanged. Two rules keep the shell inside the budget:
  - `console/package.json` declares `sideEffects` as a list: the CSS (`**/*.css`), `./src/main.tsx`, `./src/gallery/main.tsx`, `./src/shared/zod.ts`, and any other module whose import must run code, each named. A named import from the `@/ui` index then pulls only the modules it uses. A module added later that must run on import is added to the list in the same PR. Vitest doesn't read the field, so `src/test/setup.ts` needs no entry; `src/routeTree.gen.ts` needs none because its exports are used. To check the list, step 25 builds once with `"sideEffects": true` and once with the list, and compares the two manifests: a module that is in the first build's chunks, missing from the second's, and runs code that matters on import (a `z.config`, an i18n activation, a registration) belongs on the list. The PR names the comparison's result.
  - Console code builds every Zod schema with the `zod/mini` API (Zod 4, spec 0003 WEB-1). `@/shared/zod` applies `jitless` (SEC-2) and re-exports `zod/mini` as `z`, and it is the only module that imports from `zod`. A restriction rule in `tooling/config/console-restrictions.ts` fails any other import of `zod` or its subpaths, type only imports included (use `z.ZodMiniType`), in every area, tests and the gallery included, so primitives are tested with the same API features use. Its allowed home is a region for `src/shared/zod.ts`, never an inline disable, and its fixture is `tooling/lint-fixtures/` `sty-8` (named the way `check-lint-fixtures` expects). The conversion: `x.catch(v)` becomes `z.catch(x, v)`, `x.optional()` becomes `z.optional(x)`, `z.number().int().min(200).max(480)` becomes `z.int().check(z.minimum(200), z.maximum(480))`, `z.array(x).max(n)` becomes `z.array(x).check(z.maxLength(n))`, `z.string().trim().min(1)` becomes `z.string().check(z.trim(), z.minLength(1))`, and `.regex(r)` and `.refine(f)` become `z.regex(r)` and `z.refine(f)` inside `.check(...)`; every function named here exists in `zod/mini` 4.6.
- **STY-9 (MUST)**: Until feature 13 adds the `_shell` routes, `pnpm size` measures the shell through a probe build. The `size` script becomes `vite build --mode size-probe --outDir dist-size-probe && node scripts/size-entries.mjs && size-limit`, run after `pnpm build` as today. `vite.config.ts` becomes `defineConfig(({ mode }) => ...)`, and only when `mode` is `size-probe`, `resolve.alias` is an array whose first entry maps `@/features/welcome/components/welcome-page` to `src/size-probe/shell-probe.tsx`, ahead of the `@` entry. The probe build must leave `src/routeTree.gen.ts` unchanged (the route files are the same, so the generated tree is too; step 25 confirms with `git diff --exit-code` after it). That file renders `AppShell` in project scope from an inline `ShellModel` (one user, org, and project, no nav items, search functions that resolve to an empty list), crumbs for the org, the project, and an "Overview" page, and a `PageHeader` as the content. It imports only `@/ui`, never `src/gallery` (GAL-3), and holds no JSX text; its labels are plain string props, and `src/size-probe/**` gets the gallery's exemptions: Lingui's catalog `exclude` and the ESLint I18N-8 override, so its placeholder text never reaches the catalog. `scripts/size-entries.mjs` also reads `dist-size-probe/.vite/manifest.json` and fails with a message naming the missing file if it isn't there. From it, it writes one more entry, `Shell first load (AC-3, probe)`: the entry chunk and the chunks of the keys matching `^src\/routes\/__root\.tsx` and `^src\/routes\/index\.tsx` (prefix matches, so the `?tsr-split=component` keys count), plus their static imports, with paths under `dist-size-probe/` and a limit of 230 KB. Every probe JavaScript file also gets the 80 KB per file entry. `tooling/scripts/size-entries.test.mjs` is updated for the new limit and entry count and gains a probe manifest fixture, including the missing manifest failure. `console/dist-size-probe/` goes in the root `.gitignore` beside `console/dist/`, and `check-gallery-not-shipped.mjs` keeps reading `dist/` only, which never holds the probe. The console CI job already runs `pnpm size`, so the probe needs no new job. Feature 13 deletes the probe, its mode, and its entry once the BDL-1 entry counts the `_shell` route files.

**Tokens (TKN)**

- **TKN-1 (MUST)**: Three layers. Raw values exist only in `theme.css`. Semantic tokens in the `@theme` block (`--color-fg`, `--color-bg-subtle`, `--text-base`, `--radius-md`, `--spacing-sidebar`, `--ease-standard`, and so on) are the only ones that produce utilities. Values that are not Tailwind theme namespaces are plain CSS variables on `:root`, used only inside `src/ui` through arbitrary properties (for example `z-(--z-modal)`, `duration-(--duration-slow)`): the layers `--z-*`, the durations `--duration-*`, and `--row-height`.
- **TKN-2 (MUST)**: Colors are OKLCH. The semantic color tokens, their light and dark values, and what each is for are the table in `design.md` _Color_: surfaces (`bg`, `bg-subtle`, `bg-muted`, `bg-raised`, `scrim`, `tooltip`), borders (`border`, `border-strong`), text (`fg`, `fg-muted`, `tooltip-fg`), accent (`accent`, `accent-hover`, `accent-fg`, `accent-text`, `accent-subtle`, `accent-subtle-fg`), `focus`, and the status families (`danger`, `success`, `warning`, `info`, each with `-text` and `-subtle`, and `danger` also with `-hover` and `-fg`).
- **TKN-3 (MUST)**: Type uses the system stacks (`--font-sans`, `--font-mono`), the weights 400, 500, and 600, and the six step scale in `design.md` _Typography_ (12, 13, 14, 16, 20, and 24 px, in rem, each with its line height). No web font is loaded.
- **TKN-4 (MUST)**: Radius tokens are `sm` 4 px, `md` 6 px, `lg` 8 px, and `full`. Shadow tokens are `sm` (popovers, menus, tooltips, toasts) and `lg` (dialogs, sheets, drawers, the palette). Layers and durations follow TKN-1 (`design.md` _Elevation_ and _Motion_).
- **TKN-5 (MUST)**: Layout sizes are `--spacing-*` tokens (so `h-topbar` and `w-sidebar` exist): top bar 48 px, sidebar 240 px, icon rail 56 px, resource list 280 px by default (200 to 480), collapsed list strip 40 px, docs pane 400 px, dialog 400 or 560 px, sheet 480 px, palette 640 px, toast 360 px, auth card 400 px, control height 32 px (28 px small, 40 px large), content width for forms and settings at most 768 px. `--row-height` is 40 or 28 px by density.
- **TKN-6 (MUST)**: `theme.css` is the only source of token values. `tooling/scripts/design-tokens.mjs` reads it and writes the token tables into `design.md` between `<!-- tokens:<name>:start -->` and `<!-- tokens:<name>:end -->` markers (one block per table: colors, fonts, type, layout, density, shape, layers, motion), then formats the file with Oxfmt. `pnpm fix` runs it in write mode; `pnpm check` runs it in check mode (generate in memory, format, compare) and fails when a block is out of date. The "Use" column comes from a `/* use: … */` comment on each declaration in `theme.css`.
- **TKN-7 (MUST)**: `src/ui/theme.contrast.test.ts` imports `theme.css?raw`, reads both themes, converts each OKLCH value to sRGB (clipping each channel to 0 to 1 for colors outside sRGB), and checks: every text token (`fg`, `fg-muted`, `accent-text`, `danger-text`, `success-text`, `warning-text`, `info-text`) on every surface token (`bg`, `bg-subtle`, `bg-muted`, `bg-raised`, `accent-subtle`, and the four `-subtle` status surfaces) at 4.5:1; the paired foregrounds (`accent-fg` on `accent` and `accent-hover`, `danger-fg` on `danger` and `danger-hover`, `accent-subtle-fg` on `accent-subtle`, `tooltip-fg` on `tooltip`) at 4.5:1; and `border-strong`, `focus`, and `accent` on every surface token at 3:1. A status text on a different status's surface is not checked (it never appears). Disabled controls are exempt (WCAG 1.4.3 excludes inactive components). The values in `design.md` passed every check while writing this spec.

**Themes (THM)**

- **THM-1 (MUST)**: The stored preference is `system`, `light`, or `dark` under `orvano:v1:theme`, declared in `shared/storage/items.ts` (STO-2); a missing or invalid value means `system`.
- **THM-2 (MUST)**: `public/theme-boot.js` (spec 0003 STO-6) is a classic script loaded from `index.html` in `<head>`, before the stylesheet. Inside try and catch it reads the preference, resolves `system` through `matchMedia('(prefers-color-scheme: dark)')`, and sets `data-theme="light|dark"` and the `color-scheme` style property on `<html>`. It does nothing else.
- **THM-3 (MUST)**: Light tokens sit on `:root`; dark tokens on `[data-theme="dark"]`; and, so the console still themes with no script, the dark tokens also apply under `@media (prefers-color-scheme: dark)` to `:root:not([data-theme])`. Tailwind's `dark:` variant is redefined as `@custom-variant dark (&:where([data-theme="dark"], [data-theme="dark"] *))`, and is used only in `src/ui`.
- **THM-4 (MUST)**: `shared/stores/theme.ts` holds the preference and the resolved theme. With `system` it listens to the media query and updates `data-theme` live. Changing the preference writes storage (STO-2) and updates `<html>` at once; another tab follows through the storage event (STO-5).
- **THM-5 (MUST)**: Theme switches never animate colors, so a switch costs one repaint and never flickers.
- **THM-6 (MUST)**: `@media (prefers-contrast: more)` maps `--color-border` to `--color-border-strong` and `--color-fg-muted` to `--color-fg`. In `@media (forced-colors: active)`, the focus ring uses `outline` (kept by the browser), the items AC-6 names (the current nav item, the current tab, a selected row) get a 2 px inline start mark in `Highlight` drawn so nothing moves (an inset `box-shadow` or an `outline`, never a border), and icons use `currentColor`. Checkboxes, radios, and switches keep their own forced colors rendering. No separate high contrast theme ships.

**Motion (MOT)**

- **MOT-1 (MUST)**: Durations are 120 ms (hover, press), 200 ms (popover, menu, toast), and 280 ms (sheet, drawer), with one easing curve (`design.md` _Motion_). Slides use `translate-x` with an `rtl:` counterpart (`translate-x-full rtl:-translate-x-full`), because translation is physical. Under `prefers-reduced-motion: reduce`, slides and scales become instant and the skeleton shimmer stops; fades of 120 ms or less may stay.

**Shell (SHL)**

- **SHL-1 (MUST)**: The shell takes its data through props only. `src/ui/shell` defines these types and never imports from `features/` or `shared/api` (spec 0003 DIR-2). Feature 13 builds them in the `_shell` route from its queries and `staticData.crumb` (RTE-7); the gallery builds them from fixtures.

  ```ts
  type ShellScope = "account" | "org" | "project";
  // Loose until feature 13 registers the shell routes, then narrowed to the router's typed link options.
  interface ShellLinkOptions {
    to: string;
    params?: Record<string, string>;
    search?: Record<string, unknown>;
    exact?: boolean;
  }
  interface ShellModel {
    user: { id: string; name: string; email: string };
    scope: ShellScope;
    org?: { id: string; name: string }; // set for org and project scope
    project?: { id: string; name: string }; // set for project scope
    nav: readonly ShellNavItem[]; // display order; project scope: only services the project allows (RTE-9)
    searchOrgs: SwitcherSearch;
    searchProjects: SwitcherSearch;
    orgLinks?: { all: ShellLinkOptions; create?: ShellLinkOptions };
    projectLinks?: { all: ShellLinkOptions; create?: ShellLinkOptions };
    onSignOut: () => void;
  }
  type SwitcherSearch = (
    query: string,
    options: { limit: number; signal: AbortSignal },
  ) => Promise<readonly SwitcherItem[]>;
  interface ShellNavItem {
    id: NavId;
    link: ShellLinkOptions;
  }
  interface SwitcherItem {
    id: string;
    name: string;
    link: ShellLinkOptions;
  }
  type Crumb =
    | { kind: "org" | "project" } // label from ShellModel, renders the switcher
    | { kind: "page"; label: string; link?: ShellLinkOptions }; // translated label; no link on the last crumb
  interface AppShellProps {
    model: ShellModel;
    crumbs: readonly Crumb[];
    resourceList?: ReactNode;
    children: ReactNode;
  }
  ```

  The `overview` nav item sets `exact: true`; every other item is active when the path starts with its link.

- **SHL-2 (MUST)**: Nav labels and icons belong to the design system, keyed by `NavId`, in `src/ui/shell/nav-registry.ts`: project scope `overview`, `auth`, `databases`, `storage`, `realtime`, `functions`, `sites`, `messaging`, then `usage`, `logs`, `settings` (UX-1 order); org scope `projects`, `members`, `org-settings`; account scope `profile`, `security`, `preferences`. Labels are lazy `msg` descriptors (I18N-1). A `NavId` the registry doesn't know fails the type check.
- **SHL-3 (MUST)**: Landmarks: the skip link, `header` (top bar), `nav` labeled "Breadcrumb" (around React Aria's `Breadcrumbs` list), `nav` (primary sidebar, labeled "Project", "Organization", or "Account"), an optional `nav` for the resource list (labeled by the service), and `main` with `id="main"` and `tabIndex={-1}`. The page's `h1` is rendered by `PageHeader` (RTE-7 moves focus to it).
- **SHL-4 (MUST)**: Top bar order, inline start to end: the sidebar button (below 64rem only), the brand mark (a link to the console home), `Breadcrumbs`, then at the inline end `ActivityIndicator`, the help button, the command palette button (showing its Mod+K shortcut), and the account menu button (the avatar). The order is the same in every scope and at every width. Below 25rem (400 px) the breadcrumbs show only the collapsed menu (which holds the org and project items that open their switchers) and the current page crumb; the current page truncates with its full name as accessible name and tooltip. Every crumb control is at least 24 by 24 px at every width (WCAG 2.5.8).
- **SHL-5 (MUST)**: Primary sidebar: 240 px, `bg-subtle`, a border at the inline end. Items are 32 px high with a 16 px icon and the label; the current item uses `accent-subtle` with `accent-subtle-fg` text and `aria-current="page"`. The collapse button sits at the bottom. Collapsed, it is the 56 px icon rail with tooltips.
- **SHL-6 (MUST)**: `useIsWide()` in `src/ui/shell` is the one breakpoint check, on `matchMedia('(min-width: 64rem)')` (the same as Tailwind's `lg`, so CSS and JavaScript agree at any default font size). Below it, the primary sidebar and the resource list render inside `Drawer` (a modal React Aria `Modal` sliding from the inline start, with an `aria-label` naming its content), opened by the top bar sidebar button and by a "Show list" button in `ResourceLayout`. Navigating from a drawer closes it, and an open drawer closes when `useIsWide()` turns true.
- **SHL-7 (MUST)**: The brand mark is `src/ui/shell/brand-mark.tsx`: an inline SVG geometric mark in `accent` plus the word "Orvano" at semibold, with the mark alone in the icon rail. "Orvano" is a brand name on the I18N-8 allow list. The mark is `aria-hidden`, and the link's accessible name is "Orvano home".
- **SHL-8 (MUST)**: The content area has 24 px padding at the inline sides from 64rem and 16 px below, and uses `scroll-padding-top` equal to any sticky header and `scroll-padding-bottom` equal to the paging bar and visible toasts, so the focused element is never hidden (UX-35, WCAG 2.4.11).
- **SHL-9 (MUST)**: `DocsPane` is a complementary region (`aside` labeled "Help") at the inline end. From 64rem it takes 400 px from the content area (the content narrows; nothing is covered); below 64rem it is a full width `Drawer` from the inline end. On open, focus moves to the pane's heading; Escape (handled by the pane, since a non modal region gets no Escape from React Aria) and the close button close it and return focus to the help button. Until feature 32 it renders a message and a link to `DOCS_FALLBACK_URL`, a constant in `src/shared/config.ts` set to `https://github.com/orvanohq/orvano#readme` (the same for every install, so WEB-8 holds), with `target="_blank"` and `rel="noopener noreferrer"`.
- **SHL-10 (MUST)**: `CommandPalette` actions come from a local registry in `src/ui/shell/palette-actions.ts`, built from `ShellModel.nav`, the theme and density setters, `ShortcutHelp`, and `DocsPane`. This is a deliberate subset of UX-25: the create and copy actions and the server results arrive with feature 25 (Follow-up records it in spec 0001). Recents (UX-25, `orvano:v1:{userId}:palette`, up to 10) are saved from the first release. Mod+K does nothing while a modal (dialog, sheet, drawer) is open.
- **SHL-11 (MUST)**: `shared/stores/layout.ts` is the one store for `orvano:v1:{userId}:layout` (sidebar collapsed, density, and UX-37's last SDK), with one Zod schema and field by field updates, so its three writers never overwrite each other. Feature 13's boot step (spec 0003 RTE-10, after the session loads and before the first render) reads it and sets `data-density` on `<html>`; until then the gallery sets it.
- **SHL-12 (MUST)**: Switchers are React Aria `Autocomplete` with a `ListBox` in a `Popover`. The empty query runs at once on open; typed queries run 150 ms after the last keystroke; each call passes `limit: 20` and a fresh `AbortSignal`, aborting the previous one. The current org or project carries `aria-current="true"` and a check icon, and the highlight starts on it when the list opens. After results arrive, the announcer says the count ("12 projects"). An error shows a message and a Retry button below the list (not a list item), and `announce()` says the error once per failure and the result of a retry (AC-12; no `role="alert"`). The "All projects" and "New project" links (from `orgLinks` or `projectLinks`) sit below the list when given.
- **SHL-13 (MUST)**: The resize separator is a custom `role="separator"` with `aria-orientation="vertical"`, `aria-valuenow`, `aria-valuemin`, `aria-valuemax`, and an accessible name ("Resize table list"). Pointer drags use React Aria's `useMove`; keys are handled by the separator: ArrowLeft and ArrowRight change the width by 16 px (the arrow pointing toward the content grows the list, so the keys swap in right to left), Home and End jump to the limits. The maximum is the lower of 480 px and the width that leaves 480 px of content (UX-3). Width and collapsed state are saved on pointer up and on each key change, not during a drag. Collapsed, the list is a 40 px strip holding only an expand button.
- **SHL-14 (MUST)**: `RouteTabs` never wraps and has no More menu. When the tabs don't fit, the row scrolls on the inline axis, the current tab is scrolled into view on load and on navigation, and a fade marks each edge where tabs are cut off. The tabs stay plain links (UX-5).

**Primitives (PRM)**

- **PRM-1 (MUST)**: Feature 11 builds these in `src/ui`:
  - Foundations: `Link` (`createLink`), `Button`, `IconButton`, `Icon`, `Avatar`, `VisuallyHidden`, `Tooltip`, `Kbd`.
  - Shell: `AppShell`, `SkipLink`, `PrimarySidebar`, `Drawer`, `Breadcrumbs`, `OrgSwitcher`, `ProjectSwitcher`, `AccountMenu`, `ResourceLayout`, `ResourceList`, `PageHeader`, `RouteTabs`, `Tabs`, `ActivityIndicator`, `HelpButton`, `DocsPane`, `CommandPalette`, `ShortcutHelp`, `useShortcut`, `useIsWide`, `useDocumentTitle`, `AuthLayout`, `BrandMark`.
  - Forms: `src/ui/form` (`useAppForm`, `Form`, `Field`, `TextField`, `PasswordField`, `NumberField`, `TextArea`, `Select`, `Combobox`, `Checkbox`, `CheckboxGroup`, `RadioGroup`, `Switch`, `SubmitButton`, `ErrorSummary`), `UnsavedChangesGuard`.
  - Overlays: `Dialog`, `SideSheet` (modal), `Popover`, `Menu`, `ConfirmDelete`.
  - Feedback and states: `notify`, `Announcer`, `Toast` with its region, `Banner`, `InlineError`, `EmptyState`, `ErrorState`, `Skeleton`, `GridSkeleton`, `ContentSkeleton`, `useDelayedPending`, `Progress`, `StatusChip`, `Spinner` (inside buttons and small controls only, UX-21).
  - Data display: `Table` (a static React Aria `Table`, not the grid), `SettingsCard`, `DangerZone`, `CopyField`, `SecretReveal`, `CodeSnippet` (plain monospace, no highlighting), `RelativeTime`, `Badge`.
- **PRM-2 (MUST)**: Every primitive is a React Aria Components component (or composition) when React Aria has one, styled per STY-3. A primitive with no React Aria counterpart (`EmptyState`, `SettingsCard`, `Skeleton`) uses semantic HTML. Built in strings of a primitive (Close, Copy, Copied, Show more, Loading) are Lingui messages in `src/ui`; React Aria's own strings come from its locale packs (I18N-6).
- **PRM-3 (MUST)**: Primitives accept no `className` and no `style` prop. Look is chosen by typed props: `variant` (`Button`: `primary`, `secondary`, `ghost`, `danger`), `size` (`sm`, `md`, `lg`), `tone` (`Banner`, `Badge`: `neutral`, `info`, `success`, `warning`, `danger`), and `status` (`StatusChip`: `queued`, `running`, `ready`, `failed`, `cancelled`, which maps to a tone and an icon in `design.md`). A primitive whose only content is an icon requires `aria-label` in its type (A11Y-3).
- **PRM-4 (MUST)**: An action the caller may not take (UX-30) uses `unavailableReason?: MessageDescriptor` on `Button`, `IconButton`, `Menu` items, and `Switch` (a setting you can see but not change is an action you can't take). React Aria's `isDisabled` removes focus, so this is Orvano's own wrapper: it sets `aria-disabled="true"`, keeps the element focusable, ignores presses and Enter, styles through `aria-disabled:`, and links the reason with `aria-describedby`. On buttons the reason also shows in a tooltip; on menu items and switches it shows as the description line. `isDisabled` is kept for controls that truly can't act (a submit while pending).
- **PRM-5 (MUST)**: The grid library is **react-data-grid** (adazzle, MIT). Feature 11 only records it and adds it to STY-5's import list. The rows feature checks it against UX-11 to UX-19 before wrapping it in `src/ui/data-grid` (BDL-2 path import), and imports or restyles its CSS inside `theme.css`. If it fails a must rule there, that feature returns to `/architect` with the runner up, TanStack Table plus TanStack Virtual.
- **PRM-6 (MUST)**: `ConfirmDelete`'s tier map is `src/ui/confirm-delete/tiers.ts`, typed `Record<ResourceKind, 1 | 2>`. `ResourceKind` is the union of spec 0001 UX-23's kinds: tier 1 `organization`, `project`, `database`, `bucket`; tier 2 `table`, `column`, `index`, `function`, `deployment`, `site`, `domain`, `api_key`, `webhook`, `messaging_provider`, `messaging_topic`, `end_user`, `console_member`, `backup`, `file`; plus the `row` and `rows` forms with their own dialogs. A kind missing from the map fails the type check. Dependents come from a `loadDependents(signal)` prop returning spec 0014 CPX-2's `items`.
- **PRM-7 (MUST)**: `SecretReveal` holds the secret only in component state, never in a store, storage, or the URL (spec 0003 STO-6, URL-5). After "I've copied it" it shows only the `masked` prop (prefix and last 4, UX-33) and drops the secret.
- **PRM-8 (MUST)**: `notify({ tone, message, source })` in `src/ui/toast` is the only way to raise a message. It adds the message to the history store (the last 50), merges it with an identical visible toast (bumping a count), pushes it to React Aria's `ToastQueue` (`maxVisibleToasts: 3`), and nothing else announces it (React Aria's toast region does). Success toasts get a 6 s timeout, which React Aria pauses on hover and focus; errors get none. If the installed React Aria still exports the toast parts as `UNSTABLE_`, `src/ui/toast` wraps them and says so in a code comment. `CommandPalette` uses React Aria's `Autocomplete` with a `Menu` inside a `Dialog`, under the same rule.
- **PRM-9 (MUST)**: Copying uses `navigator.clipboard.writeText`. If it is missing (not a secure context) or rejects, the primitive selects the value in its field, shows an inline message "Press Ctrl+C to copy" (⌘C on Apple platforms), and announces nothing as copied.
- **PRM-10 (MUST)**: These primitives are imported by path, never through the `@/ui` index, and loaded lazily where they first appear (spec 0003 BDL-2): `@/ui/command-palette`, `@/ui/shortcut-help`, `@/ui/docs-pane`, `@/ui/side-sheet`, `@/ui/table`, `@/ui/confirm-delete`. Their triggers (the palette button, `?`, the help button) are in the first load; the panels are not.
- **PRM-11 (MUST)**: Small behaviors fixed here: `RelativeTime` shows relative text through `Intl.RelativeTimeFormat` (`numeric: "auto"`) up to 7 days, then `Intl.DateTimeFormat` `dateStyle: "medium"`; its tooltip shows local `dateStyle: "medium", timeStyle: "long"` and the same in `timeZone: "UTC"`, refreshed every 60 s (UX-34, I18N-5). `Kbd` and `useShortcut` treat Mod as Meta on Apple platforms (detected from `navigator.userAgentData?.platform`, falling back to `navigator.platform`) and Control elsewhere, and write `aria-keyshortcuts` as `Meta+K` or `Control+K`; `?` is matched on `event.key`. `GridSkeleton` draws 8 rows and the column count it is given (default 4). `Avatar` shows up to two initials from the user's name (first letters of the first and last words, uppercased for the active locale), else the first letter of the email, on `accent-subtle`; it is `aria-hidden` inside the account button, whose name is "Account".

**Icons (ICN)**

- **ICN-1 (MUST)**: Icons come from `lucide-react`, imported only in `src/ui`. `src/ui/icons.ts` re-exports the curated set under Orvano names (`ServiceDatabasesIcon`, `ChevronStartIcon`), and feature code imports from there. Adding an icon is a one line change in that file.
- **ICN-2 (MUST)**: `Icon` renders at 16 px (20 px in empty states and banners, 24 px max), uses `currentColor`, a 1.75 stroke, and is `aria-hidden` unless given `label`. Icons that point a direction (chevrons, arrows, "back") set `flipInRtl`, which mirrors them under `dir="rtl"`.

**Right to left (RTL)**

- **RTL-1 (MUST)**: In the gallery, right to left is proven without a new catalog. The console's own screens use the same proof, with no `ar` catalog (spec 0017 LOC-5). The gallery's direction switch sets `dir="rtl"` on `<html>` and gives React Aria's `I18nProvider` the real locale `ar`, so its menus, popovers, and keyboard handling flip, while Lingui keeps the active catalog. The RTL browser test (AC-18) does the same. The pseudo locale `ar-XB` is not used anywhere: CI's WebKit resolves it as left to right, so a test or a review in Safari would see a half mirrored console. When a real right to left language ships, I18N-6 sets both from the account locale.
- **RTL-2 (MUST)**: Layout uses logical utilities only (`ms-`, `me-`, `ps-`, `pe-`, `start-`, `end-`, `text-start`, `text-end`, `rounded-s`, `rounded-e`, `rounded-ss` and the other corner forms, `border-s`, `border-e`, `scroll-ms`). STY-5's direction rule rejects a class token matching `^-?(m|p|scroll-m|scroll-p)[lr]-|^-?(left|right)-|^(text|float|clear)-(left|right)$|^rounded-([lr]|tl|tr|bl|br)(-|$)|^border-[lr](-|$)|^origin-(left|right|top-left|top-right|bottom-left|bottom-right)$|^bg-linear-to-[lr]$|^bg-gradient-to-[lr]$`, after stripping variant prefixes. `translate-x` is allowed only with an `rtl:` counterpart in the same class string (MOT-1), checked in review.
- **RTL-3 (MUST)**: Text the user supplied (names, IDs, keys, endpoints) renders in its own direction isolate (`<bdi>`, or `dir="auto"` on the element that truncates), in the resource list, the breadcrumbs, the switchers, and table cells. Truncation then cuts the end of the text in its own direction, so a Latin name in a right to left page keeps its start. The full text stays the accessible name and the tooltip (UX-34). Keyboard shortcuts are not language: `Kbd` sets `dir="ltr"` with `unicode-bidi: isolate`, so its keys keep their order.

**Gallery (GAL)**

- **GAL-1 (MUST)**: `console/gallery.html` and `src/gallery/main.tsx` are a second development entry. The gallery uses a code based TanStack router with hash history and declares the app's shell paths (`/$orgId/$projectId/databases` and so on), so active links match as in the app while `routeTree.gen.ts` stays unchanged. It uses the real providers and `theme.css`.
- **GAL-2 (MUST)**: Scenes: the shell in project, org, and account scope (with and without a resource list, sidebar collapsed, narrow width), every primitive in every variant and state, `AuthLayout`, and the boot error page. Its toolbar sets theme, density, locale (`en`, pseudo), direction, a width frame (320, 360, and 800 px, changed without remounting the scene), and a fixture scenario (normal, slow, fail, empty) so the switchers, the resource list, and forms show their loading, error, and empty states; a reset control clears the gallery's `orvano:v1:*` storage and reloads. A States scene shows the field family invalid, read only, and disabled. Placeholder data lives in `src/gallery/fixtures.ts`, in the shapes of SHL-1. The pseudo locale grows every message by about 35% with its placeholders intact, so UX-34's 30% growth shows (spec 0003 I18N-7), and a gallery only helper applies the same transform to scene strings, which never enter the catalog (GAL-3). Every scene sets its own document title (`Primitives · Gallery`) and scrolls to the top when it opens.
- **GAL-3 (MUST)**: `vite build` takes only `index.html` as input. `tooling/scripts/check-gallery-not-shipped.mjs` runs after `pnpm build` in CI and fails if the output has `gallery.html`, a chunk in `dist/.vite/manifest.json` whose source is under `src/gallery`, or a JavaScript file in `dist/assets/` holding a string only the gallery fixtures contain. `lingui.config.ts` excludes `src/gallery` from extraction, and the literal string lint (I18N-8) doesn't apply there. `src/gallery` may import `src/ui` and `src/shared`; nothing imports `src/gallery` (STY-5). Spec 0003 DIR-1 gains these lines (Follow-up).

**Screen reader test script (SRT)**

Run in both pairs (VoiceOver with Safari on macOS, NVDA with Firefox on Windows) against the gallery, at 1280 px and at 800 px wide. Record pass or fail and the fix per step in `verify.md`.

- **SRT-1**: Load the project shell. The first Tab reaches "Skip to content"; activating it puts focus on `main`, which is announced. On a service with a resource list, the skip link also skips the list.
- **SRT-2**: The landmarks list (VO rotor, NVDA D) shows banner, the "Breadcrumb" navigation, the "Project" navigation, and main, each once.
- **SRT-3**: Move through the sidebar. Each item reads its name and "current page" on the current one. Collapse to the icon rail; names are still read.
- **SRT-4**: Breadcrumbs read as a list with the last crumb as current page. Open the project switcher: hear the current project marked as current. Type, and hear the result count and each result. With the gallery's Fail scenario, hear the search error once, then the result of Retry.
- **SRT-5**: Open the account menu. Theme and Density read as groups with the checked option; changing theme is visible at once.
- **SRT-6**: Open a `Dialog`: focus moves into it, its title is read (and, for an alert dialog such as `ConfirmDelete`, its description), Tab stays inside, Escape closes, focus returns to the opener.
- **SRT-7**: Open a modal `SideSheet` with a form. Submit it empty: focus moves to the error summary, which reads the count and links to each field.
- **SRT-8**: Raise a success, an info, and an error toast while focus is in a field. Each is announced once, politely for success and info, focus doesn't move, and the success toast closes after 6 s (longer while focused).
- **SRT-9**: Press the copy button of a `CopyField`; "Copied" is announced once. Tab to a `CodeSnippet`: it reads as a named region with its copy hint.
- **SRT-10**: Press Mod+K. The palette opens as a dialog with a combobox; arrow keys read each action; Escape returns focus.
- **SRT-11**: At 800 px wide, open the sidebar drawer: focus moves in and is trapped, the drawer's name is read; Escape closes and returns focus to the sidebar button.
- **SRT-12**: Switch the gallery direction to right to left (`ar`) and repeat SRT-3 and SRT-11: reading order follows the visual order from the right.
- **SRT-13**: Tab into a `Table` that scrolls sideways (at 800 px): its scroll region's name is read, and arrow keys read each row with its header.

### Data model sketch

No database tables and no server changes. The persistent state is browser storage items already declared in spec 0003 STO-2:

| Item                                | Key                                            | Fields                                                                                                                     |
| ----------------------------------- | ---------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| Theme preference                    | `orvano:v1:theme`                              | `"system" \| "light" \| "dark"`                                                                                            |
| Layout (existing key, these fields) | `orvano:v1:{userId}:layout`                    | `sidebarCollapsed: boolean`, `density: "comfortable" \| "compact"` (beside `lastSdk`, which UX-37 owns; one store, SHL-11) |
| Resource list per service           | `orvano:v1:{userId}:{projectId}:svc:{service}` | `collapsed: boolean`, `width: number` (200 to 480; out of range resets to 280)                                             |
| Palette recents                     | `orvano:v1:{userId}:palette`                   | up to 10 action IDs, newest first                                                                                          |

In memory stores (`shared/stores`): theme, layout (SHL-11), messages (the last 50), shortcut registry, and the interaction lock of spec 0003 QRY-4 (held while any form is dirty; the shell only reads it).

### State transitions

Theme preference: `system`, `light`, and `dark`, any to any, by the account menu or the palette. The resolved theme follows the preference, and under `system` it follows the OS media query.

### API surface

No HTTP endpoints. The surface is the `src/ui` component and hook API above (SHL-1, PRM-1 to PRM-11). The props that cross into feature 13:

| Contract                          | Shape                                                          | Provided by                                     |
| --------------------------------- | -------------------------------------------------------------- | ----------------------------------------------- |
| `ShellModel`, `Crumb[]`           | SHL-1                                                          | Feature 13's `_shell` route (gallery: fixtures) |
| `SwitcherSearch`                  | `(query, { limit, signal })`                                   | Feature 13's org and project search             |
| `ConfirmDelete` `loadDependents`  | `(signal) => Promise<{ items: { kind, count, estimated }[] }>` | The feature that deletes (spec 0014 CPX-2)      |
| `SecretReveal` `secret`, `masked` | strings                                                        | The create response (feature 13 API keys)       |
| `useAppForm`                      | spec 0003 FRM-1 to FRM-4                                       | Each form                                       |
| `notify`                          | PRM-8                                                          | Any feature                                     |

### Value sourcing

| Action                                | Value produced or displayed             | Source                                                                                                          |
| ------------------------------------- | --------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Account menu                          | name, email, avatar initials            | `ShellModel.user`, from the session (spec 0014 CPX-3, built by feature 13); initials derived per PRM-11         |
| Sidebar                               | which items, and their order            | `ShellModel.nav`; in project scope the project's capabilities (spec 0003 RTE-9, feature 13)                     |
| Sidebar                               | item label and icon                     | `nav-registry.ts` by `NavId` (SHL-2)                                                                            |
| Sidebar, tabs, resource list          | current item                            | the router's active link match (`exact` for Overview), rendered as `aria-current`                               |
| Breadcrumbs                           | page crumb labels and links             | the `crumbs` prop, built by feature 13 from `staticData.crumb` (RTE-7)                                          |
| Breadcrumbs                           | org and project names                   | `ShellModel.org.name`, `ShellModel.project.name`                                                                |
| Switchers                             | results, limit                          | `SwitcherSearch(query, { limit: 20, signal })`                                                                  |
| Switchers                             | result count announcement               | the length of the returned list, through the announcer                                                          |
| Switchers                             | "All" and "New" links                   | `ShellModel.orgLinks`, `.projectLinks`                                                                          |
| Document title                        | `Resource · Service · Project · Orvano` | the crumbs in reverse (org left out, per UX-4), plus the brand name                                             |
| `<html data-theme>`                   | resolved theme                          | `orvano:v1:theme`, else `system`, resolved by the media query (THM-2, THM-4)                                    |
| `<html data-density>`, rail collapsed | density, collapsed state                | the layout store (SHL-11), defaults `comfortable` and expanded; `userId` from `ShellModel.user.id`              |
| Resource list                         | width, collapsed, maximum               | `orvano:v1:{userId}:{projectId}:svc:{service}`, default 280 px and expanded; maximum from the viewport (SHL-13) |
| `<html lang dir>`, React Aria locale  | locale and direction                    | `shared/i18n` (I18N-6); in the gallery and the RTL test, RTL-1                                                  |
| Toasts, banners, history              | messages                                | `notify()` and the messages store (PRM-8)                                                                       |
| Screen reader announcements           | text                                    | the announcer store, through `@react-aria/live-announcer`                                                       |
| `ShortcutHelp`                        | shortcut list                           | the `useShortcut` registry (A11Y-7)                                                                             |
| `Kbd`, `aria-keyshortcuts`            | Mod key name                            | the platform (PRM-11)                                                                                           |
| Command palette                       | actions, recents                        | `palette-actions.ts` (SHL-10); `orvano:v1:{userId}:palette`                                                     |
| `ActivityIndicator`                   | running jobs                            | none until feature 46; shows "No running jobs"                                                                  |
| `DocsPane`                            | fallback link                           | `DOCS_FALLBACK_URL` in `shared/config.ts` (SHL-9)                                                               |
| `ConfirmDelete`                       | tier                                    | `tiers.ts` by `ResourceKind` (PRM-6, UX-23)                                                                     |
| `ConfirmDelete`                       | what goes with it                       | `loadDependents(signal)` (spec 0014 CPX-2)                                                                      |
| `SecretReveal`                        | secret, then masked value               | `secret` and `masked` props (PRM-7)                                                                             |
| `CopyField`                           | copy result                             | `navigator.clipboard`, else the PRM-9 fallback                                                                  |
| `RelativeTime`                        | relative text, exact local and UTC time | the `datetime` prop through `shared/format` (I18N-5, PRM-11)                                                    |
| `GridSkeleton`                        | rows and columns                        | 8 rows; columns from its prop (PRM-11)                                                                          |
| Every visible color, size, radius     | token value                             | `theme.css` (TKN-6)                                                                                             |

### Key invariants

- Only the tokens in `theme.css` produce color, font, text size, radius, and shadow utilities; feature code cannot express any other.
- `design.md`'s token tables are generated from `theme.css` and never stale (TKN-6), and every contrast check passes in both themes (TKN-7).
- One stylesheet, no runtime style injection, and no CSP violation in the built console (STY-2, STY-6).
- The shell never fetches; everything it shows comes from its props, the router, stores, or storage (SHL-1).
- The only live regions are the `Announcer`, React Aria's toast region, `Banner`, and `InlineError` (AC-12).
- Primitives accept no `className` or `style`, and only `src/ui` imports React Aria, Lucide, or the grid (PRM-3, ICN-1, AC-19).
- The gallery is never in a production build or the production catalog (GAL-3).
- No physical direction utility anywhere in `console/src` (RTL-2).

### Security model

No new data, endpoints, or permissions. What matters here:

- The CSP of spec 0006 NET-2 stays strict and is now tested (STY-6): no inline script (the theme script is a file, THM-2), no inline style element, no third party font, image, or script origin (system fonts, Lucide compiled into the bundle).
- Nothing new goes to storage beyond layout preferences (STO-6). `SecretReveal` keeps the secret in memory only (PRM-7).
- The docs fallback link is the only external link the shell renders, opened with `noopener noreferrer` (SHL-9). With `Referrer-Policy: same-origin` the console URL never leaks.
- The gallery and its fixtures never ship (GAL-3), so no demo surface exists on a real install.
- Feature 13 owns hiding services the operator can't open (RTE-9, UX-30); the shell renders only what `ShellModel.nav` holds.

### Configuration required

None. No environment variable, secret, or third party account. New dependencies, each on spec 0009 DEP-1 and pinned at least 3 days old (TL-8), with gzip size in the PR (BDL-3): `tailwindcss` and `@tailwindcss/vite` (MIT, dev; they pull `lightningcss`, MPL-2.0, used unmodified, DEP-3), `react-aria-components`, `react-aria`, `@react-aria/live-announcer` (Apache-2.0), `lucide-react` (ISC), `@tanstack/react-form` and `zod` (MIT, already named in spec 0003 WEB-1). `react-data-grid` (MIT) is installed by the rows feature, not here.

### Critical test scenarios

Each names its harness: Vitest browser mode (component tests, spec 0003 QA-1), Playwright (e2e, run against `vite preview` for these static checks), or a Node script in `tooling/scripts`.

- Happy path (Vitest, three engines): the gallery's project shell scene renders in both themes and both densities with no axe violations; the skip link, sidebar, breadcrumbs, switcher, account menu, and palette all work by real key presses; verifies **AC-5**, **AC-7**, **AC-9**, **AC-11**, **AC-13**, **AC-15**.
- Theme boot (Playwright): with `orvano:v1:theme` set to `dark` and the OS emulated light, `data-theme` is `dark` before the app script runs; with storage throwing, the emulated OS theme wins; with System selected, `emulateMedia` switching the OS theme updates the page at once; verifies **AC-4**.
- Tokens (Node and Vitest): changing one token in `theme.css` to a low contrast value fails `theme.contrast.test.ts`; changing any token without running `pnpm fix` fails `pnpm check` on `design.md`; verifies **AC-1**, **AC-5**.
- CSP (Playwright): the built console under NET-2's header reports zero `securitypolicyviolation` events; verifies **AC-2**, **AC-23**.
- Narrow and zoom (Vitest with viewport commands): at 800 px the drawer traps focus and returns it, and widening past 64rem closes it; at 320 px nothing scrolls sideways, every crumb control is at least 24 by 24 px, and the current page crumb shows its name or truncates with a tooltip; verifies **AC-8**, **AC-9**.
- Right to left (Vitest): with `dir="rtl"` and `ar` the sidebar is at the right, the drawer opens from the right, the separator's arrow keys are swapped, a chevron is mirrored, `Kbd` keeps ⌘ before K, and a long Latin resource name keeps its start; a lint fixture with `ml-2` fails; verifies **AC-18**.
- Forced colors and reduced motion (Playwright `emulateMedia`): with `forcedColors: 'active'` the focus ring and the current sidebar item are visible; with `reducedMotion: 'reduce'` a sheet opens with no transform transition; verifies **AC-6**.
- Resource list (Vitest): arrow keys on the separator change the width by 16 px, Home and End go to the limits, the maximum respects the 480 px content floor, and the width survives a remount from storage; verifies **AC-10**.
- Timing (Vitest, real timers): a handler released after 250 ms shows no skeleton; one released after 400 ms shows it until at least 900 ms; verifies **AC-17**.
- Toasts (Vitest): four success toasts show three; two identical ones merge with a count; an error stays; the history holds them all; verifies **AC-12**.
- Clipboard (Vitest): with `navigator.clipboard.writeText` rejecting, the value is selected, the keyboard hint shows, and nothing announces "Copied"; verifies **AC-24**.
- Not shipped (Node script after build): no gallery file or chunk in `dist/`; verifies **AC-20**.
- Budgets (Node, `pnpm size` after build): the CSS entry is within 30 KB, every JavaScript file within 80 KB, the BDL-1 first load and the shell probe's `Shell first load (AC-3, probe)` entry within 230 KB; an import of `zod` outside `@/shared/zod` fails lint through its fixture (STY-8); verifies **AC-3**.
- Unavailable action (Vitest): a `Button` with `unavailableReason` is focusable, ignores Enter and click, and exposes the reason through `aria-describedby`; verifies **AC-15**.

## Build plan

Tracer Bullet inside this feature: milestone 1 runs one thin thread through every layer (tokens, theme boot, Tailwind build, generated design tables, lint rules, the gallery, one primitive, the shell frame, the contrast, size, and CSP checks, and axe in three engines), so every later milestone only adds primitives to a pipeline that already works. Each milestone is one `/develop` run and one PR.

**Milestone 1: the thread**

1. Confirm `@tailwindcss/vite` works with the installed Vite 8 (else the PostCSS fallback in STY-1); add Tailwind 4; write `src/ui/theme.css` with the reset scales, every token from `design.md` with its `use` comment, the dark, media query, `prefers-contrast`, `forced-colors`, and density blocks, the `dark` variant, the base layer, and the plain variables; set `build.cssCodeSplit: false` (STY-1, STY-2, STY-7, TKN-1 to TKN-5, THM-3, THM-6); satisfies **AC-2**, **AC-6**.
2. `public/theme-boot.js`, the theme store and `orvano:v1:theme` item, and the `<head>` wiring in `index.html` (THM-1 to THM-5); satisfies **AC-4**.
3. `tooling/scripts/design-tokens.mjs` in `pnpm fix` and `pnpm check`, `theme.contrast.test.ts`, and the CSS size-limit entry (TKN-6, TKN-7, STY-4); satisfies **AC-1**, **AC-3**, **AC-5**.
4. Restriction rules and fixtures: `style` and arbitrary values outside `src/ui`, the RTL-2 pattern, the React Aria, `lucide-react`, and `react-data-grid` import rule, nothing imports `src/gallery` (STY-5, RTL-2, ICN-1, GAL-3); satisfies **AC-18**, **AC-19**.
5. The gallery entry, its hash router with the shell paths, toolbar (theme, density, locale, direction, width), fixtures, the Lingui exclusion, and `check-gallery-not-shipped.mjs` in CI (GAL-1 to GAL-3, RTL-1); satisfies **AC-18**, **AC-20**.
6. `cx`, `Icon` and `icons.ts`, `Link` (with the preload test), `Button` and `IconButton` with `unavailableReason`, `Tooltip`, `VisuallyHidden`, `Avatar`; a React Aria chunk group in `vite.config.ts` if BDL-1's 80 KB per file needs it (PRM-4, PRM-11); satisfies **AC-3**, **AC-15**.
7. The `AppShell` frame with `SkipLink`, landmarks, `BrandMark`, and a static `PrimarySidebar` in project scope in the gallery, tested with axe in three engines and both themes; `AuthLayout` around the current welcome page; the CSP e2e test (STY-6); satisfies **AC-5**, **AC-7**, **AC-21**, **AC-23**.

**Milestone 2: the shell**

8. `ShellModel` and `Crumb` types, `nav-registry.ts`, the sidebar in all three scopes, collapse to the icon rail, and the layout store (SHL-1, SHL-2, SHL-5, SHL-11); satisfies **AC-7**, **AC-8**, **AC-11**.
9. `useIsWide`, `Drawer`, and the narrow layout below 64rem, the 320 px reflow (SHL-6, SHL-8); satisfies **AC-8**.
10. `Breadcrumbs`, `OrgSwitcher`, `ProjectSwitcher`, `useDocumentTitle` (SHL-12, UX-4); satisfies **AC-9**.
11. `ResourceLayout` and `ResourceList` with the keyboard separator and the per service storage item (SHL-13, UX-2); satisfies **AC-10**.
12. `PageHeader`, `RouteTabs`, `Tabs`, `Menu`, `Popover`, and `AccountMenu` with the theme and density sections (UX-5, UX-36, UX-32); satisfies **AC-11**, **AC-15**.
13. The right to left browser test across the shell; satisfies **AC-18**.

**Milestone 3: forms and overlays**

14. `src/ui/form`: `useAppForm`, the field components, `SubmitButton`, `ErrorSummary`, and the FRM-2 timing tests (spec 0003 FRM-1 to FRM-6); satisfies **AC-16**.
15. `Dialog`, modal `SideSheet` (lazy, PRM-10), and `UnsavedChangesGuard` on `useBlocker` (UX-6, UX-9, RTE-8); satisfies **AC-15**, **AC-16**.
16. `ConfirmDelete` with `tiers.ts` and `loadDependents` (UX-23, PRM-6); satisfies **AC-17**.

**Milestone 4: feedback and data display**

17. `notify`, `Announcer`, `Toast` and its region, `Banner`, `InlineError`, the messages store with its 50 item history (UX-24, UX-41, PRM-8); satisfies **AC-12**.
18. `EmptyState`, `ErrorState`, `Skeleton`, `GridSkeleton`, `ContentSkeleton`, `useDelayedPending`, `Progress`, `Spinner`, `StatusChip`, `Badge` (UX-20 to UX-22, UX-29 chip states); satisfies **AC-15**, **AC-17**.
19. `Table`, `SettingsCard`, `DangerZone`, `CopyField`, `SecretReveal`, `CodeSnippet`, `RelativeTime` (UX-33, UX-34, PRM-7, PRM-9, PRM-11); satisfies **AC-15**, **AC-24**.
20. The boot error page and root error component on tokens (FLT-2); satisfies **AC-21**.

**Milestone 5: power features and sign off**

21. `useShortcut`, `ShortcutHelp`, `Kbd`, and `CommandPalette` with local actions and recents (UX-25 subset, UX-26, A11Y-7, SHL-10, PRM-10); satisfies **AC-13**.
22. `ActivityIndicator` with message history, `HelpButton`, and `DocsPane` with the fallback link (UX-29, UX-31, SHL-9); satisfies **AC-12**, **AC-14**.
23. Finish `design.md`'s component sections against what was built, and check the bundle and CSS budgets; satisfies **AC-1**, **AC-3**.
24. Run the screen reader script SRT-1 to SRT-13 in both pairs and record the results in `verify.md` (the engineer runs it; `/check verify` checks it is recorded); satisfies **AC-22**.

**Milestone 6: the shell's first load budget**

25. Add the `sideEffects` list to `console/package.json` and compare the manifests (STY-8); move every Zod user to `zod/mini` with the restriction rule and its `sty-8` fixture: `src/shared/zod.ts`, `src/main.tsx`, `src/shared/storage/items.ts`, `stored.ts` and `stored.test.ts`, `src/test/setup.ts`, `src/ui/dialog.test.tsx`, `src/ui/form/form.test.tsx`, `fields.test.tsx`, and `unsaved-changes-guard.test.tsx`, `src/gallery/main.tsx`, and the gallery's forms and overlays scenes; raise `firstLoadLimit` in `scripts/size-entries.mjs` to 230 KB; add the shell probe to `vite.config.ts`, `src/size-probe/`, `scripts/size-entries.mjs` and its test, `pnpm size`, `lingui.config.ts`, the root `eslint.config.ts`, and `.gitignore`; put the measured sizes in the PR. Measured while deciding (2026-10-27, gzip): about 218.9 KB with the shell, against 235.7 KB without these changes (STY-8, STY-9); satisfies **AC-3**.

## Consequences

**Positive**:

- Feature 13 and every later screen compose finished, tested primitives, so screens stay consistent with no per screen styling.
- Resetting Tailwind's palette and scales makes off brand colors and sizes impossible to write, not just discouraged, and the contrast test makes a bad token impossible to merge.
- System fonts and a build time stylesheet keep the CSP strict, add no font bytes, and need no license change; the CSP is now tested, not assumed.
- One source for tokens (`theme.css`) with generated tables means `design.md` can't drift.
- The gallery gives contributors and `/check verify` one place to see every state in every theme, density, and direction.

**Negative / tradeoffs**:

- Building the primitives on React Aria Components by hand is a lot of work (about 65 components and hooks across five milestones) compared with adopting a styled kit.
- Several behaviors React Aria doesn't provide are Orvano's own code to maintain: `unavailableReason`, toast merging and history, the resize separator's keys, and the docs pane's focus handling.
- Primitives without `className` sometimes force a new prop or variant for a one off need; that is the price of consistency, and a new variant goes through `design.md`.
- System fonts render a little differently on macOS, Windows, and Linux, so pixel perfect screenshots aren't possible across platforms.
- React Aria Components and TanStack Form add real weight: the shell alone is about 37 KB gzip of the first load, so BDL-1's first load was raised to 230 KB (STY-8), and it still depends on the lazy set in PRM-10, the `sideEffects` list, and `zod/mini`.
- Tailwind v4 sets a modern browser floor (STY-7).
- The screen reader script is manual and needs a Windows machine or VM for NVDA, so sign off depends on a person.
- The tailwind-4-docs skill needs a local docs sync, and its snapshot is not open source, so it must stay out of git.

**Neutral**:

- The visual direction (neutral zinc, indigo, 6 px radius) is close to other developer consoles; brand personality can come later through the mark and illustrations without changing the tokens' roles.
- Chart colors, syntax highlighting, and the grid's look are deferred to the features that need them, under the same token rules.

## Follow-up

- [x] (Done 2026-10-28 with the review triage decisions.) Spec 0003 (by `/architect`, after milestone 1): resolve RTE-6's "verify with feature 11" note with the working `Link` form; widen DIR-3's import list with `lucide-react` and `react-data-grid`; add `gallery.html`, `src/gallery/`, and the `src/ui` subfolders (`form`, `shell`, `toast`, `confirm-delete`, `data-grid`) to DIR-1; amend A11Y-4 so React Aria's toast region and `Banner` are the allowed live regions beside the announcer; record in the scope table that styling is Tailwind CSS v4 and the grid is react-data-grid (spec 0015).
- [x] (Done 2026-10-28: UX-25 notes the subset and the menu pattern; the feature 11 follow up in spec 0001 now names react-data-grid and the owners, and closes when AC-22 is recorded.) Spec 0001 (by `/architect`): note the deliberate UX-25 subset (SHL-10) until feature 25; close the feature 11 follow up once AC-22 is recorded, naming react-data-grid as the grid and moving `DataGrid`, `LogStream`, `JsonEditor`, and `RelationPicker` to the features in _Scope and boundaries_.
- [x] `/sync`: add `react-aria` and `tailwind-4-docs` to `console/AGENTS.md` _Agent skills_ (console only, not root), with the note that `tailwind-4-docs` needs `python .claude/skills/tailwind-4-docs/scripts/sync_tailwind_docs.py --accept-docs-license` once per clone; record the declined skills (the Lucide and data grid skills, too thin; `wshobson/agents@tailwind-design-system`, overlaps `design.md`; no MCP server found); point `console/AGENTS.md` at `console/design.md`.
- [ ] Feature 13: build `ShellModel` and `crumbs` from the CPX-3 session, the org and project queries, project capabilities, and `staticData.crumb`; wire `_shell`, `_auth`, `useDocumentTitle`, the switchers' search with `limit`, and the density step at boot (SHL-11); narrow `ShellLinkOptions` to the router's typed options.
- [ ] The rows feature (16): verify react-data-grid against UX-11 to UX-19, bring its CSS into `theme.css`, and build `DataGrid` and the non modal row sheet (PRM-5).
- [ ] Feature 45: chart palette tokens (categorical and sequential), checked against both themes like TKN-7.
- [ ] Later: a real logo to replace the geometric mark (one file, SHL-7).
- [ ] AC-22 (owed after milestone 5): run the screen reader script SRT-1 to SRT-13 with VoiceOver and Safari on macOS and with NVDA and Firefox on Windows, and record each result in [`verify.md`](verify.md) before the feature is `done`.
- [x] AC-3 (settled by `/architect` on 2026-10-27): the first load with the shell is 230 KB gzip, the popovers stay eager, and the shell probe checks it in CI (STY-8, STY-9); spec 0003 BDL-1, WEB-1, URL-1, and its search schema example are amended to match. The measurements are in [rationale.md](rationale.md#ac-3-first-load-budget-2026-10-27).
- [x] Step 25 (by `/develop`, before `/check verify` tests AC-3): build milestone 6.
- [ ] Feature 13: when the `_shell` routes land, delete the shell probe (STY-9) and check that the BDL-1 entry stays within 230 KB with the real session, org, and project queries.
- [x] Review triage questions Q1 to Q13 (settled by `/architect` on 2026-10-28): see [rationale.md](rationale.md#review-triage-decisions-2026-10-28) and the [triage](../../reviews/2026-10-28-triage-design-system-console-shell.md#decisions).
- [x] Review fix batches 1 to 7 (by `/develop`, before AC-22 is run): build the [triage](../../reviews/2026-10-28-triage-design-system-console-shell.md)'s batches against the rules above (SHL-4, SHL-12, SHL-14, PRM-4 on `Switch`, RTL-1, RTL-3, THM-6, GAL-2, GAL-3, STY-6).
