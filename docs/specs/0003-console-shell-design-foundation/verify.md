# Verify: Console shell and design foundation · spec 0003 · updated 2026-11-27
_Steps derived from spec 0003 acceptance criteria and its Value sourcing table. `/check verify` runs these; `/test` locks the durable ones._

## UI / manual

Run the Compose stack (`deploy/smoke-test.sh` leaves nothing running; for a manual pass use `docker compose -f deploy/compose.yaml -f deploy/compose.build.yaml up --build`) and open `https://localhost/console/`.

- [x] Open `/console` → lands on `/console/`, title `Projects · Orvano`, instance rail with Projects current → AC-1, AC-8
- [x] Open `/console/members` (no slash) → address becomes `/console/members/` → AC-1
- [x] Open `/console/project/database/?project=prj_01JF8K2M4Q` and reload → the Database stub renders again, the top bar shows `prj_01JF8K2M4Q` in mono, the service rail shows the back link and 12 services with Database current → AC-1, AC-8
- [x] Open `/console/no-such-page/` → 404, "Page not found" with a link back to your projects, no rail → AC-2
- [x] Open `/console/project/database/` with no `project`, then `?project=bad id` → each lands on `/console/`, and Back skips the project page → AC-9
- [x] On a project page, hover and focus each rail item → tooltip with its label; every service link carries `?project=` → AC-8, AC-9
- [x] Every rail destination shows its `h1`, its one line description, and "{Title} isn't built yet" with "This page arrives in milestone {M}." → AC-10
- [x] Narrow the window below 768px → rail hidden, "Open navigation" opens a 240px drawer with labels; Tab stays inside, Escape closes, focus returns to the button → AC-11
- [x] Theme menu: System follows the OS (change the OS setting with the page open); Light and Dark override it both ways; reload keeps the choice with no flash → AC-6
- [x] DevTools Network on `/console/`, a project stub and `/console/kit/` → every request is under `/console/`; Plex Sans and Plex Mono load from `/console/_next/static/` → AC-7
- [x] `/console/kit/` by keyboard only: every button, badge, status dot, table ID button (opens a 480px sheet, Escape returns focus), select, checkbox, the bucket name error (shown inline and announced), the delete gate (enables only on `recipes-dev`), and Copy code (announces "Copied") → AC-12
- [x] Turn on reduced motion in the OS → sheets, tooltips and hovers change with no transition → AC-13
- [x] Press Tab once on any page → "Skip to main content" appears first; Enter moves focus to the main region → AC-13

## Commands

- [x] `dotnet test --solution Orvano.slnx` → `ConsoleFilesTests` pass: 503 `console_not_built` with an empty folder, the startup warning names the folder, 405, 308, 301, immutable and no-cache, 404, no console headers outside the branch → AC-1, AC-2, AC-3, AC-4
- [x] `Console__RootPath=/tmp/empty dotnet run --project server/src/Orvano.Host` (with a reachable database) → `/console/` answers 503 problem+json with `code` `console_not_built` and a `hint`; `/healthz` and `/readyz` unchanged → AC-3
- [x] `curl -sI https://localhost/console/` and a `/console/_next/static/…js` file through Caddy → the four security headers on both, `no-cache` and `public, max-age=31536000, immutable`; `curl -sI https://localhost/healthz` has none → AC-4
- [x] `pnpm --filter @orvano/console lint` → passes; edit `src/styles/tokens.css` by hand or add `bg-blue-500` in `src/` → lint fails naming the problem → AC-5
- [x] `pnpm --filter @orvano/console build && pnpm --filter @orvano/console test:e2e` → all Playwright and axe tests pass (axe: zero violations on three pages in light and dark) → AC-6 to AC-13
- [x] `dotnet run --project server/src/Orvano.AppHost` → a `console` resource runs; its link opens `/console/`; editing a file under `console/src` shows without a restart → AC-14
- [x] `deploy/smoke-test.sh` → passes, including the "Spec 0003" step → AC-15
- [x] CI on the pull request → `web` (lint with token and colour checks), `console-e2e`, both `images` (amd64 and arm64) and `smoke` are green → AC-15

## Value sourcing

- [x] File bytes come from `Console:RootPath`: point it at `console/out`, then at another folder with a different `index.html` → the host serves whichever folder is set → Value sourcing, serve a console file
- [x] `Cache-Control` follows the path: a `.js` outside `_next/static/` (none exists today; check `/console/index.txt`) is `no-cache`, one inside is immutable → Value sourcing, Cache-Control
- [x] The missing files `code` and `hint` match the spec wording exactly → Value sourcing, missing files answer
- [x] Change a description or milestone in `src/shell/nav.ts`, rebuild → the rail tooltip, drawer label, stub page and `<title>` all change together → Value sourcing, rail item and title
- [x] On `/console/members/`, Members is current and Projects is not; on any project page, the back link to Projects is never current → Value sourcing, active item
- [x] `/console/kit/` and the not found page show no rail; `/console/project/nope/?project=prj_x` (unknown service) shows no rail either → Value sourcing, pick the rail
- [x] Open the console on a different host name (for example `127.0.0.1` instead of `localhost`) → the top bar shows that host → Value sourcing, instance address
- [x] `?project=prj_a&project=prj_b` → the top bar and links use `prj_a` (the first wins) → Value sourcing, project id
- [x] Clear `localStorage` `orvano-theme` with the OS in dark → dark; set it to `light` → light at first paint → Value sourcing, theme
- [x] Copy on the SQL block → the clipboard holds exactly that block's code → Value sourcing, code block copy
- [x] The delete gate accepts only `recipes-dev`, the name the kit passes in → Value sourcing, destructive confirmation

## Acceptance-criteria coverage

- AC-1: UI steps 1 to 3, the host tests, the smoke test · AC-2: UI step 4, host tests, smoke · AC-3: host tests and the empty folder command · AC-4: host tests, the curl command, smoke · AC-5: the lint command · AC-6: the theme step and `theme.spec.ts` · AC-7: the network step and `network.spec.ts` · AC-8: UI steps 1 to 3 and 6, `shell.spec.ts` · AC-9: UI step 5, `shell.spec.ts` · AC-10: UI step 7, `shell.spec.ts` · AC-11: UI step 8, `drawer.spec.ts` · AC-12: the kit step, `kit.spec.ts` · AC-13: reduced motion and skip link steps, `a11y.spec.ts` · AC-14: the AppHost command · AC-15: the smoke test and CI steps
