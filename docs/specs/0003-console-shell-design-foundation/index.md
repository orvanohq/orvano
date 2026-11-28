# 0003. Console shell and design foundation

**Date**: 2026-11-27
**Status**: Proposed

## Summary

This spec designs the empty dashboard that every later console page plugs into. The `orvano` host serves the console's static files at `/console`. The design tokens (the named colours, sizes and fonts in `docs/design/design-system/`) are generated into the console's CSS and mapped onto shadcn/ui. The app shell (left rail, top bar, theme menu) works in light and dark, and six base components are built and checked for keyboard use and contrast. Nothing here calls the API yet. Each service page is a stub that a later feature replaces.

## Requirements

**User stories**:
- As a self hoster, I want `/console` to open a working dashboard from the same image that runs the API, so that I don't have another thing to deploy.
- As a developer using Orvano, I want a calm, dense dashboard in light or dark that follows my OS until I pick otherwise, so that it fits how I work.
- As a keyboard or screen reader user, I want every part of the shell and every base component to be reachable and labelled, so that I can use the console without a mouse.
- As a contributor building a later feature, I want the shell, the tokens and the base components ready, so that my page only adds its own content.

**Acceptance criteria** (the contract, each one checkable on its own):
- **AC-1**: Through Caddy, `GET https://<ORVANO_DOMAIN>/console/` returns `200` with the console's HTML, served by `orvano` from files inside its image. `GET /console` redirects to `/console/`. A page address without its trailing slash (`/console/members`) redirects to the slash form. Reloading any console page, including `/console/project/database/?project=prj_x`, renders that page.
- **AC-2**: A path under `/console/` that matches no exported file returns `404` with the console's not found page, which links back to Projects.
- **AC-3**: When the console files are missing, `orvano` still starts, `/healthz`, `/readyz` and every other path behave as before, and any path under `/console` returns `503` as `application/problem+json` with `code` `console_not_built` and a `hint` saying how to build the console or use the dev server. The host logs one warning at startup naming the folder it looked in.
- **AC-4**: Every response under `/console` carries `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY` and `Content-Security-Policy: frame-ancestors 'none'`. Files under `/console/_next/static/` carry `Cache-Control: public, max-age=31536000, immutable`. Every other console file carries `Cache-Control: no-cache`. Responses outside `/console` get none of these headers from this feature.
- **AC-5**: The console's token CSS is generated from `docs/design/design-system/tokens.json` by a script. A check fails the console lint step when the committed file differs from what the script would write. The shadcn/ui variables are mapped onto the tokens as the design system README lists. No hex colour or raw Tailwind palette class (`bg-blue-500`) appears in `console/src` outside the generated file, and a check in the lint step enforces this.
- **AC-6**: A theme menu offers System, Light and Dark. System is the default and follows the OS, including a change made while the page is open. Light and Dark override the OS in both directions. The choice survives a reload, and the first paint already uses the chosen theme (no flash of the other one).
- **AC-7**: IBM Plex Sans and IBM Plex Mono load in weights 400, 500 and 600 from files under `/console`. Loading any console page makes no request outside `/console/*` on the same origin: no font host, no analytics, no API call.
- **AC-8**: Instance pages (`/console/`, `/console/members/`, `/console/audit-log/`, `/console/settings/`) show the instance rail with Projects, Members, Audit log and Instance settings. Project pages (`/console/project/<service>/?project=<id>`) show the service rail with the 12 services in the README order plus a link back to Projects. The rail is 56px wide with Lucide icons at 20px, each item has an accessible name and a tooltip label, and the current item carries `aria-current="page"` and the `brand-subtle` background with `brand-ink` icon colour. The 48px top bar shows the placeholder mark and "Orvano", the instance address, the project id in mono on project pages, and the theme menu. Every page's `<title>` is "{page title} · Orvano" (for example `Projects · Orvano`).
- **AC-9**: A project page opened without a `project` parameter, or with one that is not a valid project id, replaces the address with `/console/` without adding a history entry. Every service rail link carries the current `project` parameter forward.
- **AC-10**: Every rail destination renders a page header (`h1` title and a one line description in `ink-muted`) and an empty state with an `h2` saying the page isn't built yet and one sentence naming the milestone it arrives in.
- **AC-11**: Below 768px the rail is hidden and a labelled menu button in the top bar opens it as a sheet from the left, with each item's label visible. Focus moves into the sheet and stays there, Escape or choosing an item closes it, and focus returns to the menu button.
- **AC-12**: The unlinked page `/console/kit/` shows every base component and state: button (primary, secondary, ghost, destructive; heights 32 and 36; disabled), badge (neutral, brand, success, warning, danger, each with an optional dot), status dot with its word, table (36px rows, sticky header on `surface-subtle`, right aligned numbers, mono IDs, each row's ID is a button that opens a 480px side sheet, and clicking anywhere on the row does the same), select, checkbox, form (labels above fields, help text, a Zod validated field whose error shows inline and is announced, and a destructive confirmation that enables only when the resource name is typed), side sheet (480px, focus kept inside, Escape closes, focus returns), and code block (mono on `surface-subtle`, language label, copy button that announces "Copied"). Every interactive element is reachable by keyboard and shows a visible focus ring in `brand`.
- **AC-13**: An automated scan with axe (WCAG 2.0, 2.1 and 2.2, levels A and AA) reports zero violations on `/console/`, one project stub and `/console/kit/`, in both light and dark. A "Skip to main content" link is the first stop in the tab order. With `prefers-reduced-motion: reduce`, sheets, tooltips and hovers have no transition.
- **AC-14**: `dotnet run --project server/src/Orvano.AppHost` also starts the console dev server as a resource named `console`. Its page opens at `/console/` and an edit to a console file shows without a restart.
- **AC-15**: In CI, both images still build for `linux/amd64` and `linux/arm64` with the console built inside the `orvano` image; the smoke test checks AC-1 (including the `Projects · Orvano` title and one Next route data `.txt` file), AC-2 and AC-4 through Caddy; a console job runs the Playwright and axe tests against the built export; and the lint step runs the token and colour checks from AC-5.

## Decision

**Chosen option**: Option 1: query string project addresses, files served by the host, tokens generated from `tokens.json`

The console is a plain Next.js static export with one page per route, project pages read the project id from `?project=`, `orvano` serves the files with ordinary static file handling, and the console's token CSS is generated from the design system's `tokens.json`.

**Implementation skills**: `shadcn` (`shadcn-ui/ui`, `.claude/skills/shadcn/`) · `nextjs-app-router-patterns` (`wshobson/agents`, `.claude/skills/nextjs-app-router-patterns/`) · `tailwind-4-docs` (`lombiq/tailwind-agent-skills`, `.claude/skills/tailwind-4-docs/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`) · `react-hook-form` (`pproenca/dot-skills`, `.claude/skills/react-hook-form/`) · `zod` (`pproenca/dot-skills`, `.claude/skills/zod/`) · `playwright-best-practices` (`currents-dev/playwright-best-practices-skill`, `.claude/skills/playwright-best-practices/`) · `playwright-cli` (`microsoft/playwright-cli`, `.claude/skills/playwright-cli/`) · `aspire` (`microsoft/aspire-skills`, `.claude/skills/aspire/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`)

`nextjs-app-router-patterns` leans on Server Components, streaming and request time data. None of that exists in a static export. Where it and this spec or `console/AGENTS.md` differ, this spec wins.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

**Data model sketch**: none. This feature stores nothing on the server and adds no migration. The only saved state is the theme choice in the browser's `localStorage` (`orvano-theme`: `system`, `light` or `dark`).

### Files this feature creates or changes

| Path | What it is |
|---|---|
| `server/src/Orvano.Host/ConsoleFiles/` | `ConsoleFilesOptions`, the `/console` pipeline branch (redirects, static files, not found, missing files, headers, caching). The folder and namespace avoid the name `Console`, which would shadow `System.Console` |
| `server/src/Orvano.Host/Program.cs` | Binds `ConsoleFilesOptions` and maps the branch |
| `server/tests/Orvano.IntegrationTests/` | Host tests for AC-1 to AC-4 against a temporary folder of fixture files |
| `server/src/Orvano.AppHost/` | Adds the `console` dev server resource |
| `deploy/orvano.Dockerfile` | A Node build stage that runs the console build, and a copy of `console/out` to `/app/console` in the final stage |
| `deploy/smoke-test.sh` | Checks for `/console/`, a missing page and the headers |
| `console/next.config.ts` | Adds `trailingSlash: true` |
| `console/components.json` | shadcn/ui settings: Radix primitives, the CLI's default style for Radix (check the current name with the `shadcn` skill rather than assuming `new-york`), CSS variables on, aliases under `@/`. `shadcn init` must not add a PostCSS config: the scaffold runs Tailwind through `@tailwindcss/turbopack` |
| `console/scripts/tokens.mjs` | Writes `src/styles/tokens.css` from `tokens.json`; `--check` exits non zero when the file is stale |
| `console/scripts/check-colours.mjs` | Fails on a raw colour in `src/` outside `src/styles/tokens.css`. It flags: hex literals (`#` followed by 3, 4, 6 or 8 hex digits) in `.ts`, `.tsx` and `.css` files; `rgb(`, `rgba(`, `hsl(`, `hsla(`, `oklch(`, `oklab(`, `lab(`, `lch(` and `color-mix(` with literal values; Tailwind arbitrary colours (`bg-[#…]`, `text-[rgb(…)]`); Tailwind palette classes for every default palette name (`slate`, `gray`, `zinc`, `neutral`, `stone`, `red`, `orange`, `amber`, `yellow`, `lime`, `green`, `emerald`, `teal`, `cyan`, `sky`, `blue`, `indigo`, `violet`, `purple`, `fuchsia`, `pink`, `rose`) after any colour utility prefix; and `white` or `black` after a colour utility prefix (`bg-black/50`, `text-white`). Components under `src/components/ui/` are not exempt: after `shadcn add`, change the sheet overlay to `bg-overlay` and any `text-white` to the right token |
| `console/scripts/serve-export.mjs` | A small static server for tests that mounts `out/` at `/console` with the same slash, not found and header rules as the host |
| `console/src/styles/tokens.css` | Generated and committed, with a header that says not to edit it |
| `console/src/app/globals.css` | Tailwind entry, the token import, the dark variant, the shadcn mapping and the theme scale |
| `console/src/app/layout.tsx` | Root layout: fonts, providers (theme, TanStack Query, tooltips), the title template, skip link, top bar, rail slot and `main#main` |
| `console/src/app/icon.svg` | The placeholder mark as favicon, replacing `favicon.ico` |
| `console/src/app/(instance)/` | `page.tsx` (Projects), `members/`, `audit-log/`, `settings/` stubs |
| `console/src/app/project/` | `layout.tsx` with the project guard; one stub folder per service |
| `console/src/app/kit/page.tsx` | The base component page |
| `console/src/app/not-found.tsx` | The not found page |
| `console/src/shell/` | Rail, top bar, drawer, theme menu, nav config, project id hook and link helper |
| `console/src/components/ui/` | shadcn/ui components added by the CLI and adjusted to the tokens |
| `console/src/components/` | `code-block.tsx`, `status-dot.tsx`, `page-header.tsx`, `empty-state.tsx` |
| `console/tests/` and `console/playwright.config.ts` | Playwright and axe tests |
| `.github/workflows/ci.yml` | The `web` job runs the token and colour checks; a new `console-e2e` job runs Playwright |
| `console/README.md` | How to run, build, test, regenerate tokens, and what the kit page is for |

### Host: serving `/console`

The orvano image already has a read only root filesystem and no shell, which suits immutable files baked into the image.

- **Folder.** `ConsoleFilesOptions.RootPath`, bound from the `Console` section with `ValidateOnStart` (it must be a non empty path). Default `console`, resolved against the content root, so the image uses `/app/console`. A relative or absolute path both work.
- **Pipeline.** `app.Map("/console", …)`, which matches `/console` and `/console/…` but not `/consolefoo`, and sets `PathBase` so the default files redirect builds the right address. Nothing outside the branch changes. Inside, in this order:
  1. Register the four AC-4 security headers with `Response.OnStarting`, so every response gets them, redirects and errors included.
  2. If `index.html` is missing from the folder, answer the AC-3 problem with `Cache-Control: no-store`. This comes first, so it covers every path, `/console` included.
  3. A method other than GET or HEAD gets `405` with `Allow: GET, HEAD` and `no-cache`.
  4. `/console` exactly (an empty path in the branch) redirects to `/console/` with 308 and `no-cache`.
  5. Default files, then static files, over one `PhysicalFileProvider` for the folder. The framework's own redirect for a folder path without its slash is a 301. Keep it, and set `no-cache` on it through the redirect's response so browsers don't cache it forever. Static files set `Cache-Control` per AC-4 in `OnPrepareResponse`.
  6. Anything left over gets `404.html` with status `404` and `no-cache`.
- **Missing files.** Checked per request with one file existence lookup on `index.html`, so building the console while the host runs starts working without a restart. The startup warning is logged once.
- **Shared cases.** The table in *HTTP surface* below is the contract for both the host integration tests and the test server `serve-export.mjs`, so the two can't drift apart. Each test suite lists its cases in the same order as that table. The problem body is `type` `about:blank`, `title` "Console not built", `status` 503, `code` `console_not_built`, and `hint` "Build it with `pnpm --filter @orvano/console build` and point `Console:RootPath` at `console/out`, or open the console dev server from the AppHost." The full error catalogue arrives with feature 8; this is its first entry.
- **Content types.** The default ASP.NET mappings cover `.html`, `.js`, `.css`, `.txt` (Next's route data files), `.woff2`, `.svg` and `.ico`. Unknown extensions are not served.
- **Compression.** Caddy already compresses responses (`encode zstd gzip`), so the host adds none.

### Docker image

A new stage in `deploy/orvano.Dockerfile`: `FROM --platform=$BUILDPLATFORM node:26.13.0-slim AS console` (the `.nvmrc` major, pinned to the exact release contributors use; bump it with `.nvmrc`). Node 25 and later no longer ship Corepack, so the stage installs pnpm with `npm install -g pnpm@12.23.0`, the version in the root `package.json` `packageManager` field (keep the two in step). It sets `NEXT_TELEMETRY_DISABLED=1`, copies the root `package.json`, `pnpm-lock.yaml`, `pnpm-workspace.yaml` and every workspace manifest (`console/package.json` and `website/package.json`, so the frozen lockfile matches), runs `pnpm install --frozen-lockfile --ignore-scripts --filter @orvano/console...` (`--ignore-scripts` because the root `prepare` script installs git hooks and the image has no `.git`), copies `console/`, and runs `pnpm --filter @orvano/console build`. The output is the same for every processor type, so it runs once on the builder. The final stage gains `COPY --from=console /src/console/out /app/console` and still has no `RUN` step. `docs/` stays in `.dockerignore`, which works because the generated token file is committed.

### Design foundation

- **Token generation.** `scripts/tokens.mjs` reads `tokens.json` and writes `tokens.css`: light colour values, spacing, radius, shadow and the two font families on `:root`, and the dark colour values under `:root[data-theme="dark"]`. There is no `prefers-color-scheme` block, because next-themes resolves System to `light` or `dark` and writes it into `data-theme`. The type scale (display, h1, h2, h3, body, body strong, small, caption, mono, mono small) is written as Tailwind `--text-*` theme values with their line heights and weights. Scripts: `pnpm --filter @orvano/console tokens` writes, `tokens:check` compares. The console `lint` script runs `tokens:check` and `check-colours` before ESLint.
- **shadcn/ui mapping** (in `globals.css`, from the README): `--background` → `bg`, `--foreground` → `ink`, `--card` and `--popover` → `surface`, their foregrounds → `ink`, `--primary` → `brand`, `--primary-foreground` → `on-brand`, `--secondary` → `surface`, `--secondary-foreground` → `ink`, `--muted` and `--accent` → `surface-subtle`, `--muted-foreground` → `ink-muted`, `--accent-foreground` → `ink`, `--border` → `border` (the same name and meaning), `--input` → `border-strong`, `--ring` → `brand`, `--destructive` → `danger`, `--destructive-foreground` → `on-brand`, `--radius` → `radius-md`. The sidebar variables map to `surface-subtle`, `ink`, `brand-subtle` and `brand-ink`.
- **Radius.** The token values `radius-sm` 4px, `radius-md` 6px and `radius-lg` 10px are set directly in `@theme`, replacing shadcn's scale computed from `--radius`, which would make `sm` 2px.
- **Extra colours.** `brand-hover`, `brand-subtle`, `brand-ink`, `success`, `warning`, `danger`, their `-subtle` forms, `surface-subtle`, `border-strong`, `ink-subtle` and `overlay` become Tailwind colours (`bg-success-subtle`, `text-brand-ink`), so components never need a raw value.
- **Mapping form.** The shadcn and extra colours sit in `@theme inline`, so each utility points at the CSS variable and the dark switch takes effect live.
- **shadcn sizes.** shadcn components use `text-sm` and `text-xs`. `@theme` sets `--text-sm` to 14px/20px (body), `--text-xs` to 12px/16px (caption) and adds `--text-small` at 13px/18px, so generated components land on the type scale without rewriting classes. `rounded-*` uses the token radii above. `shadow-*` classes are removed from generated components except on floating ones (menus, tooltips, sheets), which use `shadow-popover`.
- **Contrast on subtle backgrounds.** Measured from `tokens.json`: `danger` on `danger-subtle` is 4.23:1 in light, and `brand` on `brand-subtle` is 4.39:1 in dark, both below AA. So text on any `-subtle` background uses `brand-ink` (brand) or `ink` (success, warning, danger, neutral), never the strong colour. The strong status colour goes on the dot and the icon only. The word next to it is still always there. Error text under a field is `danger` on `surface` (4.83:1 light, 5.61:1 dark).
- **Dark variant.** `@custom-variant dark (&:where([data-theme=dark], [data-theme=dark] *));`
- **Destructive text.** The README says white text on `danger`. In dark, white on `#F26363` is about 3.1 to 1, below AA for 14px text. The console uses `on-brand` instead, which is white in light (4.8 to 1 on `#DC2626`) and `#0B1020` in dark (above 6 to 1). This uses existing values and invents no hue. Follow up: update the README.
- **Fonts.** `@fontsource/ibm-plex-sans` and `@fontsource/ibm-plex-mono`, imported in the root layout as `@fontsource/ibm-plex-sans/latin-400.css`, `latin-500.css`, `latin-600.css` and the same three for mono. Fontsource registers the families as `IBM Plex Sans` and `IBM Plex Mono`, which match the `font-sans` and `font-mono` strings in `tokens.json`. The export bundles the `.woff2` files under `/console/_next/static/`.
- **Favicon.** Delete the scaffold's `src/app/favicon.ico` and add `src/app/icon.svg`: the placeholder mark (a `brand` square with `radius-sm` corners) at 32 by 32. Next writes the `<link rel="icon">` with the `/console` base path, so the browser never asks for `/favicon.ico` at the origin root.
- **Theme.** next-themes (version 0.4.6 or later, for React 19) with `attribute="data-theme"`, `defaultTheme="system"`, `enableSystem`, `disableTransitionOnChange`, storage key `orvano-theme`. `<html>` carries `suppressHydrationWarning`. The theme menu is a shadcn dropdown with three radio items, its trigger shows the sun or moon icon for the resolved theme, and its accessible name is "Theme".
- **Motion.** 120 to 160ms ease out on hovers, tooltips and sheets, all turned off under `prefers-reduced-motion: reduce` by one rule in `globals.css`.
- **Providers.** TanStack Query's `QueryClientProvider` with a client made once per page load and no queries. The devtools package is not added.

### Routes

`trailingSlash: true`, so every page exports as `<route>/index.html` and Next's own links use the slash form.

| Route | Rail | Page title | Arrives with |
|---|---|---|---|
| Route | Rail | Page title | Description line | Milestone shown | (built by) |
|---|---|---|---|---|---|
| `/` | instance | Projects | The projects on this instance. Dev and production are separate projects. | M1 | feature 14 |
| `/members/` | instance | Members | People who can sign in to this instance, and their roles. | M1 | feature 13 |
| `/audit-log/` | instance | Audit log | Every change made on this instance, by people, keys and agents. | M1 | feature 16 |
| `/settings/` | instance | Instance settings | Email, domains, GitHub, backups and updates for this instance. | M2 | feature 19 |
| `/project/overview/` | service | Overview | How this project is doing, and how to connect to it. | M9 | feature 86 |
| `/project/auth/` | service | Auth | Users, sign in methods and security settings. | M2 | features 18 to 28 |
| `/project/database/` | service | Database | Tables, schema, migrations and permissions. | M3 | features 30 to 35 |
| `/project/storage/` | service | Storage | Buckets and files. | M4 | feature 48 |
| `/project/realtime/` | service | Realtime | Live events and connections. | M4 | feature 53 |
| `/project/functions/` | service | Functions | Deployments, runs, triggers and secrets. | M5 | feature 61 |
| `/project/sites/` | service | Sites | Deployments, previews and domains. | M8 | feature 85 |
| `/project/webhooks/` | service | Webhooks | Endpoints and delivery history. | M5 | features 62 and 63 |
| `/project/email/` | service | Email | Templates, topics and delivery history. | M5 | features 64 and 65 |
| `/project/ai/` | service | AI | Connected agents, tokens and approvals. | M7 | feature 79 |
| `/project/logs/` | service | Logs | Search every log this project produces. | M9 | feature 87 |
| `/project/settings/` | service | Settings | Platforms, API keys, members and project export. | M1 | feature 14 |
| `/kit/` | none | Base components | Every base component and state, for testing and reference. | none | this feature |
| not found | none | Page not found | none | none | this feature |

The "built by" column is for contributors and is not shown. The stub sentence shows the milestone only ("This page arrives in milestone M3.").

- **Layout ownership.** The root layout owns the skip link, the top bar, the rail slot and `<main id="main">`, so every page (the kit and not found pages included) has the same skip target. The rail slot picks the rail from `usePathname()`, which is known at build time for every exported page: instance routes get the instance rail, `/project/…` gets the service rail, and `/kit/` and not found get none. The first paint already shows the right rail. Instance pages never read `project`. Project pages need it and send you to `/` without it. In practice that is the "by the project param" rule, without a flash. `project/layout.tsx` adds only the project guard around its children.
- **Nav config.** One typed list in `src/shell/nav.ts` holds, per item: label, page title, Lucide icon (Projects `layout-grid`, Members `users`, Audit log `list`, Instance settings `settings`, and the README's service icons with Overview `layout-dashboard`), route, description line and milestone, exactly as in the table above. Rails, drawer, stub pages and page titles all read it. A later feature changes its own entry only.
- **Page titles.** Each page exports `metadata.title` from its `nav.ts` entry, and the root layout sets the template `%s · Orvano`. The kit page uses "Base components" and not found uses "Page not found".
- **Active item.** An item is current when `usePathname()` equals its route exactly, with the trailing slash. `/` is current only on `/`, so Projects is never highlighted on project pages. The service rail's back link to Projects is never current.
- **Project id.** `useProjectId()` returns `{ status: "loading" }`, `{ status: "valid", id }` or `{ status: "invalid" }`. It reads `project` with `useSearchParams().get("project")`, so when the parameter appears twice the first one wins, and checks it against `^prj_[0-9A-Za-z]{1,64}$`. Features 7 and 14 own the real id format and tighten this pattern. Suspense wraps only the parts that read the id (the guard, the project id in the top bar, and the service rail's links), never the whole layout, so the prerendered HTML still contains the rail and top bar. While loading, the rail links render without the parameter and the top bar shows no id. `projectHref(route, id)` builds every service link once the id is valid.
- **Project guard.** It renders nothing in place of the page content until the status is `valid`. On `invalid` it calls `router.replace("/")`. So a project page never shows content for a missing or invalid id, not even for one frame.
- **Not found.** `not-found.tsx` renders in the root layout with no rail: an `h1` "Page not found", one sentence ("Check the address, or go back to your projects."), and a link to Projects. It exports as `404.html`.

### Shell layout

- **Top bar** (48px, `surface`, bottom `border`): menu button (below 768px only), the 10px `brand` square with "Orvano" in Plex Sans 600 as a link to `/`, a `/` separator in `border-strong`, the instance address in `mono-small` `ink-muted`, and on project pages a second separator and the project id in `mono-small`. Then a spacer and the theme menu. The approvals badge, the `⌘K` trigger and the user menu are not rendered. Features 75, 88 and 12 add them in that order of the right hand group: approvals, palette, theme, user.
- **Rail** (56px, `surface-subtle`, right `border`): 40px square items with a 20px icon, `radius-md`, a tooltip with the label on hover and focus (side right), `aria-label` set to the label, and `aria-current="page"` on the match. On project pages a Projects item (`arrow-left`) sits above a separator.
- **Main** (`id="main"`, `tabIndex={-1}` as the skip link target): `space-6` padding. Stub pages, the kit and settings pages cap at 1280px. Full width pages arrive later.
- **Skip link**: visually hidden until focused, first in the DOM inside `<body>`.
- **Drawer**: shadcn `Sheet` from the left, 240px wide, same items with icon and label in a column, closes on route change. The menu button's accessible name is "Open navigation" and it carries `aria-expanded`. The sheet's title is "Navigation" (visible) with a visually hidden description ("Go to a page in this instance" or "in this project").
- **Tooltips**: shadcn `Tooltip` with a 300ms open delay, opened on focus too, and closed by Escape.
- **Sheet descriptions**: every sheet and dialog has a title and a description, visually hidden where the design shows none, so Radix gets both and screen readers announce them.
- **Stub page**: `PageHeader` (h1 and description) then `EmptyState` with `h2` "{Title} isn't built yet" and "This page arrives in milestone {milestone}." The design system's one primary action is left out because there is nothing to act on yet. Each real page brings its own.

### Base components

Added with the shadcn CLI and adjusted to the tokens: `button`, `badge`, `table`, `field` and `input`, `label`, `select`, `checkbox`, `sheet`, `dropdown-menu`, `tooltip`, `separator`. Our own: `code-block`, `status-dot`, `page-header`, `empty-state`.

- **Button**: variants `default` (brand), `secondary` (`surface` with `border-strong`), `ghost`, `destructive`; sizes `sm` 32px and `default` 36px; `radius-md`; focus ring 2px `ring` with a 2px offset.
- **Badge**: `-subtle` background, optional 6px dot in the strong colour, `radius-sm`, caption type. Text follows the contrast rule above: `brand-ink` for brand, `ink` for success, warning and danger, and `ink-muted` on `surface-subtle` for neutral.
- **Status dot**: 8px circle in the status colour, always followed by its word in `ink` in the same element.
- **Table**: 36px rows, caption type headers on `surface-subtle`, sticky header, `border` dividers, `tabular-nums` and right alignment for numbers, mono IDs. A row's ID cell holds a real `<button>` (styled as mono text) that opens the row's sheet, so keyboard and screen reader users get a normal button inside normal table semantics. A click anywhere else on the row also opens it, as a mouse shortcut. The row itself gets no role. When the table can scroll, its wrapper has `tabIndex={0}`, a `max-height`, and an `aria-label` naming the table, so keyboard users can scroll it.
- **Form**: React Hook Form with the Zod resolver on shadcn's `Field` components. Labels above, 36px controls, `space-4` between fields, help text in small `ink-muted`, the error linked with `aria-describedby` and announced through `aria-live="polite"`. The destructive confirmation compares the typed text with the resource name before enabling the danger button.
- **Side sheet**: shadcn `Sheet` from the right, 480px (full width below 480px), title and description wired for screen readers.
- **Code block**: `<pre>` in mono on `surface-subtle`, `radius-md`, caption language label top left, ghost copy button top right with the label "Copy code". The copy uses `navigator.clipboard.writeText`, and a polite live region says "Copied" for two seconds. With no clipboard access the button says "Copy failed".
- **Kit page**: one section per component with an `h2` per section, the theme menu in the top bar to switch themes, and no link from either rail.

### Kit sample data

| Section | Content |
|---|---|
| Buttons | "Create bucket" in each variant at both sizes, plus a disabled primary. The kit is the one page with more than one primary |
| Badges | Neutral "Draft", brand "Selected", success "Deployed", warning "Pending approval", danger "Failed", each shown with and without a dot |
| Status dots | Success "Healthy", warning "Degraded", danger "Down" |
| Table | Label "Recent deliveries", columns ID (mono), Endpoint, Status (badge), Latency (right aligned, `ms`), Size (right aligned, `KB`). 25 rows built in code from a seed list (IDs `dlv_01JF…` with a counter suffix, three endpoints, mixed statuses, latencies 12 to 480 ms), in a wrapper with a 320px max height so the sticky header shows. The sheet shows the row's ID as its title, its fields in a list, and a code block of the row as JSON |
| Form | Field "Bucket name": text, required, Zod rule `/^[a-z0-9-]{3,63}$/`, help "Lowercase letters, numbers and dashes, 3 to 63 characters.", error "Use 3 to 63 lowercase letters, numbers or dashes." Select "Region" with `local` and `s3`. Checkbox "Public bucket" with help "Anyone with the link can read files." Primary "Create bucket" runs validation and shows the values in a code block on success. A separate block "Delete bucket" asks you to type `recipes-dev` to enable the destructive "Delete bucket" button |
| Side sheet | Opened from the table, contents above |
| Code block | Language `sql`: `select id, title from recipes where author_id = $1 limit 20;` and language `bash`: `orvano db migrate up` |

### Contributor dev server

The AppHost adds the console with Aspire's JavaScript hosting (`Aspire.Hosting.JavaScript`): app name `console`, folder `../../../console`, pnpm as the package manager, the `dev` script, an HTTP endpoint bound to the `PORT` the dev server reads, and an external URL of `/console/`. It references nothing, because the console makes no API calls yet.

### Tests

- **Host integration tests** (`Orvano.IntegrationTests`, the existing host fixture): a temporary folder with `index.html`, `404.html`, `members/index.html` and `_next/static/app.js`, pointed to by `Console__RootPath`. They cover the redirects, the two cache headers, the four security headers, the `404`, `/healthz` having no console headers, and, with an empty folder, the `503` problem with its `code`.
- **Playwright** (`@playwright/test` and `@axe-core/playwright`, Chromium only for now): `webServer` builds nothing and runs `node scripts/serve-export.mjs --port 3100`, which serves `console/out` (resolved from the script's own folder) at `/console`. `baseURL` is `http://localhost:3100/console/`. CI builds first. Specs:
  - `shell.spec.ts`: both rails, active item, project guard (including no extra history entry), links carry the param, stub content, page titles, not found.
  - `navigation.spec.ts`: sets a marker on `window`, clicks through five rail items, and asserts the marker survives (no full page reload) and no request returned 4xx. This catches a missing or misserved Next route data file, which would otherwise fall back silently to full reloads.
  - `theme.spec.ts`: emulated `colorScheme` set per test; System follows dark and light, overrides work both ways, the choice survives reload, and `data-theme` is present on `<html>` before the first paint.
  - `drawer.spec.ts`: viewport 375 by 812; opens, focus stays inside, Escape closes, focus returns to the menu button.
  - `kit.spec.ts`: each component by keyboard, the copy announcement (the context is granted `clipboard-read` and `clipboard-write`; localhost counts as a secure origin), the form error, the destructive gate.
  - `a11y.spec.ts`: axe on the three pages in both themes with tags `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa`, plus the skip link and the reduced motion rule.
  - `network.spec.ts`: records every request on the three pages and fails on any outside `/console/`.
- **CI**: the `web` job gains the token and colour checks through `lint`. A new `console-e2e` job uses the same Node and pnpm setup and cache as `web`, installs dependencies, builds the console, runs `playwright install --with-deps chromium` and runs the tests, uploading the report on failure. The `images` and `smoke` jobs need no new steps beyond the Dockerfile and smoke script changes.

### API surface

This feature adds no API endpoint. Its HTTP surface is the static branch. Every row also carries the four AC-4 security headers. Rows are checked in this order:

| Case | Method | Response | `Cache-Control` |
|---|---|---|---|
| Any path, `index.html` missing | any | 503 problem+json, `code` `console_not_built` | `no-store` |
| Any path | not GET or HEAD | 405, `Allow: GET, HEAD` | `no-cache` |
| `/console` | GET, HEAD | 308 to `/console/` | `no-cache` |
| `/console/{path}` without a trailing slash, where `{path}/index.html` exists | GET, HEAD | 301 to `/console/{path}/` | `no-cache` |
| `/console/_next/static/{file}` | GET, HEAD | 200, the file | `public, max-age=31536000, immutable` |
| `/console/{path}` matching any other file (HTML, route data `.txt`, icon) | GET, HEAD | 200, the file | `no-cache` |
| Anything else under `/console/` | GET, HEAD | 404 with `404.html` | `no-cache` |
| `/consolefoo`, `/healthz`, any path outside the branch | any | unchanged, no console headers | unchanged |

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Serve a console file | The file bytes | `ConsoleFilesOptions.RootPath` (config `Console:RootPath`, default `console` under the content root; the image puts the export at `/app/console`) |
| Serve a console file | `Cache-Control` value | Derived from the path: under `_next/static/` or not |
| Missing files answer | `code` and `hint` | Constants in `ConsoleFiles/`, wording in this spec |
| Render a rail item | Label, icon, route, description, milestone | `src/shell/nav.ts`, milestones from `docs/scope/index.md` |
| Mark the current item | Active item | `usePathname()` equal to the nav route, slash included |
| Any page | `<title>` | The page's `nav.ts` title (or the fixed kit and not found titles) through the root layout template `%s · Orvano` |
| Pick the rail | Instance, service or none | `usePathname()` in the root layout's rail slot, known at build time |
| Top bar | Instance address | `window.location.host`, read after mount (empty placeholder of fixed width before, so hydration matches) |
| Top bar, project links | Project id | The `project` query parameter through `useProjectId()` |
| Theme | Light or dark | next-themes: `localStorage` key `orvano-theme`, else `prefers-color-scheme` |
| Code block copy | Copied text | The block's own `code` prop |
| Destructive confirmation | Name to type | A prop on the confirmation component (the kit passes `recipes-dev`) |

### Key invariants

- The console never calls anything but its own files in this feature. Data arrives later only through the TypeScript SDK and TanStack Query.
- `tokens.css` always equals what `scripts/tokens.mjs` writes from `tokens.json`. Nobody edits it by hand.
- No colour literal or raw palette class exists in `console/src` outside `tokens.css`.
- The `/console` branch never changes a response outside `/console`.
- A project page never renders with an invalid project id.
- One primary button per view (the kit page is the exception, because it exists to show every variant).

### Security model

There is no sign in yet (feature 12), so every console page is public. That's safe here because the pages hold no data and make no calls. The host serves only files under `RootPath` through `PhysicalFileProvider`, which refuses paths that climb out of the folder. Framing is blocked by both `X-Frame-Options` and `frame-ancestors`, and `nosniff` stops content type guessing. A strict script policy (CSP) is left to the security review (feature 99), because a static export inlines its bootstrap scripts and next-themes adds one, so a nonce is not possible and hashes change with every Next release. Nothing is regulated, and nothing here needs an audit log.

### Configuration required

- `Console__RootPath` (config `Console:RootPath`): optional. The folder that holds the exported console. The default `console` fits the image. Point it at `console/out` to serve a local build from `dotnet run`.

### Critical test scenarios

- Happy path: the Compose stack serves `/console/` through Caddy, a reload on a project stub renders it, and both rails show the right items, verifies **AC-1**, **AC-8**, **AC-10**, **AC-15**
- Failure case: the host runs with an empty console folder and answers `503 console_not_built` under `/console` while `/readyz` stays `200`, verifies **AC-3**
- Failure case: a project page with `?project=bad id` lands on `/console/` with no extra history entry, verifies **AC-9**
- Theme: emulated OS dark with the Light choice renders light at first paint after a reload, verifies **AC-6**
- Accessibility: axe reports zero violations on the three pages in both themes, and every kit component works by keyboard, verifies **AC-12**, **AC-13**
- Privacy: the network spec sees no request outside `/console/`, verifies **AC-7**
- Narrow screen: the drawer keeps focus and returns it to the menu button, verifies **AC-11**

## Build plan

Ordered as a tracer bullet: the first slice proves the whole path (image, host, Caddy, browser) with the bare scaffold page, then each slice thickens one strand.

1. **The thread.** `ConsoleFilesOptions` and the `/console` branch with every case in the HTTP surface table. Set `trailingSlash: true`. Look at the exported `out/` once and record in `console/README.md` how Next names the route data `.txt` files, then make sure the host serves one. Add the Node stage and the copy to `deploy/orvano.Dockerfile`. Extend `deploy/smoke-test.sh` and add the host integration tests. The page is still the scaffold's (set its title to `Projects · Orvano` so the smoke check holds from the start), satisfies **AC-1**, **AC-2**, **AC-3**, **AC-4**, **AC-15**
2. **Design foundation.** `scripts/tokens.mjs` with `--check`, `scripts/check-colours.mjs`, the generated `tokens.css`, the `globals.css` mapping (including the type scale overrides and the subtle background contrast rule), shadcn init (`components.json`), Fontsource fonts, the favicon, next-themes with the theme menu, the TanStack Query provider, and the reduced motion rule. Wire both checks into `lint`, satisfies **AC-5**, **AC-6**, **AC-7**
3. **Shell and routes.** `nav.ts`, the root layout with the rail slot, the project guard layout, rail, top bar, drawer, skip link, page titles, `useProjectId` and `projectHref`, every stub page, and the not found page, satisfies **AC-8**, **AC-9**, **AC-10**, **AC-11**
4. **Base components and kit.** Add and adjust the shadcn components, build `code-block`, `status-dot`, `page-header` and `empty-state`, and build `/kit/` with every state and the sample data above, satisfies **AC-12**
5. **Tests, CI and dev loop.** Playwright and axe with `serve-export.mjs` and all seven specs, the `console-e2e` CI job, the AppHost `console` resource, `console/README.md`, satisfies **AC-13**, **AC-14**, **AC-15**

## Consequences

**Positive**:
- Every later page starts from a working shell, real tokens and tested base components, and only adds its own content.
- One image still runs everything, and the console works on an offline instance with no outside requests.
- The design system stays the one source of colour. A change to `tokens.json` reaches the console by running one script, and CI catches forgetting it.
- Accessibility is checked by machine from the first page, so later pages inherit a baseline that passes axe at AA instead of a debt. Axe doesn't check every AA rule (it skips the contrast of input borders, for one), so "passes axe" is the claim, not "meets AA".

**Negative / tradeoffs**:
- Project addresses carry `?project=prj_…` instead of a path segment, which looks less tidy and must be passed forward on every project link (the helper does this, but a hand written link would drop it).
- The orvano image build now needs Node and the pnpm store, which adds build time and a second toolchain to the Dockerfile.
- The console's dark destructive button differs from the README's "white text" until the README is updated.
- No script policy yet. Until feature 99, an injected script would not be blocked by the browser. Nothing on the page is worth stealing yet, but that changes at feature 12.
- Stub pages ship to self hosters in early versions. They are honest but empty.
- Playwright runs Chromium only, so Firefox and Safari bugs can slip through until the completeness pass (feature 89).
- Input and control borders use `border-strong`, which measures 1.52:1 on `surface` in light and 1.75:1 in dark, below the 3:1 that WCAG asks of control edges (and that the README claims). This feature uses the token as designed. Fixing it is a design system change (follow up).
- Status badges show the strong colour only on the dot, not the text, which is a small departure from the README, made for contrast.
- An open console tab after an upgrade asks for old chunk files that no longer exist and gets a 404, so it needs a reload. Acceptable for a self hosted dashboard.

**Neutral**:
- `useSearchParams` forces a Suspense boundary around anything that reads the project id. Later pages follow the same pattern.
- The page `/console/kit/` is public. It holds sample data only.
- The AGENTS.md "docs page with a compiling sample" rule does not apply: the console is not a public API, and the docs site arrives with feature 6. Contributor docs live in `console/README.md`.

## Follow-up

- [ ] Update `docs/design/design-system/README.md` so destructive buttons use `on-brand` text, not white (the dark value fails AA), and consider an `on-danger` token in `tokens.json`.
- [ ] Design system: raise `border-strong` to at least 3:1 on `surface` in both themes, and update the README's badge rule (text on `-subtle` uses `brand-ink` or `ink`; `danger` on `danger-subtle` is 4.23:1 and `brand` on `brand-subtle` in dark is 4.39:1). Then regenerate `tokens.css`.
- [ ] Features 7 and 14: replace the loose project id pattern in `useProjectId` with the real `ProjectId` format.
- [ ] Feature 99: design a hash based `Content-Security-Policy` for `/console`.
- [ ] Feature 89: add Firefox and WebKit to the Playwright projects.
- [ ] The newly installed skills (`nextjs-app-router-patterns`, `tailwind-4-docs`, `tanstack-query`, `react-hook-form`, `zod`, `playwright-best-practices`) are not yet listed in `console/AGENTS.md` `## Agent skills`, and the Next.js DevTools MCP server (`vercel/next-devtools-mcp`) belongs on its `MCP servers:` line as recommended. These are console only conventions, so the nested file is their home, not root AGENTS.md.
- [ ] `console/AGENTS.md` still says project addresses need a static export strategy. After this ships, it should point at the query string rule and `useProjectId`.
