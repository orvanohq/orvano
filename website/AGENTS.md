# Website

## Overview

`@orvano/website` is orvano.dev (spec 0011): one static Astro Starlight site with the docs, five quickstarts, an API reference and an error page per code generated from the contract, and `llms.txt` for coding agents. It is hosted on Cloudflare Workers static assets (no Worker code) and goes live only from a real release.

## Key files

| File | Owns |
|---|---|
| `astro.config.mjs` | Starlight, the sidebar groups in their spec order (AC-6), the OpenAPI, llms, and link validator plugins, and `ORVANO_SITE_ENV` (`noindex` on previews, the analytics beacon in production) |
| `ec.config.mjs` | Expressive Code (code blocks): themes and the plugin that makes every `<pre>` keyboard focusable. Starlight's `<Code>` reads it too, so code block options go here, not in `astro.config.mjs` |
| `scripts/prepare.ts` | Runs before `dev` and `build`: writes the error pages, the changelog page, and `.generated/openapi.docs.json` (the public contract plus SDK snippet tabs). All gitignored |
| `scripts/markdown-copies.ts`, `scripts/headers.ts` | After `astro build`: a `.md` copy of every page, then the Content Security Policy with the inline script hashes added to `dist/_headers` |
| `scripts/check-site.ts` | `check:site`: serves `dist/` like Cloudflare and runs axe, the CSP, the 404, and the `noindex` or beacon checks in Chromium |
| `scripts/screenshots.ts` | The console journey against a local stack: the docs screenshots in light and dark, and `quickstart.env` (the IDs and key) for the quickstarts CI job |
| `scripts/check-web-quickstart.ts`, `scripts/check-server-quickstart.ts` | The quickstarts CI job's checks against a running example |
| `src/lib/site.ts` | The documented version (from `VERSION`), the image, install, and local stack commands every page reuses |
| `src/components/ExampleCode.astro`, `src/lib/regions.ts` | Quickstart code pulled from `examples/` |
| `src/components/Screenshot.astro` | A console screenshot in both themes |
| `src/content/docs/` | Every page: `index.mdx` (landing), `docs/**`, `404.md`, and the generated `errors/` |
| `src/content/error-fixes/<code>.mdx` | The hand written "Why it happens" and "How to fix" of each error code |
| `src/styles/brand.css` | The console's brand (`console/design.md`) mapped onto Starlight's variables |
| `public/_headers`, `wrangler.jsonc` | Security headers, and the Worker: assets dir, 404 handling, the `orvano.dev` Custom Domain, preview URLs |

## Commands

```bash
pnpm --filter @orvano/contract build                          # first, so contract/dist/errors.json is current
ORVANO_SITE_ENV=preview pnpm --filter @orvano/website build   # the site into dist/
ORVANO_SITE_ENV=preview pnpm --filter @orvano/website check:site   # axe and the CSP on the built site
pnpm --filter @orvano/website dev                             # serve while you write
ORVANO_LOCAL_DIR=<folder of an install --local> pnpm --filter @orvano/website screenshots
```

## Conventions

- `ORVANO_SITE_ENV` (`preview` or `production`) is required for every build, so `pnpm -r build` skips this package (CI builds it in `website.yml`). `check:site` takes the same value the build used.
- Never type a version or an install command into a page: import them from `src/lib/site.ts`, so every page documents exactly `VERSION` (AC-8).
- A quickstart page has no code of its own. Use `<ExampleCode example="..." file="..." region="..." />` with `#region <name>` and `#endregion <name>` comments in the example's file; a missing file or region fails the build.
- Screenshots come only from `pnpm screenshots`; never edit a PNG by hand. Rerun it after a console change the docs show, and give each `<Screenshot>` alt text that describes the step.
- No page may load anything from another origin, and no inline event handlers (`onclick=`): the CSP allows only the site's own files, its hashed inline scripts, and the Cloudflare beacon. Fonts stay files (Vite never inlines them as `data:` URLs).
- The docs rule for every feature (a new operation, error code, or console screen updates its page in the same pull request) is in `CONTRIBUTING.md`.
- Previews (`wrangler versions upload --preview-alias pr-<n>`) run only for same repo pull requests, never with `pull_request_target`. Production deploys only from `release.yml` after a real release, or a manual `website.yml` run behind the `website-production` environment.

## Gotchas

- The generated folders (`src/content/docs/errors/`, `docs/changelog.md`, `.generated/`) are rewritten on every `dev` and `build`; edit `error-fixes/`, `CHANGELOG.md`, or the contract instead.
- `starlight-links-validator` can't see the API reference routes, so `prepare.ts` checks links to `/docs/api/` against the contract instead, and fails when a reference tag has no guide page linking to it.
- `404.md` is a `draft`, which keeps it out of `llms.txt`; Starlight still serves it as the 404 page.
- `links.yml` checks external links weekly and keeps one issue open; it never runs on a pull request.

## Related specs

- [0011 Docs site and quickstarts](../docs/specs/0011-docs-site-quickstarts/index.md) (with `verify.md`)

Its Agent Skills (`astro-starlight`, `astro`, `wrangler`) are listed in the root [AGENTS.md](../AGENTS.md).

_Drafted by /sync from the introducing change, worth a quick human pass._
