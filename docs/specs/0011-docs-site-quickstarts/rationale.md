# 0011. Rationale: docs site at orvano.dev with five tested quickstarts

The decision record behind [index.md](index.md). `/develop` builds from `index.md`; this file explains why.

## Context

Orvano has shipped its first two versions of auth (sign up, sign in, sessions, email verification, recovery, magic links, email codes) through every layer except one: docs. The scope's definition of "end to end" adds a docs page and a snippet per SDK from v0.2 on, and row 11's done when is concrete: a new developer follows the Next.js or Flutter quickstart from install to a signed in user without help. Today the only written guidance is the README's install section and short SDK READMEs.

Several facts already constrain the design. The API sends `https://orvano.dev/errors/<code>` as the `type` of every problem details response, so a site at that exact path is already promised to every SDK user who logs an error. SdkGen already writes a code snippet per public operation per SDK into `contract/dist/examples/`, and the contract build already writes a public only OpenAPI file for docs (spec 0001). The per code error descriptions, though, exist only as `@doc` text in `contract/errors.tsp`; the OpenAPI enum carries the names without them. Every package is still `0.0.0`, and `release.yml` only publishes for real (npm, pub.dev, NuGet, GHCR) from tag `v0.1.0` on, so any install command a quickstart shows fails today.

Running Orvano is the hardest first step. The supported path (spec 0006) is `install.sh` on a Linux server with a domain, which is far too heavy for a first try on a laptop, and the email features need an SMTP server before anything works. The audience spans five SDK surfaces with very different toolchains (Node, Flutter with three platforms, Dart, .NET), and readers will be on macOS, Windows, and Linux.

The domain `orvano.dev` sits at Squarespace, whose DNS edits need an SMS code only you can enter, and `.dev` is HSTS preloaded, so it must be HTTPS. The `docs/` folder already belongs to specs and scope. The project is one maintainer working with agents, so whatever ships has to be cheap to operate and has to catch its own rot, because nobody will reread five quickstarts by hand before every release. Without a decision, every later feature row keeps adding "docs" to its done list with nowhere to put them, and the error links stay broken.

## Options considered

### Option 1: Static Astro Starlight site on Cloudflare, generated reference, quickstarts tested as real apps (chosen)

One static site in `website/`: Starlight for the docs shell, a Starlight OpenAPI plugin for native reference pages fed by `openapi.public.json` plus SdkGen's snippets, generated error pages from a new `errors.json`, quickstart code pulled from runnable apps in `examples/` that CI builds and signs in with, and a new `orvano install --local` mode (with Mailpit) as the shared starting point. Hosted on Cloudflare Workers static assets, deployed only from real releases, with per PR previews.

**Pros**:
- Static output ships almost no JavaScript, which makes WCAG AA, CSP, and hosting simple.
- Search (Pagefind), dark mode, tabs, sidebar, and `llms.txt` come from Starlight and its plugins, not custom code.
- Quickstarts and error pages are checked by CI, so the two things readers rely on most can't silently break.
- Reuses what already exists: the public OpenAPI, the snippets, the console brand tokens, the installer's secret rules, Playwright, the scenario style of real stack testing.

**Cons**:
- A new toolchain (Astro and community Starlight plugins) and a new vendor account (Cloudflare) to operate.
- The quickstart CI job is heavy and runs on many kinds of change.
- Docs reach production only with a release.
- Needs server work (`--local`, the SMTP seed) before the first quickstart can be written.

### Option 2: Fumadocs on Next.js

A React based docs framework on Next.js, with strong OpenAPI support and polished defaults, hosted on any Next.js friendly platform.

**Pros**:
- Same language as the console and the Next.js SDK; React components can be shared.
- Excellent OpenAPI and TypeScript docs integrations.

**Cons**:
- Heavier build and runtime; a pure static export loses some features, and the full feature set wants a Node or edge runtime.
- More JavaScript on every page, more work to meet AA and a strict CSP.
- Ties the docs to Next.js upgrades, a fast moving framework.

### Option 3: Docusaurus

The long established React docs framework, with versioning and a large plugin ecosystem.

**Pros**:
- Proven at large scale; versioned docs built in.
- Many OpenAPI plugins and examples to copy from.

**Cons**:
- Heavy webpack based builds and noticeably more client JavaScript.
- Dated default design that would need real work to match the console.
- Versioning, its main advantage, isn't wanted before 1.0.

### Option 4: Hosted docs platform (for example Mintlify) with embedded reference

A commercial docs host that reads Markdown from the repo and renders reference pages from OpenAPI.

**Pros**:
- Least to build: hosting, search, reference, and analytics included.
- Polished out of the box.

**Cons**:
- Paid beyond a free tier, and a closed platform for an Apache 2.0 project whose pitch is "host it yourself".
- Less control over URLs (the `/errors/<code>` path is fixed by the API), headers, and build time checks such as the error coverage gate.
- Content in a vendor specific format, hard to move later.

## Rationale

The forces that matter most are that one maintainer has to keep five quickstarts and a growing reference correct across fast changing versions, and that the first step (running Orvano) is currently a server install. Option 1 answers both. Pulling quickstart code from real apps and running them in CI turns "the docs are wrong" from a support ticket into a failed PR, which is the only way five toolchains stay correct without a docs team. Generating reference and error pages from the contract means each later feature pays only for its guide prose. And `install --local`, reusing the installer's tested secret rules rather than shipping a compose file with a known key, gives readers a laptop path with real security defaults; Mailpit in the same stack removes SMTP as a prerequisite for every email guide.

Starlight wins over Fumadocs and Docusaurus on operational weight: a static site with almost no client JavaScript is the easiest thing to keep accessible, fast, and locked down with a strict CSP, and it needs no runtime at all. The tradeoff is relying on community Starlight plugins for OpenAPI and `llms.txt`; the prebuild step keeps that risk small, since it owns the data shaping and a plugin can be swapped without touching the contract. A hosted platform was the fastest start but conflicts with the project's open, self hosted identity and with the hard URL and build gate requirements.

Cloudflare beats Netlify and GitHub Pages because it serves `_headers` and `_redirects` (GitHub Pages can't set a CSP), has free per PR preview URLs, and puts the domain's DNS somewhere you can edit without an SMS code, which also unlocks the planned `get.orvano.dev` short install URL. The one time nameserver move is the cost, and it is called out as a prerequisite. Deploying only from real releases follows directly from the packages not being published yet: a docs site that shows install commands for packages that don't exist fails the done when on day one. It also keeps the "latest release only" versioning honest, at the price of docs fixes waiting for a release or a manual approved deploy.

### Decisions made while writing (recommend items)

- **Cloudflare Workers static assets, not Cloudflare Pages.** Cloudflare's current direction for static sites, with `_headers`, `_redirects`, 404 handling, and preview aliases through `wrangler`. Runner up: Pages, which works the same for this site but is where Cloudflare invests less.
- **`errors.json` from a contract build script, not by parsing `errors.tsp` in the website.** Keeps the contract the only source and puts the output under SDK CI's stale check. Runner up: a custom TypeSpec emitter, cleaner in theory but more machinery for one enum.
- **The HTTP status comes from the trailing `(NNN)` in each `@doc`.** Every member already follows that convention; the build now enforces it. Runner up: a new decorator per member, which duplicates what the text already says.
- **SDK samples merged into a gitignored docs copy of the OpenAPI as `x-codeSamples`.** Never edits the contract output; falls back to a component override if the plugin ignores the extension. Runner up: SdkGen writing a third OpenAPI file, more committed churn for no gain.
- **Seed install SMTP in `api` at startup, not in `migrate`.** Sealing a password needs the master key, which only `api` and `worker` hold. Runner up: `worker`, which works but starts later.
- **All zero UUID as `updated_by_user_id` for the seeded row.** The column has no foreign key, and the value clearly means "configuration". Runner up: making the column nullable, a migration for no real benefit.
- **A small `docker-compose.local.yml` overlay selected through `COMPOSE_FILE`,** not env driven ports in the main file. Compose can't remove a published port through interpolation, and the overlay only overrides the gateway's ports and adds Mailpit, so it can't drift from the main file's services. The cross check caught this; the first draft had a `local` profile. Runner up: a full second compose file, which duplicates every service.
- **`orvano@local.test` as the local From address.** `.test` is reserved and the scenario fixture already uses `orvano@scenarios.test`, which passes spec 0009's validation.
- **Fixed dev ports for examples** (Next.js 3000, Vite 5173, Flutter web 5050, Dart 3001, .NET 3002), so instructions and CI are stable. Port 5000 is avoided because macOS AirPlay holds it. One Web platform `localhost` covers all three web apps, since spec 0003 matches web platforms by host on any port.
- **Android reaches the stack through `adb reverse`, not `10.0.2.2`.** The gateway answers only `Host: localhost`, and SDKs build the token issuer from the endpoint, so every client must use `http://localhost:<port>`.
- **A per folder `COMPOSE_PROJECT_NAME`,** so a fresh folder never reuses an old database volume whose passwords no longer match.
- **Examples outside every workspace, with an `examples/Directory.Build.props` stop file.** What CI builds is exactly what a reader copies. Runner up: workspace members, which build faster in the monorepo but break when copied out.
- **Server quickstarts create users with a local command, not an HTTP route,** so no reader copies an open user creation endpoint into production.
- **`starlight-links-validator` for internal links and a weekly external link workflow (for example lychee)** that opens an issue, matching your pick of internal per PR and external weekly.
- **Screenshots and quickstart CI share one Playwright console journey,** so the screenshots always show the steps CI actually ran.

### Engineer picks during the design conversation

All five as full quickstarts (over two app quickstarts plus two shorter server ones), a local Docker stack via `orvano install --local`, Mailpit included, the console path for the first project, one site at orvano.dev, concept, SDK, console guide, and changelog pages in v0.2, Astro Starlight, native static reference pages, Cloudflare, real apps tested in CI, `website/` and `examples/`, latest release only, Pagefind, `llms.txt`, Cloudflare Web Analytics, per PR previews, Playwright screenshots, a small landing page, the console brand, generated plus hand written error pages, the docs rule with CI coverage, `http://localhost:7700`, a general `ORVANO_INSTALL_SMTP_URL`, loopback only binding, Flutter on Chrome per PR and mobile nightly, internal link checks per PR and external weekly, and PowerShell tabs. The console guide was picked against the recommendation; it is in scope, and Consequences notes its upkeep cost.
