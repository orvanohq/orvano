# 0011. Docs site at orvano.dev with five tested quickstarts

**Date**: 2026-10-02
**Status**: In Progress

## Summary

Orvano gets a public docs site at orvano.dev, built with Astro Starlight (a static docs theme) in a new `website/` workspace and hosted on Cloudflare. It has a quickstart for each of the five SDKs, guides for every auth flow, concept, SDK, console, and self hosting pages, an API reference and an error page per code generated from the contract, and an `llms.txt` for coding agents. A reader starts Orvano on their own machine with one new command, `orvano install --local`, which also runs a local inbox (Mailpit) so the email guides work out of the box. Every quickstart is a real app in `examples/` that CI builds and signs in with on each pull request, and the site goes live only with a real release, so a page never points at a package or image that doesn't exist yet.

## Requirements

**User stories**:
- As a developer new to Orvano, I want to run Orvano on my laptop and follow one quickstart to a signed in user, without asking anyone for help.
- As a developer whose SDK call failed, I want the `type` link in the error response to open a page that tells me why it happened and how to fix it.
- As a backend developer, I want a Dart or .NET quickstart that shows me how to verify a user's token on my own server.
- As a self hoster, I want the install, upgrade, and configuration steps on a real docs site, not only in the README.
- As a coding agent, I want the docs as plain Markdown so I can read them without scraping HTML.
- As an Orvano maintainer, I want the docs to fail CI when a quickstart breaks, a link breaks, or an error code has no page.

**Acceptance criteria**:

*Local stack*

- **AC-1**: `docker run --rm --user "$(id -u):$(id -g)" -v "<dir>:/install" ghcr.io/orvanohq/orvano:<V> install --local --yes` (PowerShell: the same without `--user`) writes `.env` (0600), `docker-compose.yml`, `docker-compose.local.yml`, and `initdb/` into `<dir>`, owned by the reader, with spec 0006's secret rules (every secret generated, none shared between installs). `--local` needs no `--existing-data`, never asks for a domain or email, skips the DNS check, and refuses `--domain` and `--email` with exit 2. `--port <n>` (1024 to 65535, not `8025`, default `7700`) sets the port. It ends by printing, in this order: the next command (`docker compose up -d --wait`, run in `<dir>`, which needs Docker Compose 2.24.6 or later), the console URL `http://localhost:<port>`, the setup link `http://localhost:<port>/setup#<ORVANO_SETUP_TOKEN>`, the Mailpit inbox URL `http://localhost:8025`, the master key with spec 0006's backup warning, and the stop and reset commands (`docker compose down`, and `docker compose down -v` before you delete the folder). Docs always use `localhost`, never `127.0.0.1`: the gateway answers only `Host: localhost`, and token issuers are built from that URL.
- **AC-2**: After `docker compose up -d --wait` in that folder, `GET http://localhost:<port>/v1/health` answers 200, the console loads at `/`, and the setup link creates the first admin (spec 0006 AC-23). The stack publishes exactly two host ports, `127.0.0.1:<port>` (the gateway, listening on the same `<port>` inside the container, since the Caddyfile's site address is `ORVANO_PUBLIC_URL`) and `127.0.0.1:8025` (the Mailpit web inbox), and nothing on any other interface. Postgres, `api`, `worker`, `realtime`, and Mailpit's SMTP port stay on the compose network only. Its containers and volumes are named after a `COMPOSE_PROJECT_NAME` of `orvano-local-<6 random hex>`, generated once per folder, so two local folders (or a server install on the same machine) never share a database volume.
- **AC-3**: Running `install --local` again on the same folder keeps every secret and `COMPOSE_PROJECT_NAME` in `.env` byte for byte (spec 0006 AC-9) and updates `ORVANO_VERSION` and the managed files, so a newer image upgrades the local stack in place. A rerun with a different `--port` changes `ORVANO_PUBLIC_URL`; spec 0006's "everyone must sign in again" question applies, and `--yes` accepts it. Running `--local` on a folder whose `.env` has `ORVANO_VERSION` but not `ORVANO_LOCAL=true` (any server install, including `--domain localhost`) refuses with exit 2 and changes nothing. Running `install.sh` (or `install` without `--local`) on a folder with `ORVANO_LOCAL=true` refuses with exit 2 the same way.
- **AC-4**: A new setting `ORVANO_INSTALL_SMTP_URL` seeds the install SMTP. When it is set (an empty value means unset, as in spec 0009 AC-28) and the install has no SMTP row yet (project `console`, spec 0009), `api` writes one at startup with `INSERT ... ON CONFLICT DO NOTHING`, so two `api` replicas can't race: host and port from the URL, security `none` for `smtp://`, `starttls` for `smtp+starttls://`, `tls` for `smtps://`, username and password from the URL's user info (percent decoded, the password sealed like a console save), From from `ORVANO_INSTALL_SMTP_FROM`. It never overwrites an existing row, so a change made in the console wins; a row deleted in the console comes back on the next `api` start while the setting is set. A set but invalid value (bad scheme, no host, port outside 1 to 65535, user info with `smtp://`, or `ORVANO_INSTALL_SMTP_FROM` missing or not an email) stops `api`, `worker`, and `realtime` at startup (compose passes both settings to all three) with an error that names the setting but never prints the value. `install --local` sets `ORVANO_INSTALL_SMTP_URL=smtp://mailpit:1025` and `ORVANO_INSTALL_SMTP_FROM=orvano@local.test`, so a verification email sent from a local project shows up in the Mailpit inbox.

*Site*

- **AC-5**: `pnpm --filter @orvano/website build` turns `website/` (Astro Starlight) into a static site in `website/dist/` with: a landing page at `/` (one line pitch, the four differentiators from `docs/scope/index.md` each labeled "planned" until it ships, the local start command, a card per quickstart, a GitHub link); docs under `/docs/`; error pages under `/errors/`; the API reference under `/docs/api/`; `/llms.txt`, `/llms-full.txt`, and a Markdown copy of every docs page; a Pagefind search index; a sitemap; and a `404` page. No page loads a script, font, or style from any origin other than the site's own, except the Cloudflare Web Analytics beacon in production builds (AC-24). A build variable `ORVANO_SITE_ENV` (`preview` or `production`, required) decides `noindex` and the beacon.
- **AC-6**: The sidebar has these groups in this order, each with at least these pages: **Get started** (Overview, Run Orvano locally, and the five quickstarts); **Concepts** (Orgs and projects, API keys and scopes, Platforms, Users and sessions, Client and server SDKs, Errors); **Auth guides** (Email and password, Email verification, Password reset, Magic links, Email codes, Change email, Manage sessions, Verify tokens on your server, Manage users with an API key); **SDKs** (JavaScript, Next.js, Flutter, Dart server, .NET); **Console** (First admin and setup, Orgs and projects, API keys, Platforms, Team members and roles, Email settings and templates); **Self hosting** (Install on a server, Upgrade and repair, Configuration, Email delivery); **API reference**; **Errors**; **Changelog**.
- **AC-7**: The site uses the console's brand from `console/design.md`: its accent color tokens, Inter for text, JetBrains Mono for code, dark first with a complete light theme and a toggle that follows the system setting until changed. Every page meets WCAG 2.2 AA: axe finds no violations on the landing page, one quickstart, one guide, one API reference page, and one error page, in both themes.
- **AC-8**: Every docs page shows the Orvano version it documents (the `VERSION` file at build time). Install and image commands use that exact version, never `latest`.

*Quickstarts*

- **AC-9**: Five quickstarts exist, each a page under `/docs/quickstarts/` and a finished app under `examples/`: `nextjs-quickstart` (Next.js App Router with `@orvano/nextjs`), `flutter-quickstart` (`orvano_flutter`), `js-quickstart` (Vite with plain TypeScript and `@orvano/js`), `dart-quickstart` (a `shelf` HTTP server with `orvano_dart`), `dotnet-quickstart` (an ASP.NET Core Minimal API with the `Orvano` NuGet package). Every code block on a quickstart page is pulled at build time from a named region of a file in its example, so the page and the app can't disagree.
- **AC-10**: Each quickstart starts from a running local stack (it links to "Run Orvano locally" and repeats only the three commands) and walks the console path with screenshots: create the first admin from the setup link, create an org and a project, then add the platform the app needs (one Web platform `localhost`, which spec 0003 matches on any port, so it covers Next.js on 3000, Vite on 5173, and Flutter web on 5050; the iOS bundle ID `dev.orvano.quickstart`; the Android package name `dev.orvano.quickstart`) or create a server API key with `users.read` and `users.write` (Dart, .NET). The two app IDs are constants shared by the Flutter example and the Playwright script.
- **AC-11**: The three app quickstarts (Next.js, Flutter, JavaScript) end with an app where you can sign up with email and password, sign in, see your name and email, and sign out, and the page tells you what you should see after each step. Flutter shows web, iOS, and Android in tabs only where they differ: the platform you register; on Android, `adb reverse tcp:<port> tcp:<port>` so the emulator reaches the stack at `http://localhost:<port>` (never `10.0.2.2`, which the gateway won't answer), plus the debug only Android setting that allows plain HTTP to `localhost`; and `flutter run -d chrome --web-port 5050` for web.
- **AC-12**: The two server quickstarts (Dart, .NET) end with a running HTTP server (Dart on port 3001, .NET on 3002) and a small command in the same example (`dart run bin/create_user.dart`, `dotnet run -- create-user`) that creates a user with the API key. The server's `GET /me` reads `Authorization: Bearer <token>`, checks it with the SDK's `verifyAccessToken`, loads the user with `users.get`, and answers 200 with their ID, email, and name; a missing or bad token gets 401. The page gets a token with one `curl` call to `POST /v1/account/sessions/password` (with the `X-Orvano-Project` header) and then calls `/me` with it. The server key comes from an environment variable and never appears in code.
- **AC-13**: Commands that differ between shells show two tabs, macOS and Linux, and Windows PowerShell (the `docker run -v` path and setting environment variables). Commands that are the same show once.
- **AC-14**: Each example pins its Orvano packages to exactly the `VERSION` being released (a CI check compares them), builds and runs on its own when copied out of the repo (it inherits no repo build settings, lint rules, or workspace links), and has a README that points to its quickstart page.

*Generated reference*

- **AC-15**: The API reference has one page per operation in `contract/dist/openapi.public.json`, grouped by tag, showing method, path, parameters, request and response schemas, error responses, the audience (client SDK, server SDK, or both), the API key scope when one is needed, and a tab per SDK with SdkGen's snippet from `contract/dist/examples/<sdk>/`. No `console` or test operation appears (spec 0001 AC-17, AC-18).
- **AC-16**: A new generated file `contract/dist/errors.json`, written by the contract build, lists every member of `ErrorCode` (console codes included, since the API sends `Problem.type` for all of them) with its code, HTTP status, and description from its `@doc`. It leaves out every `TestErrorCode` member and every `ErrorCode` member whose `@doc` starts with `` `Test` environment only`` (today `contract_violation`). The build fails if a member's `@doc` does not end with one HTTP status in parentheses. SDK CI's stale output check covers it.
- **AC-17**: For every code in `errors.json` the site has a page at exactly `/errors/<code>` (the URL the API already sends in `Problem.type`, `contract/errors.tsp`) showing the code, the HTTP status, the description, a hand written "Why it happens" and "How to fix" section from `website/src/content/error-fixes/<code>.mdx`, and how to catch it in each SDK (`OrvanoError` in JS, `OrvanoException` in Dart and .NET, matching on `code`). `/errors/` lists every code. The build fails if a code has no fix file or a fix file has no matching code.

*Quality gates*

- **AC-18**: On every pull request whose changes touch the website area (AC-27), CI builds the site and fails on: any broken internal link or anchor, a quickstart code region that does not exist, an error code without a fix file, an API reference tag that no guide page links to, or Prettier or ESLint findings in `website/`.
- **AC-19**: A `quickstarts` CI job runs on pull requests that change `examples/**`, `sdks/**`, `contract/**`, `deploy/**`, `console/**`, or `server/src/Orvano.Server/Install/**` (not on content only changes). It builds the server and gateway images and packs every SDK from the checkout, starts a stack with `install --local`, runs a Playwright script that follows the quickstart console steps (AC-10) and saves the IDs and key it creates, then copies each example to a temporary folder outside the repo, points it at the packed SDKs there (pnpm `file:` tarballs, a `pubspec_overrides.yaml`, a local NuGet feed in a temporary `nuget.config`; the committed pins stay untouched), and passes only if: Next.js and JavaScript sign up, sign in, show the user, and sign out in Chromium; Flutter does the same as an integration test on Chrome; Dart and .NET answer 200 on `/me` with a real token and 401 without one. Screenshots are not part of this gate.
- **AC-20**: The nightly SDK workflow also runs the Flutter quickstart integration test on the Android emulator against a local stack (with `adb reverse`), and on the iOS simulator against the macOS job's existing stack built from source (those runners have no Docker).
- **AC-21**: The same Playwright script, run with `pnpm --filter @orvano/website screenshots`, saves every console screenshot the docs use, in light and dark, into `website/src/assets/screenshots/`. Pages render both images and CSS on Starlight's `[data-theme]` attribute shows the one matching the reader's theme. Every screenshot has alt text that describes the step. Nothing checks committed screenshots for staleness; rerunning the script after a console change is part of that change.
- **AC-22**: A weekly workflow checks every external link on the built site and opens (or updates) one GitHub issue listing the dead ones. It never fails a pull request.

*Hosting and release*

- **AC-23**: orvano.dev serves the site over HTTPS from Cloudflare (Workers static assets), with `_headers` setting `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, HSTS, and this Content Security Policy: `default-src 'self'; script-src 'self' 'wasm-unsafe-eval' <sha256 hashes of Starlight's inline scripts, computed at build> https://static.cloudflareinsights.com; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' https://cloudflareinsights.com; frame-ancestors 'none'; base-uri 'self'; form-action 'self'` (`wasm-unsafe-eval` for Pagefind, inline styles for Starlight and its code blocks). The axe run (AC-7) loads pages with this policy and fails on any CSP violation. `www.orvano.dev` redirects to `orvano.dev` through a Cloudflare Redirect Rule on a proxied `www` record (`_redirects` can't match a host). An unknown path answers 404 with the site's 404 page; an unknown `/errors/<code>` shows the 404 page with a link to `/errors/`.
- **AC-24**: Production builds (`ORVANO_SITE_ENV=production`) include the Cloudflare Web Analytics beacon (cookieless, no consent banner); preview builds don't, and every preview page carries `noindex`.
- **AC-25**: Production deploys happen only from `release.yml` on a real (not dry run) release, after every package and image publish job succeeds and an `examples` smoke job builds all five examples from the real registries (npm, pub.dev, NuGet) at the released version, building the site from the release tag. A dry run or manual release builds the site but doesn't deploy it. A manual `website.yml` run can redeploy production from a chosen ref, gated by a GitHub environment that needs your approval.
- **AC-26**: A pull request from a branch in this repo that touches the website area gets a preview deploy at its own Cloudflare preview URL, posted as a PR comment, with `noindex` on every page. Pull requests from forks get the build checks but no preview and no access to the Cloudflare token.
- **AC-27**: `changed-areas.sh` gains a `website` area (`website/**` including its Markdown, `examples/**`, `contract/**`, `sdks/**`, `deploy/**`, `server/src/Orvano.Server/Install/**`, `console/**` for the screenshots, `CHANGELOG.md`, `VERSION`), and the new jobs run only when it changed.

*Content moves and rules*

- **AC-28**: The README's install section (spec 0006) moves into "Install on a server" and "Upgrade and repair"; the README keeps a two line summary and links to them. The "Changelog" page renders `CHANGELOG.md` at build time.
- **AC-29**: The auth guides cover spec 0010's docs follow up: one page per flow with a snippet per SDK, the landing page pattern (a Continue button that posts, `Referrer-Policy: no-referrer`), Flutter deep links with https app links, and reading `emailVerified` to gate features. Each email guide starts from a finished quickstart and uses the local Mailpit inbox. The https app links section can't run against a local stack (it needs a real domain); it says so and is the one guide not covered by CI.
- **AC-30**: The docs rule for every later feature is written down: a feature that adds a public operation, error code, or console screen adds or updates its guide, concept, or console page in the same pull request. AC-17 and AC-18's checks enforce the parts CI can see.

## Decision

**Chosen option**: Option 1: a static Astro Starlight site in `website/`, on Cloudflare, with generated reference pages and quickstarts tested as real apps.

Build orvano.dev as one static Starlight site that generates its API reference and error pages from the contract, pulls quickstart code from runnable apps in `examples/`, and deploys only from real releases, with a new `orvano install --local` mode as every quickstart's starting point.

**Implementation skills**: `astro-starlight` (`fusengine/agents`, `.agents/skills/astro-starlight/`) · `astro` (`astrolicious/agent-skills`, `.agents/skills/astro/`) · `wrangler` (`cloudflare/skills`, `.agents/skills/wrangler/`) · `workers-best-practices` (`cloudflare/skills`, `.agents/skills/workers-best-practices/`) · `pnpm` (`antfu/skills`, `.claude/skills/pnpm/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `nextjs-app-router-patterns` (`wshobson/agents`, `.agents/skills/nextjs-app-router-patterns/`) · `flutter-add-integration-test` (`flutter/agent-plugins`, `.agents/skills/flutter-add-integration-test/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `multi-stage-dockerfile` (`github/awesome-copilot`, `.claude/skills/multi-stage-dockerfile/`)

The `astro-starlight` skill opens with a "MANDATORY" step that spawns agents named `fuse-ai-pilot:*`, which don't exist in this project. Skip that step and use only its reference pages.

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### Repository layout

```
website/                         @orvano/website (new pnpm workspace, private)
  astro.config.mjs               Starlight, starlight-openapi, starlight-llms-txt, starlight-links-validator, sitemap
  wrangler.jsonc                 Workers static assets: assets dir ./dist, 404 page handling
  public/_headers, _redirects    CSP and security headers, www redirect
  scripts/prepare.mjs            prebuild: errors pages, docs OpenAPI with SDK samples, changelog, version
  scripts/screenshots.ts         Playwright console journey (AC-19, AC-21)
  src/content/docs/              index.mdx (landing), docs/**, errors/ (generated, gitignored)
  src/content/error-fixes/       <code>.mdx, hand written, one per ErrorCode
  src/assets/screenshots/        committed PNGs, light and dark
  src/styles/brand.css           console tokens mapped onto Starlight variables
examples/
  Directory.Build.props          empty stop file, so .NET examples don't inherit the repo's props
  nextjs-quickstart/  js-quickstart/  flutter-quickstart/  dart-quickstart/  dotnet-quickstart/
contract/
  scripts/error-catalog.mjs      writes dist/errors.json after tsp compile (AC-16)
deploy/compose/docker-compose.local.yml   new overlay embedded in the image: loopback gateway port, Mailpit
deploy/compose/docker-compose.yml   unchanged ports; passes the two SMTP seed settings to api, worker, realtime
server/src/Orvano.Server/Install/   gains --local and --port
server/src/Orvano.Messaging/        gains the ORVANO_INSTALL_SMTP_URL seed
.github/workflows/website.yml    build, checks, previews, manual production deploy
.github/workflows/links.yml      weekly external link check
```

`website/` joins `pnpm-workspace.yaml`. `examples/*` stay out of every workspace (pnpm, the Dart `pubspec.yaml` workspace, `Orvano.slnx`) so each one is exactly what a reader copies. Check early in the build that a standalone `pubspec.yaml` under the root Dart workspace resolves cleanly with `pub get`; if pub objects, give the Dart examples their own `pubspec_overrides.yaml` free resolution or move the check into the temporary copy CI already makes (AC-19). Root lint and format still cover them: Prettier and ESLint for the TS examples (with an `examples/` override that allows their framework defaults), `dart format` and `dart analyze` for Dart and Flutter, `dotnet format` run on the example project directly.

### Data model sketch

No database tables. The one persisted change is a row the existing `orvano.messaging_smtp_settings` table already allows (spec 0009): AC-4 seeds the `console` row with `updated_by_user_id` set to the all zero UUID, which means "set from configuration" (that column has no foreign key). No migration.

New generated file `contract/dist/errors.json`:

| Field | Type | Source |
|---|---|---|
| `code` | string | `ErrorCode` member name |
| `status` | integer | the trailing `(NNN)` in its `@doc` |
| `description` | string | the `@doc` text without that trailing status |

### Site URLs

| Path | What |
|---|---|
| `/` | landing page (Starlight splash layout) |
| `/docs/` | Overview |
| `/docs/local/` | Run Orvano locally |
| `/docs/quickstarts/{nextjs,flutter,js,dart,dotnet}/` | quickstarts |
| `/docs/concepts/*`, `/docs/auth/*`, `/docs/sdks/*`, `/docs/console/*`, `/docs/self-hosting/*` | hand written pages |
| `/docs/api/*` | generated API reference |
| `/docs/changelog/` | from `CHANGELOG.md` |
| `/errors/`, `/errors/<code>` | generated error pages |
| `/llms.txt`, `/llms-full.txt`, `<page>.md` | AI readable copies |

### Build pipeline (`website/scripts/prepare.mjs`, runs before `astro build`)

1. Read `VERSION` and expose it to pages (AC-8).
2. Read `contract/dist/errors.json` and `src/content/error-fixes/*.mdx`; fail on a missing or orphan fix file; write `src/content/docs/errors/<code>.mdx` and the index (AC-17).
3. Copy `contract/dist/openapi.public.json` to a gitignored `website/.generated/openapi.docs.json` and add an `x-codeSamples` entry per operation per SDK from `contract/dist/examples/<sdk>/<operationId>.<ext>`. If the OpenAPI plugin doesn't render `x-codeSamples`, override its operation component to show the same tabs. The contract files are never edited (AC-15).
4. Render `CHANGELOG.md` into the changelog page (AC-28).
5. Check that every quickstart region referenced by a page exists (AC-18, via Starlight's code import or a small remark plugin).

### Local stack (`orvano install --local`)

The server compose file keeps its ports exactly as they are (Compose can't drop a published port through env interpolation). `--local` adds a small managed overlay, `deploy/compose/docker-compose.local.yml`, embedded in the server image beside the main file and written into the folder on every run:

- `gateway.ports: !override ["127.0.0.1:${ORVANO_LOCAL_PORT}:${ORVANO_LOCAL_PORT}"]`, replacing `80`, `443`, and `443/udp`. Caddy listens on that same port inside the container because `ORVANO_PUBLIC_URL` (its site address) is `http://localhost:<port>`. `!override` needs Docker Compose 2.24.6 or later; the "Run Orvano locally" page states that floor, and `install.sh`'s `MIN_COMPOSE` doesn't matter here because `install.sh` isn't used.
- A `mailpit` service (pinned image), web inbox published on `127.0.0.1:8025`, SMTP on `mailpit:1025` inside the network only.

The main file gains only `ORVANO_INSTALL_SMTP_URL` and `ORVANO_INSTALL_SMTP_FROM` (as `${VAR:-}`) on `api`, `worker`, and `realtime`.

`.env` keys `--local` writes beyond spec 0006's table: `COMPOSE_FILE=docker-compose.yml:docker-compose.local.yml` with `COMPOSE_PATH_SEPARATOR=:` (Compose reads both from `.env`, so a plain `docker compose up` picks up the overlay; the explicit separator makes the same `.env` work on Windows, where the default is `;`), `COMPOSE_PROJECT_NAME=orvano-local-<6 random hex>` (generated once, outranks the file's `name: orvano`), `ORVANO_PUBLIC_URL=http://localhost:<port>`, `ORVANO_LOCAL=true` and `ORVANO_LOCAL_PORT=<port>` (the installer's own markers for AC-3; the server never reads them), `ORVANO_INSTALL_SMTP_URL=smtp://mailpit:1025`, `ORVANO_INSTALL_SMTP_FROM=orvano@local.test`. Postgres tuning reads the Docker VM's memory, as on a server.

On macOS and Linux the container runs as the reader's user (`--user "$(id -u):$(id -g)"`), so the files are theirs; on Windows, Docker Desktop's mount handles ownership and the flag is left out. `localhost` can resolve to `::1` first; Node, Dart, and .NET fall back to `127.0.0.1` on their own, and the troubleshooting section of "Run Orvano locally" says so, along with what to do when port 7700 or 8025 is already taken (rerun with `--port`, or stop the other program).

### API surface

No new HTTP operations and no contract change apart from the generated `errors.json`.

| Interface | Form | Key inputs | Key outputs | Auth | Key errors |
|---|---|---|---|---|---|
| `orvano install --local` (CLI, container) | `docker run --rm [--user uid:gid] -v <dir>:/install ghcr.io/orvanohq/orvano:<V> install --local [--port n] [--yes]` | `--port`, existing `.env` | `.env`, `docker-compose.yml`, `docker-compose.local.yml`, `initdb/`, printed summary (AC-1) | none; only the mounted folder | exit 2: `--domain`/`--email` given, port out of range or `8025`, folder holds a server install |
| `orvano install` without `--local` | as spec 0006 | as spec 0006 | as spec 0006 | as spec 0006 | adds exit 2 when the folder's `.env` has `ORVANO_LOCAL=true` (AC-3) |
| Install SMTP seed | `api` startup | `ORVANO_INSTALL_SMTP_URL`, `ORVANO_INSTALL_SMTP_FROM` | `console` SMTP row, once | n/a | role refuses to start on an invalid value (AC-4) |
| Contract build | `pnpm --filter @orvano/contract build` | `contract/**/*.tsp` | `dist/openapi*.json`, `dist/errors.json` | n/a | build fails on a malformed `@doc` status |
| Site build | `pnpm --filter @orvano/website build` | contract `dist/`, `examples/`, `CHANGELOG.md`, `VERSION` | `website/dist/` | n/a | build fails per AC-17, AC-18 |
| Screenshots | `pnpm --filter @orvano/website screenshots` | a running local stack | PNGs, `quickstart.env` (IDs and key, gitignored) | drives the console as the first admin | fails if a console step is missing |
| Quickstart `GET /me` (Dart, .NET examples) | HTTP on the example server | `Authorization: Bearer` | `{ id, email, name }` | app user access token | 401 missing or invalid token |

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| `install --local` | port | `--port`, else the existing `.env` value on a rerun, else `7700` |
| `install --local` | secrets, setup token, master key | generated by spec 0006's `InstallSecrets`, kept on rerun |
| `install --local` | printed setup link | `ORVANO_PUBLIC_URL` + `/setup#` + `ORVANO_SETUP_TOKEN` from the `.env` it just wrote |
| `install --local` | "this folder is a server install" | existing `.env` has `ORVANO_VERSION` and `ORVANO_LOCAL` is not `true` |
| `install` without `--local` | "this folder is a local install" | existing `.env` has `ORVANO_LOCAL=true` |
| `install --local` | compose project name | generated `orvano-local-<6 hex>` on first run, kept from `.env` on rerun |
| Flutter, Playwright | iOS bundle ID, Android package name | the constant `dev.orvano.quickstart`, shared by both |
| Example servers, Flutter web | dev ports | fixed here: Next.js 3000, Vite 5173, Flutter web 5050, Dart 3001, .NET 3002 |
| Install SMTP seed | host, port, security, username, password | parsed from `ORVANO_INSTALL_SMTP_URL` (scheme gives security) |
| Install SMTP seed | From address | `ORVANO_INSTALL_SMTP_FROM` |
| Install SMTP seed | `updated_by_user_id` | the all zero UUID (decided here) |
| Every docs page | documented version | `VERSION` at build time |
| Install and image commands | version in the command | `VERSION` at build time |
| Quickstart page code | the code | named regions in `examples/<name>/` files |
| API reference page | operations, schemas, audience, scope | `openapi.public.json` (`x-orvano-audience`, `x-orvano-scope`) |
| API reference page | SDK snippets | `contract/dist/examples/<sdk>/<operationId>.<ext>` |
| Error page | code, status, description | `contract/dist/errors.json` |
| Error page | why and how to fix | `website/src/content/error-fixes/<code>.mdx` |
| Error page | SDK error type names | fixed in this spec: `OrvanoError` (JS), `OrvanoException` (Dart, .NET) |
| Changelog page | entries | `CHANGELOG.md` |
| Console screenshots | images | Playwright script against a local stack |
| Quickstart CI | project ID, server key, platforms | created by the Playwright console journey, saved to `quickstart.env` |
| Example apps | Orvano endpoint and project ID | env vars per example (`NEXT_PUBLIC_ORVANO_ENDPOINT` and `NEXT_PUBLIC_ORVANO_PROJECT`, `VITE_ORVANO_*`, `--dart-define` for Flutter, `ORVANO_ENDPOINT`, `ORVANO_PROJECT`, `ORVANO_API_KEY` for servers) |
| Example apps | Orvano package versions | pinned to `VERSION` (AC-14) |
| Production deploy | which build | the release tag's checkout |
| Analytics beacon | site token | `PUBLIC_CF_BEACON_TOKEN` build variable, set only for production |
| Every page | `noindex`, beacon on or off | `ORVANO_SITE_ENV` build variable (`preview` or `production`) |
| Landing page | the four differentiators and their "planned" label | the ★ rows in `docs/scope/index.md` |
| Preview URL | per PR alias | `wrangler versions upload --preview-alias pr-<number>` |

### Key invariants

- The site never documents an unpublished version: production builds come only from a real release tag after publish succeeded (AC-25).
- A quickstart page and its example can't disagree, because the page has no code of its own (AC-9).
- Every `ErrorCode` the API can send has a page at the URL in `Problem.type` (AC-17).
- No `console` or test operation or snippet appears in the API reference, and no test error code gets a page (AC-15, AC-16). Console error codes do get pages, because the API links to them.
- A folder is either local or a server install, never converted in either direction, and the server compose file's ports are unchanged (AC-3).
- Two local folders never share containers or volumes (AC-2).
- The install SMTP seed never overwrites a row (AC-4).
- The local stack listens only on `127.0.0.1` (AC-2).
- Example apps contain no secrets; server keys come from the environment (AC-12).

### Security model

- **The site** is public and static. No forms, no cookies, no user data. CSP limits scripts to the site's own origin and the analytics beacon. The analytics are cookieless and collect no personal data, so no consent banner.
- **Deploy credentials**: one Cloudflare API token scoped to deploying this Worker only, plus the account ID, stored in two GitHub environments, `website-preview` and `website-production`. `website-production` requires your approval for manual runs. Workflows use `pull_request`, never `pull_request_target`, so fork code never runs with the token (AC-26).
- **Local stack**: binds to `127.0.0.1` only, so the setup link, console, and Mailpit inbox are not reachable from the network. Its secrets are real and unique per folder; nothing is a shared default. Mailpit has no authentication, which is acceptable only because of the loopback binding.
- **`ORVANO_INSTALL_SMTP_URL`** can carry a password: it is never logged, printed in a validation error, or sent in an event, per the AGENTS.md rule. The seeded password is sealed with the master key like a console save.
- **Examples** teach safe patterns: the server key lives in an environment variable, the Dart and .NET user creation is a local command (never an open HTTP route), and the Android plain HTTP setting is scoped to debug builds and to `localhost` only.
- No regulated data is involved.

### Configuration required

- `ORVANO_INSTALL_SMTP_URL` (passed to and validated by `api`, `worker`, and `realtime`; seeded by `api`): seeds the install SMTP once, AC-4. Unset or empty means no seed.
- `ORVANO_INSTALL_SMTP_FROM`: the From address for that seed; required when the URL is set.
- Installer markers in a local `.env`: `ORVANO_LOCAL`, `ORVANO_LOCAL_PORT`, `COMPOSE_FILE`, `COMPOSE_PATH_SEPARATOR`, `COMPOSE_PROJECT_NAME`, all written only by `--local`.
- Site build variable `ORVANO_SITE_ENV` (`preview` or `production`, required); `PUBLIC_CF_BEACON_TOKEN` (production only).
- GitHub secrets `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` in environments `website-preview` and `website-production`; variable `PUBLIC_CF_BEACON_TOKEN` in `website-production`.

**Prerequisites only you can do** (not code):
1. Create a Cloudflare account and add `orvano.dev` as a zone. Copy every existing record from Squarespace (the Mailgun MX, SPF, DKIM, DMARC, and the GitHub `_gh-orvanohq-o` TXT record), keeping mail records DNS only (not proxied). If DNSSEC is on at Squarespace, turn it off before the switch and set it up again on Cloudflare after. Then switch the nameservers at Squarespace (needs your SMS code), and check mail forwarding and the GitHub and pub.dev domain verifications still pass.
2. Add a proxied `www` record and a Redirect Rule from `www.orvano.dev/*` to `https://orvano.dev/$1` (AC-23).
3. Create the scoped API token and the two GitHub environments, and do one `wrangler deploy` of a placeholder page to workers.dev with no custom domain, so preview uploads have a Worker to attach to before the first release (AC-26). Turn on Web Analytics for `orvano.dev` to get the beacon token.

### Critical test scenarios

- Happy path: on a clean CI runner, `install --local` then `docker compose up -d --wait` serves `/v1/health` on `127.0.0.1:7700`, the Playwright journey creates an admin, org, project, platforms, and a key, and all five examples sign in (or verify a token) against it. Verifies **AC-1**, **AC-2**, **AC-10**, **AC-11**, **AC-12**, **AC-19**.
- Email out of the box: on that stack, `account.createVerification` for a quickstart user puts a message in Mailpit within 30 seconds. Verifies **AC-4**.
- Seed never overwrites: with an install SMTP row saved from the console, restarting `api` with `ORVANO_INSTALL_SMTP_URL` set leaves the row unchanged; an invalid URL stops `api` with an error that doesn't contain the URL. Verifies **AC-4**.
- Local and server folders never convert: `install --local` on a folder from `--domain localhost` (or any `https://` install) exits 2 and every file stays byte for byte; `install` without `--local` on a local folder exits 2 the same way. Rerunning `--local` keeps every secret and the project name. Verifies **AC-3**.
- Loopback only: after `up`, `docker compose ps --format json` shows only `127.0.0.1:7700` and `127.0.0.1:8025` published, and a second local folder gets its own containers and volume. Verifies **AC-2**.
- Android reaches the stack: in the nightly job, after `adb reverse`, the Flutter quickstart test signs in against `http://localhost:7700`. Verifies **AC-11**, **AC-20**.
- Console error codes have pages: `/errors/console_session_required` and `/errors/setup_token_invalid` exist; `/errors/contract_violation` does not. Verifies **AC-16**, **AC-17**.
- Error coverage: adding an `ErrorCode` member without a fix file fails the site build; a `@doc` without a trailing status fails the contract build; the built site has `/errors/<code>` for every code in `errors.json`. Verifies **AC-16**, **AC-17**.
- No private surface: the built site contains no `/v1/console/` or `/v1/test/` path and no `test.` operation. Verifies **AC-15**.
- Broken things fail the PR: a broken internal link, a missing code region, and a reference tag no guide links to each fail the build. Verifies **AC-18**.
- Release gating: a dry run release builds the site and skips deploy; the preview workflow on a fork PR runs the build without the token. Verifies **AC-25**, **AC-26**.
- Accessibility: axe passes on five page types in both themes. Verifies **AC-7**.
- Headers: a production response has the exact CSP, `nosniff`, referrer policy, and HSTS, and pages load under it with no CSP violation (search included); `www` redirects; `/errors/not_a_code` gives the 404 page. Verifies **AC-23**.
- Examples from real registries: on a real release, the `examples` smoke job builds all five against the published packages before the site deploys. Verifies **AC-25**.

## Build plan

Tracer Bullet: first one thin thread from a laptop to a signed in user through every layer (local stack, site, one quickstart, CI test, preview deploy), then thicken it with the other quickstarts, the generated reference, the content, and production.

1. **Local stack**: `--local` and `--port` in `InstallOptions` (skipping `--existing-data`, the domain prompt, and the DNS check), the local `.env` keys, the project name, both refusal rules, the embedded `docker-compose.local.yml` overlay with the loopback gateway port and Mailpit, the printed summary; unit tests for the rules and the install CI job extended to run `--local` as a non root user and check the published ports. Satisfies **AC-1**, **AC-2**, **AC-3**.
2. **Install SMTP seed**: parse and validate `ORVANO_INSTALL_SMTP_URL` and `ORVANO_INSTALL_SMTP_FROM` in `MessagingModule.ConfigureServices` (empty means unset), pass them to `api`, `worker`, and `realtime` in compose, seed the `console` row once at `api` startup with `ON CONFLICT DO NOTHING`, integration test against real Postgres and Mailpit. Satisfies **AC-4**.
3. **Site skeleton**: `website/` workspace with Starlight, brand tokens, the sidebar groups (empty pages allowed for now), landing page, version display, Pagefind, sitemap, `_headers`, `wrangler.jsonc`, link validator, ESLint and Prettier wiring. Satisfies **AC-5**, **AC-6**, **AC-7**, **AC-8**.
4. **Next.js thread**: `examples/nextjs-quickstart` with code regions, its quickstart page, "Run Orvano locally", the Playwright console journey (screenshots plus `quickstart.env`), and the `quickstarts` CI job running only Next.js. Satisfies **AC-9**, **AC-10**, **AC-11**, **AC-13**, **AC-14**, **AC-19**, **AC-21**.
5. **Website CI and previews**: `website.yml` (build, checks, preview deploy for same repo PRs, manual production deploy behind the environment), the `website` area in `changed-areas.sh`. Satisfies **AC-18**, **AC-26**, **AC-27**.
6. **Remaining quickstarts**: Flutter (tabs, `adb reverse` on Android, debug only cleartext for `localhost`, web port 5050, shared app ID constants), JavaScript (Vite), Dart (`shelf`, `create_user`), .NET (Minimal API, `create-user`, `examples/Directory.Build.props`), each added to the `quickstarts` job; Flutter Android (local stack) and iOS (the macOS job's source built stack) run in `sdks-nightly.yml`. Satisfies **AC-9**, **AC-11**, **AC-12**, **AC-13**, **AC-14**, **AC-19**, **AC-20**.
7. **Generated reference**: `contract/scripts/error-catalog.mjs` and `errors.json` in the contract build and the SDK stale check; the prebuild error pages with all fix files written; the OpenAPI plugin with SDK sample tabs, audience and scope. Satisfies **AC-15**, **AC-16**, **AC-17**.
8. **Content**: concept pages, auth guides (spec 0010's follow up), SDK pages, console guide pages with screenshots, self hosting pages moved from the README, changelog page, `llms.txt`, the docs rule written into the contributing guidance. Satisfies **AC-6**, **AC-21**, **AC-28**, **AC-29**, **AC-30**.
9. **Production**: the `examples` smoke job and the deploy job in `release.yml` after the publish jobs, `ORVANO_SITE_ENV` with the beacon for production only and `noindex` for previews, the CSP with build time script hashes, the `www` Redirect Rule, 404 behavior, the weekly external link workflow, axe checks across page types; then your Cloudflare prerequisites and the first real release. Satisfies **AC-7**, **AC-22**, **AC-23**, **AC-24**, **AC-25**.

## Consequences

**Positive**:
- A reader goes from nothing to a signed in user on their own laptop with no server, domain, or SMTP account, and the email guides work immediately through Mailpit.
- Quickstarts can't rot silently: a breaking SDK or console change fails the PR that causes it.
- Error responses become self serve: every `Problem.type` link resolves to a fix.
- Reference, snippets, and error pages regenerate from the contract, so "every feature adds its docs" costs a guide page, not a reference page.
- `ORVANO_INSTALL_SMTP_URL` is useful beyond docs: the server installer and the scenario stack can seed SMTP the same way later.

**Negative / tradeoffs**:
- Docs fixes reach orvano.dev only with a release, unless you run the approved manual deploy. Before 1.0 that is acceptable; reconsider once releases slow down.
- Nothing is live until the first real release (version 0.1 or later), which also means the first real npm, pub.dev, NuGet, and GHCR publish. The NuGet org and prefix reservation are still pending and become a blocker.
- The `quickstarts` CI job is heavy (two images, five SDK packs, five toolchains, a browser) and runs on any contract, SDK, console, or install change.
- Five quickstarts, about 30 hand written pages, and a fix section per error code are a lot of prose to keep current; the console guide will need updates nearly every version.
- Moving orvano.dev's nameservers to Cloudflare is a one time risk to the domain's mail forwarding and verifications if a record is missed.
- `website/` adds a new toolchain (Astro, Starlight plugins) to the repo, and a new Cloudflare account to operate.

**Neutral**:
- The server image embeds a second, small compose overlay for local installs; the server compose file only gains the two SMTP seed settings, so a server install's behavior doesn't change.
- Local installs need Docker Compose 2.24.6 or later (for `!override`), slightly newer than the server installer's floor.
- Examples live outside every workspace, so Renovate or Dependabot updates for them need their own config entries.
- Starlight's OpenAPI and llms plugins are community maintained; a stalled plugin means swapping it, which the prebuild step makes cheap.

## Follow-up

- [x] `withastro/astro@astro-developer` was installed, then removed: it targets contributors to the Astro repo itself, not sites built with Astro. Record it under `Declined:` in root `AGENTS.md` so it isn't offered again.
- [ ] Record in root `AGENTS.md` (`/sync` owns it): the new `website/` and `examples/` folders and their commands, the `astro`, `astro-starlight`, and `wrangler` skills under `## Agent skills`, the docs rule (AC-30) under `## Rules`, Cloudflare MCP noted as skipped for now, and a nested `website/AGENTS.md` context file.
- [ ] Root `AGENTS.md` names spec storage as `docs/specs/`; because the docs site lives in `website/`, not `docs/`, the workflow's artifact base stays `docs/` (no move to `.workflow/`).
- [ ] Consider having the console's "Connect your app" card link to the matching quickstart on orvano.dev (not part of this spec).
- [ ] Spec 0006 Follow-up "Short install URL" (`get.orvano.dev`) becomes easy once orvano.dev is on Cloudflare; worth folding into that row.
- [ ] Finish the NuGet org and `Orvano` prefix reservation before the first real release (blocks AC-25).
- [ ] Revisit docs versioning at 1.0 (row 39).
