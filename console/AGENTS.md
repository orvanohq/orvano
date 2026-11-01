# Console

The React console, served by the server at `/console`. How screens behave is [spec 0001](../docs/specs/0001-console-ux-patterns/index.md); how the code is written is [spec 0003](../docs/specs/0003-react-console-practices/index.md); serving and the dev proxy are [spec 0007](../docs/specs/0007-stack-architecture/index.md) CSL-1 to CSL-3. Cite rule IDs in reviews.

## Commands

Run these from `console/`, or from the root as `pnpm --filter @orvano/console <script>`. pnpm is pinned in the root `package.json` (`packageManager`). If your corepack is too old to run pnpm 12, use `npx pnpm@12.12.1 <command>`.

- Install: `pnpm install` at the repo root (one workspace, one root lockfile; CI uses `--frozen-lockfile`)
- Format, lint, and type check: `pnpm check` at the root (`pnpm fix` applies the fixers). The console has no lint or format config of its own: Oxlint and Oxfmt read the root `vite.config.ts` and `.oxlintrc.json`, ESLint the root `eslint.config.ts`
- Dev server on `http://localhost:5173/console/`: `pnpm dev`. It proxies every path outside `/console` to the server on 5080, so run the server too.
- Build (typecheck, then Vite): `pnpm build`. Typecheck alone: `pnpm typecheck`
- Tests (Vitest browser mode in Chromium): `pnpm test`. Install the browser once with `pnpm exec playwright install chromium`
- The `src/ui` tests in Firefox and WebKit too (spec 0003 QA-1): `pnpm test:engines`, after `pnpm exec playwright install firefox webkit`
- After adding or changing strings: `pnpm extract` (updates `src/locales/*/messages.po`). `pnpm check` fails when the catalogs or `src/routeTree.gen.ts` are out of date
- Bundle size (BDL-1), after a build: `pnpm size`. Coverage: `pnpm test:coverage`

## Conventions

- The base path is `/console`, fixed at build time. `import.meta.env` is read only in `src/shared/config.ts`.
- Every user facing string goes through the Lingui macros. The macros and the React Compiler run in one Babel pass in `vite.config.ts`; the scaffold confirmed both transforms run (spec 0003 I18N-2).
- Route files in `src/routes/` hold only route options and point `component` at a component in `src/features/`. `src/routeTree.gen.ts` is generated: commit it, never edit it.
- pnpm waits 3 days before installing a new release, and blocks install scripts (root `pnpm-workspace.yaml`).
- The restriction rules (imports, globals, syntax from spec 0003) live in `tooling/config/console-restrictions.ts`, one rule set per folder. A rule's allowed home (for example `src/shared/config.ts` for WEB-8) is a region there, never an inline disable. Each rule ID has a fixture in `tooling/lint-fixtures/` that must fire (`pnpm check:lint-fixtures`).
- `vite.config.ts` splits React, TanStack, and Lingui into their own chunks so no file passes BDL-1's 80 KB gzip.
- The root route sends every path to `/setup` while `GET /v1/console/config` says `setupRequired`, so a fresh dev database opens there too (spec 0016 FRS-3): the server prints a link when it starts, and `orvano admin setup-token` makes a new one. The setup token lives only in component state or a ref, never in storage, the query cache, a query key, or a log (FRS-5).
- Design system: build all UI to [`design.md`](design.md) (art direction and the maximalist product bar); token values live in CSS (`src/ui/theme.css`). How the primitives are built is [spec 0015](../docs/specs/0015-design-system-console-shell/index.md).
- Feature code imports primitives from `@/ui` (`src/ui/index.ts`); only the lazy ones (PRM-10) and heavy ones (BDL-2) are imported by their own path.
- `package.json` `sideEffects` lists the only files with import side effects (CSS, the two entries, `src/shared/zod.ts`). A new module with a top level side effect goes on that list, or the build drops it.
- Zod only through `zod/mini`, imported as `z` from `@/shared/zod`; lint fails any direct `zod` import, tests included (spec 0015 STY-8).
- The first load with the shell is at most 230 KB gzip (spec 0015 STY-8, spec 0003 BDL-1). `pnpm size` checks it on the real `_shell` routes (spec 0017 CNS-7). Run it when you touch anything the shell or the root route loads.
- When you add a restriction, add its fixture and put its rule ID in the expected list in `tooling/scripts/console-restrictions.test.mjs` and the count in `tooling/scripts/check-lint-fixtures.test.mjs`.
- After moving strings or the files that hold them, run `pnpm extract` again: the catalogs keep `#:` line references, so a move alone fails `pnpm check`. On a rebase conflict in `messages.po`, run `pnpm extract` rather than merge by hand.

## Tests

- Render through `renderWithProviders(url)` in `src/test/render.tsx`, which runs the real route tree. Pass in app URLs (`/`, not `/console/`): memory history does not apply the router's base path. It seeds the boot config with setup done; pass `config: { setupRequired: true }` to test the redirect to `/setup` (spec 0016 FRS-3).
- Lingui's `I18nProvider` renders nothing until a locale is active, so the helper activates `en` like `main.tsx` does.
- Vitest browser mode serves from `/`, so `BASE_URL` is `/` in tests, and `vitest.config.ts` drops the dev proxy (it would forward Vitest's own requests to the server).
- A failed browser test saves a screenshot under `.vitest/attachments/failure-screenshots/` (git ignored). Look there first when a test fails.
- To focus an element directly, use `focusElement` from `src/test/focus.ts`: it retries until the element holds focus, since Firefox and WebKit on CI can lag a frame.
- A right to left test uses the real `ar` locale, not the pseudo `ar-XB`: CI's WebKit resolves `ar-XB` as left to right.
- If the CI console job times out in `playwright install --with-deps`, the apt mirror stalled: treat it as a flake and run the job again (`_console.yml` already caches the packages and retries).

## Agent skills

Load only what the task needs. Spec 0003 wins where a skill disagrees (WEB-0).

- [vite](../.claude/skills/vite/): `antfu/skills`, Vite config and plugins
- [vitest](../.claude/skills/vitest/): `antfu/skills`, tests (browser mode only, not its jsdom setups)
- [pnpm](../.claude/skills/pnpm/): `antfu/skills`, package management
- [lingui-framework-setup](../.claude/skills/lingui-framework-setup/) and [lingui-best-practices](../.claude/skills/lingui-best-practices/): `lingui/skills`, translation setup and message writing
- [router-plugin](../.claude/skills/router-plugin/): shipped in `@tanstack/router-plugin`, route generation and code splitting
- [tanstack-router-best-practices](../.claude/skills/tanstack-router-best-practices/): `deckardger/tanstack-agent-skills`, routing, loaders, and search params (spec 0003 RTE and URL rules win)
- [playwright-cli](../.claude/skills/playwright-cli/): `microsoft/playwright-cli`, browser automation and E2E tests
- [accessibility](../.claude/skills/accessibility/): `addyosmani/web-quality-skills`, WCAG checks
- [react-aria](../.claude/skills/react-aria/): `react-aria.adobe.com`, React Aria Components (PRM-3 wins: primitives take no `className` or `style`)
- [tailwind-4-docs](../.claude/skills/tailwind-4-docs/): `lombiq/tailwind-agent-skills`, Tailwind CSS v4. Run `python .claude/skills/tailwind-4-docs/scripts/sync_tailwind_docs.py --accept-docs-license` once per clone; never commit its `references/docs/` snapshot
- [vercel-react-best-practices](../.claude/skills/vercel-react-best-practices/): `vercel-labs/agent-skills`, React patterns (not its SWR, `next/dynamic`, or hand written `memo` advice)
- Declined (2026-10-16): a TanStack Query skill, and the community TanStack docs MCP
- Declined (2026-10-26): the Lucide and data grid skills (too thin), `wshobson/agents@tailwind-design-system` (overlaps `design.md`); no MCP server found

_Drafted by /sync from the introducing change, worth a quick human pass._
