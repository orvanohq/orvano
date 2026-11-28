# Console

## Overview

The Orvano dashboard, npm package `@orvano/console` (private): a Next.js 16 static export with React 19 and Tailwind 4, served by `orvano` at `/console`. It talks to Orvano only through the public API, via the TypeScript SDK (feature 17). shadcn/ui and TanStack Query arrive with the console shell (feature 5).

## Key files

| File | Owns |
|---|---|
| `next.config.ts` | `output: "export"`, `basePath: "/console"`, unoptimized images |
| `src/app/layout.tsx` | Root layout (placeholder until feature 5 maps the design system) |
| `src/app/globals.css` | Tailwind entry; design tokens land here with feature 5 |

## Commands

```bash
pnpm --filter @orvano/console dev
pnpm --filter @orvano/console build   # writes the static site to console/out
```

## Conventions

- Look and feel come from `docs/design/design-system/` (`tokens.css` plus its README, which says how to map tokens onto shadcn/ui variables). Reference screens are in `docs/design/mockups/` (projects, approval queue, table editor, setup check). Do not invent hues.
- Use shadcn/ui components and their semantic color classes (`bg-primary`, `text-muted-foreground`), never raw palette classes.
- Static export only: no server components that fetch at request time, no route handlers, no middleware. Data loads in the browser through the SDK and TanStack Query.
- Folder by feature under `src/`; every screen meets WCAG AA (keyboard reachable, visible focus, AA contrast, labelled controls, status never shown by color alone).
- TypeScript `strict`, no `any`.

## Gotchas

- Every URL is under `/console`; links must go through Next's `Link` or `basePath` aware helpers, not hand written absolute paths.
- Addresses that contain project ids need a static export strategy; feature 5 settles it.

## Agent skills

- [shadcn](../.claude/skills/shadcn/): `shadcn-ui/ui`, adding, composing and theming shadcn/ui components
- [playwright-cli](../.claude/skills/playwright-cli/): `microsoft/playwright-cli`, driving the console in a browser and writing Playwright tests

MCP servers: Playwright (recommended, `claude mcp add playwright npx @playwright/mcp@latest`)

## Related specs

- [0001 Stack and architecture](../docs/specs/0001-stack-architecture/index.md)

_Drafted by /audit from the repo, worth a quick human pass. Edit freely: once a line stops matching this draft, later runs treat it as curated and will flag rather than overwrite it._

<!-- BEGIN:nextjs-agent-rules -->

## This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` (resolved from this file's directory; in monorepos the `next` package may not be visible from the repo root) before writing any code. Heed deprecation notices.

This block is written and re-added by `next dev` — verify at `node_modules/next/dist/server/lib/generate-agent-files.js`. Removing it from a diff only re-creates the uncommitted change; committing it with your work keeps the tree clean.

<!-- END:nextjs-agent-rules -->
