# Docs site

## Overview

The Orvano documentation, npm package `@orvano/website` (private), built with Astro 7 and Starlight. Static output with machine readable `llms.txt` (later docs features). Theming to match the design system is feature 6.

## Key files

| File | Owns |
|---|---|
| `astro.config.mjs` | Starlight setup: title, social links, sidebar |
| `src/content/docs/` | The pages, in Markdown or MDX |
| `src/content.config.ts` | The docs content collection |

## Commands

```bash
pnpm --filter @orvano/website dev
pnpm --filter @orvano/website build   # writes the static site to website/dist
```

## Conventions

- Every service feature ships a docs page with a code sample that CI compiles.
- Voice follows the design system: plain, second person, sentence case, no exclamation marks.

## Gotchas

- The build warns that the sitemap needs `site` in `astro.config.mjs`; set it once the docs domain is decided.

## Related specs

- [0001 Stack and architecture](../docs/specs/0001-stack-architecture/index.md)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._
