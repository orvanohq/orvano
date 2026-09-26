# 0005. Console design system and shell

**Date**: 2026-09-26
**Status**: Proposed

## Summary

This spec gives the Orvano console its look and its frame. The look is a dark first design system (a shared set of colors, type, spacing, and components) built from shadcn/ui components on Base UI (a library of accessible building blocks), styled with Tailwind CSS v4, with an indigo violet accent, Inter and JetBrains Mono fonts, and a compact or comfortable density you can switch. The frame is the shell every screen lives in: a top bar with an org and project switcher, a sidebar that grows as products ship, light, dark, and system themes, and the handling for sign in redirects, missing projects, and projects that are still being set up. For building, it means Tailwind and shadcn in `console/`, a token file and a `console/design.md`, about 25 base components with keyboard and accessibility tests in a real browser, a handful of shell pages wired to the real API through `@orvano/console-client`, and strict security headers on the console in Caddy.

## Requirements

**User stories**:
- As a developer using Orvano, I want a fast, readable console that works in dark and light and on any screen, so that I can manage my projects comfortably, even from a phone during an incident.
- As a developer with several orgs and projects, I want to switch org and project from anywhere in one or two clicks, and land back where I was, so that moving between apps costs no thought.
- As a keyboard or screen reader user, I want every control reachable and announced, with visible focus, so that I can use the console without a mouse.
- As the Orvano team, I want one design system and one shell that every later row plugs into, so that each product adds screens without inventing its own styles, states, or navigation.

**Acceptance criteria** (the contract):

Design system
- **AC-1**: `console/design.md` documents every token in *Design tokens* (color in both themes, type, density, spacing and layout, radius, elevation, motion, focus, breakpoints, layers), the icon rules, the UI text rules, and the component inventory with each component's variants, states, and keyboard behavior. `console/src/styles/tokens.css` implements exactly those tokens, and components use only tokens: no color literal (`#`, `rgb(`, `hsl(`, `oklch(`) appears in `console/src` outside `tokens.css`, checked by a test.
- **AC-2**: A first visit shows the dark theme. The account menu offers Light, Dark, and System. System follows the operating system and changes live when it changes, without a reload. The choice survives a reload, and the page never paints in the wrong theme first: `<html>` carries `data-theme` before the first paint.
- **AC-3**: The default density is compact. Choosing Comfortable in the account menu changes, in every component, the control height (32 to 40 px), the table row height (36 to 44 px), the body text (14 to 16 px), and the icon size (16 to 18 px). The choice survives a reload and applies before the first paint, like the theme.
- **AC-4**: In both themes, every text and background pair listed in *Contrast pairs* meets 4.5:1, and every control border and focus ring meets 3:1 against the surfaces it sits on, checked by a test that computes the ratios from `tokens.css`.
- **AC-5**: Every component in *Component inventory* exists in `console/src/components/ui/`, renders correctly in both themes and both densities, and has a browser test that runs its keyboard script from `console/src/components/ui/keyboard-scripts.ts` (see *Keyboard scripts*) and runs axe (an automated accessibility checker) with zero violations in both themes.
- **AC-6**: Every interactive element shows a solid 2 px focus ring on keyboard focus and none on mouse click. Dialogs, drawers, menus, and popovers trap focus while open, close on Escape, and return focus to the element that opened them.
- **AC-7**: Overlays (dialogs, drawers, menus, popovers, tooltips, toasts) animate in at most 200 ms and out at most 150 ms. With `prefers-reduced-motion: reduce`, nothing animates. Page changes never animate.
- **AC-8**: The console loads fonts and icons only from its own origin: a page load makes no request to any other host. Text uses Inter; IDs, keys, and code use JetBrains Mono; numbers in tables use tabular figures.
- **AC-9**: In development, `/dev/components` renders every inventory component in every variant and state (default, hover, focus, disabled, loading, error, empty), with theme and density switches. The production build contains none of it: in the production shape, `/dev/components` shows the 404 page, and no built file contains the catalog marker string `orvano-dev-catalog`.

Shell
- **AC-10**: For a signed in console user, every shell page shows a top bar (logo, org and project switchers, account menu) and, in org and project context, a sidebar. The page has one `banner`, one `navigation` named for its context, and one `main` landmark, and a "Skip to content" link is the first tab stop and moves focus to `main`.
- **AC-11**: Console URLs are `/orgs`, `/orgs/$orgId`, and `/projects/$projectId` (products add paths below it). Each one loads directly from a pasted link, and browser back and forward move between them.
- **AC-12**: `/` sends you to the project in `orvano.lastProject` when that project loads for you, and otherwise to `/orgs`. Opening any project page records its ID there.
- **AC-13**: The org switcher lists your orgs from `consoleOrgs.list`, 100 per page, and loads the next page when you reach the end of the list. Typing filters the loaded orgs by name (case insensitive, anywhere in the name); when nothing loaded matches and more pages exist, a "Load more to search further" option appears. Orgs being deleted appear dimmed with a Deleting badge, after the active ones: after every page fetch the whole loaded list is sorted again (stable, active first, then the API's order), never one page at a time. It opens with Enter or Space, arrows move, Enter picks, and Escape closes and returns focus. Picking an org opens `/orgs/$orgId`.
- **AC-14**: The project switcher lists the current org's projects from `consoleProjects.list` under the same paging, filtering, and keyboard rules, with a status badge on every project that is not active. Picking a project opens the same product in the new project (the first path segment after the project ID) when the new project is active, and its overview otherwise. Both switchers have an empty footer slot where later rows add "Create" links.
- **AC-15**: In org context the sidebar shows Projects; in project context it shows Overview plus every product entry registered in `console/src/shell/nav.ts`. Product entries appear only while the project is `active`. The current entry carries `aria-current="page"`. `/orgs` has no sidebar.
- **AC-16**: At 1024 px wide and above, the sidebar toggles between 240 px and a 56 px icon rail with its toggle button or the `[` key (ignored while typing in a text field). In rail mode every entry has a tooltip with its label. The state survives a reload. Below 1024 px the sidebar is a drawer, opened from a menu button in the top bar, that traps focus and closes on navigation, Escape, or a click outside.
- **AC-17**: At 360 px wide, no shell page scrolls sideways. Tables scroll inside their own focusable, labelled container. Below 640 px the top bar shows only the deepest switcher (the project switcher in project context, the org switcher in org context), and the org switcher moves to the top of the sidebar drawer, so both stay reachable.
- **AC-18**: A project that is `provisioning` shows a "Setting up" panel instead of its pages, checks again every 2 seconds, and shows the overview as soon as it turns `active`, with no reload; after 60 seconds it adds a "taking longer than usual" note. A `failed` project shows a "Setup failed" panel. A `deleting` project shows a "Being deleted" panel with its purge date, and a purge failure note when `purgeFailedAt` is set. Each panel has an action slot that row 7 fills. An org being deleted shows a Deleting banner on its page.
- **AC-19**: A project URL that answers 404 `project_not_found`, or an org URL that answers 404 `not_found`, shows a "doesn't exist or you don't have access" message inside the shell, with the top bar still working, and removes that project from `orvano.lastProject` if it was there. Any URL that matches no route shows the 404 page.
- **AC-20**: Any console call that answers 401 `console_session_required` sends you to `/sign-in?redirect=<path and query you were on>` and clears cached data, exactly once: when several calls fail together, the first one's location wins and the rest are ignored until that navigation ends. The `redirect` value is accepted only as a path on this origin (it starts with one `/`, never `//` or a scheme). Until row 8, `/sign-in` explains that sign in arrives with row 8.
- **AC-21**: A page whose data fails to load (5xx, network failure, or timeout) shows its error panel with the error message, code, and request ID, and a Retry button that loads it again; never raw JSON or a stack trace. Form fields show their own validation errors under the field; a server error on submit shows in an alert at the top of the form. A failed action shows an error toast that stays until dismissed; a success toast disappears after 5 seconds, paused while hovered or focused.
- **AC-22**: The role helpers hide owner only admin areas (nav entries and panels marked owner) from developers and viewers, and render other actions your role can't take as disabled but focusable, with a tooltip and an accessible description giving the reason (for example "Owners only"). The API's 403 stays the real guard.
- **AC-23**: Every page sets the document title to `<Page> · <Org or project name> · Orvano`. Every page renders its title through the shared `PageHeading` component (`<h1 id="page-title" tabIndex={-1}>`), and after an in app navigation (not the first load) the root moves focus to `#page-title` so screen readers announce it.

Security and development
- **AC-24**: Every console file Caddy serves carries the headers in *Security headers*. In the production shape, visiting every shell page with a session produces no Content Security Policy violation. Hashed files under `/assets/` are cached for a year as immutable; `index.html` and `theme-init.js` are sent with `Cache-Control: no-cache`.
- **AC-25**: `dotnet run --project dev/Orvano.AppHost -- --OrvanoDev:Fixtures=true` runs `api` and `worker` in the `Test` environment with `tests/scenarios/fixtures.yaml`, and the Vite dev proxy adds the fixture console cookie to `/v1/console` calls, so the dev console opens straight into the Fixtures org. Without the switch, the AppHost behaves as it does today. None of this reaches the production build or images.
- **AC-26**: Against the scenario server with the gateway in front (compose profile `console`), an end to end browser test starts with the fixture cookie set, lands on `/orgs`, and reaches the Scenarios project overview through the org and project switchers using only the keyboard, with data from the real API.

## Decision

**Chosen option**: Option 1: shadcn/ui components on Base UI, styled with Tailwind CSS v4 through CSS variable tokens, in a sidebar and top bar shell wired to the real console API.

Components are copied into the repo from the shadcn registry in its Base UI flavor, then restyled through one token file, so Orvano owns every line and every later row builds from the same parts.

**Library choices** (verify current versions before building; this space moves fast):

| Where | Library | License | Use |
|---|---|---|---|
| `console` | `tailwindcss` v4 and `@tailwindcss/vite` | MIT | Styling, tokens through `@theme` |
| `console` | shadcn CLI (`shadcn@latest`, run with `pnpm dlx`, never a dependency), preset `base-nova` | MIT | Copies components into `src/components/ui/` |
| `console` | Base UI (the package the shadcn CLI installs for the `base` flavor, `@base-ui/react` at the time of writing) | MIT | Accessible primitives: focus, keyboard, overlays, combobox, toast |
| `console` | `class-variance-authority`, `clsx`, `tailwind-merge`, `tw-animate-css` | Apache 2.0 / MIT | Component variants and overlay animations (what shadcn generates) |
| `console` | `lucide-react` | ISC | Icons |
| `console` | `@tanstack/react-form` and `zod` (v4) | MIT | Every console form; validation through Standard Schema |
| `console` | `@tanstack/react-table` | MIT | Headless table logic for `DataTable` |
| `console` | `@fontsource-variable/inter`, `@fontsource-variable/jetbrains-mono` | OFL 1.1 | Self hosted variable fonts |
| `console` (dev) | `vitest`, `@vitest/browser-playwright`, `vitest-browser-react`, `playwright` | MIT / Apache 2.0 | Component tests in real Chromium |
| `console` (dev) | `axe-core` | MPL 2.0 | Accessibility checks in tests |
| `console` (dev) | `culori` | MIT | OKLCH to sRGB conversion for the contrast test |
| `console` (dev) | `@playwright/test` | Apache 2.0 | End to end tests against the compose stack |

**Implementation skills**: `shadcn` (`shadcn-ui/ui`, `.claude/skills/shadcn/`) · `tailwind-v4-shadcn` (`secondsky/claude-skills`, `.claude/skills/tailwind-v4-shadcn/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `tanstack-form` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-form/`) · `tanstack-table` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-table/`) · `zod` (`pproenca/dot-skills`, `.claude/skills/zod/`) · `tanstack-router` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-router/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `vercel-react-best-practices` (`vercel-labs/agent-skills`, `.claude/skills/vercel-react-best-practices/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`) · `aspire` (`microsoft/aspire-skills`, `.claude/skills/aspire/`)

Where a skill disagrees with this spec (the shadcn sidebar's cookie and `Ctrl+B` shortcut, `tailwind-v4-shadcn`'s React theme provider, the default `ring-ring/50` focus style), this spec wins.

## Feature design

### Data model sketch

No server data changes. The console keeps four preferences in the browser's `localStorage`, read inside `try`/`catch`; a missing, unreadable, or unknown value falls back to the default. None holds a secret.

| Key | Values | Default | Written by |
|---|---|---|---|
| `orvano.theme` | `dark` \| `light` \| `system` | `dark` | account menu |
| `orvano.density` | `compact` \| `comfortable` | `compact` | account menu |
| `orvano.sidebar` | `expanded` \| `collapsed` | `expanded` | sidebar toggle, `[` |
| `orvano.lastProject` | a project ID (`^[a-z0-9]{1,60}$`, anything else is ignored) | none | project layout route on load; removed on 404 |

Server data comes only from existing console operations (spec 0003, row 7): `Org` (`id`, `name`, `status`, `role`) and `Project` (`id`, `orgId`, `name`, `status`, `deletedAt`, `purgeAfter`, `purgeFailedAt`, `createdAt`).

### Design tokens

These are the values `design.md` documents and `tokens.css` implements, as CSS custom properties on `:root` (dark, the default), `[data-theme=light]`, and `[data-density=comfortable]`, mapped into Tailwind with `@theme inline`. Token names follow shadcn's so copied components work unchanged. Colors are OKLCH with hue 280 (indigo violet) for the accent and a faint tint of it in the neutrals. The values are the starting point and the contrast test (AC-4) is the judge: if a pair fails, change only the lightness (the first number) until it passes, and update `design.md` to match.

**Color**

| Token | Dark (default) | Light | Use |
|---|---|---|---|
| `--background` | `oklch(0.155 0.006 280)` | `oklch(0.985 0.002 280)` | Page |
| `--foreground` | `oklch(0.965 0.004 280)` | `oklch(0.21 0.01 280)` | Body text |
| `--card` / `--card-foreground` | `oklch(0.185 0.007 280)` / foreground | `oklch(1 0 0)` / foreground | Cards, panels |
| `--popover` / `--popover-foreground` | `oklch(0.205 0.008 280)` / foreground | `oklch(1 0 0)` / foreground | Menus, popovers, dialogs |
| `--muted` | `oklch(0.235 0.008 280)` | `oklch(0.962 0.004 280)` | Quiet fills, skeletons |
| `--muted-foreground` | `oklch(0.72 0.012 280)` | `oklch(0.50 0.015 280)` | Secondary text, hints |
| `--secondary` / `--secondary-foreground` | `oklch(0.255 0.009 280)` / foreground | `oklch(0.945 0.005 280)` / foreground | Secondary buttons |
| `--accent` / `--accent-foreground` | `oklch(0.27 0.012 280)` / foreground | `oklch(0.94 0.012 280)` / foreground | Hover and selected surfaces (shadcn's meaning of accent) |
| `--primary` / `--primary-foreground` | `oklch(0.54 0.21 280)` / `oklch(0.99 0 0)` | `oklch(0.51 0.23 280)` / `oklch(0.99 0 0)` | Primary buttons, brand |
| `--link` | `oklch(0.76 0.12 280)` | `oklch(0.48 0.20 280)` | Text links |
| `--ring` | `oklch(0.72 0.15 280)` | `oklch(0.51 0.23 280)` | Focus ring |
| `--border` | `oklch(0.28 0.008 280)` | `oklch(0.905 0.005 280)` | Decorative dividers only |
| `--input` | `oklch(0.52 0.012 280)` | `oklch(0.62 0.012 280)` | Control borders (3:1) |
| `--destructive` / `--destructive-foreground` | `oklch(0.54 0.20 25)` / `oklch(0.99 0 0)` | `oklch(0.54 0.21 27)` / `oklch(0.99 0 0)` | Destructive buttons, error badges |
| `--danger-text` | `oklch(0.72 0.17 22)` | `oklch(0.50 0.19 27)` | Error text and icons on page surfaces |
| `--success` / `--success-foreground` | `oklch(0.52 0.13 155)` / `oklch(0.99 0 0)` | `oklch(0.50 0.13 155)` / `oklch(0.99 0 0)` | Success badges |
| `--success-text` | `oklch(0.78 0.15 155)` | `oklch(0.45 0.12 155)` | Success text and icons |
| `--warning` / `--warning-foreground` | `oklch(0.72 0.15 75)` / `oklch(0.20 0.02 75)` | `oklch(0.80 0.15 80)` / `oklch(0.25 0.04 70)` | Warning badges (dark text on amber) |
| `--warning-text` | `oklch(0.82 0.14 80)` | `oklch(0.48 0.11 70)` | Warning text and icons |
| `--sidebar` / `--sidebar-foreground` | `oklch(0.135 0.006 280)` / foreground | `oklch(0.97 0.003 280)` / foreground | Sidebar surface |
| `--sidebar-accent` / `--sidebar-accent-foreground` | `oklch(0.24 0.02 280)` / foreground | `oklch(0.93 0.02 280)` / foreground | Current sidebar entry |
| `--sidebar-border`, `--sidebar-ring` | `--border`, `--ring` | `--border`, `--ring` | |

Status never relies on color alone: every badge carries its word (Active, Setting up, Failed, Deleting), and error text carries an icon.

**Contrast pairs** (AC-4, both themes): `foreground` on `background`, `card`, `popover`, `muted`, `accent`, `sidebar`, `sidebar-accent`; `muted-foreground` on `background`, `card`, `popover`, `muted`; `link`, `danger-text`, `success-text`, `warning-text` on `background` and `card`; each `*-foreground` on its fill (`primary`, `destructive`, `success`, `warning`, `secondary`) at 4.5:1. `input` on `background`, `card`, `popover`, and `ring` on `background`, `card`, `popover`, `sidebar` at 3:1. The list lives in `console/src/styles/contrast-pairs.ts` and the test reads it.

**Type** (Inter for UI, JetBrains Mono for code, IDs, and keys; `font-feature-settings: "cv11"` for Inter's single story `a`; tables use `tabular-nums`)

| Role | Compact | Comfortable | Weight |
|---|---|---|---|
| Page title (`h1`) | 24/32 px | 24/32 px | 600 |
| Section title (`h2`) | 18/28 px | 18/28 px | 600 |
| Subsection (`h3`) | 15/22 px | 16/24 px | 600 |
| Body (`--text-body`) | 14/20 px | 16/24 px | 400 |
| Label, button | 14/20 px | 16/24 px | 500 |
| Small, hint (`--text-small`) | 12/16 px | 14/20 px | 400 |
| Mono (`--text-mono`) | 13/20 px | 14/20 px | 400 |

**Density** (set by `data-density` on `<html>`)

| Token | Compact | Comfortable | Used by |
|---|---|---|---|
| `--control-h` | 2rem (32 px) | 2.5rem (40 px) | Buttons, inputs, selects |
| `--control-h-sm` | 1.75rem (28 px) | 2rem (32 px) | Small buttons, menu items (all targets stay at or above 24 px, WCAG 2.5.8) |
| `--control-px` | 0.625rem | 0.875rem | Horizontal padding in controls |
| `--row-h` | 2.25rem (36 px) | 2.75rem (44 px) | Table rows, switcher items |
| `--icon` | 1rem | 1.125rem | Lucide icon size |
| `--stack` | 0.75rem | 1rem | Default gap between form fields |

**Spacing and layout**: Tailwind's default 4 px spacing scale for everything else. Top bar 48 px. Sidebar 240 px expanded, 56 px as a rail; drawer 280 px. Page padding 24 px from 640 px up, 16 px below. Forms at most 640 px wide; long text at most 65 characters per line; tables take the full width.

**Radius**: `--radius: 0.5rem`, so shadcn's scale gives 4 px (`sm`, badges), 6 px (`md`, controls), 8 px (`lg`, cards, dialogs, popovers), 12 px (`xl`, empty state illustrations).

**Elevation**: surfaces step up in lightness (background, card, popover) with a 1 px `--border`; shadows only on overlays: `--shadow-overlay` is `0 8px 24px oklch(0 0 0 / 0.45)` dark and `0 8px 24px oklch(0.2 0.02 280 / 0.12)` light.

**Motion**: enter 150 ms (drawer 200 ms), exit 100 ms, easing `cubic-bezier(0.2, 0, 0, 1)`, overlays only, through `tw-animate-css`. `@media (prefers-reduced-motion: reduce)` sets every duration to 0.

**Focus**: `:focus-visible { outline: 2px solid var(--ring); outline-offset: 2px }` on every interactive element, replacing shadcn's translucent `ring-ring/50` (which fails 3:1). Inside inputs the ring sits on the border (offset 0). Never removed without a replacement.

**Breakpoints**: Tailwind's defaults (`sm` 640, `md` 768, `lg` 1024, `xl` 1280). Below `lg` the sidebar becomes a drawer.

**Layers** (`z-index`): sticky headers 10, sidebar 20, drawer and overlays 50 (portals keep their open order), toasts 100.

**Icons**: Lucide only, at `--icon` size, stroke 2, `aria-hidden` unless the icon is the only content of a control, in which case the control has an `aria-label` and a tooltip. **Logo**: a text wordmark "Orvano" in Inter 600 beside a simple SVG ring mark in `--primary`; `public/favicon.svg` uses the same mark. Swapping in a real logo changes files, not code.

**UI text**: sentence case everywhere; buttons are verbs ("Delete project", never "OK"); no trailing periods on labels or buttons; IDs, keys, and code in mono with a copy button; dates through `Intl.DateTimeFormat` in the browser's locale and time zone, recent ones as relative time ("3 minutes ago") with the full date in a tooltip. English only, strings live in components.

### Component inventory

All live in `console/src/components/ui/`, added with the shadcn CLI (Base UI flavor) and then restyled to the tokens, unless marked custom. Keyboard behavior is what each test drives (AC-5).

| Component | Source | Variants and states | Keyboard |
|---|---|---|---|
| Button | shadcn `button` | primary, secondary, outline, ghost, destructive, link; sizes default, sm, icon; loading (spinner, `aria-busy`), disabled, `aria-disabled` with reason | Enter and Space activate; `aria-disabled` stays focusable and does nothing |
| Input, Textarea | shadcn `input`, `textarea` | default, invalid (`aria-invalid`), disabled, read only | native |
| Select | shadcn `select` | default, invalid, disabled | arrows, typeahead, Enter, Escape |
| Checkbox, Switch | shadcn `checkbox`, `switch` | checked, unchecked, indeterminate (checkbox), disabled | Space toggles |
| Field | shadcn `field` and `label` | label, hint, error (linked with `aria-describedby`), required marker | label click focuses control |
| Form alert | shadcn `alert` | error, warning, info, success | `role="alert"` for errors |
| Dialog | shadcn `dialog` | default, with form | focus trap, Escape closes, focus returns |
| Confirm dialog | shadcn `alert-dialog` | default, destructive (typed name confirmation option) | focus starts on Cancel, Escape cancels |
| Dropdown menu | shadcn `dropdown-menu` | items, checkbox and radio items, separators, submenus, destructive item | arrows, typeahead, Enter, Escape, Right and Left for submenus |
| Popover | shadcn `popover` | default | Escape closes, focus returns |
| Combobox | shadcn `combobox` | search, grouped, loading more, empty, footer slot | arrows, typing filters, Enter picks, Escape closes |
| Tooltip | shadcn `tooltip` | default | shows on focus and hover, Escape hides (WCAG 1.4.13) |
| Tabs | shadcn `tabs` | default | arrows move, automatic activation |
| Toast | shadcn `toast` (Base UI) | success (5 s, pauses on hover and focus), error (stays), with action; at most 3 visible | F6 moves to the toast region; Escape dismisses |
| Table, DataTable | shadcn `table` plus custom `data-table.tsx` on TanStack Table | loading (skeleton rows), empty, error, "Load more" for cursor paging, optional client sort over loaded rows, sticky header | scroll container has `tabIndex=0` and a label; sort buttons in headers |
| Badge | shadcn `badge` | neutral, primary, success, warning, destructive, status (dot plus word) | not interactive |
| Card | shadcn `card` | default, interactive (whole card is one link) | link focus |
| Empty state | shadcn `empty` | icon, title, text, action slot | |
| Skeleton, Spinner | shadcn `skeleton`, `spinner` | text, block, circle | `aria-hidden`; the loading region has `aria-busy` |
| Breadcrumb | shadcn `breadcrumb` | with switchers, collapsed | links |
| Sidebar | shadcn `sidebar` (with `sheet` for the drawer) | expanded, rail, drawer | `[` toggles; drawer traps focus |
| Separator, Kbd | shadcn `separator`, `kbd` | | |
| Copy button | custom `copy-button.tsx` | idle, copied (2 s, announced "Copied" through a polite live region), failed | Enter and Space |
| Code block | custom `code-block.tsx` | inline, block with copy button, no syntax colors yet | scroll container focusable |

Changes to the copied sidebar: its state goes to `localStorage` (`orvano.sidebar`), never a cookie (a cookie would ride along on every API call), and its shortcut is `[` instead of `Ctrl+B`.

### Keyboard scripts

`console/src/components/ui/keyboard-scripts.ts` turns the Keyboard column above into data the tests run, the way `contrast-pairs.ts` does for contrast. It exports one typed entry per inventory component: the component id, the catalog example to render, and an ordered list of steps, each a key (or key chord) to press and what must hold afterwards (which element has focus, whether an overlay is open, an attribute such as `aria-expanded` or `aria-checked`, or a value). One shared browser test walks every entry in both themes and runs axe after the last step; a component's own test file covers anything a script can't express. A new component or variant is not done until it has an entry.

### Shell structure

**Routes** (TanStack Router file routes in `console/src/routes/`):

| File | URL | Does |
|---|---|---|
| `__root.tsx` | | Providers, toast region, route announcer and focus handling, 404 page (`notFoundComponent`) |
| `sign-in.tsx` | `/sign-in` | Placeholder until row 8; `validateSearch` accepts `redirect` only as a same origin path (AC-20) |
| `dev.components.tsx` | `/dev/components` | Catalog, dev only: `beforeLoad` throws `notFound()` unless `import.meta.env.DEV`, and the component is `lazy(() => import('@/dev/catalog'))` created only inside `if (import.meta.env.DEV)` (no static import of `src/dev/` anywhere), so the build drops it (AC-9). `src/dev/catalog.tsx` contains the marker string `orvano-dev-catalog`. |
| `_app.tsx` | (pathless) | The session guard and the frame: top bar, sidebar, `main`, the route error panel |
| `_app/index.tsx` | `/` | Landing redirect (AC-12) |
| `_app/orgs/index.tsx` | `/orgs` | Your orgs, each as a section with its oldest 6 projects (the API's order) as cards and "View all" when `nextCursor` is set |
| `_app/orgs/$orgId/route.tsx` | | Loads the org; org sidebar; Deleting banner; in shell not found |
| `_app/orgs/$orgId/index.tsx` | `/orgs/$orgId` | The org's projects as a DataTable (name, status, ID with copy, created), "Load more", empty state with action slot |
| `_app/projects/$projectId/route.tsx` | | Loads the project and its org; records `orvano.lastProject`; status gating (AC-18); in shell not found (AC-19); project sidebar |
| `_app/projects/$projectId/index.tsx` | `/projects/$projectId` | Overview placeholder (name, ID with copy, org, created) |

**Top bar**: logo (links to `/orgs`) · breadcrumb with the org switcher and, in project context, the project switcher · account menu (icon button) with Theme (radio items: Light, Dark, System) and Density (Compact, Comfortable); row 8 adds your name, email, and Sign out. Below 1024 px a menu button opens the sidebar drawer; below 640 px the breadcrumb keeps only the deepest switcher (AC-17).

**Switcher paging**: the switchers use `useInfiniteQuery` with `limit` 100. An `IntersectionObserver` (root: the listbox) watches the last option; when it becomes visible and `hasNextPage` is true, the next page loads and a "Loading more" row shows at the end, so reaching the end with the arrow keys loads more too. The whole loaded list is sorted again after each fetch (AC-13).

**Page headings**: `console/src/shell/page-heading.tsx` exports `PageHeading`, the only way a page renders its `h1` (`id="page-title"`, `tabIndex={-1}`). The root's `onResolved` router subscription focuses `#page-title` after every navigation except the first (AC-23).

**Nav registry** (`console/src/shell/nav.ts`): the only place sidebar entries are declared. Each entry has `id`, `label`, a Lucide `icon`, a route `to`, and optional `minRole` (entries above your role are hidden, AC-22). Row 5 registers `orgNav: [Projects]` and `projectNav: [Overview]`; each product row adds its line.

**Console client** (`console/src/lib/console-client.ts`): one `Client` from `@orvano/console-client` with `endpoint: window.location.origin` for org level calls, and `projectClient(projectId)`, which returns a cached `Client` per project (the SDK binds the project per client, sending `X-Orvano-Project`). No `fetch` anywhere else; the placeholder health fetch in `routes/index.tsx` goes away.

**Query setup**: queries use `retry: false` (the client already retries idempotent calls, spec 0001) and `staleTime: 30_000`. Keys: `['console', 'orgs']` (infinite), `['console', 'orgs', orgId]`, `['console', 'orgs', orgId, 'projects']` (infinite), `['console', 'projects', projectId]`. The project query polls every 2 seconds while `provisioning`; when the status changes it invalidates that org's project list. `QueryCache` and `MutationCache` `onError` handle the 401 redirect (AC-20) in one place.

**Session guard**: `_app.tsx` `beforeLoad` runs `ensureInfiniteQueryData` on the first page of `consoleOrgs.list` (the switcher needs it anyway). A 401 `console_session_required` there, or in any query or mutation, calls one function, `redirectToSignIn()` in `console/src/lib/session.ts`. It keeps a module level in flight flag: the first call captures the router's current `pathname` plus `search`, clears the query cache, and navigates to `/sign-in` with `replace`; later calls return at once until that navigation settles. Row 8 swaps the probe for `consoleAccount.get` and adds its session codes to the same check.

**Theme bootstrap**: `console/public/theme-init.js`, a small classic script loaded by `<script src="/theme-init.js"></script>` in `<head>` before any stylesheet (no `defer`, no module, not inline, so the CSP needs no hash). It reads `orvano.theme` and `orvano.density`, resolves `system` through `matchMedia('(prefers-color-scheme: dark)')`, and sets `data-theme`, `data-density`, and `color-scheme` on `<html>`. `index.html` also ships `data-theme="dark" data-density="compact"` for the moment before it runs. After start, a React `PreferencesProvider` owns both values: at mount it reads the already resolved `data-theme` and `data-density` from `<html>` (it never resolves System a second time), then writes `localStorage` on change and listens for system theme changes while the choice is System. Tailwind's dark variant is `@custom-variant dark (&:where([data-theme=dark], [data-theme=dark] *))`.

**Paths**: `@/*` maps to `src/*` in `tsconfig.app.json` and `vite.config.ts` (the shadcn CLI needs it). Shell code in `src/shell/`, shared helpers in `src/lib/`, tokens in `src/styles/`, the catalog in `src/dev/`.

### Security headers

In `deploy/gateway/Caddyfile`, inside the console `handle` block:

```
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
Cross-Origin-Opener-Policy: same-origin
```

`/assets/*`: `Cache-Control: public, max-age=31536000, immutable`. `index.html` and `/theme-init.js`: `Cache-Control: no-cache`. React sets inline styles through the DOM (allowed under `style-src 'self'`); a library that injects a `<style>` element would be blocked, which is why the toast is Base UI's and not `sonner`. If the end to end run finds a violation from a library, allow that one style by hash, never with `'unsafe-inline'` for scripts, and record the exception in `design.md`.

### API surface

No new operations. The shell calls existing `console` operations through `@orvano/console-client` with the console cookie (spec 0003, spec 0004 AC-27).

| Operation | Method and path | Key inputs | Key outputs | Auth | Key errors (and what the shell does) |
|---|---|---|---|---|---|
| `consoleOrgs.list` | GET `/v1/console/orgs` | `cursor?`, `limit` 100 (switcher) or 25 (`/orgs`) | `items: Org[]`, `nextCursor` | `consoleSession` | 401 `console_session_required` → sign in redirect; 400 `invalid_cursor` → reload from the first page |
| `consoleOrgs.get` | GET `/v1/console/orgs/{orgId}` | `orgId` | `Org` (with your `role`) | `consoleSession` | 404 `not_found` → in shell not found |
| `consoleProjects.list` | GET `/v1/console/orgs/{orgId}/projects` | `orgId`, `cursor?`, `limit` 100 (switcher), 25 (org page), 6 (`/orgs` sections) | `items: Project[]`, `nextCursor` | `consoleSession` | 404 → in shell not found |
| `consoleProjects.get` | GET `/v1/console/project` | `X-Orvano-Project` from the URL | `Project`, any status | `consoleSession` | 404 `project_not_found` → in shell not found, clear `orvano.lastProject` |

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Org switcher | org list, names, statuses | `consoleOrgs.list` pages |
| Org switcher label | current org name | org context: `consoleOrgs.get(orgId)` from the URL; project context: `consoleOrgs.get(project.orgId)` |
| Project switcher | project list, names, statuses | `consoleProjects.list(orgId)` for the current org |
| Project pages | name, status, `purgeAfter`, `purgeFailedAt`, `createdAt` | `consoleProjects.get` with `X-Orvano-Project` = URL `projectId` |
| Purge date and relative times | formatted dates | `Project.purgeAfter` and `createdAt`, formatted with `Intl` in the browser's locale and time zone |
| Role gating (AC-22) | your role for the page | `Org.role` of the org in context (the project's org in project context); projects have no role of their own (spec 0003) |
| Project switch target (AC-14) | the product to keep | first path segment after `/projects/$projectId/` in the current URL, kept only if the target project's `status` is `active` |
| `/orgs` "View all" | whether an org has more projects | `nextCursor` not null on its 6 item page |
| Theme, density, sidebar, landing | preferences and last project | `localStorage` keys in *Data model sketch*; System resolved by `matchMedia('(prefers-color-scheme: dark)')` |
| Sign in redirect | `redirect` | router location (`pathname` plus `search`) when the 401 arrives |
| Error panel and toasts | message, code, request ID | `OrvanoError.message`, `.code`, `.requestId` (`@orvano/js`) |
| Form field errors | per field messages | the form's Zod schema on the client; server problems carry no per field list (spec 0001), so a server 400 shows its `message` in the form alert |
| Page title | `<Page> · <name> · Orvano` | each route's static page label, plus the org or project name from the queries above |
| Sidebar entries | labels, icons, links, `minRole` | `console/src/shell/nav.ts` |
| Dev console cookie (AC-25) | `orvano_console` value | `CONSOLE_DEV_SESSION`, set by the AppHost from one named constant, `"test-console-session"`, whose comment points at the first `consoleSessions` entry in `tests/scenarios/fixtures.yaml`. The duplication is accepted: a mismatch fails loudly (the dev console lands on `/sign-in`), and row 8 deletes both |
| `/orgs` project cards | which 6 projects | the first page of `consoleProjects.list` with `limit` 6, oldest first (the API's order) |

### Key invariants

- The console talks to the API only through `@orvano/console-client`; no `fetch` or handwritten `/v1` URL in `console/src`.
- Components use only tokens; no color literal outside `tokens.css` (AC-1 test). Every new component or variant appears in the catalog and has an entry in `keyboard-scripts.ts`. Every page renders its `h1` through `PageHeading`.
- Nothing in `localStorage` is a secret; the session lives only in the `HttpOnly` cookie.
- The console never contacts another origin: fonts and icons are bundled.
- Sidebar entries exist only in `nav.ts`; product rows add entries, never their own navigation.
- Every dev only piece (catalog, proxy cookie, fixture mode) is absent from the production build and images.
- A route under `_app` never renders product content for a project that is not `active`.

### Security model

The console is same origin with the API (spec 0002), and its session is the `HttpOnly` cookie that spec 0004 defines, so no token is ever readable by console code. The role helpers are presentation only: every permission is enforced by the API through `IConsoleAccess` (spec 0003), and the UI reflects `Org.role` so people are not offered actions they will be refused. The `redirect` parameter is checked as a same origin path, closing the open redirect. The CSP forbids scripts and styles from anywhere but the console's own files, forbids framing (clickjacking), and limits connections to the origin. The dev session exists only in the Vite dev server's proxy, reads an environment variable the AppHost sets only when you pass the fixtures switch, and relies on the API's rule that fixtures are refused outside `Test`. No personal data beyond what the API already returns is shown or stored.

### Configuration required

- `OrvanoDev:Fixtures` (AppHost configuration, default `false`): pass `-- --OrvanoDev:Fixtures=true` to run `api` and `worker` in `Test` with `ORVANO_TEST_FIXTURES` pointing at `tests/scenarios/fixtures.yaml`, and to give the console `CONSOLE_DEV_SESSION`. Row 8 removes it.
- `CONSOLE_DEV_SESSION` (Vite dev server only, never `ORVANO_*` so it stays outside the server's startup validation): when set, `vite.config.ts` appends `orvano_console=<value>` to the `Cookie` header of proxied `/v1/console` requests. Unset in every other setting.

### Critical test scenarios

- Happy path, end to end: compose profile `console`, fixture cookie, `/` → `/orgs` → org switcher to Fixtures → project switcher to Scenarios → overview, keyboard only, real API data; verifies **AC-10**, **AC-11**, **AC-12**, **AC-13**, **AC-14**, **AC-26**.
- Session: with no cookie, `/projects/scenarios0000000000a` lands on `/sign-in?redirect=%2Fprojects%2Fscenarios0000000000a` after exactly one navigation, even though several calls fail with 401 together; `/sign-in?redirect=//evil.example` and `?redirect=https://evil.example` are ignored; verifies **AC-20**.
- Provisioning: create a project through the API, open its URL at once, see "Setting up", then the overview without a reload; verifies **AC-18**.
- Not found: open `/projects/zzzzzzzzzzzzzzzzzzzz` after storing it as the last project; see the in shell message, `orvano.lastProject` removed, `/` then goes to `/orgs`; verifies **AC-12**, **AC-19**.
- Theme: first visit dark with no flash (screenshot of the first paint); switch to System with the emulated scheme light, then flip the emulation, and the page follows live; reload keeps the choice; verifies **AC-2**.
- Density: switch to Comfortable; a button measures 40 px and a table row 44 px; verifies **AC-3**.
- Contrast: every pair in `contrast-pairs.ts` in both themes meets its ratio; verifies **AC-4**.
- Components: for each inventory component, its keyboard script plus axe in dark and light; focus returns to the trigger after Escape; verifies **AC-5**, **AC-6**.
- Reduced motion: with reduced motion emulated, a dialog's computed animation duration is 0; verifies **AC-7**.
- Offline assets: a production shape page load records no request to another host; verifies **AC-8**.
- Catalog: production shape `/dev/components` shows 404, and `grep -r orvano-dev-catalog console/dist` finds nothing; verifies **AC-9**.
- Responsive: at 360 px, `document.documentElement.scrollWidth` equals the viewport on each shell page; the drawer opens, traps focus, closes on Escape; verifies **AC-16**, **AC-17**.
- Errors: stop the api; the org page shows the error panel with code and request ID, Retry works once the api is back; verifies **AC-21**.
- Roles: in the catalog and unit tests, a viewer sees an owner only nav entry hidden and a developer action disabled with its reason announced; verifies **AC-22**.
- Titles and focus: navigating by the switcher sets the title and moves focus to the `h1`; verifies **AC-23**.
- Headers: `curl -I` in the production shape shows every header and cache rule; the end to end run records no `securitypolicyviolation` event; verifies **AC-24**.
- Dev fixtures: with the switch, the dev console shows the Fixtures org; without it, the AppHost's resources and environments are unchanged; verifies **AC-25**.
- Tokens only: the color literal scan over `console/src` passes; verifies **AC-1**.

## Build plan

Tracer Bullet: task 1 is the thin thread (real tokens, one real component, the frame, and the org switcher reading real orgs from the API in dev and in a browser test), and each later task thickens it. This row builds no server code beyond the AppHost switch and the Caddyfile. Row 7's console screens start after task 4; row 8's console session later replaces the dev cookie and the guard's probe.

1. **Thin thread**: add Tailwind v4 (`@tailwindcss/vite`), the `@/*` alias, and `pnpm dlx shadcn@latest init --preset base-nova` in `console/`; `tokens.css` with the dark color tokens and compact density; the fonts; `theme-init.js` and the `index.html` changes; `console-client.ts`; the `_app` layout with the session guard, a bare top bar, and the org switcher (Combobox, first page only) on real data; `/orgs` listing orgs; the AppHost `OrvanoDev:Fixtures` switch and the Vite proxy cookie; Vitest browser mode with Playwright Chromium and axe, one Button test, and a `console` test job in `.github/workflows/ci.yml`. Remove the placeholder health page. Satisfies **AC-8**, **AC-10**, **AC-13**, **AC-20**, **AC-25**.
2. **Design system**: the full token set (both themes, both densities, radius, elevation, motion, focus, layers); `PreferencesProvider` and the account menu's Theme and Density items; the contrast test and the color literal scan; `console/design.md`. Satisfies **AC-1**, **AC-2**, **AC-3**, **AC-4**, **AC-7**.
3. **Components and catalog**: every component in the inventory (shadcn add, restyle to tokens, the focus ring change, the sidebar changes, the custom copy button, code block, and `DataTable`), the form pattern (TanStack Form, Zod, Field, Form alert) and toast helpers, the role helpers (`useOrgRole`, `RoleGate`, `aria-disabled` with reason), `keyboard-scripts.ts` and the shared keyboard and axe test, the `/dev/components` catalog (build once in production mode and confirm the marker string is absent before moving on). Satisfies **AC-5**, **AC-6**, **AC-7**, **AC-9**, **AC-21**, **AC-22**.
4. **Shell complete**: org and project routes, the nav registry, sidebar with rail and drawer and `[`, project switcher with the product keeping rule, switcher paging (the observer, "Load more to search further") and filtering and Deleting ordering, `/orgs` sections, the org page table, status gating with polling, in shell not found, route error panels, `redirectToSignIn()`, landing and `orvano.lastProject`, `PageHeading` with titles and focus on navigation, the 360 px layout. Satisfies **AC-10**, **AC-11**, **AC-12**, **AC-13**, **AC-14**, **AC-15**, **AC-16**, **AC-17**, **AC-18**, **AC-19**, **AC-20**, **AC-21**, **AC-23**.
5. **Production shape and end to end**: Caddyfile headers and cache rules; a `console` profile in `tests/scenarios/compose.yml` that adds the gateway image in front of the scenario `api` (so the scenario SDK jobs are unaffected); Playwright end to end tests in `console/e2e/` for the happy path, session, provisioning, not found, headers and CSP, offline assets, and catalog absence; a CI job that runs them. Satisfies **AC-8**, **AC-9**, **AC-24**, **AC-26**.

## Consequences

**Positive**:
- Every later console row gets tokens, components, form and table patterns, error and empty states, role helpers, and a place in the sidebar, so it builds screens, not infrastructure.
- Accessibility is enforced by tests (contrast, axe, keyboard scripts) rather than by review alone, which backs the WCAG AA rule in `AGENTS.md`.
- The shell is on the real API from day one, so row 7 and row 8 change data, not wiring.
- A strict CSP now is far cheaper than retrofitting one after screens depend on inline code.
- Owning the component code means no upstream breaking change lands without a diff you chose.

**Negative / tradeoffs**:
- Owning the code also means owning upgrades: shadcn changes arrive only when someone runs `shadcn diff` and merges by hand.
- Base UI is younger than Radix, with fewer examples and third party add ons; some shadcn recipes online assume Radix (`asChild` instead of `render`).
- Tailwind class lists make markup long, and every contributor has to learn the token names.
- Dark first risks a weaker light theme; only the contrast test and the catalog's theme switch guard it.
- The density toggle doubles the visual matrix every component must be checked in.
- Browser mode tests need Chromium in CI and run slower than jsdom tests.
- The strict CSP may block a future library that injects styles or scripts; each exception costs a hash and a note.
- Local dev fixtures mean dev runs the API in `Test` (contract checking on), which differs from a real Development run until row 8.
- Preferences stay in one browser and do not follow you to another device.
- The switcher filters only the orgs and projects already loaded; on a very large install you may scroll before you find one.
- `/orgs` makes one project request per org shown (up to 25), acceptable for typical installs, wasteful for very large ones.

**Neutral**:
- New console dependencies (see *Library choices*) and a `components.json` for the shadcn CLI.
- Console tests move from the jsdom plus Testing Library plan in `console/AGENTS.md` to Vitest browser mode.
- ESLint may need a per folder exception for shadcn's generated files (for example `react-refresh/only-export-components` on `buttonVariants`).
- A new compose profile and gateway service in the scenario compose, used only by the console end to end job.

## Follow-up

- [ ] Row 7: fill the switcher footers ("Create org", "Create project"), the status panels' action slots (Retry provisioning, Restore, Retry purge), and add org Settings to `orgNav`, using the form and confirm dialog patterns from this spec.
- [ ] Row 8: replace the session guard's probe with `consoleAccount.get` and add its session codes to the 401 handling; build the real `/sign-in` that honours `redirect`; add name, email, and Sign out to the account menu; remove the AppHost `OrvanoDev:Fixtures` switch and `CONSOLE_DEV_SESSION`.
- [ ] Row 15: add Members to `orgNav`.
- [ ] A command palette (`Ctrl+K`/`⌘K`) to jump to any org, project, or page, once there are enough pages to jump to.
- [ ] A `q` search parameter on `consoleOrgs.list` and `consoleProjects.list`, when installs grow past a few hundred orgs or projects; the switcher then searches the server.
- [ ] Sync theme and density to the console account once account self service exists.
- [ ] Add a per field `errors` list to the `Problem` contract (spec 0001) so server validation can mark fields inline, not only in the form alert.
- [ ] Row 6 (installer): add `Strict-Transport-Security` for HTTPS installs in Caddy.
- [ ] Replace the placeholder ring mark and favicon with a real Orvano logo when one exists.
- [ ] i18n: add a message catalog library if translations are requested.
- [ ] `/sync`: link `console/design.md` from `console/AGENTS.md`; change its test line to Vitest browser mode with Playwright and axe; add the six skills installed for this row (`shadcn`, `tailwind-v4-shadcn`, `accessibility`, `tanstack-form`, `tanstack-table`, `zod`) to `console/AGENTS.md`'s `## Agent skills`; add the shadcn MCP server (`npx shadcn@latest mcp`) to the MCP servers line; record the Base UI MCP and a11y MCP (`ronantakizawa/a11ymcp`) as declined; note `CONSOLE_DEV_SESSION` and the fixtures switch in the console and root context.
- [ ] Connect the shadcn MCP server in your MCP settings (a user config step), for example `claude mcp add shadcn -- npx shadcn@latest mcp`.
- [ ] Verify before the first build: Tailwind v4's Vite plugin supports Vite 8, the Base UI package name the shadcn CLI installs, `base-nova` still being the preset name, and Vitest browser mode's current provider package; these were not checked on the web.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).
