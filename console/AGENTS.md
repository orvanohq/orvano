# Console

## Overview

The Orvano dashboard, npm package `@orvano/console` (private): a Next.js 16 static export with React 19 and Tailwind 4, served by `orvano` at `/console`. It talks to Orvano only through the public API, via the TypeScript SDK (feature 17). shadcn/ui (Radix, `radix-nova`), TanStack Query, next-themes, React Hook Form and Zod came with the console shell (feature 5, spec 0003).

## Key files

| File | Owns |
|---|---|
| `next.config.ts` | `output: "export"`, `basePath: "/console"`, `trailingSlash: true`, unoptimized images |
| `src/app/layout.tsx` | Root layout: fonts, providers, `%s · Orvano` title template, skip link, top bar, rail slot, `main#main` |
| `src/app/globals.css` | Tailwind entry: imports the tokens, the dark variant, the shadcn mapping onto tokens, the reduced motion rule |
| `src/styles/tokens.css` | Generated from `docs/design/design-system/tokens.json` by `scripts/tokens.mjs`; never edit by hand |
| `src/shell/nav.ts` | Every rail destination (label, title, icon, route, description, milestone); rails, drawer, stubs and titles read it |
| `src/shell/project-id.ts` | `useProjectId()` and `projectHref()` for `?project=` project addresses |
| `src/components/ui/` | shadcn/ui components, adjusted to the tokens after `shadcn add` |
| `src/app/kit/` | `/console/kit/`, every base component and state; not linked from the rails |
| `scripts/check-colours.mjs` | Fails `lint` on any raw colour in `src/` outside `tokens.css` |
| `scripts/serve-export.mjs` | Serves `out/` at `/console` with the host's rules, for Playwright and for looking at a build |

## Commands

```bash
pnpm --filter @orvano/console dev
pnpm --filter @orvano/console build   # writes the static site to console/out
pnpm --filter @orvano/console tokens  # regenerate src/styles/tokens.css after tokens.json changes
pnpm --filter @orvano/console lint    # token check, colour check, then ESLint
pnpm --filter @orvano/console test:e2e  # Playwright and axe against console/out (build first)
```

## Conventions

- Look and feel come from `docs/design/design-system/` (`tokens.css` plus its README, which says how to map tokens onto shadcn/ui variables). Reference screens are in `docs/design/mockups/` (projects, approval queue, table editor, setup check). Do not invent hues.
- Use shadcn/ui components and their semantic color classes (`bg-primary`, `text-muted-foreground`), never raw palette classes.
- Static export only: no server components that fetch at request time, no route handlers, no middleware. Data loads in the browser through the SDK and TanStack Query.
- Folder by feature under `src/`; every screen meets WCAG AA (keyboard reachable, visible focus, AA contrast, labelled controls, status never shown by color alone).
- TypeScript `strict`, no `any`.
- Colours only through the shadcn names (`bg-primary`) or the token names (`bg-surface-subtle`, `text-ink-muted`); `lint` rejects hex values, colour functions with literals, palette classes and `text-white` or `bg-black`. Text on a `-subtle` background uses `brand-ink` or `ink`, never the strong colour (contrast).
- Project pages read the project from `?project=` (spec 0003): build every project link with `projectHref(route, id)`, and wrap anything that calls `useProjectId()` in Suspense (it uses `useSearchParams`). Project pages sit under `src/app/project/`, whose layout guards the id.
- A new page exports `metadata.title` from its `nav.ts` entry; a page that replaces a stub changes its own `nav.ts` entry only.
- After `shadcn add`, adjust the component to the tokens (36px and 32px heights, `radius-md`, the 2px brand focus ring with offset, `shadow-popover` only on floating parts, `bg-overlay` for scrims), then check it on `/console/kit/` in both themes.
- Every sheet and dialog has a title and a description (visually hidden where the design shows none). A sheet opened without a `SheetTrigger` returns focus itself through `onCloseAutoFocus`.

## Gotchas

- Every URL is under `/console`; links must go through Next's `Link` or `basePath` aware helpers, not hand written absolute paths.
- Project addresses use the query string (`/console/project/database/?project=prj_…`), not a path segment, because a static export cannot prerender every project id.
- Run Prettier from the repo root (`pnpm exec prettier --write console`): run from `console/` it misses the root `.prettierignore` and reformats `AGENTS.md` and the generated `tokens.css`.
- `next dev` appends a "This is NOT the Next.js you know" block to this file and adds it back on every run; leave it committed.
- Locally, Playwright reuses whatever already answers on port 3100, so a running `orvano` host on 3100 gets tested instead of `serve-export.mjs`.

## Agent skills

- [shadcn](../.claude/skills/shadcn/): `shadcn-ui/ui`, adding, composing and theming shadcn/ui components
- [playwright-cli](../.claude/skills/playwright-cli/): `microsoft/playwright-cli`, driving the console in a browser and writing Playwright tests
- [nextjs-app-router-patterns](../.claude/skills/nextjs-app-router-patterns/): `wshobson/agents`, App Router layouts and routing (its server rendering advice does not apply to a static export; spec 0003 wins)
- [tailwind-4-docs](../.claude/skills/tailwind-4-docs/): `Lombiq/Tailwind-Agent-Skills`, Tailwind 4 utilities, `@theme` and variants
- [tanstack-query](../.claude/skills/tanstack-query/): `tanstack-skills/tanstack-skills`, browser data loading and caching
- [react-hook-form](../.claude/skills/react-hook-form/): `pproenca/dot-skills`, forms with the Zod resolver
- [zod](../.claude/skills/zod/): `pproenca/dot-skills`, schemas and validation
- [playwright-best-practices](../.claude/skills/playwright-best-practices/): `currents-dev/playwright-best-practices-skill`, Playwright tests and axe scans

MCP servers: Playwright (recommended, `claude mcp add playwright npx @playwright/mcp@latest`) · Next.js DevTools (recommended, `vercel/next-devtools-mcp`)

## Related specs

- [0001 Stack and architecture](../docs/specs/0001-stack-architecture/index.md)
- [0003 Console shell and design foundation](../docs/specs/0003-console-shell-design-foundation/index.md)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._

<!-- BEGIN:nextjs-agent-rules -->

## This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` (resolved from this file's directory; in monorepos the `next` package may not be visible from the repo root) before writing any code. Heed deprecation notices.

This block is written and re-added by `next dev` — verify at `node_modules/next/dist/server/lib/generate-agent-files.js`. Removing it from a diff only re-creates the uncommitted change; committing it with your work keeps the tree clean.

<!-- END:nextjs-agent-rules -->
