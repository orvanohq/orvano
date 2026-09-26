# 0005. Rationale: console design system and shell

Decision record for [index.md](index.md). `/develop` builds from the index; this file explains why.

## Context

> ⚠️ Premise note: this row decides two things that could have been separate specs, the design system and the shell. They stay together because the shell is the design system's first and largest consumer, and the scope row's "done when" needs both at once. The shell also assumes a console session that only row 8 delivers. Until then it runs on the fixture session through a dev only switch, stated here as an explicit assumption and removed by row 8 (see the index's Follow-up).

The console today is one placeholder page with a bare stylesheet. Every product in the plan (auth, databases, storage, functions, messaging, webhooks, jobs, backups) ships a console screen in the same version as its API, so from v0.1 on, roughly one new screen area arrives per version. Without a shared visual language and frame, each row would pick its own spacing, colors, form handling, empty and error states, and navigation, and the console would read as a dozen tools stitched together. Row 7's screens (sign up, orgs, projects, keys, platforms) are already waiting on this decision.

The forces are specific. Orvano is self hosted, so the console must work offline from the public internet (no fonts or scripts from a CDN) and should carry strict security headers, because an admin console is a high value target. `AGENTS.md` requires WCAG AA, strict TypeScript, named exports, kebab case files, and tests in CI, so the choice must be testable for accessibility, not just claim it. The product will grow data heavy screens (users, tables, logs, jobs), so density matters. The team is small and the whole codebase leans on the TanStack family, so fewer and more familiar tools win. Developers will reach for the console at odd hours, sometimes from a phone.

The shell has its own constraints. Project IDs are unique across an install (spec 0003), orgs and projects move through lifecycle states (provisioning, failed, deleting), the list endpoints are cursor paged, the SDK client binds one project per instance, and roles live on the org. The console is same origin with the API behind Caddy in production and behind the Vite proxy in dev, and the only console session before row 8 is the `Test` environment's fixture cookie.

Not deciding means row 7 invents the system under deadline, and every row after it inherits whatever row 7 improvised.

## Options considered

### Option 1: shadcn/ui on Base UI, Tailwind CSS v4 tokens (chosen)

Copy components from the shadcn registry in its Base UI flavor into `src/components/ui/`, restyle them through CSS variable tokens mapped into Tailwind v4's `@theme`, and build the shell from the same parts (shadcn's sidebar, combobox, dropdown menu).

**Pros**:
- Base UI supplies the hard accessibility work (focus traps, roving focus, combobox and menu keyboard models, toasts) and is actively developed by the MUI team, with a stable v1.
- Code is owned, not imported: every component can be changed to fit Orvano's tokens, focus ring, and density without fighting a library's theme API.
- Tailwind v4's CSS first config makes tokens plain CSS variables, so theme and density are attribute swaps with no runtime cost.
- The largest pool of examples, blocks, and contributor familiarity for React dashboards; the official `shadcn` skill and MCP server are installed or available.
- Base UI's toast renders no `<style>` element, so a strict CSP holds.

**Cons**:
- Owned code means manual upgrades through `shadcn diff`.
- Base UI has fewer community examples than Radix; many online recipes need `asChild` translated to `render`.
- Tailwind markup is verbose, and the defaults (translucent focus ring, cookie backed sidebar) need changes to meet this spec.

### Option 2: shadcn/ui on Radix

The same approach on Radix primitives, the original shadcn pairing.

**Pros**:
- The most documented combination; nearly every shadcn example assumes it.
- Radix primitives are mature and widely deployed.

**Cons**:
- Radix development has slowed since its handover, and shadcn's own direction now includes Base UI as a first class base; starting a multi year console on the slower base invites a migration later (the `migrate-radix-to-base` skill exists for that reason).
- Its toast pairing is `sonner`, which injects styles and conflicts with a strict `style-src`.

### Option 3: React Aria Components with own styling

Adobe's headless components, styled with Tailwind through the same tokens.

**Pros**:
- The strongest accessibility and internationalisation story available, including grids, tables, and date pickers built for screen readers.
- Very consistent keyboard and focus behavior across every component.

**Cons**:
- No copyable styled component set of the same breadth; every component's markup and states are ours to write, which is several times the work for row 5.
- More verbose APIs and a smaller pool of dashboard examples.
- Its data grid strengths matter most for row 18's table editor, which will pick its own grid anyway.

### Option 4: Hand rolled components on CSS Modules

Plain CSS Modules with CSS variables and components written from scratch on native elements.

**Pros**:
- Zero dependencies and full control; CSS stays in plain `.css` files.
- Nothing to upgrade.

**Cons**:
- Every combobox, menu, focus trap, and toast is ours to get right and keep right; these are exactly where accessibility bugs live.
- No component recipes, no ecosystem, slowest path to row 7's screens.

## Rationale

Option 1 wins because it puts the accessibility risk on a maintained primitive library while keeping every visual decision in Orvano's hands. The force that dominates is the combination of WCAG AA as a hard rule and a small team: hand rolling (Option 4) puts the hardest accessibility work on that team, and React Aria (Option 3) solves accessibility beautifully but leaves every component's markup to write. shadcn on a headless base gives working, accessible components on day one that are still plain files in the repo, which is what lets this spec change the focus ring, the sidebar's persistence, and the density model freely.

Between the two shadcn bases, Base UI fits the longer horizon. It is where shadcn itself is heading, it is under active development, and its toast leaves the strict CSP intact, where Radix pairs with `sonner`, which injects styles. The cost is a thinner pool of examples, which the installed `shadcn` skill (it documents the Base UI differences) and the shadcn MCP server offset.

The shell choices follow from the data. Project IDs are unique across the install, so flat `/projects/$projectId` URLs keep links short and survive a project moving orgs. The org list endpoint is also the cheapest proof of a session, so the guard reuses it instead of waiting for row 8's `consoleAccount.get`. Wiring the shell to the real API now (through a fixture cookie in dev) keeps this a true Tracer Bullet thread: row 8 changes how the cookie is obtained, not how the shell loads data. Dark first with a system option, compact by default, and a sidebar that only lists shipped products all serve the people who will use the console most: developers working in dense, data heavy views for long stretches.

### Smaller decisions and their runners up

| Decision | Pick | Why | Runner up |
|---|---|---|---|
| Design source | Proposed direction (no design yet) | No mocks or brand exist | Screenshots of consoles you like |
| Feel | Dark first, developer tool | The audience lives in dark editors and terminals | Calm neutral, light first |
| Accent | Indigo violet (hue 280) | Distinct from Supabase green and Appwrite pink; holds AA in both themes | Teal |
| Type | Inter + JetBrains Mono, self hosted | UI legibility, tabular numbers, open licenses, no CDN | Geist + Geist Mono |
| Density | Toggle, compact by default, one token set | Data heavy screens, comfort on demand, every component follows one attribute | Compact only |
| Default theme | Dark with Light, Dark, System | Matches dark first; System is one click away | Follow the OS |
| Preference storage | `localStorage`, applied before paint | No backend change, no flash | On the console account |
| Theme bootstrap | External `theme-init.js` in `<head>` | Works under `script-src 'self'` without a hash | Inline script with a CSP hash |
| Color space | OKLCH | Lightness is perceptual, so AA fixes are one number | HSL |
| Focus ring | Solid 2 px `--ring`, offset 2 px | Meets 3:1 (WCAG 1.4.11, 2.4.13) | shadcn's translucent 3 px ring |
| Layout | Sidebar + top bar | Scales past a dozen products | Icon rail + panel |
| URLs | Flat `/projects/$projectId` | Unique IDs, short links, survive moves | Nested under the org |
| Unbuilt products | Hidden until shipped | Nothing looks broken | Shown as "Soon" |
| Switcher | Breadcrumb comboboxes | One click per level, searchable | One combined menu |
| Shell data | Real API, fixture cookie in dev | True end to end thread | Placeholder data |
| Dev session | AppHost fixtures switch + Vite proxy cookie | Nothing reaches production; no browser route to remove | Dev page that sets the cookie |
| No session | Redirect to `/sign-in?redirect=` | Row 8 fills the page, not the flow | Inline signed out panel |
| Landing | Last used project, else `/orgs` | Returns you where you were | Always `/orgs` |
| Min width | 360 px, desktop first | WCAG reflow, phone use during incidents | Tablet and up |
| Row 5 pages | Frame pages only | Row 7 owns org and project management | Frame + create dialogs |
| Org pick target | `/orgs/$orgId` | Gives row 7 an org page to extend | The org's first project |
| Components | What rows 7 and 8 need | Designed once, up front | Only what the shell uses |
| Icons | Lucide | shadcn's default, consistent, tree shaken | Phosphor |
| Forms | TanStack Form + Zod 4 | Same family as Router and Query, typed fields | React Hook Form + Zod |
| Tables | TanStack Table, headless | Cursor paging, same family, our styling | Plain `<table>` |
| Toast | Base UI toast | shadcn's pick for Base UI; no style injection under CSP | `sonner` |
| Errors | Inline for forms, toast for actions, panel for pages | Error text stays where you fix it; toasts only for transient outcomes | Toast for everything |
| Error toasts | Stay until dismissed | WCAG 2.2.1, errors must not vanish | Auto dismiss |
| Catalog | Dev only route `/dev/components` | No second build to maintain | Storybook |
| Catalog URL | `/dev/components` | A leading `_` makes a TanStack file route pathless | `/_dev/components` |
| `design.md` | `console/design.md` | Beside the code it governs | `docs/design.md` |
| Tests | Vitest browser mode + Playwright Chromium | Real focus, layout, and CSS | jsdom + Testing Library |
| Accessibility checks | axe in every component and page test, plus a contrast test | Catches roles, labels, and color | Contrast test only |
| End to end | Scenario compose, `console` profile with the gateway | Real Caddy routing and CSP, real API | `vite preview` with a proxy |
| Command palette | Later | Few pages to jump to yet | In row 5 |
| i18n | English only, `Intl` for dates and numbers | No demand yet | Lingui now |
| Project states | Badge plus gated overview with polling | One place handles `project_not_ready` for every product | Badge only |
| Deleting items | Dimmed, badged, listed last | Restore stays reachable | Only on `/orgs` |
| Big lists | Page by 100 on scroll, filter loaded | No contract change now | Server search param |
| Unknown project | In shell not found, clear last used | Keeps the URL and the switcher | Redirect with a toast |
| Roles in UI | Hide admin areas, disable others with a reason | People see what exists and why they can't | Always show, let the API refuse |
| Sidebar collapse | Icon rail, `[`, remembered | Room for tables; no browser shortcut clash | Always expanded |
| Sidebar persistence | `localStorage` | shadcn's cookie would ride on every API request | Cookie |
| Motion | Minimal, overlays only, off with reduced motion | Calm, fast on big tables | None |
| Security headers | Strict CSP and basics now | Cheap now, painful later | Row 38 |
| Project client | One cached `Client` per project | The SDK binds a project per client; no SDK change | Add `project` to `RequestOptions` |
| Query retries | Off in Query, on in the client | Avoids retrying twice | Query default (3) |
| shadcn preset | `base-nova`, tokens replaced | The default Base UI style, least to undo | Another named style |
