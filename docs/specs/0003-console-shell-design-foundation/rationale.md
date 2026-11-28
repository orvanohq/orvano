# 0003. Console shell and design foundation: rationale

The decision record behind [index.md](index.md). `/develop` reads the index and skips this file.

## Context

The console is a Next.js static export served by `orvano` at `/console` (spec 0001). It uses only the public API through the TypeScript SDK, which arrives with feature 17, and there is no sign in until feature 12. Feature 5 has to give every later console page a place to land: the shell from the design system README (a 56px rail and a 48px top bar), the tokens mapped onto shadcn/ui, light and dark themes, and base components that already meet WCAG AA. It also closes the M0 thread at the browser end, so the console has to come out of the real image through the real Caddy and host, not a separate dev server.

Spec 0001 left one question open for this feature: a static export can only produce pages it knows at build time, but project pages need a project id in the address, and ids exist only at run time. Whatever this feature picks becomes the addressing rule for every project page across all of Phase 2, so changing it later means touching every link and every page.

Three other forces shape the design. The design system's `tokens.css` switches to dark with a `prefers-color-scheme` media query, which a user's Light choice cannot override, so the console can't use that file as it is. Self hosted instances may have no internet, and every visitor's IP going to a font host is a privacy leak, so nothing at run time may reach outside the instance. And the orvano image has a read only root filesystem, no shell and no `RUN` in its final stage (spec 0002), which suits files baked in at build time but rules out anything that writes or generates at startup.

If this isn't settled now, each later feature would invent its own route shape, colour mapping and component variants, and accessibility would turn into a pass over 40 pages at M9 instead of a baseline every page inherits.

## Options considered

### Option 1: query string project addresses, host serves the files, tokens generated from `tokens.json`

One exported page per route. Project pages live under `/project/<service>/` and read the id from `?project=`. `orvano` serves the export with ordinary static file handling (default files, a trailing slash redirect, a not found page). A script generates the console's token CSS from `tokens.json` with a `data-theme` selector instead of the media query, and CI fails if the committed file is stale.

**Pros**:
- Fully supported by Next's static export with no workaround. Client navigation, prefetching and reloads all just work.
- The host needs only standard static file middleware, so nothing breaks when Next changes its file layout.
- The rail is decided by the route at build time, so there's no flash between rails.
- One source of colour truth, with drift caught by CI.

**Cons**:
- The address `…/project/database/?project=prj_…` is less tidy than a path segment, and every project link has to carry the parameter.
- Reading search params forces Suspense boundaries in static export.

### Option 2: path addresses with a host rewrite

Project pages at `/console/projects/prj_…/database/`. Next exports one template page per service for a placeholder id, and the host rewrites any real id to that template, for both the HTML and the `.txt` route data files Next fetches during client navigation. The page reads the real id from `location.pathname`.

**Pros**:
- The tidiest addresses, matching what people expect from hosted dashboards.
- The id is part of the path, so no link can drop it by accident.

**Cons**:
- The host has to know Next's internal file naming for route data, which has changed between major versions. An upgrade can break client navigation silently while full reloads still work, which is hard to catch.
- `useParams` returns the placeholder, so every page needs a custom hook that disagrees with Next's own API.
- The test server and the host both need the same rewrite rules.

### Option 3: one page with a client router

Export a single `index.html` and route everything inside it with a client side router (TanStack Router or React Router), with the host falling back to `index.html` for every path under `/console`.

**Pros**:
- Any address shape, including path ids, with one simple fallback rule on the host.
- Typed routes and search params if TanStack Router is used.

**Cons**:
- It throws away Next's router, which spec 0001 chose Next for, and adds a second routing system for contributors and agents to learn.
- Unknown paths can no longer return a real `404`, because everything falls back to `200`.
- The whole console loads as one bundle up front unless code splitting is set up by hand.

## Rationale

Option 1 wins because addressing is the decision that will be hardest to change, and it's the only option that depends on nothing beyond Next's documented static export behaviour. Option 2's tidy addresses rest on the host copying Next's private file layout. That layout is exactly the kind of thing that changes in a major release, and the failure (client navigation breaks while reloads work) is the kind that slips past tests. Option 3 trades that risk for a second router and loses real `404`s, against spec 0001's choice of Next. A query parameter costs one helper (`projectHref`) and a Suspense boundary, both cheap and both visible in code review.

Serving through the host, not Caddy's file server, keeps spec 0001's "one image runs everything": the files ship inside the orvano image, so the version of the console always matches the API it was built for. Generating tokens instead of copying them keeps `tokens.json` the one source, and it solves the media query problem at the same time. The script writes a `data-theme` selector that next-themes controls, so a Light choice beats a dark OS.

The smaller calls, each made with you during design (pick first, runner up second):

| Decision | Pick | Why | Runner up |
|---|---|---|---|
| Rail items for unbuilt pages | All items, stub pages | The whole shell is testable now, and each feature replaces one stub | Only built items |
| API calls | None, TanStack Query provider wired | Keeps the feature pure UI until the SDK exists | `/healthz` status dot |
| Narrow screens | Rail becomes a drawer below 768px | Usable on a phone for a quick check, simple layout | Desktop only with a minimum width |
| Rail mode | By route (project pages need `?project=`) | Same effect as choosing by the param, with no flash on first paint | One merged rail |
| Theme | System, Light, Dark with next-themes | What shadcn documents, no flash, static export safe | Hand rolled three way |
| Token source | Generated from `tokens.json` | One source, CI catches drift, fixes the override problem | Copy and patch `tokens.css` |
| Fonts | Fontsource packages | No network at build or run time, private, pinned by the lockfile | `next/font/google` |
| Forms | React Hook Form with Zod | The most documented shadcn path, so agents write it correctly | TanStack Form |
| Code block | No highlighting yet | Avoids shipping a large highlighter before a page needs it | Shiki |
| Tests | Playwright with axe | Checks what this feature is about: focus, contrast, the real export | Adding Vitest too |
| Dev loop | AppHost runs `next dev` | One command starts everything, as the project promises | Separate `pnpm dev` |
| Missing files | `503` with a hint | The host and API keep working, and the cause is obvious | Fail at startup |
| Component checks | Unlinked `/kit/` page | One page to test and to use as a living reference | Dev only page |
| Headers | Baseline now, CSP in feature 99 | Blocks framing and sniffing today, without a policy that breaks on Next upgrades | Hash based CSP now |
| Empty top bar slots | Leave out what does nothing | No dead controls for keyboard and screen reader users | Visible but disabled |

Calls made while writing, not asked:

- **`trailingSlash: true`.** Each page becomes `<route>/index.html`, which the host's default files handling serves with no extension mapping. The runner up, `page.html` files, needs an extension fallback rule on the host and in the test server.
- **Missing files checked per request.** One file lookup per request lets a contributor build the console while the host runs. The runner up, checking once at startup, would need a restart and saves nothing measurable.
- **Destructive button text uses `on-brand`.** The README's white text fails AA in dark (about 3.1 to 1). `on-brand` passes in both themes and invents no colour. The runner up, adding an `on-danger` token, is the right long term fix but changes the design system, so it's a follow up.
- **Chromium only in Playwright for now.** Fast CI for a shell with little browser specific code. Firefox and WebKit join in feature 89.
- **A small Node test server instead of the host for Playwright.** The console job then needs no .NET or Postgres. The host's own serving is proven by its integration tests and the Compose smoke test. The runner up, running the real host in the console job, would double the job's setup for little extra coverage.
- **Project id pattern `^prj_[0-9A-Za-z]{1,64}$`.** Loose on purpose, because features 7 and 14 own the real format. The runner up, guessing a ULID shape now, would likely be wrong.
- **Folder name `ConsoleFiles`.** A namespace ending in `Console` shadows `System.Console` across the host.
